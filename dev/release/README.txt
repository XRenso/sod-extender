SOD Extender - one-time install for Shadows of Doubt code mods
==============================================================

INSTALL (once)
1. Steam > Shadows of Doubt > right click > Manage > Browse local files.
2. Unpack this whole archive into that folder (next to "Shadows of Doubt.exe").
3. Start the game from Steam. The FIRST start takes 1-2 minutes longer and needs the internet
   once: BepInEx downloads Unity libraries and builds wrappers for this game version.
   This repeats once after each game update.
4. Subscribe to code mods on the Steam Workshop as usual. Turn them on in the game's Mods menu.
   The "SOD Extender" button on the Mods screen shows which mods loaded and which failed.

SAFETY
Code mods run with no sandbox and with your user rights, like any program. Install only mods
you trust.

TURN OFF FOR ONE SESSION
In the game folder, open doorstop_config.ini and set   enabled = false

UNINSTALL
Delete from the game folder: winhttp.dll, doorstop_config.ini, .doorstop_version,
the folders BepInEx and dotnet, and this README. Then in Steam: Properties > Installed Files >
Verify integrity. Your saves are not touched. Mod settings are in BepInEx/config/SodMods;
mod save data are *.sodmods files next to your saves.

PROBLEMS
Logs: BepInEx/LogOutput.log (game folder) and the extender report:
%USERPROFILE%/AppData/LocalLow/ColePowered Games/Shadows of Doubt/SodExtender/report.txt
