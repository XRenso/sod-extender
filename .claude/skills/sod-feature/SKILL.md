---
name: sod-feature
description: End-to-end workflow for adding or changing a mechanic in a Shadows of Doubt mod — design, API verification, implementation with tests, in-game verification and review, with the right model for each step. Use when the user asks for a new mechanic, rule change or feature.
---
# Feature workflow

Follow the steps in order. Pick the cheapest agent for each step (see "Model routing" in CLAUDE.md). The main session orchestrates and talks to the user.

1. **Clarify** (main session). Restate the rule in one paragraph: what triggers it, what changes, what the player sees. Ask the user only about real design forks.
2. **Design** (main session for small features; `sod-designer` for new systems). Output: the patches to use, the state to keep, how it survives save and load, the acceptance tests.
3. **Verify the API** (`sod-api-scout`). Send all needed identifiers in one request. Prove behaviour with `exec` or a logging patch, because interop has no method bodies.
4. **Implement** (`sod-implementer`; the main session can do one-file changes). Pure rules in plain classes. Thin Harmony patches. Risky changes behind a config flag that is OFF by default.
5. **Check** (`sod-tester` or directly): `build`, then `test --compile-only`.
6. **In the game** (one agent at a time): `reload Name` → `load "save"` → `test --filter Feature` → `log --errors` → `screenshot` when visuals matter, viewed with Read.
7. **Review** (`sod-reviewer`) for non-trivial diffs. Fix what is confirmed.
8. **Report** to the user in Russian, 3–6 lines: what changed, how it was verified, what is not verified.

Every feature must survive save and load. Use SOD.Common events and save-data helpers where they fit; check them with `sod-api` first.
