# SOD Extender v1

## Goal
A player installs the extender once, like REPENTOGON for Isaac. After that, every code mod installs by a normal Steam Workshop subscription and appears in the game's own Mods menu. All our mods share one base, so they do not conflict and need little boilerplate.

Decided by the user on 2026-11-06: a separate one-time download is acceptable. A manual BepInEx install and an installer step per mod are not.

## How it works
1. **Install once.** The player unpacks the extender archive into the game folder. It holds BepInEx (engine, not shown to the player), the .NET runtime and `SodExtender.dll`.
2. **Mod format.** A normal game mod folder: `ModSettings.txt`, optional game data, and a `Code/` folder with one mod DLL plus its dependencies. The game ignores `Code/`.
3. **Discovery.** A postfix on `ModLoader.GetModsComplete` reads `sortedModsList`. So the game's menu controls enabled state and load order. Local and Workshop mods take the same path (`ModSettingsData.GetContentDirectory()`).
4. **Load.** For each enabled mod with `Code/`: load the DLLs, apply its `[HarmonyPatch]` classes, create its `SodMod` subclass, call `OnLoad`.
5. **Isolation.** A failure in one mod (missing method after a game update, exception in `OnLoad`) removes that mod's patches, marks it Failed, and the next mods still load.
6. **Conflicts.** After loading, every method patched by 2+ mods goes to the report.
7. **Report.** `LocalLow/.../SodExtender/report.txt` and the BepInEx log.

## Status 2026-11-06: M1 works locally
Verified in the real game with a loaded save (11/11 tests): local mods are found through the game's list, a disabled mod stays off, a Harmony patch on `Player.Update` runs, a failure in `OnLoad` or a missing method turns off only that mod and removes its patches, a mod that throws at run time goes off after 3 errors while other mods on the same method keep running, overlaps go to the report.

Clean install verified on 2026-11-06: with BepInEx and the runtime moved away, `SodExtender-0.1.0.zip` (36 MB, 295 files) unpacked into the game folder. The first launch generated the interop in 68 s from the bundled `unity-libs`. A mod folder in the Workshop layout loaded, its patch ran in gameplay, with no errors in the log. SOD.Common is not needed. Limit of this check: the dev bridge was added to keep the game silent and to drive it, and the game was windowed.

Workshop path verified on 2026-11-06 with test item `3834680272` (ExampleMod). Upload accepted the DLL in `Code/`. After the subscription Steam put it in `steamapps/workshop/content/986130/3834680272/Code/`. The game listed it as `steamWorkshop` with the Workshop title as its name, `GetContentDirectory()` returned that folder, the extender loaded it, and its patch ran in gameplay.

Facts from this check:
- The game has no `SteamWorkshopController` in any scene. Its upload code is used by an external tool. We upload with `dev/tools/workshop/ws_game_upload.py`, which creates that class on a temporary object and calls `UploadContent`.
- `UploadContent` has no visibility option. The bridge's `Api.ForcePrivateWorkshopUploads()` forces Private before each submit (the log shows "forced Private: True"). The user confirmed on the Steam page that the item is Private.
- Steam API results cannot be read with `GetAPICallResult`: the game's dispatcher takes them first. A `CallResult` with a managed delegate also fails, because IL2CPP rejects these structs in delegates. So use the game's own result handlers.
- Workshop mods load on the second mod listing (at the menu), not on the first (at the title screen). A patch on code that runs before that point misses its first call.

Open: the developer's external upload tool with DLLs in `Code/` (not tried); a run by a real player (normal launch, with sound, no bridge).

## Verified game facts (build 24616330, v42.04)
- Local mods: `%USERPROFILE%/AppData/LocalLow/ColePowered Games/Shadows of Doubt/Mods/<Folder>/`.
- A folder is a mod only if it has `ModSettings.txt`. `Content/` is not needed. For a local mod, `GetContentDirectory()` returns the folder itself.
- The game does not expose a code-loading path: no `.lua`, no script runtime, no DLL load from mods (names and metadata strings only; bodies are not readable).

## Not verified yet
- An install on another PC (the clean-install check ran on this PC, with this game copy).
- Licences of BepInEx and its parts before public release.

## Milestones
- **M1 (done):** discovery, load, isolation, conflict report, example mod, install archive.
- **M2:** in-game panel with mod status, game events, per-mod settings and save data on the shared API.
- **M3:** one-file distribution, public release. The Workshop test item is done.

### M2 decisions (user, 2026-11-08)
- Order: M2 complete, then M3. M2 parts in order: game events → per-mod settings → save data → status panel.
- Status panel: a button in the game's own Mods menu (not an overlay window).
- Save data: the user allows a test save to verify it, under these conditions: a NEW save slot only, never overwrite an existing save; back up the whole Save folder before and compare after; delete only that test slot afterwards.
- Mod save data lives NEXT TO the save file in the Save folder (user, 2026-11-08), so it follows the save to another PC / Steam Deck through Steam Cloud. The rule "never write to the Save folder" is for Claude during dev, not a product rule. Open check: Steam Auto-Cloud lists only `Save/*.sodb` and `Cities/*` in `remotecache.vdf`; whether it syncs an extra file or a subfolder next to the save is not known. Verify with the test save (does the mod file appear in `userdata/<id>/986130/remotecache.vdf`). Also handle: save deleted → delete its mod data; save copied/renamed by the game.

## Security
Code mods run with no sandbox and with the player's rights. The extender page and each release must say this.

## M2 events (part 1, 2026-11-08)
API: `SodMod.OnGameStarted(bool isNewGame)`, `OnMainMenu()`, `OnHourChanged(int)`, `OnDayChanged()`, `OnPauseChanged(bool)`; also static events on `SodEvents`. Code: `extender/SodExtender/Events.cs`. Dispatch goes through `Events.Dispatch`: each Loaded mod in load order, try/catch per call. A throw is charged by `PatchGuard.ReportError` in the same 3-error budget as patches; turn-off runs from `Tick`. Failed mods get no events. Pure rules: `TimeTracker`, `PauseTracker`.

Verified in the game (tests `EventTests.*`):
- `MurderController.OnStartGame` postfix fires once after `sod.py load`. `RestartSafeController.newGameLoadCity`, `.generateNew` and `CityConstructor.generateNew` are all false after a save load.
- `SessionData.PauseGame` / `ResumeGame` postfixes fire; repeated calls give one event per real change.
- Hour: `floor(SessionData.decimalClock)` diffed in a `SessionData.Update` postfix. Day: key from `monthInt`, `dateInt`, `dayInt`. `SessionData.SetGameTime(gameTime + 1, leapYearCycle)` fires an hour event, `+25` fires a day event (gameTime is in hours).
- A throwing event handler: 3 errors turn the mod off; the other mod keeps getting events.

Not verified:
- `isNewGame = true`: VERIFIED 2026-11-17 for a new game in an existing city (Casablanca Quarter): `newGameLoadCity` true, `GameStarted:True` once, ExampleMod saw empty save data. The extender now also counts `generateNew` (fresh city generation, not tested: it writes to Cities/). Note: `startedGame` becomes true BEFORE `OnStartGame` — a new game waits on the tutorial popup first; `GameStarted` fires after it.
- `OnMainMenu`: logic is `startedGame` true to false in `ExtenderBehaviour.Update`. No safe way to return to the menu without a save was found, so it never ran in a test.
- Pause state after the game start/menu is assumed "not paused" (tracker starts false).
- Real (not forced) hour passing at normal speed; only `SetGameTime` jumps were tested.

## M2 settings (part 2, 2026-11-08)
API: `SodMod.Config` (`BepInEx.Configuration.ConfigFile`), `SodMod.Setting<T>(key, default, description, section = "General")` (wraps `Config.Bind`), `ModRecord.Config` (same object; null for Failed mods). File: `BepInEx/config/SodMods/<safe name>.cfg`, outside the mod folder so Workshop updates keep it. Code: `ModConfig.cs` (pure `SafeFileName`, `PathFor`), `Extender.LoadMod`. The ConfigFile is created before `OnLoad`. If the mod fails, `record.Config` is set to null and `mod.Config` is dropped; the .cfg file stays.
For the panel: enumerate `Config.Keys` (`ConfigDefinition` Section/Key) and `Config[def]` (`ConfigEntryBase`: `BoxedValue`, `Description`, `SettingType`); set `BoxedValue`.

Verified in the game (tests `ExtenderTests.TestSafeFileName`, `TestSettingFileHasDefault`, `TestSettingFileMatchesValue`, `TestSettingChangeIsSaved`, `TestFailedModHasNoConfig`):
- `SaveOnConfigSet` is true by default (decompiled `BepInEx.Core.dll`); a value change appears in the .cfg at once.
- The .cfg is written with the default on first load; ExampleMod binds `General/EnabledLogLine`.
- By hand: edited the file to `false`, restarted with `reload`: the loaded value was `False` and the log line was not written.
- 22/22 in-game tests pass.

Not verified:
- Changing a value while the game runs, then a restart (the test restores the value; only the file content was checked, plus a manual file edit for the restart).
- Two mods with names that sanitize to the same file name share one file (not handled).
- A mod that binds a setting then fails: .cfg keeps the entry; not tested with a fixture.
- Editing the .cfg by hand while the game runs does not reload (no `ConfigReloaded` watcher).
- Custom types and `AcceptableValueRange` in the panel.

## M2 save data (part 3, 2026-11-08)
API: `SodMod.SaveData` (`ModSaveData`: `Set<T>(key, value)`, `Get<T>(key, fallback)`, `Has`, `Remove`, `Clear`, `Count`), `virtual SodMod.OnBeforeSave()`. Values are JSON (System.Text.Json from `dotnet/`). Each mod sees only its own section. Code: `extender/SodExtender/SaveData.cs`. ExampleMod counts game starts per save (`GameStarts`).
File: for save `X.sodb` or `X.sod` the file is `X.sodmods` in the same folder (`SavePaths.ToModsPath`, last extension only; other extensions give null and nothing is written). Format `{ "format": 1, "mods": { "<Mod>": { "<key>": value } } }`. Write is atomic (`X.sodmods.tmp`, then `File.Replace`/`Move`). If every section is empty the file is deleted (no stale data after an overwrite). Unknown format or corrupt file: logged, treated as empty (the next save overwrites it).
Hooks (thin Harmony patches over public `SaveStore.OnLoad/OnSave/OnDeleted`, which never throw):
- Load: prefix `CityConstructor.StartLoading`. If `RestartSafeController.loadSaveGame`, path = `saveStateFileInfo.FullName`; otherwise (new game) data is cleared. Runs before `OnGameStarted`. Sections of mods that are not installed are kept in memory and rewritten on the next save.
- Save: prefix `SaveStateController.CaptureSaveStateAsync(path, isOverwrite)`: `'/'` becomes `'\'`, every Loaded mod gets `OnBeforeSave()` (a throw goes to `PatchGuard.ReportError`), then the file is written. So the mod file is written BEFORE the game finishes its own save; if the game save fails, the mod file may be newer than the save. No `OnAfterSave`: the method is async and no completion point was verified.
- Delete: prefix on `MainMenuController.DeleteSave` remembers `selectedSave.info.FullName`; postfix calls `OnDeleted`, which deletes the `.sodmods` only if the save file is gone. If DeleteSave finishes later (async), the file stays orphaned.

Verified in the game (tests `SaveDataTests.*`, temp folder `persistentDataPath/SodExtender/test-saves`, never `Save`; 28/28 pass with the old 22): path mapping (.sod, .sodb, upper case, dots, spaces, Cyrillic, unknown ext); round trip with Cyrillic names, string/list/int, wrong-type fallback; new game and missing file give empty data; unknown section survives a save; throwing `OnBeforeSave` is charged (mod turned off after 3), file still written for others; `OnDeleted` only when the save is absent; corrupt/unknown-format file does not throw. The load prefix ran on a real save load (`Победа.sodb`): only a read, no file created in `Save`, ExampleMod saw an empty section ("started 1 time").

Waits for the real test save:
- The real `path` string passed to `CaptureSaveStateAsync` (absolute or relative, with or without "b", slash style). The mapper handles .sod/.sodb, but the folder of a relative path is unchecked.
- That `StartLoading` prefix sees the right `saveStateFileInfo` for a normal load and that a save made by the game and loaded again returns the data.
- That the Harmony prefix on an `async Task` method works as a prefix in IL2CPP (patch applies without error; not yet called by the game).
- Steam Cloud: whether `X.sodmods` appears in `remotecache.vdf` and syncs.
- The real `DeleteSave` (sync or not, `selectedSave` still set in the prefix).
- Rename/copy of a save by the game (the mod file does not follow).

### Real test save (2026-11-08, user-approved)
- Called `SaveStateController.CaptureSaveStateAsync("<persistentDataPath>/Save\SodExtenderTest.sod", false)` in gameplay (save `Победа` loaded). With `useSaveGameCompression = true` the game wrote `SodExtenderTest.sodb`; the extender prefix fired and wrote `SodExtenderTest.sodmods` next to it (ExampleMod `GameStarts: 1`, TestSaveData `answer: 42`).
- Loading `SodExtenderTest` read the file back: ExampleMod counter became 2 after `OnGameStarted`.
- **Steam Cloud syncs `.sodmods`:** after the game closed, `remotecache.vdf` listed both `Save/SodExtenderTest.sodb` and `Save/SodExtenderTest.sodmods`.
- The 4 user saves stayed byte-identical to the backup. The test slot was deleted by hand; the cloud removal on the next sync is not verified.
- Not verified: the game's own `DeleteSave` hook (needs UI selection of a save; skipped as risky), the menu save path (`MainMenuController.SaveGame`), save rename/copy.
- Boot hangs: 2 of 3 launches today hung with SOD.Common 2.1.5 in `BepInEx/plugins` (once before Unity log, once right after UniverseLib registration, white screen). Without it the launch took 18 s. SOD.Common is parked in `dev/build/parked/SODCommon`. Not proven as the cause (3 launches only).

### Idea, later (user, 2026-11-08): support for SOD.Common / BepInEx plugin mods
Recommended shape: a separate optional layer, not built into the core. E.g. the extender loads a `BasePlugin` DLL from a Workshop `Code/` folder, and SOD.Common itself ships as a Workshop item that such mods depend on. Keeps the core small and free of SOD.Common's boot risk. Do after M3; first prove or clear SOD.Common as the cause of the boot hangs.

## M2 panel (2026-11-08, partly verified)
- Code: `StatusPanel.cs`, `PanelText.cs`; flag `BepInEx/config/sod.extender.cfg` `[Panel] Enabled`, default false (left false on disk).
- Built: `StatusPanel.Tick` (from `ExtenderBehaviour.Update`, once per second) finds the Mods screen and clones the game's `ButtonArea/Back` as "SOD Extender" (persistent listeners off, `useAutomaticText` off, `UnityAction` listener). The panel is a clone of `ModsPanel`; the game's children are hidden while open and restored on Back. Rows are TMP with the game font; bool settings are cloned buttons that toggle `BoxedValue`.
- Verified in the menu with 3 fixtures: `PanelTests` 4/4 pass, a real `OnPointerClick` opens it. Screenshot `dev/build/screenshots/m2_panel4.png`.
- Not done: full suite (28 + 4) not run because the game hung; design default ON only after the suite passes; cosmetic highlight on the cloned button.
- Facts: `OnOpenModMenu()` and `modsButton.onClick.Invoke()` do not open the Mods screen; `MainMenuController.SetMenuComponent(MainMenuController.Component.mods)` does. Screenshots lag ~3 s. Il2Cpp `as RectTransform` fails, use `GetComponent`. `Transform` is not enumerable.
- OnMainMenu: no safe exit-to-menu method (no menu button for it). Candidate: loading a save from gameplay; `SessionData` may be recreated, so the true→false watch may miss it. Not tried.

## Boot / load hangs (2026-11-08, OPEN, next task)
3 hangs in ~10 launches today, each at a different point: (1) before Unity (empty Player.log) with SOD.Common present; (2) white screen right after SOD.Common's UniverseLib registration; (3) SOD.Common parked, 10 fixture mods deployed, during `load "Победа"` (last log line "Setting intial Position", no crash trace). The first hang also happened to the events implementer earlier (one-off then). Not explained by SOD.Common alone. Investigate before more in-game work: read-only first (logs, extender per-frame work: SessionData.Update postfix, StatusPanel.Tick, Events.Tick, PatchGuard finalizers; doorstop/Steam), then a minimal launch matrix.

Launch matrix (2026-11-08, afternoon, user-approved; deployed extender = 12:10 build, save `Победа` loaded only, no save written): **0 hangs in 11 launches.**
- Series 1, fixtures without TestThrowPatch/TestThrowEvent, `menu` + `load` ×3: all OK (in game in 31–32 s).
- Series 2, ExampleMod + TestOverlap + TestThrowPatch, `menu` + `load` ×3: all OK. The run-time unpatch of `Player.Update` happened in the same order as in hang 3 (turn-off, then "Setting intial Position"). Hypothesis "unpatch during load hangs" not confirmed. Note: ExampleMod in this series was a newer build from the offline-tests session (TypeLoadException `IKeyValueStore` vs the old deployed extender), so its own behaviour differed.
- Series 3, SOD.Common back in `plugins`, no mods, start only ×5: all OK (17–21 s). SOD.Common restored to `dev/build/parked/SODCommon` after.
- 0/11 vs 3/~10 earlier (p ≈ 0.02 at a 30 % rate): the morning conditions differed. Candidates not tested: user input in the game window, heavy `test`/`exec` sessions before the hang, parallel agents building or deploying while the game ran. Read-only suspects in the bridge (not tested): `Screen.SetResolution` every second while the size is not 1280×720, `MuteAllEvents` from the first frame (may start FMOD before the game).
- Evidence capture (added, verified with one launch): `sod.py kill` and `launch` copy LogOutput.log, ErrorLog.log, Player.log, heartbeat and a meta file (plugins, local mods) to `dev/build/launch-logs/<BepInEx log time>/` (last 30 kept). The bridge heartbeat is now `utc frame= t= scene= startedGame=`. `sod.py status` prints the heartbeat age first and no longer dies when the bridge is silent. Triage of the next hang: heartbeat age grows with a live process = main thread blocked at that frame/scene; heartbeat fresh but no progress = a load stuck in game logic.
- **Hang 4 (2026-11-08 16:23, reproduced, evidence in `dev/build/launch-logs/20261108-162307`).** Full suite setup: new extender build (offline-tests refactor), ALL 10 fixtures, panel flag true, `menu` + `load "Победа"`. Same point as hang 3: TestThrowPatch turned off (unpatch of `Player.Update`), overlap warning, "Setting intial Position", then nothing. Heartbeat stopped at frame 6174, scene Main, startedGame=False and was 345 s old with a live process: **the main thread is blocked** (not a slow load). Reported by the user ("игра зависла"); whether the user touched the window is not known.
- So: hang with all fixtures 2 of 2 (hang 3, hang 4); with ExampleMod + TestOverlap + TestThrowPatch 0 of 3; without TestThrowPatch/TestThrowEvent 0 of 3. The trigger needs TestThrowPatch's unpatch plus something from the other fixtures (prime suspect TestThrowEvent: a second turn-off/unpatch in the same window; also TestEvents/TestSaveData/TestThrowSave patches). Next: bisect with launches (all minus TestThrowEvent; TestThrowPatch + TestThrowEvent only).
- Bisect (2026-11-08 16:30–16:45, user-approved): series A, all 10 fixtures, panel flag **false**: A1 OK, A2 hung at the same point (heartbeat stopped, scene Main, startedGame=False; logs `launch-logs/20261108-163633`). So the panel is not the cause. Series B, all fixtures minus TestThrowEvent: 0 of 3.
- TestThrowEvent only throws in `OnPauseChanged`; in the hung and the good runs it never threw before "Setting intial Position". So its code is not in the hang path; its presence only changes timing (one more assembly and mod). The B result is likely chance (small sample).
- Tally of load hangs: every one (hang 3, hang 4, A2) came right after a run-time turn-off that unpatched `Player.Update` at the start of a save load. Loads with that unpatch: 3 hangs of 11. Loads without it: 0 of 6 (+ 6 start-only launches). Working hypothesis: unpatching a hot method while the save load starts can block the main thread; timing-dependent.
- Fix idea (not built): defer the unpatch of a turned-off mod until the game is stable (in gameplay some seconds after `startedGame`, or idle in the menu); until then the finalizer keeps swallowing that mod's exceptions without logging. Behind a flag OFF by default; verify with full fixtures ×5+ loads.

## Offline tests (2026-11-08, prototype)
Goal: mechanics tests in milliseconds, with no game launch, repeatable (like the Isaac setup). A full game simulation offline is not possible: under IL2CPP the interop method bodies are stubs, so no game method runs outside the game process. So mod rules are plain C# behind thin adapters, and only the rules are tested offline.

Shape:
- **Pure code** = files with no game, Unity, BepInEx or Harmony types: `extender/SodExtender/Pure/` and `mods/<Name>/src/Rules/`. The same files compile into the shipped DLLs and, as linked source, into the test project.
- **Interfaces** (`Pure/Abstractions.cs`): `IKeyValueStore` (implemented by `ModSaveData`), `IGameClock`, `IModLog`, `IModEvents`. Real adapters: `GameAdapters.Clock` (reads `SessionData`), `GameAdapters.Log(SodMod.Log)`. Fakes: `dev/tests/offline/Fakes` (`FakeClock`, `FakeLog`, `FakeGame` + `EventRecorder`). `ModSaveData` is pure, so tests use the real one.
- **FakeGame** drives `IModEvents` with the extender's own `TimeTracker`/`PauseTracker`, so the event rules (first frame silent, one event per real change, reset on load) are the real ones. It does not model game timing, other mods or `PatchGuard`.
- **Guard:** `dev/tests/offline` has its own `Directory.Build.props`, which stops the root one. The test project has no reference to BepInEx, Il2CppInterop or `BepInEx/interop`. A game type in a pure file fails `sod.py unit` with CS0103 (checked). `ExtenderRulesTests.TestNoGameAssemblies` also checks the references at run time.
- **Runner:** a console app, not `dotnet test`: there is no NuGet cache on this PC, and xunit/MSTest need packages. Same convention as the in-game suite: public static class, `public static void Test*()`, `using static SodOffline.Assert` (`Check`, `Equal`, `Throws`). Sorted order. Target `net8.0` (the SDK's runtime); pure code stays net6-compatible C#.
- **Mod pattern:** `src/Mod.cs` (the `SodMod` subclass) is a thin adapter: it calls a rule in `src/Rules/` and passes `SaveData`, `GameAdapters.Clock`, etc. Example: ExampleMod `Rules/StartCounter.cs`, tests in `mods/ExampleMod/tests/offline/`. `mods/*/tests/*.cs` (one level) stays the in-game suite.

Run: `py dev/tools/sod.py unit` (`--filter X`, `--list`, `--build-only`). The edit hook runs `unit --build-only` for a `.cs` under a `tests/offline` folder. First result: 18/18 in 70 ms (about 4 s with the build).

Runtime change: none in behaviour. Moved with no code change: `TimeTracker`, `PauseTracker` (from `Events.cs`), `ModSaveData`, `SavePaths` (from `SaveData.cs`), `ModConfig.SafeFileName` (now a partial class). Added, not used by the extender: `GameAdapters`, `GameTime`, the interfaces; `ModSaveData` now implements `IKeyValueStore`. ExampleMod calls `StartCounter` with the same key and log text. No flag was needed, because no working path changed.

Not verified:
- In the game: `GameAdapters.Clock` and `GameAdapters.Log` (only compiled), and ExampleMod after the refactor (the build passes; the in-game `GameStarts` check was not run again).
- `TimeEventsPatch` still has its own copy of the `DayKey`/hour formula; `GameTime` repeats it and a test pins the value. To have one source, the patch must call `GameTime` (a runtime change: verify in the game first).
- The in-game suite still has the pure tests (`TestTimeTracker*`, `TestPauseTracker*`, `TestSafeFileName`, the path part of `SaveDataTests`). They can go after the user agrees.
- The pause adapter: `SessionData.play` probably means "not paused", but this is not verified, so `IGameClock` has no pause field.

## Deferred unpatch (2026-11-17, fix for the load hangs, flag ON by default since 2026-11-17)
- Flag `BepInEx/config/sod.extender.cfg` `[Guard] DeferUnpatch` (default true since 2026-11-17, user decision; false = the old immediate unpatch). When true, a mod that reaches the error budget is turned off at once (State = Failed, no events, the finalizer swallows its errors silently), but `UnpatchSelf` waits for `StableGate` (`Pure/UnpatchRules.cs`): `startedGame` true and no frame ≥ 0.5 s for 5 s. `record.Error` carries " (patches removed later)" until then; the log says "deferred unpatch done". Code: `PatchGuard.Tick`, `Extender.FinishTurnOff`. Offline tests `UnpatchRulesTests` (4). `ExtenderTests.TestThrowingModIsTurnedOff` waits for the deferred unpatch, so it passes in both modes.
- Verified (user-approved series, 2026-11-17 11:48–12:00): all 10 fixtures, flag ON, panel flag false, `launch` + `menu` + `load "Победа"` ×5: **0 hangs of 5** (before: 3 of 11 loads with the run-time unpatch). Each run: TestThrowPatch off during load with "(patches removed later)", unpatch done in gameplay, game responsive. Run 1 full suite: 29/32; the 3 fails are PanelTests (panel flag false, expected). Log `dev/build/series-defer.log`, launch logs in `dev/build/launch-logs/`.
- Not done: a larger sample would make the claim stronger; a turn-off in the menu with no later load keeps the patches until quit (harmless: the finalizer swallows).
- After the series: fixtures removed from LocalLow Mods. Then default switched to true (code and the local cfg).

## M2 closing items (2026-11-17)
- **Full in-game suite passed in one run for the first time: 32/32** (all 10 fixtures, panel flag true, DeferUnpatch true, `load "Победа"`). Log `dev/build/suite-panel.log`.
- **Panel default ON** (`[Panel] Enabled` default true in code; local cfg true). Flag stays, false hides the button.
- **Mod key (cfg collision fix):** `ModRecord.Key` / `SodMod.Key` = `ModConfig.UniqueKey(name, directory, taken)` (pure). Usually `SafeFileName(name)`; on a clash (sanitized names, case-only twins, two Workshop items with one name) the later mod gets `~` + FNV-1a hash of its folder name (stable across runs). The key is used for the settings file, the Harmony id (`sodmod.<key>`, so `UnpatchSelf` of one twin no longer removes the other's patches) and the save data section. A clash logs a warning. For a normal name key == name, so existing files and save data keep working. Offline `ModKeyTests` (5); in game 32/32 again with this build (`dev/build/suite-key.log`). A real clash was not tried in the game (needs two fixtures with one name).
- Still open in M2: `isNewGame=true` (needs a new game — ask the user), `OnMainMenu`, the game's `DeleteSave` hook (in-game check). Then M3.
- **Boot hang 2026-11-17 13:29 (type 1 again, before Unity):** `launch` after two good runs that day. Game process started 13:29:09 (about 5 min after the `launch` call), Responding=True, `Player.log` 0 bytes, `BepInEx/ErrorLog.log` 0 bytes at 13:29:10, `LogOutput.log` NOT rewritten, bridge silent 300 s. So it stopped before the BepInEx preloader wrote its log: no extender or mod code ran. Mods: ExampleMod, TestEvents. Evidence `dev/build/launch-logs/20261117-132910-boothang/` (copied by hand: `kill` keys the archive by LogOutput time, so it missed this run). Save/Cities hashes unchanged. Open: why the 5-min delay between `launch` and the process start (Steam?), and the doorstop/preloader stage.
- **New game without the mouse (2026-11-17, worked):** `SetMenuComponent(newGameSelect)` → `NewGameTypeButton(false)` → `SelectCityButton()` (fills `selectCityDropdown`, built-in cities first; RestartSafeController.newGameLoadCity becomes true) → `selectedCityContinueButton.button.onClick.Invoke()` (→ `charSetup`) → the "Продолжить" button (object `Start`) needs `ExecuteEvents.pointerClickHandler`, `onClick.Invoke()` does nothing → `loadingCity`, ~25 s → tutorial popup: pointer click on "Выкл." (object `Left Button`) → `OnStartGame`. Save/Cities hashes unchanged after it. Second launch that day booted fine (17 s).
- **OnMainMenu (VERIFIED 2026-11-17 by hand):** load `Победа`, open the menu with `EnableMainMenu(true)` → no event (`startedGame` stays true, `mainMenuActive` true). Then `sod.py load "Обучалка"` from that menu → TestEvents saw `GameStarted:False, MainMenu, GameStarted:False`. So in this game `OnMainMenu` means "the current game ends because another save or a new game starts"; there is no plain "quit to menu". Doc comment of `SodMod.OnMainMenu` fixed. No automated test (needs a second load in one run). Save/Cities hashes unchanged.
- **DeleteSave hook (VERIFIED 2026-11-17, user-approved, test slot only):** backup of `SodExtenderTest.sodb` + `.sodmods` in `Documents\SodExtender-backup\testslot-2026-11-17`. Menu → Load Game screen → `SelectNewSave(entry "SodExtenderTest.sodb")` → `MainMenuController.DeleteSave()`. The game deletes the `.sodb` synchronously, so the postfix saw it gone and removed `SodExtenderTest.sodmods`. Hash diff of Save/Cities: only those two files gone. Not hooked: `DeleteCurrentSaveGame()` (unknown when the game calls it). Not checked: what Steam Cloud does with the removed `.sodmods`.
- **M2 status 2026-11-17: all items verified.** Next: M3 (one-file distribution, licences, release page).

## M3 (started 2026-11-17)
User decisions (2026-11-17): our code under **MIT** (`LICENSE`, holder XRenso); host **GitHub Releases**; form **zip to unpack** (no installer).
- `package-extender` no longer bundles `BepInEx/unity-libs` (Unity's own engine DLLs, 67 files + the 2021.3.45 zip: not ours to redistribute). BepInEx downloads them on the first start (default `UnityBaseLibrariesSource`). The zip now adds `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt` (BepInEx/plugins/SodExtender/) and `SodExtender-README.txt` (root: install, safety, turn off, uninstall, logs); a guard fails the build if the zip holds unity-libs, interop, config or the dev bridge. Result `dev/build/packages/SodExtender-0.2.0.zip`, 32.8 MB, 231 files. Version 0.2.0 (M2 features).
- Draft release text: `dev/release/RELEASE-0.2.0.md`.
- NOT verified: first start WITHOUT unity-libs (download path). A test needs moving `BepInEx/unity-libs` and `BepInEx/interop` away; touching `interop` is against the project rules, so it needs the user's explicit OK (or a second PC / clean copy).
- Open before publishing: full licence texts in the zip (LGPL requires the text, not only the name; now only names + URLs); confirm licences of AssetRipper.*, Disarm, Gee.External.Capstone, SemanticVersioning, StableNameDotNet, WasmDisassembler; a run by a real player (normal launch, sound, no bridge); first git commit; GitHub repository.
- **First start without bundled Unity libs: VERIFIED 2026-11-17** (user moved `unity-libs` and `interop` to `*.bak`). BepInEx downloaded `https://unity.bepinex.dev/libraries/2021.3.45.zip`, ran Cpp2IL, generated interop; title screen in 99 s; "SOD Extender 0.2.0 ready"; no errors. New `unity-libs` byte-identical to the old one; new `interop` has the same file list (DLL bytes differ, as expected for a regeneration); all projects build. `*.bak` folders left for the user to delete (rules forbid us touching interop).
