# SOD Extender

A one-time install that lets **Shadows of Doubt** load code mods from the Steam Workshop — like REPENTOGON for Isaac. After the install, a code mod is a normal Workshop subscription and appears in the game's own Mods menu. Players never install BepInEx by hand.

**Players:** download the zip from [Releases](../../releases) and follow `SodExtender-README.txt` inside it.

## For mod authors
A code mod is a normal game mod folder (`ModSettings.txt`, optional data) plus a `Code/` folder with one DLL that has a `SodMod` subclass:

```csharp
public class Mod : SodMod
{
    public override void OnLoad() => Log.LogInfo("hello");
    public override void OnGameStarted(bool isNewGame) { /* gameplay ready */ }
}
```

The extender applies the mod's `[HarmonyPatch]` classes, then calls `OnLoad`. API: game events (`OnGameStarted`, `OnMainMenu`, `OnHourChanged`, `OnDayChanged`, `OnPauseChanged`), per-mod settings (`Config`, `Setting()`), per-save data (`SaveData`). A mod that fails is turned off and does not break other mods; methods patched by several mods are reported. Code mods run with no sandbox, with the player's rights.

Start a mod: `py dev/tools/sod.py new-mod Name`. Example: `mods/ExampleMod`.

## Repository layout
| Path | What |
|---|---|
| `extender/SodExtender/` | the extender (BepInEx 6 IL2CPP plugin, net6.0) |
| `mods/` | example mod and `Test*` failure fixtures for the in-game tests |
| `dev/tools/sod.py` | CLI: build, deploy, launch, test, package |
| `dev/tests/` | offline tests (`sod.py unit`, no game) and in-game tests |
| `dev/SodDevBridge/` | dev-only plugin for automated testing, never shipped |
| `dev/design/extender-v1.md` | design notes and verified game facts |
| `dev/release/` | player README, licence notices, release notes |

The repository lives inside the Steam game folder; `.gitignore` is a whitelist of our files only. Building needs the game with BepInEx 6.0.0-be.788 installed (references come from `BepInEx/` and `dotnet/`).

Tested with Shadows of Doubt v42.04 (Steam build 24616330) on Windows.

## Licence
MIT, see [LICENSE](LICENSE). The release zip also bundles BepInEx and the .NET runtime under their own licences: see [THIRD-PARTY-NOTICES](dev/release/THIRD-PARTY-NOTICES.txt). Not affiliated with ColePowered Games.
