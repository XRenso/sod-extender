# ExampleMod
Minimal code mod for the SOD Extender.

Setting `General / EnabledLogLine` (bool, default true): write a log line on load.
The file is `BepInEx/config/SodMods/ExampleMod.cfg`; it is outside the mod folder, so Workshop updates keep it.

Save data: `SaveData.Get/Set` counts game starts in the current save (key `GameStarts`).
The extender stores it in a `.sodmods` file next to the save file, so it follows the save.

## Layout
- `src/Mod.cs`: the `SodMod` subclass, a thin adapter over game and extender API.
- `src/Rules/`: pure rules (no game types). Offline tests: `tests/offline/`, run with `py dev/tools/sod.py unit --filter ExampleMod`.
