using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using FailBetter.Core;
using FailBetter.Core.QAssoc;
using Sunless.Game.ApplicationProviders;
using Sunless.Game.Data.SNRepositories;
using Sunless.Game.Scripts.Physics;
using Sunless.Game.UI.Menus;

namespace SunlessQoL
{
    [BepInPlugin(GUID, "Mr Eaten's Many Things", "2.17.6")]
    public class QoLPlugin : BaseUnityPlugin
    {
        public const string GUID = "uptoh.sunless.manythings";

        internal static QoLPlugin Instance;
        internal static ManualLogSource Log;

        // ---- config (persisted to BepInEx/config) ----
        private ConfigEntry<float> _terror, _hunger, _fuel, _damage;
        private ConfigEntry<bool> _disableExplosions, _previewTerror;
        private ConfigEntry<float> _accel;
        private ConfigEntry<KeyCode> _toggleKey, _holdKey, _sayKey;

        // Read by the static set_Hull patch; updated each frame.
        internal static float DamageMult = 1f;
        internal static bool DisableExplosions;
        internal static bool PreviewTerror;
        private const float EngineHeatCap = 10f;

        // ---- captured vanilla nav-constant defaults (grabbed once) ----
        private bool _captured;
        private float _baseHunger, _baseGloom, _baseEngineFuel, _baseLightFuel;

        // ---- time acceleration runtime state ----
        private bool _toggledOn;       // set by toggle key
        private bool _accelerating;    // toggle OR hold, computed each frame
        private bool _appliedScale;    // whether we currently own Time.timeScale

        private enum Rebind { None, Toggle, Hold, Say }
        private Rebind _rebind = Rebind.None;

        // ---- ui ----
        private bool _show;
        private int _tab;
        private static readonly string[] TabNames = { "Zee Law", "7 Numbers", "The Ship", "The Crew", "Bank" };
        private Rect _rect = new Rect(70f, 70f, 460f, 0f);
        private string _status = "";
        private GUIStyle _header;
        private bool _styled;

        // ---- "7 Numbers" panel: Iron, Mirrors, Pages, Hearts, Veils, Hunger, Terror ----
        private static readonly string[] NumNames =
            { "Iron", "Mirrors", "Pages", "Hearts", "Veils", "Hunger", "Terror", "Wounds", "Echoes" };
        private readonly string[] _num = new string[9];
        private bool _numLoaded;
        private string _lastFocus = "";

        // ---- "The Ship" panel ----
        // Paperdoll slot order: 0 Deck, 1 Forward, 2 Aft, 3 Auxiliary, 4 Bridge, 5 Engines
        private static readonly string[] SlotNames =
            { "Deck", "Forward", "Aft", "Auxiliary", "Bridge", "Engines" };
        private int _selectedSlot = -1;       // -1 = paperdoll; 0..5 = gear list for that slot
        private Vector2 _gearScroll;
        private System.Collections.Generic.List<Quality> _allGear;
        private System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Quality>> _gearBySlot;
        private System.Collections.Generic.List<Quality> _officerSlots;
        private System.Collections.Generic.List<Quality> _ships;
        private bool _selectedShip;
        private static System.Reflection.MethodInfo _getByIdMethod;
        private string _hull = "";
        private bool _shipLoaded;
        private string _crew = "";
        private bool _crewLoaded;

        // ---- Bank panel (unlimited persistent storage, keyed by per-run character id) ----
        private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<int, int>> _bank;
        private Vector2 _holdScroll, _bankScroll;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            _terror = Config.Bind("Rates", "TerrorGainMultiplier", 1f,
                new ConfigDescription("Multiplier for Terror gain while sailing.",
                    new AcceptableValueRange<float>(0f, 2f)));
            _hunger = Config.Bind("Rates", "HungerGainMultiplier", 1f,
                new ConfigDescription("Multiplier for Hunger gain.",
                    new AcceptableValueRange<float>(0f, 2f)));
            _fuel = Config.Bind("Rates", "FuelConsumptionMultiplier", 1f,
                new ConfigDescription("Multiplier for Fuel consumption.",
                    new AcceptableValueRange<float>(0f, 2f)));
            _damage = Config.Bind("Rates", "DamageMultiplier", 1f,
                new ConfigDescription("Multiplier for incoming Hull damage.",
                    new AcceptableValueRange<float>(0f, 2f)));
            _disableExplosions = Config.Bind("ZeeLaw", "DisableExplosions", false,
                "When true, engine boost still works, but engine heat is capped low enough to prevent fires and explosions.");
            _previewTerror = Config.Bind("ZeeLaw", "PreviewTerror", false,
                "When true, Terror storylet preview icons show the exact amount.");
            _accel = Config.Bind("TimeAcceleration", "Factor", 3f,
                new ConfigDescription("Time acceleration multiplier.",
                    new AcceptableValueRange<float>(1f, 10f)));
            _toggleKey = Config.Bind("TimeAcceleration", "ToggleKey", KeyCode.F10,
                "Tap to switch time acceleration on/off.");
            _holdKey = Config.Bind("TimeAcceleration", "HoldKey", KeyCode.F11,
                "Hold to accelerate; release to return to normal.");
            _sayKey = Config.Bind("ZeeLaw", "SomethingAwaitsYouKey", KeyCode.F9,
                "Tap to grant Something Awaits You.");

            Harmony h = new Harmony(GUID);
            PatchOne(h, typeof(MainMenuPatch));
            PatchOne(h, typeof(HullDamagePatch));
            PatchOne(h, typeof(EngineHeatPatch));
            PatchOne(h, typeof(EngineRecalculateHeatPatch));
            PatchOne(h, typeof(TerrorPreviewPatch));
            PatchOne(h, typeof(GazetteerBankTabPatch));
            Logger.LogInfo("Mr Eaten's Many Things loaded; patch registration finished.");
        }

        private void PatchOne(Harmony h, Type patchType)
        {
            try
            {
                h.PatchAll(patchType);
                Logger.LogInfo("Patched " + patchType.Name + ".");
            }
            catch (Exception e)
            {
                Logger.LogError("Failed to apply " + patchType.Name + ": " + e);
            }
        }

        internal void Toggle()
        {
            _show = !_show;
            if (_show) { _status = ""; _numLoaded = false; _lastFocus = ""; _selectedSlot = -1; _selectedShip = false; _shipLoaded = false; _crewLoaded = false; }
        }

        // Invoked by the native "Bank" tab in the port Gazetteer.
        internal static void RenderBankFromTab()
        {
            if (Instance != null) Instance.RenderBank();
        }

        internal static float CapEngineHeat(float value)
        {
            if (!DisableExplosions) return value;
            return value > EngineHeatCap ? EngineHeatCap : value;
        }

        // Renders the bank natively into the Gazetteer's two book pages as grids of
        // item portraits (the game's own ThingIcon). Content goes in a fresh child
        // container each time so we NEVER add persistent components to the shared
        // page panels (doing so broke the Story tab's scrolling).
        internal void RenderBank()
        {
            try
            {
                GameProvider gp = GameProvider.Instance;
                NavigationProvider nav = NavigationProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null || nav == null) return;
                Sunless.Game.UI.Gazetteer.Gazetteer gaz = nav.Gazetteer;
                if (gaz == null || gaz.LeftPanel == null || gaz.RightPanel == null) return;

                Character ch = gp.CurrentCharacter;
                System.Collections.Generic.Dictionary<int, int> store = BankFor(ch);

                gaz.ClearContent(false); // LeftPanel
                gaz.ClearContent(true);  // RightPanel
                gaz.SetLeftTitle("Your Hold  Cargo: " + CargoUsed(ch) + "/" + CargoCapacity(ch) + "  (click to deposit)");
                gaz.SetRightTitle("Bank  (click to withdraw)");

                GameObject leftPage = MakeBankPage(gaz.LeftPanel);
                GameObject rightPage = MakeBankPage(gaz.RightPanel);
                GameObject leftCargo = AddBankSection(leftPage, "Cargo");
                GameObject leftCurios = AddBankSection(leftPage, "Curiosities");
                GameObject rightCargo = AddBankSection(rightPage, "Cargo");
                GameObject rightCurios = AddBankSection(rightPage, "Curiosities");

                // Hold side: real possessions, click to deposit all.
                System.Collections.Generic.IList<CharacterQPossession> list = ch.QualitiesPossessedList;
                if (list != null)
                {
                    foreach (CharacterQPossession p in new System.Collections.Generic.List<CharacterQPossession>(list))
                    {
                        if (p == null) continue;
                        Quality q = p.AssociatedQuality;
                        if (q == null || q.Nature != FailBetter.Core.Enums.Nature.Thing) continue;
                        if (q.AssignToSlot != null || q.IsSlot) continue;
                        if (ch.GetUnmodifiedQualityLevel(q) <= 0) continue;
                        GameObject parent = null;
                        if (IsCargo(q)) parent = leftCargo;
                        else if (IsCuriosity(q)) parent = leftCurios;
                        else continue;
                        Quality qq = q;
                        new Sunless.Game.UI.Icons.ThingIcon(parent, p, new Action(delegate
                        {
                            Deposit(ch, qq, ch.GetUnmodifiedQualityLevel(qq), store);
                            RenderBank();
                        }));
                    }
                }

                // Bank side: display-only possessions built from the stored amounts.
                foreach (int id in new System.Collections.Generic.List<int>(store.Keys))
                {
                    int amt = store[id];
                    if (amt <= 0) continue;
                    int theId = id;
                    Quality q = Canonical(id);
                    if (q == null) continue;
                    GameObject parent = null;
                    if (IsCargo(q)) parent = rightCargo;
                    else if (IsCuriosity(q)) parent = rightCurios;
                    else continue;
                    CharacterQPossession fake = new CharacterQPossession();
                    fake.AssociatedQuality = q;
                    fake.Level = amt;
                    new Sunless.Game.UI.Icons.ThingIcon(parent, fake, new Action(delegate
                    {
                        Withdraw(ch, theId, store.ContainsKey(theId) ? store[theId] : 0, store);
                        RenderBank();
                    }));
                }

                gaz.UpdateEchoes();
            }
            catch (Exception e) { Log.LogError("RenderBank: " + e); }
        }

        // Throwaway child containers. They are destroyed by ClearContent on the next
        // render, so nothing persists on the shared Gazetteer page panels.
        private static GameObject MakeBankPage(GameObject panel)
        {
            GameObject c = new GameObject("MTBankPage", new Type[] { typeof(RectTransform) });
            c.transform.SetParent(panel.transform, false);
            RectTransform rt = c.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(360f, 0f);

            UnityEngine.UI.VerticalLayoutGroup v = c.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = false;
            v.spacing = 5f;
            v.padding = new UnityEngine.RectOffset(10, 10, 10, 10);

            UnityEngine.UI.ContentSizeFitter fit = c.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            return c;
        }

        private static GameObject AddBankSection(GameObject page, string title)
        {
            AddBankHeader(page, title);
            return MakeGrid(page);
        }

        private static void AddBankHeader(GameObject page, string title)
        {
            Font font = FindUiFont();
            GameObject h = new GameObject("MTBankHeader", new Type[] { typeof(RectTransform) });
            h.transform.SetParent(page.transform, false);
            UnityEngine.UI.Text t = h.AddComponent<UnityEngine.UI.Text>();
            t.text = title;
            t.fontSize = 22;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = new Color(0.16f, 0.16f, 0.14f, 1f);
            t.raycastTarget = false;
            if (font != null) t.font = font;
            UnityEngine.UI.LayoutElement le = h.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = 320f;
            le.preferredHeight = 30f;

            GameObject line = new GameObject("MTBankHeaderRule", new Type[] { typeof(RectTransform) });
            line.transform.SetParent(page.transform, false);
            UnityEngine.UI.Image image = line.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(0.16f, 0.16f, 0.14f, 0.85f);
            UnityEngine.UI.LayoutElement lineLe = line.AddComponent<UnityEngine.UI.LayoutElement>();
            lineLe.preferredWidth = 320f;
            lineLe.preferredHeight = 2f;
        }

        private static Font FindUiFont()
        {
            try
            {
                UnityEngine.UI.Text[] texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
                if (texts != null)
                {
                    foreach (UnityEngine.UI.Text t in texts)
                        if (t != null && t.font != null) return t.font;
                }
            }
            catch { }
            try { return (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf"); }
            catch { return null; }
        }

        private static GameObject MakeGrid(GameObject panel)
        {
            GameObject c = new GameObject("MTBankGrid", new Type[] { typeof(RectTransform) });
            c.transform.SetParent(panel.transform, false);
            RectTransform rt = c.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            UnityEngine.UI.GridLayoutGroup grid = c.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.cellSize = new Vector2(64f, 80f);
            grid.spacing = new Vector2(8f, 8f);
            grid.padding = new UnityEngine.RectOffset(0, 0, 0, 0);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            UnityEngine.UI.LayoutElement le = c.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = 280f;
            le.flexibleWidth = 0f;
            UnityEngine.UI.ContentSizeFitter fit = c.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            return c;
        }

        private static bool IsCargo(Quality q)
        {
            return q != null && q.Category == FailBetter.Core.Enums.Category.Goods &&
                q.AssignToSlot == null && !q.IsSlot;
        }

        private static bool IsCuriosity(Quality q)
        {
            return q != null && q.Category == FailBetter.Core.Enums.Category.Curiosity &&
                q.AssignToSlot == null && !q.IsSlot && q.Id != WellKnownQualityProvider.Echo.Id;
        }

        private static int CargoUsed(Character ch)
        {
            int total = 0;
            if (ch == null || ch.QualitiesPossessedList == null) return 0;
            foreach (CharacterQPossession p in ch.QualitiesPossessedList)
            {
                if (p == null || p.AssociatedQuality == null) continue;
                if (!IsCargo(p.AssociatedQuality)) continue;
                total += p.EffectiveLevel;
            }
            return total;
        }

        private static int CargoCapacity(Character ch)
        {
            try
            {
                if (ch == null) return 0;
                return ch.GetEffectiveQualityLevel(WellKnownQualityProvider.Hold);
            }
            catch { return 0; }
        }

        private bool _wasSailing;

        private void Update()
        {
            DamageMult = _damage.Value;
            DisableExplosions = _disableExplosions != null && _disableExplosions.Value;
            PreviewTerror = _previewTerror != null && _previewTerror.Value;
            try { ApplyRateConstants(); }
            catch (Exception e) { Log.LogError("ApplyRateConstants threw: " + e); }
            try { HandleKeys(); }
            catch (Exception e) { Log.LogError("HandleKeys threw: " + e); }
        }

        private void LateUpdate()
        {
            try
            {
                // Own Time.timeScale only while actively accelerating in live play.
                GameProvider gp = GameProvider.Instance;
                bool inPlay = gp != null && gp.CurrentCharacter != null
                              && gp.CurrentUIState != null && !gp.CurrentUIState.IsPaused;

                bool want = _accelerating && inPlay;
                if (want)
                {
                    Time.timeScale = _accel.Value;
                    _appliedScale = true;
                }
                else if (_appliedScale)
                {
                    Time.timeScale = 1f;
                    _appliedScale = false;
                }
            }
            catch (Exception e) { Log.LogError("LateUpdate threw: " + e); }
        }

        // Only ever touch the navigation constants while the player is actively
        // sailing. This deliberately does NOTHING during the title screen,
        // character creation, new-game world generation, storylets, ports, etc.
        private void ApplyRateConstants()
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null) return;
            if (gp.GeneratingTiles) return;          // never during world generation
            if (gp.CurrentCharacter == null) return; // not in an actual game yet

            var ui = gp.CurrentUIState;
            bool sailing = ui != null && ui.SailingObjectsCanMove;
            if (sailing != _wasSailing)
            {
                Log.LogInfo("Sailing state -> " + sailing);
                _wasSailing = sailing;
            }
            if (!sailing) return;                    // consumption only happens while sailing

            var nav = gp.NavigationConstants;
            if (nav == null) return;

            if (!_captured)
            {
                _baseHunger = nav.BaseHungerIncrease;
                _baseGloom = nav.BaseGloomIncrease;
                _baseEngineFuel = nav.EngineFuelDecrementUnit;
                _baseLightFuel = nav.LightFuelDecrementUnit;
                _captured = true;
                Log.LogInfo("Captured nav defaults: hunger=" + _baseHunger +
                    " gloom=" + _baseGloom + " engineFuel=" + _baseEngineFuel +
                    " lightFuel=" + _baseLightFuel);
            }

            nav.BaseHungerIncrease = _baseHunger * _hunger.Value;
            nav.BaseGloomIncrease = _baseGloom * _terror.Value;
            float fm = _fuel.Value;
            nav.EngineFuelDecrementUnit = _baseEngineFuel * fm;
            nav.LightFuelDecrementUnit = _baseLightFuel * fm;
        }

        private void HandleKeys()
        {
            if (_rebind != Rebind.None)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _rebind = Rebind.None;
                    _status = "Rebinding cancelled.";
                    return;
                }
                foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
                {
                    if (kc == KeyCode.None) continue;
                    if (kc >= KeyCode.Mouse0 && kc <= KeyCode.Mouse6) continue; // keyboard only
                    if (Input.GetKeyDown(kc))
                    {
                        if (_rebind == Rebind.Toggle) _toggleKey.Value = kc;
                        else if (_rebind == Rebind.Hold) _holdKey.Value = kc;
                        else _sayKey.Value = kc;
                        _status = "Bound " + (_rebind == Rebind.Toggle ? "Toggle" : (_rebind == Rebind.Hold ? "Hold" : "SAY")) + " to " + kc + ".";
                        _rebind = Rebind.None;
                        break;
                    }
                }
                return; // swallow this frame's input while rebinding
            }

            if (_toggleKey.Value != KeyCode.None && Input.GetKeyDown(_toggleKey.Value))
                _toggledOn = !_toggledOn;

            if (_sayKey.Value != KeyCode.None && Input.GetKeyDown(_sayKey.Value))
                GrantSomethingAwaitsYou();

            bool hold = _holdKey.Value != KeyCode.None && Input.GetKey(_holdKey.Value);
            _accelerating = _toggledOn || hold;
        }

        private void GrantSomethingAwaitsYou()
        {
            try
            {
                GameProvider gp = GameProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null) return;
                gp.CurrentCharacter.AcquireQualityAtExplicitLevel(WellKnownQualityProvider.SomethingAwaitsYou, 1);
                NavigationProvider nav = NavigationProvider.Instance;
                if (nav != null)
                {
                    if (nav.Boat != null) nav.Boat.SomethingAwaitsYou = 1;
                    nav.ShowHideSAYIcon(true);
                    nav.ProgressPanelUpdate(Sunless.Game.Formatters.LogEntries.AsLogEntry("Something awaits you..."), null);
                }
                _status = "Something Awaits You.";
            }
            catch (Exception e)
            {
                _status = "SAY failed: " + e.Message;
                Log.LogError(e);
            }
        }

        private void OnGUI()
        {
            if (!_show) return;
            if (!_styled)
            {
                _header = new GUIStyle(GUI.skin.label);
                _header.fontStyle = FontStyle.Bold;
                _styled = true;
            }
            _rect = GUILayout.Window(0x4D52454E, _rect, DrawWindow, "Mr Eaten's Many Things");
        }

        private void RateRow(string label, ConfigEntry<float> entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(135f));

            Rect r = GUILayoutUtility.GetRect(190f, 18f, GUILayout.Width(190f));
            DrawPerilScale(r);
            float raw = GUI.HorizontalSlider(r, entry.Value, 0f, 2f);
            float val = Mathf.Round(raw * 20f) / 20f;
            if (Mathf.Abs(val - 1f) < 0.025f) val = 1f;
            entry.Value = val;

            GUIStyle valueStyle = GUI.skin.label;
            Color prev = GUI.color;
            if (entry.Value < 1f) GUI.color = new Color(0.55f, 1f, 0.55f);
            else if (entry.Value > 1f) GUI.color = new Color(1f, 0.58f, 0.58f);
            else GUI.color = Color.white;
            GUILayout.Label("x" + entry.Value.ToString("0.00"), valueStyle, GUILayout.Width(48f));
            GUI.color = prev;
            GUILayout.EndHorizontal();
        }

        private void DrawPerilScale(Rect r)
        {
            if (UnityEngine.Event.current.type != EventType.Repaint) return;
            Rect bar = new Rect(r.x, r.y + 6f, r.width, 6f);
            Color prev = GUI.color;
            GUI.color = new Color(0.1f, 0.6f, 0.1f, 0.32f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * 0.5f, bar.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.75f, 0.1f, 0.1f, 0.32f);
            GUI.DrawTexture(new Rect(bar.x + bar.width * 0.5f, bar.y, bar.width * 0.5f, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(bar.x + bar.width * 0.5f - 1f, bar.y - 3f, 2f, bar.height + 6f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(2f);
            int newTab = GUILayout.Toolbar(_tab, TabNames);
            if (newTab != _tab)
            {
                _tab = newTab;
                _numLoaded = false;
                _lastFocus = "";
                _selectedSlot = -1;
                _selectedShip = false;
                _shipLoaded = false;
                _crewLoaded = false;
            }
            GUILayout.Space(6f);

            if (_tab == 0) DrawZeeLaw();
            else if (_tab == 1) DrawSevenNumbers();
            else if (_tab == 2) DrawTheShip();
            else if (_tab == 3) DrawTheCrew();
            else DrawBank();

            GUILayout.Space(8f);
            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status);
            if (GUILayout.Button("Close"))
            {
                CommitFocusedNumber();
                _show = false;
            }

            GUI.DragWindow(new Rect(0f, 0f, 100000f, 20f));
        }

        private void DrawZeeLaw()
        {
            RateRow("TERROR GAIN", _terror);
            GUILayout.Space(8f);
            RateRow("HUNGER GAIN", _hunger);
            GUILayout.Space(8f);
            RateRow("FUEL CONSUMPTION", _fuel);
            GUILayout.Space(8f);
            RateRow("DAMAGE FACTOR", _damage);

            GUILayout.Space(12f);
            GUILayout.Label("ZEE LAW TOGGLES", _header);
            _disableExplosions.Value = GUILayout.Toggle(_disableExplosions.Value, "Disable Explosions");
            _previewTerror.Value = GUILayout.Toggle(_previewTerror.Value, "Preview Terror");

            GUILayout.Space(12f);
            GUILayout.Label("SOMETHING AWAITS YOU", _header);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Grant key", GUILayout.Width(90f));
            if (GUILayout.Button(_rebind == Rebind.Say ? "<press a key>" : _sayKey.Value.ToString()))
                _rebind = Rebind.Say;
            GUILayout.EndHorizontal();

            GUILayout.Space(12f);
            GUILayout.Label("TIME ACCELERATION", _header);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Speed x" + _accel.Value.ToString("0.0"), GUILayout.Width(90f));
            float v = GUILayout.HorizontalSlider(_accel.Value, 1f, 10f);
            _accel.Value = Mathf.Round(v * 10f) / 10f;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Toggle key", GUILayout.Width(90f));
            if (GUILayout.Button(_rebind == Rebind.Toggle ? "<press a key>" : _toggleKey.Value.ToString()))
                _rebind = Rebind.Toggle;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Hold key", GUILayout.Width(90f));
            if (GUILayout.Button(_rebind == Rebind.Hold ? "<press a key>" : _holdKey.Value.ToString()))
                _rebind = Rebind.Hold;
            GUILayout.EndHorizontal();

            GUILayout.Label(_accelerating ? "Status: ACCELERATING x" + _accel.Value.ToString("0.0")
                                          : "Status: normal speed");
        }

        private void DrawSevenNumbers()
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null || gp.CurrentCharacter == null)
            {
                GUILayout.Label("No game in progress.");
                _numLoaded = false;
                return;
            }

            if (!_numLoaded) LoadNumbers();

            GUILayout.Label("Set a value, then click Set / press Enter / click away.", _header);
            GUILayout.Space(4f);

            for (int i = 0; i < NumNames.Length; i++)
            {
                if (i == 5 || i == 8) GUILayout.Space(12f); // stats / menaces / Echoes
                GUILayout.BeginHorizontal();
                GUILayout.Label(NumNames[i], GUILayout.Width(70f));
                GUI.SetNextControlName("num" + i);
                _num[i] = GUILayout.TextField(_num[i] ?? "", GUILayout.Width(90f));
                if (GUILayout.Button("Set", GUILayout.Width(50f)))
                    ConfirmNumber(i);
                GUILayout.Label("now " + CurrentLevel(i));
                GUILayout.EndHorizontal();
            }

            // Enter confirms the focused field.
            UnityEngine.Event e = UnityEngine.Event.current;
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                int idx = FocusedNumIndex();
                if (idx >= 0) { ConfirmNumber(idx); e.Use(); }
            }

            // Clicking away (focus leaves a field) confirms the field just left.
            string cur = GUI.GetNameOfFocusedControl();
            if (cur != _lastFocus)
            {
                if (_lastFocus != null && _lastFocus.StartsWith("num"))
                {
                    int idx;
                    if (int.TryParse(_lastFocus.Substring(3), out idx)) ConfirmNumber(idx);
                }
                _lastFocus = cur;
            }
        }

        private static int FocusedNumIndex()
        {
            string f = GUI.GetNameOfFocusedControl();
            if (string.IsNullOrEmpty(f) || !f.StartsWith("num")) return -1;
            int idx;
            return int.TryParse(f.Substring(3), out idx) ? idx : -1;
        }

        private void CommitFocusedNumber()
        {
            if (_tab != 1) return;
            int idx = FocusedNumIndex();
            if (idx < 0 && _lastFocus != null && _lastFocus.StartsWith("num"))
                int.TryParse(_lastFocus.Substring(3), out idx);
            if (idx >= 0 && idx < NumNames.Length) ConfirmNumber(idx);
        }

        private static Quality QualityFor(int i)
        {
            switch (i)
            {
                case 0: return WellKnownQualityProvider.Iron;
                case 1: return WellKnownQualityProvider.Mirrors;
                case 2: return WellKnownQualityProvider.Pages;
                case 3: return WellKnownQualityProvider.Hearts;
                case 4: return WellKnownQualityProvider.Veils;
                case 5: return WellKnownQualityProvider.Hunger;
                case 6: return WellKnownQualityProvider.Terror;
                case 7: return Canonical(110546); // "Menaces: Wounds"
                case 8: return WellKnownQualityProvider.Echo;
                default: return null;
            }
        }

        private static int CurrentLevel(int i)
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null || gp.CurrentCharacter == null) return 0;
            Quality q = QualityFor(i);
            if (q == null) return 0;
            return gp.CurrentCharacter.GetUnmodifiedQualityLevel(q);
        }

        private void LoadNumbers()
        {
            for (int i = 0; i < NumNames.Length; i++)
                _num[i] = CurrentLevel(i).ToString();
            _numLoaded = true;
        }

        private void ConfirmNumber(int i)
        {
            try
            {
                GameProvider gp = GameProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null) { _status = "No game in progress."; return; }

                int val;
                if (!int.TryParse((_num[i] ?? "").Trim(), out val))
                {
                    _status = NumNames[i] + ": '" + _num[i] + "' is not a whole number.";
                    _num[i] = CurrentLevel(i).ToString();
                    return;
                }
                if (val < 0) val = 0;

                Quality q = QualityFor(i);
                if (q == null) { _status = NumNames[i] + ": quality not found."; return; }

                gp.CurrentCharacter.AcquireQualityAtExplicitLevel(q, val);
                _num[i] = val.ToString();
                _status = NumNames[i] + " set to " + val + ".";
                Log.LogInfo(_status);
                RefreshEchoes(); // journal Echo counter is cached; nudge it to redraw
            }
            catch (Exception ex)
            {
                _status = "Error setting " + NumNames[i] + ": " + ex.Message;
                Log.LogError(ex);
            }
        }

        private static void RefreshEchoes()
        {
            try
            {
                NavigationProvider nav = NavigationProvider.Instance;
                if (nav == null) return;
                Sunless.Game.UI.Gazetteer.Gazetteer gaz = nav.Gazetteer;
                if (gaz != null) gaz.UpdateEchoes();
            }
            catch (Exception e) { Log.LogError("RefreshEchoes: " + e); }
        }

        // ===================== "The Ship" panel =====================

        private static Quality SlotQuality(int i)
        {
            switch (i)
            {
                case 0: return WellKnownQualityProvider.Deck;
                case 1: return WellKnownQualityProvider.Forward;
                case 2: return WellKnownQualityProvider.Aft;
                case 3: return WellKnownQualityProvider.Auxiliary;
                case 4: return WellKnownQualityProvider.Bridge;
                case 5: return WellKnownQualityProvider.Engines;
                default: return null;
            }
        }

        // Possessions/slots from the live character can be DIFFERENT Quality
        // instances than the eager content list, so always match by Id.
        private static CharacterQPossession FindPossession(int qualityId)
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null || gp.CurrentCharacter == null) return null;
            System.Collections.Generic.IList<CharacterQPossession> list = gp.CurrentCharacter.QualitiesPossessedList;
            if (list == null) return null;
            foreach (CharacterQPossession p in list)
                if (p != null && p.AssociatedQuality != null && p.AssociatedQuality.Id == qualityId) return p;
            return null;
        }

        private System.Collections.Generic.List<Quality> OfficerSlots()
        {
            if (_officerSlots != null) return _officerSlots;
            _officerSlots = new System.Collections.Generic.List<Quality>();
            try
            {
                foreach (Quality q in WellKnownQualityProvider.Officers)
                    if (q != null) _officerSlots.Add(q);
            }
            catch (Exception e) { Log.LogError("OfficerSlots build failed: " + e); }
            return _officerSlots;
        }

        // Slot set depends on the active tab: The Ship -> ship slots, The Crew -> officer slots.
        private Quality CurSlotQ(int i)
        {
            if (_tab == 3)
            {
                System.Collections.Generic.List<Quality> l = OfficerSlots();
                return (i >= 0 && i < l.Count) ? l[i] : null;
            }
            return SlotQuality(i);
        }

        private string CurSlotName(int i)
        {
            if (_tab == 3) { Quality q = CurSlotQ(i); return q != null ? q.Name : "?"; }
            return (i >= 0 && i < SlotNames.Length) ? SlotNames[i] : "?";
        }

        private int CurSlotId(int i)
        {
            Quality q = CurSlotQ(i);
            return q == null ? -1 : q.Id;
        }

        private Quality EquippedInSlot(int i)
        {
            CharacterQPossession slotPoss = FindPossession(CurSlotId(i));
            if (slotPoss == null) return null;
            CharacterQPossession eq = slotPoss.EquippedPossession;
            return eq == null ? null : eq.AssociatedQuality;
        }

        private bool SlotOnShip(int i)
        {
            return FindPossession(CurSlotId(i)) != null;
        }

        // Everything that assigns to a slot (ship gear, officers, avatar gear);
        // each slot's own Id filters this down later. NOTE: do NOT filter on
        // Quality.IsEquippable -- that is true only for character-avatar
        // categories (100-107), which excludes ALL ship weapons/engines.
        private System.Collections.Generic.List<Quality> AllGear()
        {
            if (_allGear != null) return _allGear;
            _allGear = new System.Collections.Generic.List<Quality>();
            try
            {
                System.Collections.Generic.IList<Quality> all =
                    QualityRepository.Instance.GetAllQualityContentEagerly();
                if (all != null)
                    foreach (Quality q in all)
                        if (q != null && q.AssignToSlot != null)
                            _allGear.Add(q);
            }
            catch (Exception e) { Log.LogError("AllGear build failed: " + e); }
            return _allGear;
        }

        private System.Collections.Generic.List<Quality> GearForSlot(int slotIndex)
        {
            int sid = CurSlotId(slotIndex);
            if (_gearBySlot == null) _gearBySlot = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Quality>>();
            if (_gearBySlot.ContainsKey(sid)) return _gearBySlot[sid];

            System.Collections.Generic.List<Quality> result = new System.Collections.Generic.List<Quality>();
            foreach (Quality g in AllGear())
            {
                if (g.AssignToSlot == null) continue;
                if (g.AssignToSlot.Id == sid) result.Add(g);
            }
            _gearBySlot[sid] = result;
            return result;
        }

        private void DrawTheShip()
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null || gp.CurrentCharacter == null)
            {
                GUILayout.Label("No game in progress.");
                _shipLoaded = false;
                return;
            }
            if (!_shipLoaded)
            {
                _hull = CurrentHull().ToString();
                _shipLoaded = true;
            }

            if (_selectedSlot >= 0) { DrawGearList(); return; }
            if (_selectedShip) { DrawShipList(); return; }

            Quality ship = gp.CurrentCharacter.Ship;
            GUILayout.Label("Ship: " + (ship != null ? ship.Name : "(none)") + "   (click a slot to fit gear)", _header);
            GUILayout.Space(4f);

            // Hull integrity, above the equipment.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Hull", GUILayout.Width(70f));
            GUI.SetNextControlName("hull");
            _hull = GUILayout.TextField(_hull ?? "", GUILayout.Width(90f));
            if (GUILayout.Button("Set", GUILayout.Width(50f))) ConfirmHull();
            GUILayout.Label("now " + CurrentHull());
            GUILayout.EndHorizontal();
            UnityEngine.Event e = UnityEngine.Event.current;
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) &&
                GUI.GetNameOfFocusedControl() == "hull")
            { ConfirmHull(); e.Use(); }

            GUILayout.Space(8f);

            // Paperdoll: left column (Deck/Auxiliary/Aft), centre, right column (Forward/Bridge/Engines)
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(150f));
            SlotButton(0); GUILayout.Space(6f); SlotButton(3); GUILayout.Space(6f); SlotButton(2);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(110f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button((ship != null ? ship.Name : "SHIP") + "\n\n(swap ship)",
                    GUILayout.Width(110f), GUILayout.Height(120f)))
            { _selectedShip = true; _gearScroll = Vector2.zero; }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(150f));
            SlotButton(1); GUILayout.Space(6f); SlotButton(4); GUILayout.Space(6f); SlotButton(5);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Combat-item slots (not yet editable):");
            GUILayout.BeginHorizontal();
            for (int s = 0; s < 6; s++) GUILayout.Box("-", GUILayout.Width(40f), GUILayout.Height(30f));
            GUILayout.EndHorizontal();
        }

        private void DrawShipList()
        {
            GUILayout.Label("Swap ship", _header);
            if (GUILayout.Button("< Back", GUILayout.Width(80f))) { _selectedShip = false; return; }

            System.Collections.Generic.List<Quality> ships = Ships();
            if (ships.Count == 0) GUILayout.Label("(No ships found.)");

            _gearScroll = GUILayout.BeginScrollView(_gearScroll, GUILayout.Height(300f));
            foreach (Quality s in ships)
                if (GUILayout.Button(s.Name)) { SwapToShip(s); _selectedShip = false; }
            GUILayout.EndScrollView();
        }

        private System.Collections.Generic.List<Quality> Ships()
        {
            if (_ships != null) return _ships;
            _ships = new System.Collections.Generic.List<Quality>();
            try
            {
                System.Collections.Generic.IList<Quality> all =
                    QualityRepository.Instance.GetAllQualityContentEagerly();
                if (all != null)
                    foreach (Quality q in all)
                        if (q != null && q.Category == FailBetter.Core.Enums.Category.Ship)
                            _ships.Add(q);
            }
            catch (Exception e) { Log.LogError("Ships build failed: " + e); }
            return _ships;
        }

        // Resolve the canonical Quality instance (the one the game's own systems use)
        // via WellKnownQualityProvider's private static GetById. Needed so SwapShip's
        // internal EquipThing reference-matches the ship's slot possession.
        private static Quality Canonical(int id)
        {
            try
            {
                if (_getByIdMethod == null)
                    _getByIdMethod = typeof(WellKnownQualityProvider).GetMethod("GetById",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public, null, new Type[] { typeof(int) }, null);
                if (_getByIdMethod != null) return (Quality)_getByIdMethod.Invoke(null, new object[] { id });
            }
            catch (Exception e) { Log.LogError("Canonical(" + id + "): " + e); }
            return null;
        }

        private void SwapToShip(Quality listShip)
        {
            try
            {
                GameProvider gp = GameProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null || listShip == null) return;

                Quality ship = Canonical(listShip.Id);
                if (ship == null) ship = listShip;

                gp.CurrentCharacter.AcquireQualityAtExplicitLevel(ship, 1); // possess the ship
                ShipyardProvider.Instance.SwapShip(ship, null, false);      // equips it + hull/crew/prefab/slots
                _shipLoaded = false;
                _status = "Ship -> " + ship.Name + ".";
                Log.LogInfo(_status);
            }
            catch (Exception e) { _status = "Ship swap failed: " + e.Message; Log.LogError(e); }
        }

        private void DrawTheCrew()
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null || gp.CurrentCharacter == null)
            {
                GUILayout.Label("No game in progress.");
                _crewLoaded = false;
                return;
            }
            if (!_crewLoaded)
            {
                _crew = CurrentCrew().ToString();
                _crewLoaded = true;
            }

            if (_selectedSlot >= 0) { DrawGearList(); return; }

            // Crew count, above the officers.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Crew", GUILayout.Width(70f));
            GUI.SetNextControlName("crew");
            _crew = GUILayout.TextField(_crew ?? "", GUILayout.Width(90f));
            if (GUILayout.Button("Set", GUILayout.Width(50f))) ConfirmCrew();
            GUILayout.Label("now " + CurrentCrew());
            GUILayout.EndHorizontal();
            UnityEngine.Event e = UnityEngine.Event.current;
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) &&
                GUI.GetNameOfFocusedControl() == "crew")
            { ConfirmCrew(); e.Use(); }

            GUILayout.Space(8f);
            GUILayout.Label("Officers (click a slot to assign):", _header);
            System.Collections.Generic.List<Quality> slots = OfficerSlots();
            for (int i = 0; i < slots.Count; i++) SlotButton(i);
        }

        private static int CurrentCrew()
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null || gp.CurrentCharacter == null) return 0;
            return gp.CurrentCharacter.GetUnmodifiedQualityLevel(WellKnownQualityProvider.Crew);
        }

        private void ConfirmCrew()
        {
            try
            {
                GameProvider gp = GameProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null) { _status = "No game in progress."; return; }
                int val;
                if (!int.TryParse((_crew ?? "").Trim(), out val))
                {
                    _status = "Crew: '" + _crew + "' is not a whole number.";
                    _crew = CurrentCrew().ToString();
                    return;
                }
                if (val < 0) val = 0;
                gp.CurrentCharacter.AcquireQualityAtExplicitLevel(WellKnownQualityProvider.Crew, val);
                _crew = val.ToString();
                _status = "Crew set to " + val + ".";
                Log.LogInfo(_status);
            }
            catch (Exception ex) { _status = "Crew error: " + ex.Message; Log.LogError(ex); }
        }

        // ===================== Bank panel =====================

        private static string BankFile()
        {
            return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "ManyThingsBank.txt");
        }

        private static void LoadBank()
        {
            _bank = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<int, int>>();
            try
            {
                string f = BankFile();
                if (!System.IO.File.Exists(f)) return;
                foreach (string line in System.IO.File.ReadAllLines(f))
                {
                    string[] p = line.Split('\t');
                    if (p.Length != 3) continue;
                    int id, amt;
                    if (!int.TryParse(p[1], out id) || !int.TryParse(p[2], out amt)) continue;
                    if (!_bank.ContainsKey(p[0])) _bank[p[0]] = new System.Collections.Generic.Dictionary<int, int>();
                    _bank[p[0]][id] = amt;
                }
            }
            catch (Exception e) { Log.LogError("LoadBank: " + e); }
        }

        private static void SaveBank()
        {
            try
            {
                System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
                foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.Dictionary<int, int>> kv in _bank)
                    foreach (System.Collections.Generic.KeyValuePair<int, int> it in kv.Value)
                        if (it.Value > 0) lines.Add(kv.Key + "\t" + it.Key + "\t" + it.Value);
                System.IO.File.WriteAllLines(BankFile(), lines.ToArray());
            }
            catch (Exception e) { Log.LogError("SaveBank: " + e); }
        }

        private static System.Collections.Generic.Dictionary<int, int> BankFor(Character ch)
        {
            if (_bank == null) LoadBank();
            string key = BankKey(ch);
            string legacy = LegacyBankKey(ch);

            if (!_bank.ContainsKey(key))
            {
                if (!string.IsNullOrEmpty(legacy) && _bank.ContainsKey(legacy))
                {
                    _bank[key] = _bank[legacy];
                    _bank.Remove(legacy);
                    SaveBank();
                    Log.LogInfo("Migrated Bank storage from captain name '" + legacy + "' to run key '" + key + "'.");
                }
                else
                {
                    _bank[key] = new System.Collections.Generic.Dictionary<int, int>();
                }
            }
            return _bank[key];
        }

        private static string BankKey(Character ch)
        {
            try
            {
                Sunless.Game.Entities.SunlessCharacter sc = ch as Sunless.Game.Entities.SunlessCharacter;
                if (sc != null && sc.GenerationStartDate != DateTime.MinValue)
                    return sc.GenerationStartDate.Ticks.ToString();
            }
            catch (Exception e) { Log.LogError("BankKey: " + e); }
            return LegacyBankKey(ch);
        }

        private static string LegacyBankKey(Character ch)
        {
            if (ch == null || string.IsNullOrEmpty(ch.Name)) return "default";
            return ch.Name;
        }

        // Items in the hold worth banking: possessed Things that aren't equipment or slots.
        private static System.Collections.Generic.List<Quality> Depositables(Character ch)
        {
            System.Collections.Generic.List<Quality> result = new System.Collections.Generic.List<Quality>();
            System.Collections.Generic.IList<CharacterQPossession> list = ch.QualitiesPossessedList;
            if (list == null) return result;
            foreach (CharacterQPossession p in list)
            {
                if (p == null) continue;
                Quality q = p.AssociatedQuality;
                if (q == null) continue;
                if (q.Nature != FailBetter.Core.Enums.Nature.Thing) continue;
                if (q.AssignToSlot != null) continue; // equipment
                if (q.IsSlot) continue;
                if (ch.GetUnmodifiedQualityLevel(q) <= 0) continue;
                result.Add(q);
            }
            return result;
        }

        private void DrawBank()
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null || gp.CurrentCharacter == null) { GUILayout.Label("No game in progress."); return; }
            Character ch = gp.CurrentCharacter;
            System.Collections.Generic.Dictionary<int, int> store = BankFor(ch);

            GUILayout.Label("Deposit from your hold into unlimited storage. Save your game after banking.", _header);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();

            // ----- Hold (left) -----
            GUILayout.BeginVertical(GUILayout.Width(215f));
            GUILayout.Label("Your Hold");
            _holdScroll = GUILayout.BeginScrollView(_holdScroll, GUILayout.Height(300f));
            foreach (Quality q in Depositables(ch))
            {
                int lvl = ch.GetUnmodifiedQualityLevel(q);
                GUILayout.BeginHorizontal();
                GUILayout.Label(q.Name + " x" + lvl, GUILayout.Width(120f));
                if (GUILayout.Button("All", GUILayout.Width(40f))) Deposit(ch, q, lvl, store);
                if (GUILayout.Button("1", GUILayout.Width(30f))) Deposit(ch, q, 1, store);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // ----- Bank (right) -----
            GUILayout.BeginVertical(GUILayout.Width(215f));
            GUILayout.Label("Bank");
            _bankScroll = GUILayout.BeginScrollView(_bankScroll, GUILayout.Height(300f));
            foreach (int id in new System.Collections.Generic.List<int>(store.Keys))
            {
                int amt = store[id];
                if (amt <= 0) continue;
                Quality q = Canonical(id);
                string nm = q != null ? q.Name : ("item #" + id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(nm + " x" + amt, GUILayout.Width(120f));
                if (GUILayout.Button("All", GUILayout.Width(40f))) Withdraw(ch, id, amt, store);
                if (GUILayout.Button("1", GUILayout.Width(30f))) Withdraw(ch, id, 1, store);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void Deposit(Character ch, Quality q, int n, System.Collections.Generic.Dictionary<int, int> store)
        {
            try
            {
                if (q == null || n <= 0) return;
                ch.ModifyQualityPossessed(q, -n);
                store[q.Id] = (store.ContainsKey(q.Id) ? store[q.Id] : 0) + n;
                SaveBank();
                _status = "Banked " + n + " " + q.Name + ".";
            }
            catch (Exception e) { _status = "Deposit failed: " + e.Message; Log.LogError(e); }
        }

        private void Withdraw(Character ch, int id, int n, System.Collections.Generic.Dictionary<int, int> store)
        {
            try
            {
                int have = store.ContainsKey(id) ? store[id] : 0;
                if (n > have) n = have;
                if (n <= 0) return;
                Quality q = Canonical(id);
                if (q == null) { _status = "Can't resolve banked item #" + id + "."; return; }
                ch.ModifyQualityPossessed(q, n);
                store[id] = have - n;
                SaveBank();
                _status = "Withdrew " + n + " " + q.Name + ".";
            }
            catch (Exception e) { _status = "Withdraw failed: " + e.Message; Log.LogError(e); }
        }

        private void SlotButton(int i)
        {
            Quality eq = EquippedInSlot(i);
            string label = CurSlotName(i) + "\n" + (eq != null ? eq.Name : "(empty)");
            Color prev = GUI.color;
            if (!SlotOnShip(i)) GUI.color = new Color(0.62f, 0.62f, 0.62f);
            if (GUILayout.Button(label, GUILayout.Height(46f)))
            {
                _selectedSlot = i;
                _gearScroll = Vector2.zero;
                Log.LogInfo("Slot " + CurSlotName(i) + " (id " + CurSlotId(i) + "): " +
                    GearForSlot(i).Count + " options.");
            }
            GUI.color = prev;
        }

        private void DrawGearList()
        {
            int i = _selectedSlot;
            GUILayout.Label((_tab == 3 ? "Assign: " : "Fit gear: ") + CurSlotName(i), _header);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Back", GUILayout.Width(80f))) { _selectedSlot = -1; return; }
            if (GUILayout.Button("Empty this slot")) { RemoveGear(i); _selectedSlot = -1; return; }
            GUILayout.EndHorizontal();

            System.Collections.Generic.List<Quality> options = GearForSlot(i);
            if (options.Count == 0)
                GUILayout.Label("(No gear found for this slot.)");
            else
                GUILayout.Label(options.Count + " options");

            float viewHeight = 280f;
            float rowHeight = 28f;
            int first = 0;
            if (options.Count > 0) first = ((int)(_gearScroll.y / rowHeight)) - 2;
            if (first < 0) first = 0;
            int visible = ((int)(viewHeight / rowHeight)) + 7;
            int last = first + visible;
            if (last > options.Count) last = options.Count;

            Quality chosen = null;
            _gearScroll = GUILayout.BeginScrollView(_gearScroll, GUILayout.Height(viewHeight));
            if (first > 0) GUILayout.Space(first * rowHeight);
            for (int idx = first; idx < last; idx++)
            {
                Quality gear = options[idx];
                if (GUILayout.Button(gear.Name, GUILayout.Height(24f))) chosen = gear;
            }
            if (last < options.Count) GUILayout.Space((options.Count - last) * rowHeight);
            GUILayout.EndScrollView();

            if (chosen != null) { EquipGear(i, chosen); _selectedSlot = -1; }
        }

        private void EquipGear(int slotIndex, Quality gear)
        {
            try
            {
                GameProvider gp = GameProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null || gear == null) return;
                Character ch = gp.CurrentCharacter;
                Quality slot = CurSlotQ(slotIndex);
                if (slot == null) return;

                ch.AcquireQualityAtExplicitLevel(gear, 1);          // possess the gear
                if (!ch.HasQualityAtAll(slot)) ch.AcquireQualityAtExplicitLevel(slot, 1); // ensure the slot
                try { ch.EquipThing(gear); } catch (Exception ee) { Log.LogError("EquipThing: " + ee); }

                // The content-list gear instance can differ from the canonical one the
                // game uses internally, so EquipThing's reference match may miss. Verify
                // by Id and, if needed, wire the slot's EquippedPossession directly.
                Quality now = EquippedInSlot(slotIndex);
                if (now == null || now.Id != gear.Id)
                {
                    CharacterQPossession gearPoss = FindPossession(gear.Id);
                    CharacterQPossession slotPoss = FindPossession(CurSlotId(slotIndex));
                    if (gearPoss != null && slotPoss != null) slotPoss.EquippedPossession = gearPoss;
                }

                _status = gear.Name + " -> " + CurSlotName(slotIndex) + ".";
                Log.LogInfo(_status);
            }
            catch (Exception e)
            {
                _status = "Equip failed: " + e.Message;
                Log.LogError(e);
            }
        }

        private void RemoveGear(int slotIndex)
        {
            try
            {
                GameProvider gp = GameProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null) return;
                CharacterQPossession slotPoss = FindPossession(CurSlotId(slotIndex));
                if (slotPoss == null) return;
                CharacterQPossession eq = slotPoss.EquippedPossession;
                if (eq != null && eq.AssociatedQuality != null)
                {
                    try { gp.CurrentCharacter.UnequipThing(eq.AssociatedQuality, true); }
                    catch { }
                }
                slotPoss.EquippedPossession = null;
                _status = CurSlotName(slotIndex) + " emptied.";
            }
            catch (Exception e) { _status = "Remove failed: " + e.Message; Log.LogError(e); }
        }

        private static int CurrentHull()
        {
            GameProvider gp = GameProvider.Instance;
            if (gp == null || gp.CurrentCharacter == null) return 0;
            return gp.CurrentCharacter.GetUnmodifiedQualityLevel(WellKnownQualityProvider.Hull);
        }

        private void ConfirmHull()
        {
            try
            {
                GameProvider gp = GameProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null) { _status = "No game in progress."; return; }
                int val;
                if (!int.TryParse((_hull ?? "").Trim(), out val))
                {
                    _status = "Hull: '" + _hull + "' is not a whole number.";
                    _hull = CurrentHull().ToString();
                    return;
                }
                if (val < 0) val = 0;
                gp.CurrentCharacter.AcquireQualityAtExplicitLevel(WellKnownQualityProvider.Hull, val);
                _hull = val.ToString();
                _status = "Hull set to " + val + ".";
                Log.LogInfo(_status);
            }
            catch (Exception e) { _status = "Hull error: " + e.Message; Log.LogError(e); }
        }
    }

    [HarmonyPatch(typeof(MainMenu), MethodType.Constructor,
        new Type[] { typeof(GameObject), typeof(string), typeof(List<MainMenu.ButtonData>), typeof(bool) })]
    public static class MainMenuPatch
    {
        private static void Postfix(MainMenu __instance, bool isTitleMenu)
        {
            QoLPlugin.Log.LogInfo("MainMenu ctor postfix (isTitleMenu=" + isTitleMenu + ")");
            if (isTitleMenu) return; // in-game ESC menu only
            try
            {
                __instance.AddButton("Mr Eaten's Many Things", new Action(OpenPanel));
            }
            catch (Exception e)
            {
                QoLPlugin.Log.LogError("AddButton failed: " + e);
            }
        }

        private static void OpenPanel()
        {
            if (QoLPlugin.Instance != null) QoLPlugin.Instance.Toggle();
        }
    }

    // Adds a native, port-only "Bank" tab to the Gazetteer (the port book with
    // Story/Hold/Journal/Officers/Shops/Shipyard). availableOutsidePort=false makes
    // it behave like Shops/Shipyard: visible only while docked. Clicking it opens
    // the Bank panel of the mod window.
    [HarmonyPatch(typeof(Sunless.Game.UI.Gazetteer.Gazetteer), MethodType.Constructor, new Type[] { typeof(GameObject) })]
    public static class GazetteerBankTabPatch
    {
        private static System.Reflection.FieldInfo _echoesLabelField;

        private static void Postfix(Sunless.Game.UI.Gazetteer.Gazetteer __instance)
        {
            try
            {
                __instance.AddTab("Bank", new Action(QoLPlugin.RenderBankFromTab), false);
                MoveEchoesDisplayRight(__instance);
            }
            catch (Exception e) { QoLPlugin.Log.LogError("Adding port Bank tab failed: " + e); }
        }

        private static void MoveEchoesDisplayRight(Sunless.Game.UI.Gazetteer.Gazetteer gaz)
        {
            try
            {
                if (gaz == null) return;
                if (_echoesLabelField == null)
                    _echoesLabelField = typeof(Sunless.Game.UI.Gazetteer.Gazetteer).GetField("_echoesLabel",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (_echoesLabelField == null) return;

                UnityEngine.UI.Text label = _echoesLabelField.GetValue(gaz) as UnityEngine.UI.Text;
                if (label == null) return;
                UnityEngine.UI.Button button = label.GetComponentInParent<UnityEngine.UI.Button>();
                RectTransform rt = button != null
                    ? button.gameObject.GetComponent<RectTransform>()
                    : label.gameObject.GetComponent<RectTransform>();
                if (rt == null) return;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + 90f, rt.anchoredPosition.y);
            }
            catch (Exception e) { QoLPlugin.Log.LogError("Moving Echoes display failed: " + e); }
        }
    }

    // Scales ALL hull damage the player takes. Every damage source (combat
    // attacks, ramming, terrain collisions, mines) reduces hull via
    // MoveBoat.set_Hull, so a single prefix that scales strict DECREASES
    // covers everything. Repairs (increases) and the self-sync in
    // UpdateShipQualities (set_Hull(get_Hull()), value == current) are ignored.
    [HarmonyPatch(typeof(MoveBoat), "set_Hull")]
    public static class HullDamagePatch
    {
        private static void Prefix(ref int value)
        {
            try
            {
                float f = QoLPlugin.DamageMult;
                if (f >= 1f) return; // Full = vanilla

                GameProvider gp = GameProvider.Instance;
                if (gp == null || gp.CurrentCharacter == null) return;

                int current = gp.CurrentCharacter.GetUnmodifiedQualityLevel(WellKnownQualityProvider.Hull);
                if (value >= current) return; // a repair or a no-op sync, not damage

                int loss = current - value;
                int scaledLoss = Mathf.RoundToInt(loss * f);
                value = current - scaledLoss; // f == 0 -> value == current -> invulnerable
            }
            catch (Exception e)
            {
                QoLPlugin.Log.LogError("Hull damage scaling failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(MoveBoat), "set_EngineTemperature")]
    public static class EngineHeatPatch
    {
        private static void Prefix(ref float value)
        {
            value = QoLPlugin.CapEngineHeat(value);
        }
    }

    [HarmonyPatch(typeof(NavigationProvider), "RecalculateEngineTempurature")]
    public static class EngineRecalculateHeatPatch
    {
        private static void Postfix(NavigationProvider __instance)
        {
            try
            {
                if (!QoLPlugin.DisableExplosions || __instance == null || __instance.Boat == null) return;
                __instance.Boat.EngineTemperature = QoLPlugin.CapEngineHeat(__instance.Boat.EngineTemperature);
                __instance.Boat.PeculiarNoises = 0;
            }
            catch (Exception e)
            {
                QoLPlugin.Log.LogError("Engine heat suppression failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(Sunless.Game.UI.Storylet.BranchPanel), MethodType.Constructor,
        new Type[] { typeof(GameObject), typeof(Branch), typeof(Sunless.Game.Entities.SunlessCharacter) })]
    public static class TerrorPreviewPatch
    {
        private static System.Reflection.FieldInfo _descriptionField;

        private static void Postfix(Sunless.Game.UI.Storylet.BranchPanel __instance, Branch branch, Sunless.Game.Entities.SunlessCharacter character)
        {
            try
            {
                if (!QoLPlugin.PreviewTerror || __instance == null || branch == null || character == null) return;
                string preview = TerrorPreviewFor(branch, character);
                if (string.IsNullOrEmpty(preview)) return;

                if (_descriptionField == null)
                    _descriptionField = typeof(Sunless.Game.UI.Storylet.BranchPanel).GetField("_description",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (_descriptionField == null) return;
                UnityEngine.UI.Text text = _descriptionField.GetValue(__instance) as UnityEngine.UI.Text;
                if (text == null) return;
                text.gameObject.SetActive(true);
                text.text = (text.text ?? "") + "\n" + preview;
            }
            catch (Exception e)
            {
                QoLPlugin.Log.LogError("Terror preview failed: " + e);
            }
        }

        private static string TerrorPreviewFor(Branch branch, Character character)
        {
            int? success = TerrorDelta(branch.SuccessEvent, character);
            int? failure = TerrorDelta(branch.DefaultEvent, character);
            int? rareSuccess = TerrorDelta(branch.RareSuccessEvent, character);
            int? rareFailure = TerrorDelta(branch.RareDefaultEvent, character);
            int? combined = null;

            string result = "";
            if (success.HasValue && failure.HasValue && success.Value != failure.Value)
                result = "Terror: success " + Signed(success.Value) + " / failure " + Signed(failure.Value);
            else if (success.HasValue)
                result = "Terror: " + Signed(success.Value);
            else if (failure.HasValue)
                result = "Terror: " + Signed(failure.Value);

            if (rareSuccess.HasValue)
                result += (result.Length == 0 ? "Terror: " : " / ") + "rare success " + Signed(rareSuccess.Value);
            if (rareFailure.HasValue)
                result += (result.Length == 0 ? "Terror: " : " / ") + "rare failure " + Signed(rareFailure.Value);
            if (result.Length == 0 && branch.ResultEvents != null)
            {
                foreach (FailBetter.Core.Event ev in branch.ResultEvents)
                {
                    int? delta = TerrorDelta(ev, character);
                    if (!delta.HasValue) continue;
                    if (!combined.HasValue) combined = delta.Value;
                    else combined = combined.Value + delta.Value;
                }
                if (combined.HasValue) result = "Terror: " + Signed(combined.Value);
            }
            return result;
        }

        private static int? TerrorDelta(FailBetter.Core.Event ev, Character character)
        {
            if (ev == null) return null;
            int total = 0;
            bool found = false;
            if (ev.QualitiesAffected != null)
            {
                foreach (FailBetter.Core.QAssoc.EventQEffect effect in ev.QualitiesAffected)
                {
                    if (effect == null) continue;
                    int qualityId = effect.AssociatedQuality != null ? effect.AssociatedQuality.Id : effect.AssociatedQualityId;
                    if (qualityId != WellKnownQualityProvider.Terror.Id) continue;
                    if (!EffectApplies(effect, character)) continue;
                    int delta = EffectDelta(effect, character);
                    total += delta;
                    found = true;
                }
            }
            if (ev.LinkToEvent != null)
            {
                int? linked = TerrorDelta(ev.LinkToEvent, character);
                if (linked.HasValue)
                {
                    total += linked.Value;
                    found = true;
                }
            }
            if (!found || total == 0) return null;
            return total;
        }

        private static bool EffectApplies(FailBetter.Core.QAssoc.EventQEffect effect, Character character)
        {
            int cur = character.GetUnmodifiedQualityLevel(WellKnownQualityProvider.Terror);
            if (effect.OnlyIfAtLeast.HasValue && cur < effect.OnlyIfAtLeast.Value) return false;
            if (effect.OnlyIfNoMoreThan.HasValue && cur > effect.OnlyIfNoMoreThan.Value) return false;
            return true;
        }

        private static int EffectDelta(FailBetter.Core.QAssoc.EventQEffect effect, Character character)
        {
            int cur = character.GetUnmodifiedQualityLevel(WellKnownQualityProvider.Terror);
            int parsed;
            if (!string.IsNullOrEmpty(effect.SetToExactlyAdvanced) && int.TryParse(effect.SetToExactlyAdvanced, out parsed))
                return parsed - cur;
            if (effect.SetToExactly.HasValue)
                return effect.SetToExactly.Value - cur;
            if (!string.IsNullOrEmpty(effect.ChangeByAdvanced) && int.TryParse(effect.ChangeByAdvanced, out parsed))
                return parsed;
            return effect.Level;
        }

        private static string Signed(int n)
        {
            return n > 0 ? ("+" + n) : n.ToString();
        }
    }
}
