---
name: sod-game
description: Launch, drive and inspect the running Shadows of Doubt game — open the menu, load a save, run C# inside the game, read logs, take screenshots, close the game. Use whenever you need to see or verify something in the real game.
---
# Driving the game

Everything goes through one CLI, run from the game root:
`py dev/tools/sod.py <command>` (exit 0 = ok, 1 = failure, 2 = environment problem).

## Hard rules
- Start the game ONLY with `launch`. Close it with `kill`.
- Keep the game silent. Never unmute FMOD.
- Never save, overwrite or delete the user's saves. Loading is safe.
- If the game is gone: run `status` once and `log --errors` once, then report. Never chain relaunches or waits.
- One agent at a time drives the game.

## Commands
| Goal | Command |
|---|---|
| Start (windowed 1280x720, muted, bridge on) | `launch [--restart]` |
| Pass the title screen, wait for the main menu | `menu` |
| List saves | `saves` |
| Load a save by a part of its name, wait for gameplay | `load "name"` |
| Is it up, scene, frame, loaded plugins | `status` |
| Run a C# expression, return its value | `exec "Player.Instance.currentHealth"` |
| Run C# statements (any `;` makes it statements) | `exec "P(1); P(2);"` |
| Multi-line C# | write a file in the scratchpad, then `exec -f path.cs` |
| Build, restart, load the new DLL | `reload ModName` |
| Close, restore the user's screen prefs | `kill` |
| Errors of this session | `log --errors` |
| Our mod's lines, or any pattern | `log --mod Name --tail 40`, `log --grep "pattern"` |
| Unity's log | `log --player --tail 40` |
| Screenshot, then view it with Read | `screenshot [path.png]` (default `dev/bridge/screenshot.png`) |
| Press keys, click pixels (**takes focus from the user**) | `key space esc`, `click X Y [--right]` |

## exec environment
- The snippet runs on the Unity main thread. Keep it short: it blocks the frame.
- Default usings: `System`, `System.Linq`, `System.Collections.Generic`, `UnityEngine`, `SodDevBridge.Api`.
- Helpers from `Api`: `P(...)` prints to the reply and the log, `Format(x)`, `Check(cond, msg)`.
- Game types are in the global namespace: `Player`, `SessionData`, `MainMenuController`.
- Each exec is a new assembly. No variables persist. To keep state, store it in a game object or write a test.
- Deployed mods are referenced, so `MyMod.Plugin.L` works in a snippet.
- In PowerShell, inner `"` quotes break the arguments. Use `exec -f` for any code with strings.

## Gotchas
- A bridge timeout usually means a blocked main thread: a scene or city load. `menu` and `load` already wait for it.
- Unity scene names: `ControllerDetect` (title), `Main` (menu and gameplay).
- `kill` is a forced close: nothing is saved.
- If the game was closed another way, the user's screen prefs stay in dev mode until the next `kill`. Run `kill` once to restore them.
