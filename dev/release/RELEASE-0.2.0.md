# SOD Extender 0.2.0

One-time install that lets Shadows of Doubt load **code mods from the Steam Workshop**. After this install, a code mod is a normal Workshop subscription and shows up in the game's own Mods menu. You never install BepInEx by hand.

## Install
1. Download `SodExtender-0.2.0.zip` below.
2. Steam → Shadows of Doubt → Manage → Browse local files. Unpack the whole zip there.
3. Start the game. The first start takes 1–2 min longer and needs the internet once (BepInEx downloads Unity libraries and builds wrappers). This repeats once after each game update.

Uninstall and troubleshooting: `SodExtender-README.txt` in the zip.

## What it does for mods
- Finds mods with a `Code/` folder through the game's own mod list (enabled state and load order come from the game).
- Isolates failures: a mod that breaks on load or throws at run time is turned off; other mods keep working.
- Reports methods patched by more than one mod.
- Shared API (`SodMod`): game events (game started, game ended, hour, day, pause), per-mod settings, per-save mod data next to the save file.
- "SOD Extender" button on the Mods screen: mod status, errors, overlaps, settings.

## Safety
Code mods run with no sandbox and with your user rights. Install only mods you trust.

## Tested on
Shadows of Doubt v42.04 (Steam build 24616330), Windows. BepInEx 6.0.0-be.788 IL2CPP.

## Licences
SOD Extender: MIT. Bundled BepInEx and the .NET runtime keep their own licences: see `THIRD-PARTY-NOTICES.txt`. Unity libraries are not included.
