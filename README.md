# DiveOrDieCoop

An experimental multiplayer/co-op mod for **DIVE or DIE: Children of Rain**, built using BepInEx 5, Harmony, and Steamworks.NET. Includes Steam lobby support and LAN networking code.

> **Project status:** Early development (v0.0.1). Multiplayer synchronization is a work in progress; expect bugs and desyncs. Enemies are NOT synced.

## Features

- **Host / Join** entry points from the game's main menu
- Steam lobby discovery and friends-list invites
- LAN transport and host discovery implementation
- Cooperative synchronization systems for diving, combat, hub interactions, loot, and various world objects
- In-game debug overlay

Features listed above describe implemented systems, **not a guarantee that all gameplay is fully synchronized**.

## Requirements

- Windows x64, Steam, and a legitimate installation of DIVE or DIE: Children of Rain
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) **Mono x64** (`5.4.x`; not IL2CPP)
- For building: .NET SDK capable of targeting .NET Framework 4.8, plus the game's managed assemblies and BepInEx assemblies

## Installation

1. Install BepInEx 5 (Mono x64) into the game directory and launch the game once.
2. Obtain a compiled mod release (source archives are not directly installable).
3. Place `DiveOrDieCoop.dll` in `<Game>/BepInEx/plugins/`.
4. Ensure `Steamworks.NET.dll` is available to the mod (normally from the game's managed assemblies; some distribution packages may provide a compatible copy).
5. Start the game through Steam and look for **CO-OP** on the main menu.

To host, select **CO-OP → HOST**, then invite a friend through the Steam overlay. To join, use **CO-OP → JOIN** or an available Steam invite.

## Build from source

Open a Windows command prompt with `dotnet` on PATH:

```bat
Build.bat "D:\Steam\steamapps\common\DIVE or DIE - Children of Rain"
```

Or define `GAME_ROOT` and run `Build.bat` without arguments. The build outputs to `artifacts/Release/` and **does not modify your game installation**. To deploy manually, copy `artifacts/Release/DiveOrDieCoop.dll` to your game’s `BepInEx/plugins` directory.

The `.csproj` uses references from `GameManagedDir` and `BepInExCoreDir`. Advanced usage:

```bat
dotnet build DiveOrDieCoop.csproj -c Release -p:GameManagedDir="C:\path\to\Game_Data\Managed" -p:BepInExCoreDir="C:\path\to\BepInEx\core"
```

The game and third-party DLLs are **not included** in this source repository. Build output is not a ready-made redistributable release package.

## Project structure

| Folder | Purpose |
|---|---|
| `Diving/` | Diver movement, combat and diving state synchronization |
| `Hub/` | Hub interactions, actions and loot synchronization |
| `Networking/` | Transport interfaces, Steam/LAN implementations and protocol messages |
| `Patches/` | Harmony patches for co-op gameplay |
| `UI/` | Co-op menus and debug interface |
| `Plugin.cs` | BepInEx plugin entry point and initialization |

## Troubleshooting

| Symptom | What to check |
|---|---|
| No CO-OP menu | Confirm BepInEx loaded and the plugin DLL is in `BepInEx/plugins/` |
| Steam initialization fails | Start the game from Steam; ensure the Steam client is logged in |
| Can't find host | Confirm the host has started a lobby, and both players run compatible mod/game versions |
| Game crashes or desyncs | Consult `BepInEx/LogOutput.log`; file an issue with reproduction steps and relevant logs (remove personal data) |
| Build reports missing assembly | Check the game install path and the managed/BepInEx DLL directories |

## Contributing

Issues and pull requests are welcome. Include the game version, mod commit/version, reproduction steps, expected/actual behavior, and relevant sanitized logs. Keep contributions focused; synchronization changes are especially helpful when tested with host and client.

## Disclaimer

Unofficial fan project; not affiliated with or endorsed by the game's developers, Steam, or BepInEx. This repository does not distribute game files.

**License:** No license is currently granted for reuse or redistribution of the source. A license can be added by the maintainer when appropriate.
