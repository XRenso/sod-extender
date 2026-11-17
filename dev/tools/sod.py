#!/usr/bin/env python3
"""Single CLI for the Shadows of Doubt mod harness. Run from the game root:
    py dev/tools/sod.py <command>

Exit codes: 0 = ok, 1 = failure (build/test/exec), 2 = environment problem.
"""
from __future__ import annotations

import argparse
import ctypes
import json
import msvcrt
import os
import re
import shutil
import subprocess
import sys
import time
import uuid
import winreg
import zipfile
from ctypes import wintypes
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parents[2]
APP_ID = "986130"
PROCESS = "Shadows of Doubt.exe"
STEAM = Path(r"C:\Program Files (x86)\Steam\Steam.exe")
BRIDGE = ROOT / "dev" / "bridge"
BUILD = ROOT / "dev" / "build"
MODS = ROOT / "mods"
PLUGINS = ROOT / "BepInEx" / "plugins"
INTEROP = ROOT / "BepInEx" / "interop"
CORE = ROOT / "BepInEx" / "core"
RUNTIME = ROOT / "dotnet"
BEPINEX_LOG = ROOT / "BepInEx" / "LogOutput.log"
GAME_DATA = Path(os.environ["USERPROFILE"]) / "AppData" / "LocalLow" / "ColePowered Games" / "Shadows of Doubt"
PLAYER_LOG = GAME_DATA / "Player.log"
DOTNET_ROOT = Path(os.environ["LOCALAPPDATA"]) / "Microsoft" / "dotnet"
DOTNET = DOTNET_ROOT / "dotnet.exe"
ILSPY = ROOT / "dev" / "toolchain" / "ilspy" / "ilspycmd.exe"
DECOMPILED = ROOT / "dev" / "reference" / "decompiled"
BRIDGE_PROJECT = ROOT / "dev" / "SodDevBridge" / "SodDevBridge.csproj"
EXTENDER_PROJECT = ROOT / "extender" / "SodExtender" / "SodExtender.csproj"
LOADER_PROJECT = ROOT / "extender" / "SodExtender.Loader" / "SodExtender.Loader.csproj"
LOCAL_MODS = GAME_DATA / "Mods"
TESTS_DIR = ROOT / "dev" / "tests" / "ingame"
OFFLINE_PROJECT = ROOT / "dev" / "tests" / "offline" / "SodOfflineTests.csproj"
PREFS_KEY = r"Software\ColePowered Games\Shadows of Doubt"
PREFS_SNAPSHOT = ROOT / "dev" / "backup-vanilla" / "screen-prefs.json"

DEV_SCREEN = ["-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720"]
LOG_NOISE = re.compile(r"DobbyDetour|\[Debug\s*:\s*(Preloader|Il2CppInterop)")


def die(msg: str, code: int = 2) -> None:
    print(f"ERROR: {msg}", file=sys.stderr)
    sys.exit(code)


def dotnet_env() -> dict:
    env = dict(os.environ)
    env.update(DOTNET_ROOT=str(DOTNET_ROOT), DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_CLI_UI_LANGUAGE="en")
    return env


# ---------------------------------------------------------------- process

def game_pids() -> list[int]:
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {PROCESS}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    return [int(m) for m in re.findall(r'^"[^"]+","(\d+)"', out, re.M)]


def game_running() -> bool:
    return bool(game_pids())


# ---------------------------------------------------------------- screen prefs

def read_screen_prefs() -> dict:
    values = {}
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, PREFS_KEY) as key:
            i = 0
            while True:
                try:
                    name, value, kind = winreg.EnumValue(key, i)
                except OSError:
                    break
                i += 1
                if name.startswith("Screenmanager") and kind == winreg.REG_DWORD:
                    values[name] = value
    except FileNotFoundError:
        pass
    return values


def snapshot_prefs() -> None:
    """Keep the user's screen mode once. Unity stores the dev window mode in PlayerPrefs."""
    if not PREFS_SNAPSHOT.exists():
        PREFS_SNAPSHOT.parent.mkdir(parents=True, exist_ok=True)
        PREFS_SNAPSHOT.write_text(json.dumps(read_screen_prefs(), indent=1), encoding="utf-8")


def restore_prefs() -> bool:
    if not PREFS_SNAPSHOT.exists():
        return False
    saved = json.loads(PREFS_SNAPSHOT.read_text(encoding="utf-8"))
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, PREFS_KEY) as key:
        for name in read_screen_prefs():
            if name not in saved:
                winreg.DeleteValue(key, name)
        for name, value in saved.items():
            winreg.SetValueEx(key, name, 0, winreg.REG_DWORD, value)
    PREFS_SNAPSHOT.unlink()
    return True


# ---------------------------------------------------------------- bridge

def bridge_call(op: str, timeout: float = 15.0, soft: bool = False, **fields) -> dict | None:
    """One caller at a time: the bridge has one request file and one response file."""
    BRIDGE.mkdir(parents=True, exist_ok=True)
    with open(BRIDGE / "call.lock", "a+b") as lock:
        end = time.time() + timeout
        while True:
            try:
                lock.seek(0)
                msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
                break
            except OSError:
                if time.time() > end:
                    if soft:
                        return None
                    die("another sod.py command is using the bridge. Wait for it or stop it")
                time.sleep(0.1)
        try:
            return _bridge_call_locked(op, timeout, soft, fields)
        finally:
            lock.seek(0)
            msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)


def _bridge_call_locked(op: str, timeout: float, soft: bool, fields: dict) -> dict | None:
    """soft=True returns None on timeout: the main thread blocks during scene and city loads."""
    if not game_running():
        die("the game is not running. Use: sod.py launch")
    req_id = uuid.uuid4().hex
    resp = BRIDGE / "response.json"
    resp.unlink(missing_ok=True)
    tmp = BRIDGE / "request.tmp"
    tmp.write_text(json.dumps({"id": req_id, "op": op, **fields}), encoding="utf-8")
    os.replace(tmp, BRIDGE / "request.json")
    end = time.time() + timeout
    while time.time() < end:
        if resp.exists():
            try:
                data = json.loads(resp.read_text(encoding="utf-8"))
            except (json.JSONDecodeError, OSError):
                time.sleep(0.02)
                continue
            if data.get("id") == req_id:
                return data
        time.sleep(0.03)
        if not game_running():
            die("the game exited during the bridge call. Read: sod.py log --errors")
    (BRIDGE / "request.json").unlink(missing_ok=True)
    if soft:
        return None
    die(f"bridge timeout after {timeout:.0f}s: the main thread is busy (scene or city load?) or the bridge is not deployed")
    return {}


# ---------------------------------------------------------------- build

def run_dotnet_build(project: Path) -> int:
    obj = BUILD / "obj" / project.stem / "project.assets.json"
    args = [str(DOTNET), "build", str(project), "-c", "Debug", "-v", "q", "-nologo", "-clp:NoSummary"]
    if obj.exists():
        args.append("--no-restore")
    p = subprocess.run(args, cwd=ROOT, env=dotnet_env(), capture_output=True, text=True, errors="replace")
    seen = set()
    for line in (p.stdout + p.stderr).splitlines():
        line = line.strip()
        if line and line not in seen and re.search(r"\b(error|warning)\b", line):
            seen.add(line)
            print(line.replace(str(ROOT) + "\\", ""))
    return p.returncode


def mod_projects() -> list[Path]:
    return sorted(MODS.glob("*/*.csproj")) if MODS.exists() else []


def resolve_projects(names: list[str]) -> list[Path]:
    # The extender goes before the mods, because mods compile against it.
    projects = {p.stem: p for p in [BRIDGE_PROJECT, EXTENDER_PROJECT, LOADER_PROJECT, *mod_projects()]}
    if not names:
        return list(projects.values())
    missing = [n for n in names if n not in projects]
    if missing:
        die(f"unknown project(s): {missing}. Known: {sorted(projects)}", 1)
    return [projects[n] for n in names]


def cmd_build(a) -> int:
    rc = 0
    for project in resolve_projects(a.names):
        code = run_dotnet_build(project)
        print(f"{project.stem}: {'OK' if code == 0 else 'FAILED'}")
        rc = rc or code
    return 1 if rc else 0


def mod_settings(folder: Path) -> dict:
    """ModSettings.txt fields, from the mod's manifest.json. The game reads this file as JSON."""
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    return {"name": manifest["name"], "version": manifest["version_number"], "loadOrderValue": 0,
            "creator": manifest.get("author", ""), "summary": manifest.get("description", ""),
            "enabled": manifest.get("enabled", True)}


def deploy(project: Path) -> None:
    """Plugins go to BepInEx/plugins. Mods go to the game's local mod folder, the same layout as Workshop."""
    src = BUILD / project.stem
    is_mod = project.parent.parent == MODS
    if is_mod:
        root = LOCAL_MODS / project.stem
        target = root / "Code"
        root.mkdir(parents=True, exist_ok=True)
        settings = root / "ModSettings.txt"
        if not settings.exists():  # The game owns this file after creation: it stores enabled and load order.
            settings.write_text(json.dumps(mod_settings(project.parent), indent=1), encoding="utf-8")
    elif project == EXTENDER_PROJECT:
        # The core is not a BepInEx plugin: the loader finds it in plugins/SodExtender/core (see core_json()).
        target = PLUGINS / "SodExtender" / "core"
        old_plugin = PLUGINS / "SodExtender" / "SodExtender.dll"  # pre-0.3 layout: the core WAS the plugin
        for stale in (old_plugin, old_plugin.with_suffix(".pdb")):
            if stale.exists():
                stale.unlink()
    elif project == LOADER_PROJECT:
        target = PLUGINS / "SodExtender"
    else:
        target = PLUGINS / project.stem
    target.mkdir(parents=True, exist_ok=True)
    for name in (f"{project.stem}.dll", f"{project.stem}.pdb"):
        if (src / name).exists():
            shutil.copy2(src / name, target / name)
    if project == EXTENDER_PROJECT:
        (target / "core.json").write_text(core_json(), encoding="utf-8")


def core_versions() -> tuple:
    """(version, api) from the core's Plugin.cs: the single source of both."""
    text = (EXTENDER_PROJECT.parent / "Plugin.cs").read_text(encoding="utf-8")
    return (re.search(r'Version = "([^"]+)"', text).group(1), int(re.search(r"ApiMajor = (\d+);", text).group(1)))


def core_json() -> str:
    version, api = core_versions()
    return json.dumps({"version": version, "api": api}) + "\n"


def cmd_deploy(a) -> int:
    if game_running():
        die("the game locks plugin DLLs. Run sod.py kill first, or use sod.py reload")
    projects = resolve_projects(a.names)
    for project in projects:
        if run_dotnet_build(project) != 0:
            print(f"{project.stem}: BUILD FAILED, nothing deployed")
            return 1
        deploy(project)
        print(f"{project.stem}: deployed")
    return 0


# ---------------------------------------------------------------- launch / kill

def cmd_launch(a) -> int:
    if game_running():
        if not a.restart:
            print("already running")
            return cmd_status(a)
        kill_game()
    if not (PLUGINS / "SodDevBridge" / "SodDevBridge.dll").exists():
        die("the bridge is not deployed. Run: sod.py deploy SodDevBridge")
    if not STEAM.exists():
        die(f"Steam not found: {STEAM}")
    snapshot_prefs()
    archive_logs("before launch")  # the previous run, if it was closed another way
    (BRIDGE / "response.json").unlink(missing_ok=True)
    (BRIDGE / "request.json").unlink(missing_ok=True)
    subprocess.Popen([str(STEAM), "-applaunch", APP_ID, *DEV_SCREEN, "--sod-dev"])
    started = time.time()
    seen = False
    # A game update regenerates the interop assemblies, which takes about 100 s.
    while time.time() - started < a.timeout:
        time.sleep(1.5)
        running = game_running()
        seen = seen or running
        if seen and not running:
            print("The game exited during startup. Log tail:")
            print_log(errors_only=True, tail=20)
            return 2
        if not seen and time.time() - started > 90:
            die("the game process did not start in 90 s. Check Steam")
        if not seen:
            continue
        hb = BRIDGE / "heartbeat.txt"
        if hb.exists() and hb.stat().st_mtime >= started:
            data = bridge_call("ping", timeout=30)
            print(f"up in {time.time() - started:.0f}s scene={data.get('scene')} plugins={data.get('plugins')}")
            print_log(errors_only=True, tail=20)
            return 0
    die(f"the bridge did not answer in {a.timeout} s. Read: sod.py log --tail 40")
    return 2


LAUNCH_LOGS = BUILD / "launch-logs"
KEEP_LAUNCH_LOGS = 30


def archive_logs(reason: str) -> Path | None:
    """The game rewrites both logs on each start. Keep a copy per launch, so a rare hang leaves evidence.
    The folder name is the BepInEx log time; a run already archived is skipped."""
    if not BEPINEX_LOG.exists():
        return None
    stamp = time.strftime("%Y%m%d-%H%M%S", time.localtime(BEPINEX_LOG.stat().st_mtime))
    target = LAUNCH_LOGS / stamp
    if target.exists():
        return None
    try:
        target.mkdir(parents=True)
        for src in (BEPINEX_LOG, BEPINEX_LOG.with_name("ErrorLog.log"), PLAYER_LOG, BRIDGE / "heartbeat.txt"):
            if src.exists():
                shutil.copy2(src, target / src.name)
        mods = sorted(p.name for p in LOCAL_MODS.glob("*") if p.is_dir()) if LOCAL_MODS.exists() else []
        plugins = sorted(p.name for p in PLUGINS.glob("*") if p.is_dir())
        (target / "meta.txt").write_text(
            f"archived: {time.strftime('%Y-%m-%d %H:%M:%S')} ({reason})\n"
            f"game running at archive: {game_running()}\n"
            f"plugins: {', '.join(plugins)}\nlocal mods: {', '.join(mods)}\n", encoding="utf-8")
    except OSError as ex:
        print(f"warning: could not archive logs: {ex}")
        return None
    for old in sorted(LAUNCH_LOGS.iterdir())[:-KEEP_LAUNCH_LOGS]:
        shutil.rmtree(old, ignore_errors=True)
    return target


def heartbeat_info() -> str:
    """Last bridge heartbeat and its age. A stale heartbeat with a live process = the main thread is blocked."""
    hb = BRIDGE / "heartbeat.txt"
    if not hb.exists():
        return "heartbeat: none"
    age = time.time() - hb.stat().st_mtime
    try:
        text = hb.read_text(encoding="utf-8").strip()
    except OSError:
        text = "?"
    return f"heartbeat: {age:.0f}s old: {text}"


def kill_game() -> None:
    for pid in game_pids():
        subprocess.run(["taskkill", "/PID", str(pid), "/F"], capture_output=True)
    end = time.time() + 15
    while game_running() and time.time() < end:
        time.sleep(0.3)
    saved = archive_logs("kill")
    if saved:
        print(f"logs saved: {saved.relative_to(ROOT)}")


def cmd_kill(a) -> int:
    kill_game()
    restored = restore_prefs()
    print(f"game closed; user screen prefs {'restored' if restored else 'unchanged'}")
    return 0


def cmd_reload(a) -> int:
    """No hot reload exists for IL2CPP plugins: build first, then swap the DLL and restart."""
    projects = resolve_projects(a.names or [p.stem for p in mod_projects()])
    for project in projects:
        if run_dotnet_build(project) != 0:
            print(f"{project.stem}: BUILD FAILED. The running game is untouched")
            return 1
    kill_game()
    for project in projects:
        deploy(project)
        print(f"{project.stem}: deployed")
    a.restart = False
    return cmd_launch(a)


def cmd_status(a) -> int:
    if not game_running():
        print("game: not running")
        return 0
    print(heartbeat_info())
    data = bridge_call("ping", soft=True)
    if data is None:
        print("bridge: no answer in 15 s. The main thread is blocked (a load, or a hang if the heartbeat stays old)")
        return 1
    print(json.dumps(data, ensure_ascii=False, indent=1))
    return 0


# ---------------------------------------------------------------- compile + exec + test

def csc_path() -> Path:
    sdks = sorted((DOTNET_ROOT / "sdk").glob("*/Roslyn/bincore/csc.dll"))
    if not sdks:
        die(f"no .NET SDK in {DOTNET_ROOT}")
    return sdks[-1]


def compile_references() -> list[Path]:
    refs = [p for p in RUNTIME.glob("System*.dll") if ".Native" not in p.name]
    refs += [RUNTIME / n for n in ("netstandard.dll", "mscorlib.dll", "Microsoft.CSharp.dll")]
    refs += [CORE / n for n in ("BepInEx.Core.dll", "BepInEx.Unity.IL2CPP.dll", "BepInEx.Unity.Common.dll",
                                "Il2CppInterop.Runtime.dll", "Il2CppInterop.Common.dll", "0Harmony.dll")]
    refs += sorted(INTEROP.glob("*.dll"))
    refs += sorted(PLUGINS.glob("**/*.dll"))
    refs += sorted(LOCAL_MODS.glob("*/Code/*.dll"))
    return [r for r in refs if r.exists()]


def compile_library(sources: list[Path], out: Path) -> bool:
    rsp = out.with_suffix(".rsp")
    lines = ["-nostdlib+", "-nologo", "-target:library", "-langversion:latest", "-debug:embedded",
             "-unsafe+", "-preferreduilang:en", "-nowarn:CS1701,CS1702,CS0162", f'-out:"{out}"']
    lines += [f'-r:"{r}"' for r in compile_references()]
    lines += [f'"{s}"' for s in sources]
    rsp.write_text("\n".join(lines), encoding="utf-8")
    p = subprocess.run([str(DOTNET), str(csc_path()), "-noconfig", f"@{rsp}"], env=dotnet_env(), capture_output=True,
                       text=True, errors="replace")
    if p.returncode != 0:
        # csc prints localized text, so never filter on the word "error".
        for line in dict.fromkeys((p.stdout + p.stderr).splitlines()):
            print(line.replace(str(ROOT) + "\\", ""))
    return p.returncode == 0


SNIPPET = """using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using static SodDevBridge.Api;
public static class __Snippet {{
  public static object Run() {{
#line 1 "snippet"
{body}
#line default
  }}
}}
"""


def snippet_body(code: str, is_file: bool) -> str:
    if not is_file and ";" not in code and "{" not in code:
        return f"return (object)({code});"
    return code if re.search(r"\breturn\b", code) else code + "\nreturn null;"


def compile_snippet(code: str, is_file: bool = False) -> Path | None:
    work = BRIDGE / "snippets"
    work.mkdir(parents=True, exist_ok=True)
    for old in work.glob("*"):  # Fresh files can belong to another running sod.py command.
        if time.time() - old.stat().st_mtime > 300:
            old.unlink(missing_ok=True)
    name = "snip_" + uuid.uuid4().hex[:10]
    src = work / f"{name}.cs"
    src.write_text(SNIPPET.format(body=snippet_body(code, is_file)), encoding="utf-8")
    dll = work / f"{name}.dll"
    return dll if compile_library([src], dll) else None


def run_snippet(code: str, timeout: float = 90) -> str:
    """Harness-internal snippet: returns its value. A failure stops the command."""
    dll = compile_snippet(code, True)
    if not dll:
        die("internal snippet does not compile (a game update changed the API?)", 1)
    data = bridge_call("exec", timeout=timeout, dll=str(dll))
    if data.get("error"):
        die(data["error"], 1)
    return data.get("value", "")


IN_GAME = "SessionData.Instance != null && SessionData.Instance.startedGame && Player.Instance != null"
# The controller exists before the menu scene finishes loading, so the scene name is the real signal.
# waitingForModdedFileLoad stays True in a healthy menu: do not use it.
HAS_MENU = ('UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Main" && MainMenuController.Instance != null '
            '&& ModLoader.Instance != null && ModLoader.Instance.modsLoaded')


def poll_snippet(condition: str, timeout: float, label: str, hold: float = 0) -> bool:
    """Wait until the condition is true for `hold` seconds with no main-thread stall in between.
    A blocked main thread only delays the check; a dead game stops it."""
    dll = compile_snippet(f"return {condition};", True)
    if not dll:
        die("internal snippet does not compile (a game update changed the API?)", 1)
    started = time.time()
    ready_since = None
    while time.time() - started < timeout:
        data = bridge_call("exec", timeout=5 if hold else 20, soft=True, dll=str(dll))
        if data and data.get("value") == "True":
            ready_since = ready_since or time.time()
            if time.time() - ready_since >= hold:
                print(f"{label} in {time.time() - started:.0f}s")
                return True
        else:
            ready_since = None
        time.sleep(2)
    print(f"{label}: not reached in {timeout:.0f}s")
    return False


def ensure_menu(timeout: float = 120) -> bool:
    data = bridge_call("ping", timeout=30)
    if data.get("scene") == "ControllerDetect":
        # The title screen waits for a key and Rewired assigns the controller from it.
        a = argparse.Namespace(keys=["space"], hold=0.1)
        cmd_key(a)
    # The scene becomes "Main" first, then its load blocks the main thread for about 40 s. Calls made before
    # that block are lost or hang a later save load, so the menu must stay responsive for a while.
    return poll_snippet(HAS_MENU, timeout, "main menu", hold=20)


def cmd_menu(a) -> int:
    return 0 if ensure_menu() else 1


def cmd_saves(a) -> int:
    if not ensure_menu():
        return 1
    print(run_snippet("""var mm = MainMenuController.Instance; mm.RefreshSaveEntries();
var names = new List<string>();
for (int i = 0; i < mm.spawnedLoadGames.Count; i++) { var e = mm.spawnedLoadGames[i]; if (e.info != null) names.Add(e.info.Name); }
return string.Join("\\n", names);"""))
    return 0


def cmd_load(a) -> int:
    """Load a save read-only: the game writes to it only when a save command runs."""
    if not ensure_menu():
        return 1
    wanted = json.dumps(a.name, ensure_ascii=False)
    # Same order as the UI: the menu's "Load Game" button fills the save list, then select, then LoadGame sets the
    # flags (loadSaveGame, saveStateFileInfo), then "Start" (mm.loadButton) begins the load.
    run_snippet("""foreach (var b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>()) {
  if (!b.gameObject.activeInHierarchy) continue;
  for (int k = 0; k < b.onClick.GetPersistentEventCount(); k++)
    if (b.onClick.GetPersistentMethodName(k) == "LoadGame") { b.onClick.Invoke(); return "ok"; }
}
throw new Exception("no visible Load Game button");""")
    time.sleep(3)
    print(run_snippet(f"""var mm = MainMenuController.Instance; mm.RefreshSaveEntries();
for (int i = 0; i < mm.spawnedLoadGames.Count; i++) {{
  var e = mm.spawnedLoadGames[i];
  if (e.info != null && e.info.Name.Contains({wanted})) {{ mm.SelectNewSave(e); mm.LoadGame(); mm.loadButton.button.onClick.Invoke(); return "loading " + e.info.Name; }}
}}
throw new Exception("no save matches: " + {wanted});"""))
    if poll_snippet(IN_GAME, a.timeout, "in game"):
        return 0
    print_log(errors_only=True, tail=20)
    return 1


def cmd_exec(a) -> int:
    code = Path(a.file).read_text(encoding="utf-8") if a.file else a.code
    if not code:
        die("nothing to run", 1)
    dll = compile_snippet(code, bool(a.file))
    if not dll:
        return 1
    data = bridge_call("exec", timeout=a.timeout, dll=str(dll))
    if data.get("output"):
        print(data["output"].rstrip())
    if data.get("error"):
        print(data["error"])
        return 1
    if data.get("value") not in (None, "null"):
        print(data["value"])
    return 0


def cmd_test(a) -> int:
    sources = sorted(TESTS_DIR.glob("*.cs")) + sorted(MODS.glob("*/tests/*.cs"))
    if not sources:
        print("no in-game tests")
        return 0
    work = BRIDGE / "tests"
    work.mkdir(parents=True, exist_ok=True)
    for old in work.glob("*"):
        old.unlink()
    out = work / f"tests_{uuid.uuid4().hex[:10]}.dll"
    if not compile_library(sources, out):
        return 1
    if a.compile_only:
        print(f"tests compile OK ({len(sources)} files)")
        return 0
    fields = {"dll": str(out)}
    if a.filter:
        fields["filter"] = a.filter
    data = bridge_call("test", timeout=a.timeout, **fields)
    for r in data.get("results", []):
        print(f"{'PASS' if r['pass'] else 'FAIL'} {r['name']} ({r['seconds']}s)")
        if not r["pass"]:
            print("  " + str(r["error"]).replace("\n", "\n  "))
    if data.get("error"):
        print(data["error"])
        return 1
    print(f"{data.get('total', 0) - data.get('failed', 0)}/{data.get('total', 0)} passed")
    return 0 if data.get("ok") else 1


def cmd_unit(a) -> int:
    """Offline tests: no game, no bridge. Pure code only (extender Pure/, mods/*/src/Rules)."""
    if run_dotnet_build(OFFLINE_PROJECT) != 0:
        print("offline tests: build FAILED (a game type in a pure file fails here)")
        return 1
    if a.build_only:
        print("offline tests: build OK")
        return 0
    args = [str(DOTNET), str(BUILD / OFFLINE_PROJECT.stem / f"{OFFLINE_PROJECT.stem}.dll")]
    if a.filter:
        args += ["--filter", a.filter]
    if a.list:
        args.append("--list")
    return subprocess.run(args, cwd=ROOT, env=dotnet_env()).returncode


# ---------------------------------------------------------------- logs + screenshot

def print_log(errors_only=False, grep=None, tail=None, player=False, mod=None) -> None:
    path = PLAYER_LOG if player else BEPINEX_LOG
    if not path.exists():
        print(f"(no log at {path})")
        return
    shared = subprocess.run(["powershell", "-NoProfile", "-Command",
                             f"Get-Content -LiteralPath '{path}' -Encoding UTF8"],
                            capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    lines = [l for l in shared.splitlines() if player or not LOG_NOISE.search(l)]
    if errors_only:
        lines = [l for l in lines if re.search(r"\[(Error|Fatal)\b|Exception|^\s+at ", l)]
    if mod:
        lines = [l for l in lines if f":{mod}]" in l.replace(" ", "") or mod in l]
    if grep:
        rx = re.compile(grep, re.I)
        lines = [l for l in lines if rx.search(l)]
    if tail:
        lines = lines[-tail:]
    print("\n".join(lines) if lines else "(no matching lines)")


def cmd_log(a) -> int:
    print_log(a.errors, a.grep, a.tail, a.player, a.mod)
    return 0


def cmd_screenshot(a) -> int:
    path = Path(a.path).resolve() if a.path else BRIDGE / "screenshot.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.unlink(missing_ok=True)
    bridge_call("screenshot", path=str(path))
    end = time.time() + 10
    last = -1
    while time.time() < end:
        if path.exists() and path.stat().st_size == last and last > 0:
            print(path)
            return 0
        last = path.stat().st_size if path.exists() else -1
        time.sleep(0.2)
    die("screenshot not written in 10 s")
    return 2


# ---------------------------------------------------------------- input (takes focus from the user!)

user32 = ctypes.WinDLL("user32", use_last_error=True)
user32.GetForegroundWindow.restype = wintypes.HWND
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]


class _MI(ctypes.Structure):
    _fields_ = [("dx", wintypes.LONG), ("dy", wintypes.LONG), ("mouseData", wintypes.DWORD),
                ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class _KI(ctypes.Structure):
    _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD), ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class _U(ctypes.Union):
    _fields_ = [("mi", _MI), ("ki", _KI), ("pad", ctypes.c_byte * 32)]


class _INPUT(ctypes.Structure):
    _fields_ = [("type", wintypes.DWORD), ("u", _U)]


def send_inputs(*items: _INPUT) -> None:
    arr = (_INPUT * len(items))(*items)
    user32.SendInput(len(items), arr, ctypes.sizeof(_INPUT))


def game_window() -> int:
    found = []
    pids = set(game_pids())
    proto = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def visit(hwnd, _):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if pid.value in pids and user32.IsWindowVisible(hwnd):
            found.append(hwnd)
        return True

    user32.EnumWindows(proto(visit), 0)
    if not found:
        die("no game window")
    return found[0]


def focus_game() -> int:
    hwnd = game_window()
    # Windows allows SetForegroundWindow after a synthetic Alt tap.
    alt = _INPUT(1, _U(ki=_KI(0x12, 0, 0, 0, 0)))
    alt_up = _INPUT(1, _U(ki=_KI(0x12, 0, 2, 0, 0)))
    send_inputs(alt, alt_up)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.15)
    return hwnd


VK = {"enter": 0x0D, "esc": 0x1B, "tab": 0x09, "space": 0x20, "backspace": 0x08, "up": 0x26, "down": 0x28,
      "left": 0x25, "right": 0x27, "shift": 0x10, "ctrl": 0x11, "alt": 0x12,
      **{f"f{i}": 0x6F + i for i in range(1, 13)}}


def cmd_key(a) -> int:
    focus_game()
    for name in a.keys:
        vk = VK.get(name.lower()) or (ord(name.upper()) if len(name) == 1 else None)
        if vk is None:
            die(f"unknown key: {name}", 1)
        scan = user32.MapVirtualKeyW(vk, 0)
        send_inputs(_INPUT(1, _U(ki=_KI(vk, scan, 0, 0, 0))))
        time.sleep(a.hold)
        send_inputs(_INPUT(1, _U(ki=_KI(vk, scan, 2, 0, 0))))
        time.sleep(0.08)
    return 0


def cmd_click(a) -> int:
    """X Y are pixels inside the game client area (same pixels as sod.py screenshot)."""
    hwnd = focus_game()
    pt = wintypes.POINT(a.x, a.y)
    user32.ClientToScreen(hwnd, ctypes.byref(pt))
    user32.SetCursorPos(pt.x, pt.y)
    time.sleep(0.1)
    down, up = (0x0008, 0x0010) if a.right else (0x0002, 0x0004)
    send_inputs(_INPUT(0, _U(mi=_MI(0, 0, 0, down, 0, 0))))
    time.sleep(0.05)
    send_inputs(_INPUT(0, _U(mi=_MI(0, 0, 0, up, 0, 0))))
    return 0


# ---------------------------------------------------------------- reference

def compact_signatures(source: str) -> str:
    """Reduce an interop type to its API: drop stub bodies, native pointers and interop attributes."""
    container = re.compile(r"\b(class|struct|enum|interface)\s+\w+")
    out, stack, pending = [], [], ""
    for raw in source.splitlines():
        line = raw.strip()
        if not line or line.startswith(("using ", "[CallerCount", "[CachedScanResults", "[Il2CppImplements")):
            continue
        skipping = "body" in stack
        if line == "{":
            is_container = bool(container.search(pending)) and not skipping
            stack.append("type" if is_container else "body")
            if is_container:
                out.append("  " * (len(stack) - 1) + "{")
            pending = ""
            continue
        if line in ("}", "};"):
            if stack.pop() == "type":
                out.append("  " * len(stack) + "}")
            continue
        if skipping or "NativeFieldInfoPtr_" in line or "NativeMethodInfoPtr_" in line \
                or "Il2CppClassPointer" in line or "static " + stack_name(source) + "()" in line:
            continue
        pending = line
        if not line.startswith("["):
            out.append("  " * len(stack) + line)
    return "\n".join(out)


def stack_name(source: str) -> str:
    m = re.search(r"\b(?:class|struct)\s+(\w+)", source)
    return m.group(1) if m else "?"


def cmd_ref(a) -> int:
    """Decompile the interop Assembly-CSharp. Bodies are IL2CPP stubs: signatures, fields, attributes only."""
    if not ILSPY.exists():
        die(f"ilspycmd not found: {ILSPY}")
    asm = INTEROP / "Assembly-CSharp.dll"
    if a.type:
        p = subprocess.run([str(ILSPY), "-t", a.type, "-r", str(INTEROP), str(asm)], env=dotnet_env(),
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
        print(compact_signatures(p.stdout) if p.stdout.strip() else p.stderr)
        return p.returncode
    DECOMPILED.mkdir(parents=True, exist_ok=True)
    p = subprocess.run([str(ILSPY), "-p", "-o", str(DECOMPILED), "-r", str(INTEROP), str(asm)], env=dotnet_env(),
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    count = sum(1 for _ in DECOMPILED.rglob("*.cs"))
    print(f"{count} files in {DECOMPILED.relative_to(ROOT)}")
    return p.returncode


# ---------------------------------------------------------------- scaffold + package

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>{name}</AssemblyName>
    <RootNamespace>{name}</RootNamespace>
    <OutputType>Library</OutputType>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="src\\**\\*.cs" />
    <!-- Build the extender first: sod.py build SodExtender -->
    <Reference Include="$(GameDir)dev\\build\\SodExtender\\SodExtender.dll" Private="false" />
  </ItemGroup>
</Project>
"""

PLUGIN_CS = """using SodExtender;

namespace {name}
{{
    // The extender applies every [HarmonyPatch] class of this DLL, then calls OnLoad.
    public class Mod : SodMod
    {{
        public override void OnLoad()
        {{
            Log.LogInfo("{name} loaded");
        }}
    }}
}}
"""


def cmd_new_mod(a) -> int:
    if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", a.name):
        die("the mod name must be a C# identifier (Thunderstore also forbids spaces)", 1)
    folder = MODS / a.name
    if folder.exists():
        die(f"{folder} already exists", 1)
    (folder / "src").mkdir(parents=True)
    (folder / "tests").mkdir()
    (folder / f"{a.name}.csproj").write_text(CSPROJ.format(name=a.name), encoding="utf-8")
    (folder / "src" / "Mod.cs").write_text(PLUGIN_CS.format(name=a.name), encoding="utf-8")
    (folder / "manifest.json").write_text(json.dumps({
        "name": a.name, "version_number": "0.1.0", "author": a.author, "description": "",
    }, indent=2), encoding="utf-8")
    (folder / "README.md").write_text(f"# {a.name}\n", encoding="utf-8")
    print(f"created mods/{a.name}. Next: sod.py reload {a.name}")
    return 0


def cmd_package_extender(a) -> int:
    """Create the player's install archive: unpack it into the game folder. Nothing is uploaded.
    It has no generated interop, so a new game version cannot get stale wrappers."""
    if run_dotnet_build(EXTENDER_PROJECT) != 0 or run_dotnet_build(LOADER_PROJECT) != 0:
        return 1
    version = core_versions()[0]
    out = BUILD / "packages" / f"SodExtender-{version}.zip"
    out.parent.mkdir(parents=True, exist_ok=True)
    entries = [ROOT / n for n in ("winhttp.dll", "doorstop_config.ini", ".doorstop_version")]
    # No BepInEx/unity-libs: those are Unity's own libraries, not ours to redistribute.
    # BepInEx downloads them on the first start (UnityBaseLibrariesSource default).
    for folder in (RUNTIME, CORE):
        entries += [p for p in folder.rglob("*") if p.is_file()]
    missing = [str(p) for p in entries[:3] if not p.exists()]
    if missing:
        die(f"missing: {missing}")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for path in entries:
            z.write(path, path.relative_to(ROOT))
        z.write(BUILD / "SodExtender.Loader" / "SodExtender.Loader.dll", "BepInEx/plugins/SodExtender/SodExtender.Loader.dll")
        z.write(BUILD / "SodExtender" / "SodExtender.dll", "BepInEx/plugins/SodExtender/core/SodExtender.dll")
        z.writestr("BepInEx/plugins/SodExtender/core/core.json", core_json())
        z.write(ROOT / "LICENSE", "BepInEx/plugins/SodExtender/LICENSE.txt")
        z.write(ROOT / "dev" / "release" / "THIRD-PARTY-NOTICES.txt", "BepInEx/plugins/SodExtender/THIRD-PARTY-NOTICES.txt")
        z.write(ROOT / "dev" / "release" / "README.txt", "SodExtender-README.txt")
        for lic in sorted((ROOT / "dev" / "release" / "licenses").glob("*.txt")):
            z.write(lic, f"BepInEx/plugins/SodExtender/licenses/{lic.name}")
        names = z.namelist()
    bad = [n for n in names if "unity-libs" in n or n.startswith(("BepInEx/interop", "BepInEx/config", "BepInEx/plugins/SodDevBridge"))]
    if bad:
        out.unlink()
        die(f"archive must not contain: {bad[:5]}")
    print(f"{out} ({out.stat().st_size / 1024 ** 2:.1f} MB, {len(names)} files)")
    return 0


def cmd_package_core(a) -> int:
    """Create the Workshop item folder of the extender core (Core/, not Code/: mod discovery skips it). Nothing is uploaded."""
    if run_dotnet_build(EXTENDER_PROJECT) != 0:
        return 1
    version, api = core_versions()
    out = BUILD / "packages" / "SodExtenderCore"
    shutil.rmtree(out, ignore_errors=True)
    (out / "Core").mkdir(parents=True)
    settings = {"name": "SOD Extender", "version": version, "loadOrderValue": 0, "creator": "XRenso",
                "summary": "Core of SOD Extender. Needs the one-time SOD Extender install (see the item page).", "enabled": True}
    (out / "ModSettings.txt").write_text(json.dumps(settings, indent=1), encoding="utf-8")
    shutil.copy2(BUILD / "SodExtender" / "SodExtender.dll", out / "Core" / "SodExtender.dll")
    (out / "Core" / "core.json").write_text(core_json(), encoding="utf-8")
    shutil.copy2(ROOT / "LICENSE", out / "Core" / "LICENSE.txt")
    print(f"{out} (core {version}, api {api})")
    return 0


def cmd_package(a) -> int:
    """Create the folder to upload through the game's Workshop tool, the same layout a player gets. Nothing is uploaded."""
    project = resolve_projects([a.name])[0]
    if project.parent.parent != MODS:
        die(f"{a.name} is not a mod", 1)
    if run_dotnet_build(project) != 0:
        return 1
    out = BUILD / "packages" / a.name
    shutil.rmtree(out, ignore_errors=True)
    (out / "Code").mkdir(parents=True)
    (out / "ModSettings.txt").write_text(json.dumps(mod_settings(project.parent), indent=1), encoding="utf-8")
    shutil.copy2(BUILD / a.name / f"{a.name}.dll", out / "Code" / f"{a.name}.dll")
    print(out)
    return 0


# ---------------------------------------------------------------- main

def main() -> int:
    ap = argparse.ArgumentParser(prog="sod.py", description=__doc__)
    sub = ap.add_subparsers(dest="command", required=True)

    s = sub.add_parser("package-core", help="create the Workshop item folder of the extender core (no upload)")
    s.set_defaults(fn=cmd_package_core)
    s = sub.add_parser("build", help="compile projects (the game may keep running)")
    s.add_argument("names", nargs="*"); s.set_defaults(fn=cmd_build)
    s = sub.add_parser("deploy", help="build and copy to BepInEx/plugins (game must be closed)")
    s.add_argument("names", nargs="*"); s.set_defaults(fn=cmd_deploy)
    s = sub.add_parser("launch", help="start through Steam: windowed, muted, bridge on")
    s.add_argument("--restart", action="store_true"); s.add_argument("--timeout", type=int, default=300)
    s.set_defaults(fn=cmd_launch)
    s = sub.add_parser("reload", help="build, kill, deploy, launch")
    s.add_argument("names", nargs="*"); s.add_argument("--timeout", type=int, default=300)
    s.set_defaults(fn=cmd_reload)
    sub.add_parser("kill", help="close the game, restore user screen prefs").set_defaults(fn=cmd_kill)
    sub.add_parser("status", help="bridge ping: scene, frame, plugins").set_defaults(fn=cmd_status)
    sub.add_parser("menu", help="pass the title screen, wait for the main menu (takes focus once)").set_defaults(fn=cmd_menu)
    sub.add_parser("saves", help="list saves known to the game").set_defaults(fn=cmd_saves)
    s = sub.add_parser("load", help="load a save by a part of its name, wait until in game")
    s.add_argument("name"); s.add_argument("--timeout", type=float, default=420); s.set_defaults(fn=cmd_load)
    s = sub.add_parser("exec", help="compile C# and run it on the main thread in the running game")
    s.add_argument("code", nargs="?"); s.add_argument("-f", "--file"); s.add_argument("--timeout", type=float, default=30)
    s.set_defaults(fn=cmd_exec)
    s = sub.add_parser("test", help="run dev/tests/ingame + mods/*/tests inside the game")
    s.add_argument("--filter"); s.add_argument("--timeout", type=float, default=600)
    s.add_argument("--compile-only", action="store_true"); s.set_defaults(fn=cmd_test)
    s = sub.add_parser("unit", help="offline tests in ms: dev/tests/offline + mods/*/tests/offline (no game)")
    s.add_argument("--filter"); s.add_argument("--list", action="store_true")
    s.add_argument("--build-only", action="store_true"); s.set_defaults(fn=cmd_unit)
    s = sub.add_parser("log", help="BepInEx log (--player: Unity Player.log)")
    s.add_argument("--errors", action="store_true"); s.add_argument("--grep"); s.add_argument("--tail", type=int)
    s.add_argument("--player", action="store_true"); s.add_argument("--mod"); s.set_defaults(fn=cmd_log)
    s = sub.add_parser("screenshot", help="capture the game frame to PNG")
    s.add_argument("path", nargs="?"); s.set_defaults(fn=cmd_screenshot)
    s = sub.add_parser("key", help="press keys in the game window (takes focus)")
    s.add_argument("keys", nargs="+"); s.add_argument("--hold", type=float, default=0.06); s.set_defaults(fn=cmd_key)
    s = sub.add_parser("click", help="click at client-area pixels (takes focus)")
    s.add_argument("x", type=int); s.add_argument("y", type=int); s.add_argument("--right", action="store_true")
    s.set_defaults(fn=cmd_click)
    s = sub.add_parser("ref", help="decompile game API: no argument = whole project, TYPE = one type")
    s.add_argument("type", nargs="?"); s.set_defaults(fn=cmd_ref)
    s = sub.add_parser("new-mod", help="scaffold mods/<Name>")
    s.add_argument("name"); s.add_argument("--author", default="dev"); s.set_defaults(fn=cmd_new_mod)
    s = sub.add_parser("package", help="create a Workshop-ready mod folder (no upload)")
    s.add_argument("name"); s.set_defaults(fn=cmd_package)
    sub.add_parser("package-extender", help="create the player's install archive (no upload)").set_defaults(fn=cmd_package_extender)

    a = ap.parse_args()
    return a.fn(a)


if __name__ == "__main__":
    sys.exit(main())
