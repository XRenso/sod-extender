#!/usr/bin/env python3
"""PostToolUse hook: compile the project of an edited .cs file.
Exit 2 sends the compiler errors back to Claude. Other files pass silently."""
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOD = str(ROOT / "dev" / "tools" / "sod.py")


def main() -> int:
    try:
        path = json.load(sys.stdin).get("tool_input", {}).get("file_path", "")
    except json.JSONDecodeError:
        return 0
    if not path.endswith(".cs"):
        return 0
    try:
        parts = Path(path).resolve().relative_to(ROOT).parts
    except ValueError:
        return 0

    if "offline" in parts and "tests" in parts:
        args = ["unit", "--build-only"]
    elif parts[:2] == ("dev", "SodDevBridge"):
        args = ["build", "SodDevBridge"]
    elif parts[:2] == ("extender", "SodExtender"):
        args = ["build", "SodExtender"]
    elif parts[:2] == ("extender", "SodExtender.Loader"):
        args = ["build", "SodExtender.Loader"]
    elif parts[:2] == ("dev", "tests") or (parts[0] == "mods" and "tests" in parts):
        args = ["test", "--compile-only"]
    elif parts[0] == "mods" and len(parts) > 2:
        args = ["build", parts[1]]
    else:
        return 0

    p = subprocess.run([sys.executable, SOD, *args], cwd=ROOT, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if p.returncode != 0:
        print(p.stdout + p.stderr, file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
