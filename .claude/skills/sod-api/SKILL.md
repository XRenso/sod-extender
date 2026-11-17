---
name: sod-api
description: Find facts about the Shadows of Doubt game API and data — classes, fields, method signatures, DDS content, strings, SOD.Common helpers, IL2CPP interop rules. Use before writing code that touches a game type you have not verified.
---
# Finding API facts

Never guess a member name. The compiler and `exec` give a fast proof.

## Sources, in order
1. **One type:** `py dev/tools/sod.py ref TypeName` prints fields, properties and method signatures. Use `Outer+Inner` for nested types.
2. **Search everything:** `py dev/tools/sod.py ref` once writes all types to `dev/reference/decompiled/`. Then use Grep there. Run it again after a game update.
3. **Live values:** `exec` in the running game. This is the only way to see real runtime state.
4. **Game data:** `Shadows of Doubt_Data/StreamingAssets/`: `DDS/` (Blocks, Messages, Trees: 6603 text files), `Strings/` (localization), `Cities/`. Read only.
5. **Existing mods** and SOD.Common: github.com/Ven0maus/SOD.Common, its wiki, and Thunderstore (`thunderstore.io/c/shadows-of-doubt`). 193 packages exist.

## The main limit: no method bodies
`BepInEx/interop/Assembly-CSharp.dll` holds wrappers. Each method body only calls native code. So `ref` proves that a method exists and shows its signature, but it does not show what the method does. To learn behaviour:
- Read names and fields, then test with `exec`.
- Add a temporary Harmony patch that logs arguments and results.
- Read other mods' source.
State what you verified and what you inferred. Never present a guess about a body as a fact.

## IL2CPP rules
- Game collections are `Il2CppSystem.Collections.Generic.*`, not `System.*`. Iterate by index or `foreach`; convert explicitly.
- `is` and `as` do not see the real il2cpp type of a wrapper. Use `obj.TryCast<T>()` and `obj.Cast<T>()`.
- A custom MonoBehaviour needs `ClassInjector.RegisterTypeInIl2Cpp<T>()` and a `(IntPtr)` constructor.
- A managed delegate passed to Unity events needs a cast or `DelegateSupport.ConvertDelegate`. Check the error text if it fails.
- A managed object held only from il2cpp can be collected. Keep a managed reference.
- Harmony patches work on wrapper methods, but the compiler can inline small native methods. `[CallerCount(0)]` in `dev/reference/decompiled/` is a warning sign: the patch can be applied and still never run. Verify each new patch with a log line in the game.
- Coroutine and async state machines appear as nested types such as `_StartSaveAsync_d__183`. These names can change with a game update.

## Reporting
Give the exact signature, the source (`ref`, `exec` result, DDS file) and the game build (see CLAUDE.md).
