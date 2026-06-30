# Mr Eaten's Many Things

Cheat and quality-of-life mod for **Sunless Sea**.

Author: countlessbats

Current version: **2.17.0**

## What It Does

Mr Eaten's Many Things adds an in-game ESC-menu panel with tabs for changing common captain, ship, crew, and storage values without editing save files.

Features:

- **Zee Law**
  - Adjust Terror gain while sailing from `0.0x` to `2.0x`, with `1.0x` as the centered default.
  - Adjust Hunger gain from `0.0x` to `2.0x`.
  - Adjust Fuel consumption from `0.0x` to `2.0x`.
  - Adjust incoming Hull damage from `0.0x` to `2.0x`.
  - Disable engine heat/explosions.
  - Preview exact Terror gains on storylet branch descriptions.
  - Grant Something Awaits You with a configurable hotkey.
  - Time acceleration with configurable toggle and hold keys.
- **7 Numbers**
  - Set Iron, Mirrors, Pages, Hearts, and Veils.
  - Set Hunger, Terror, and Wounds.
  - Set Echoes.
- **The Ship**
  - Set Hull.
  - Swap to any ship.
  - Equip and unequip valid ship equipment in Deck, Forward, Aft, Auxiliary, Bridge, and Engines slots.
- **The Crew**
  - Set Crew.
  - Assign and remove officers from officer slots.
- **Bank**
  - Unlimited persistent item storage.
  - ESC-menu banking UI.
  - Native port-only Gazetteer Bank tab.
  - Cargo and Curiosities sections.
  - Storage is keyed by a per-run captain id rather than captain name, so names are not reserved forever.

## Installation

Use the release zip from GitHub or Nexus Mods.

1. Close Sunless Sea.
2. Open your Sunless Sea install folder. For Steam this is usually:

   ```text
   C:\Program Files (x86)\Steam\steamapps\common\SunlessSea
   ```

3. Extract the release zip directly into that folder.
4. Confirm these files exist:

   ```text
   SunlessSea\winhttp.dll
   SunlessSea\doorstop_config.ini
   SunlessSea\BepInEx\core\BepInEx.dll
   SunlessSea\BepInEx\plugins\SunlessQoL.dll
   ```

5. Start the game.
6. Load a save, press `Esc`, and click **Mr Eaten's Many Things**.

If you already have BepInEx installed, you can install only:

```text
BepInEx\plugins\SunlessQoL.dll
```

## Compatibility

- Built for the current Steam Windows build of Sunless Sea.
- Uses BepInEx 5.
- Sunless Sea is a Unity 5.5 / CLR 2.0 game, so the plugin targets .NET Framework 3.5 and intentionally uses old C# syntax.

## Building From Source

Requirements:

- Windows.
- Sunless Sea installed.
- BepInEx 5 installed in the Sunless Sea folder.
- .NET Framework 3.5 compiler at:

  ```text
  C:\Windows\Microsoft.NET\Framework64\v3.5\csc.exe
  ```

Build:

```powershell
.\scripts\build.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\SunlessSea"
```

The compiled plugin is written to:

```text
dist\SunlessQoL.dll
```

Install after building:

```powershell
Copy-Item .\dist\SunlessQoL.dll "C:\Program Files (x86)\Steam\steamapps\common\SunlessSea\BepInEx\plugins\SunlessQoL.dll" -Force
```

The game must be closed while replacing the DLL.

## Third-Party Components

The source repository does not include BepInEx or game assemblies. Release zips may include BepInEx as a convenience drop-in installer.

BepInEx, Harmony, Mono.Cecil, MonoMod, and Doorstop/UnityDoorstop are third-party components with their own licenses. See `THIRD_PARTY_NOTICES.md` in this repository and the notices included in release zips.

## License

The Mr Eaten's Many Things source code is released under the MIT License. See `LICENSE`.

Sunless Sea is copyright Failbetter Games. This project is an unofficial fan mod and is not affiliated with or endorsed by Failbetter Games.
