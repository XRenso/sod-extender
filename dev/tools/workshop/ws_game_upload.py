"""Starts one upload through the game's uploader and watches for the result. Never starts a second upload."""
import re
import subprocess
import sys
import time
from pathlib import Path

sp, root = Path(sys.argv[1]), Path(sys.argv[2])
SOD = ["py", str(root / "dev" / "tools" / "sod.py")]


def sod(*args):
    p = subprocess.run([*SOD, *args], capture_output=True, text=True, encoding="utf-8", errors="replace", cwd=root)
    return p.returncode, (p.stdout + p.stderr).strip()


code = (sp / "ws_game_upload.tmpl").read_text(encoding="utf-8") \
    .replace("@FOLDER@", str(root / "dev" / "build" / "packages" / "ExampleMod").replace("/", "\\")) \
    .replace("@PREVIEW@", str(sp / "preview.png"))
path = sp / "ws_game_upload_now.cs"
path.write_text(code, encoding="utf-8")
rc, out = sod("exec", "-f", str(path))
print(out[:800])
if rc != 0:
    sys.exit(1)

state = ('var c = Resources.FindObjectsOfTypeAll<SteamWorkshopController>().FirstOrDefault(); '
         'return "id=" + (c == null ? 0 : c.publishedFileID.m_PublishedFileId) + " guard=" + PrivateUploads;')
pattern = "Mods: (Workshop|Update Item|Started upload|Mod upload|Serializ)|legal|Steam"
log = ""
for i in range(30):
    time.sleep(4)
    rc, out = sod("exec", state)
    rc2, log = sod("log", "--player", "--grep", pattern, "--tail", "6")
    print(f"t={(i + 1) * 4}s {out[:80]}")
    if re.search(r"Update Item success|Mod upload success|item update failed|item creation failed", log):
        break
else:
    print("no final result in 120 s")
print(log[:900])
