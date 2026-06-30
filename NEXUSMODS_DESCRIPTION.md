# Nexus Mods Description

## Short Description

Mr Eaten's Many Things is an in-game cheat and quality-of-life panel for Sunless Sea, with controls for zee-law rates, stats, menaces, Echoes, ship setup, crew/officers, and an unlimited persistent Bank.

## Main Description

Mr Eaten's Many Things adds a new button to the in-game ESC menu. Open it to adjust common captain, ship, crew, and storage values without editing save files.

This is a BepInEx 5 plugin for Sunless Sea. The main download is a drop-in package with BepInEx included for convenience, so a normal install is just "extract into the Sunless Sea folder."

### Features

**Zee Law**

- Change Terror gain while sailing.
- Change Hunger gain.
- Change Fuel consumption.
- Change incoming Hull damage.
- Time acceleration with configurable toggle and hold keys.

**7 Numbers**

- Set Iron.
- Set Mirrors.
- Set Pages.
- Set Hearts.
- Set Veils.
- Set Hunger.
- Set Terror.
- Set Wounds.
- Set Echoes.

**The Ship**

- Set current Hull.
- Swap to any ship.
- Equip valid ship gear into Deck, Forward, Aft, Auxiliary, Bridge, and Engines slots.
- Empty ship equipment slots.

**The Crew**

- Set current Crew.
- Assign officers into officer slots.
- Remove officers from officer slots.

**Bank**

- Unlimited persistent item storage.
- Deposit and withdraw from the mod's ESC-menu Bank tab.
- Native port-only Gazetteer Bank tab.
- Cargo and Curiosities sections.
- Click an item in your hold to deposit all of it.
- Click an item in the bank to withdraw all of it.
- Bank data is keyed by a per-run captain id rather than the captain's name, so captain names are not permanently reserved.

### Installation

1. Close Sunless Sea.
2. Download the main file.
3. Open your Sunless Sea install folder.

   For Steam, this is usually:

   `C:\Program Files (x86)\Steam\steamapps\common\SunlessSea`

4. Extract the zip directly into the `SunlessSea` folder.
5. Confirm that these files exist:

   `SunlessSea\winhttp.dll`

   `SunlessSea\doorstop_config.ini`

   `SunlessSea\BepInEx\core\BepInEx.dll`

   `SunlessSea\BepInEx\plugins\SunlessQoL.dll`

6. Start Sunless Sea.
7. Load a save, press `Esc`, and click `Mr Eaten's Many Things`.

### If You Already Have BepInEx

If BepInEx 5 is already installed, you can install only:

`BepInEx\plugins\SunlessQoL.dll`

The full zip is still safe to use, but if you have a custom BepInEx setup, back it up first.

### Uninstallation

Delete:

`BepInEx\plugins\SunlessQoL.dll`

Optional: remove BepInEx itself by deleting:

`BepInEx`

`winhttp.dll`

`doorstop_config.ini`

Do not delete BepInEx if other mods depend on it.

### Compatibility

- Windows Steam build of Sunless Sea.
- BepInEx 5.
- Built for Unity 5.5 / CLR 2.0, targeting .NET Framework 3.5.

### Building From Source

Source is available on GitHub:

https://github.com/sevrlbats/Mr-Eatens-Many-Things

Build requirements:

- Windows.
- Sunless Sea installed.
- BepInEx 5 installed in the Sunless Sea folder.
- .NET Framework 3.5 compiler at `C:\Windows\Microsoft.NET\Framework64\v3.5\csc.exe`.

Build command:

`.\scripts\build.ps1 -GamePath "C:\Program Files (x86)\Steam\steamapps\common\SunlessSea"`

### Bundled BepInEx Notice

The main release zip includes BepInEx for convenience. BepInEx is third-party software licensed under LGPL-2.1. Third-party notices and the BepInEx license are included in the release zip.

This mod is unofficial and is not affiliated with or endorsed by Failbetter Games.
