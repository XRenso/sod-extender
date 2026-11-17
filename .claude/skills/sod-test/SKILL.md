---
name: sod-test
description: Write and run tests for Shadows of Doubt mods. Offline tests of pure rules run in milliseconds with no game (sod.py unit); in-game tests run through the dev bridge. Use when adding a test, running the test suite, or verifying observable mod behaviour.
---
# Tests

## Where
- `mods/<Name>/tests/*.cs`: tests of one mod. They are NOT part of the mod DLL (`Compile` includes only `src/`).
- `dev/tests/ingame/*.cs`: shared and harness tests.

## How to run
- `py dev/tools/sod.py unit` runs offline tests (no game). Run it first; see "Pure logic".
- `py dev/tools/sod.py test [--filter TEXT]` runs tests in the running game. Deploy changes first with `reload Name`.
- `py dev/tools/sod.py test --compile-only` checks that tests compile, with no game.
- Exit 1 = a failed test. Exit 2 = environment problem (game closed, bridge timeout).

## How to write
Any `public static` method named `Test*` with no parameters is a test. Return `void`, or `IEnumerator` to wait across frames.

```csharp
using System.Collections;
using UnityEngine;
using static SodDevBridge.Api;

public static class MyFeatureTests
{
    public static void TestPureRule()
    {
        Check(MyMod.Rules.Cost(3) == 9, "cost of 3");
    }

    public static IEnumerator TestInGameEffect()
    {
        yield return WaitUntil(() => Player.Instance != null, 60f);
        // Act through game methods, then wait.
        yield return Wait(1f);
        Check(Player.Instance.currentHealth > 0f, "player is alive");
    }
}
```

- `Check(cond, msg)` fails the test. An exception also fails it. The limit is 120 s for one test.
- `Wait` and `WaitUntil` use real time, so they work while the game is paused.
- A test that needs gameplay must first check state, because it can run from the menu: load a save with `sod.py load` before the run, and fail with a clear message if `Player.Instance` is null.
- Keep the game state reproducible. A test must undo what it changes.
- Never call a save method in a test.

## Pure logic: offline tests (first choice)
Interop method bodies are stubs, so no game method runs outside the game. Put rules in plain C# that uses no game, Unity, BepInEx or Harmony types, and test them offline:
- Rules: `mods/<Name>/src/Rules/*.cs` (extender: `extender/SodExtender/Pure/`). `src/Mod.cs` stays a thin adapter that calls the rule.
- Game access through interfaces in `Pure/Abstractions.cs`: `IKeyValueStore` (pass `SaveData`), `IGameClock` (`GameAdapters.Clock`), `IModLog` (`GameAdapters.Log(Log)`), `IModEvents`.
- Tests: `mods/<Name>/tests/offline/*.cs` or `dev/tests/offline/*.cs`. Same convention (`public static class`, `public static void Test*()`), but `using static SodOffline.Assert;` (`Check`, `Equal`, `Throws`). Fakes: `SodOffline.Fakes.FakeClock`, `FakeLog`, `FakeGame` (+ `EventRecorder`); `new ModSaveData()` is real and pure. Give test classes unique names (prefix with the mod name).
- Run: `py dev/tools/sod.py unit [--filter TEXT] [--list] [--build-only]`. About 4 s with the build; the tests take ms. A game type in a pure file fails this build: move it to the adapter.
- Offline tests prove rules, not game behaviour. Check the adapter and the patch with one in-game test.
Design: `dev/design/extender-v1.md`, section "Offline tests".
