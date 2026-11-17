# Shadows of Doubt — mod workspace

C# mods for Shadows of Doubt (Unity 2021.3.45f2, **IL2CPP**) on our **SOD Extender**, a one-time install like REPENTOGON. Code mods are normal Workshop mods with a `Code/` folder. BepInEx is the hidden engine, and players never install it by hand. Design, status and open checks: `dev/design/extender-v1.md`. Create a mod with `py dev/tools/sod.py new-mod Name`.

Talk to the user in **Russian**, in STE style (skill `ste-ru`). Keep reports to 3–6 lines.

## Layout (cwd = Steam game folder)
| Path | What | Who edits |
|---|---|---|
| `extender/SodExtender/` | the extender: mod discovery, load, isolation (`PatchGuard`), overlap report, mod API (`SodMod`) | us |
| `mods/<Name>/` | a mod: `src/`, `tests/`, `manifest.json`, `README.md`. `Test*` mods are failure fixtures for `ExtenderTests`: deploy them only for that test, never ship | us |
| `dev/design/extender-v1.md` | extender design, verified facts, milestones | us |
| `dev/tools/sod.py` | the CLI for everything | us |
| `dev/tools/hook-build.py` | edit hook: compiles the project of an edited `.cs` file | us |
| `dev/SodDevBridge/` | DEV-ONLY plugin: file RPC, C# exec, in-game test runner, mute. Never ship | us, rarely |
| `dev/tests/ingame/` | in-game tests shared by all mods | us |
| `dev/tests/offline/` | offline test runner, fakes, shared offline tests (`sod.py unit`, no game) | us |
| `Directory.Build.props` | shared build settings and references | us, rarely |
| `dev/backup-vanilla/` | the user's saves as of 2026-11-06, screen prefs snapshot | **no** |
| `dev/bridge/`, `dev/build/`, `dev/toolchain/`, `dev/reference/` | generated, gitignored | no |
| `BepInEx/plugins/` | deployed plugins; `sod.py deploy` writes here | via CLI only |
| `BepInEx/interop/`, `BepInEx/core/`, `dotnet/`, `winhttp.dll` | BepInEx 6.0.788 and generated wrappers | **no** |
| `Shadows of Doubt_Data/`, `GameAssembly.dll`, `*.exe` | vanilla game, managed by Steam | **no** |

## Non-negotiable rules
- **Start the game only with `py dev/tools/sod.py launch`.** Only this path passes `--sod-dev`; without the flag the bridge stays idle, so the user's normal launch is not muted.
- **The game stays silent while Claude drives it.** The user listens to music. The bridge mutes FMOD; never unmute.
- **Close with `sod.py kill`.** It restores the user's screen prefs. Never leave the game in dev window mode for the user.
- **Never write to or delete the user's saves** in `%USERPROFILE%/AppData/LocalLow/ColePowered Games/Shadows of Doubt/Save`. The folder syncs to Steam Cloud. Loading is safe. Do not call `SaveGame`, `OverwriteSave`, or `DeleteSave`.
- **If the game crashes, stop:** check once with `sod.py status`, read `sod.py log --errors` once, report. Never loop launches or waits.
- **Experimental changes go behind a flag that is OFF by default.** Never remove a working path before the new one is verified in the game.
- The game is one shared resource: one agent at a time may drive it. `build`, `ref`, and `test --compile-only` are safe in parallel.
- `key` and `click` take focus from the user. Prefer `exec` calls to game methods; use input only when no method exists.
- Commit only when the user asks. Never upload to Thunderstore or mod.io without the user's request.

## Workflow
Cheap → expensive: `build` (the hook runs it on every `.cs` edit) → `unit` (offline tests of pure rules, no game, ms) → `exec` in the running game to try an idea → `reload Name` → `load "save"` → `test` → `log --errors` → `screenshot` (view with Read).
There is no hot reload: `reload` restarts the game (19 s to the title screen). Use `exec` for fast experiments. An `exec` takes about 1.7 s.

Skills: `sod-game` (drive the game), `sod-test` (tests), `sod-api` (find API facts), `sod-feature` (end-to-end feature), `ste-ru` (reply style).

## Model routing (cost discipline)
Use the cheapest model that can do the step. Do not spawn agents for one or two tool calls.
| Task | Agent | Model |
|---|---|---|
| API lookups in the interop wrappers and game data | `sod-api-scout` | Haiku |
| Run build and tests, triage logs | `sod-tester` | Haiku |
| Implement a well-specified change with tests | `sod-implementer` | Sonnet |
| Review a non-trivial diff | `sod-reviewer` | Sonnet |
| Open-ended design, new systems, bugs cheaper agents failed on | `sod-designer` | Opus |

## Known environment facts
- Steam app 986130, build 24616330. BepInEx 6.0.788, SOD.Common 2.1.5, plugin target `net6.0`.
- .NET SDK 8.0.427 is in `%LOCALAPPDATA%/Microsoft/dotnet` and is not on PATH; `sod.py` finds it. Builds use the runtime in `dotnet/` as references, so they need no NuGet package.
- ilspycmd 9.1 is pinned in `dev/toolchain/ilspy`; newer versions need a newer SDK.
- Logs: `BepInEx/LogOutput.log` and `%USERPROFILE%/AppData/LocalLow/ColePowered Games/Shadows of Doubt/Player.log`. Both are rewritten on each start.
- A game update makes BepInEx regenerate `BepInEx/interop` on the next launch (94 s). `launch` waits for it. After an update, rebuild the mods: wrappers can change.
- **Interop method bodies are stubs.** `sod.py ref` shows names and signatures only, not game logic. See skill `sod-api`.
- The title screen is scene `ControllerDetect`; the main menu is scene `Main`. The scene name changes first, then the load blocks the main thread. Menu calls made before that block are lost or hang a later save load. `sod.py menu` waits until the game stays responsive for 20 s.
- Load order that works: the menu's `Load Game` button → `SelectNewSave` → `LoadGame` → `loadButton.button.onClick`. `sod.py load` does this with no mouse: about 27 s to the menu and 32 s to gameplay.
- `Player.Update` also runs in the menu, and `Player.Instance` is not null there. To test gameplay, check `SessionData.Instance.startedGame`.
- Deployed mods go to `%USERPROFILE%/AppData/LocalLow/ColePowered Games/Shadows of Doubt/Mods/<Name>/` (`ModSettings.txt` + `Code/`). Remove test mods from there when testing ends, so the user's game stays clean.
- Workshop upload: `dev/tools/workshop/`. An upload is outward-facing: run it only on the user's request, and always through the Private guard. Test item `3834680272` is obsolete; the user can delete it.
- `sod.py click` is not verified: in the last check the cursor did not reach the target. Do not rely on it.
- If the user acts in the game window during a check, the result is not proof of the harness. Ask, and repeat the check alone.
- To disable all mods for one session without uninstalling: set `enabled = false` in `doorstop_config.ini`.
