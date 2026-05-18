"""Permanent fix: edit the game's PlayerSettings.runInBackground in globalgamemanagers
so Unity itself stops pausing the main thread when the game loses window focus.

Without this, any plugin that needs Unity main-thread access (Harmony patches that
mutate GameObjects, queued main-thread actions, etc.) stops working the moment you
alt-tab to another window — including our own cheat UI.

This patches the build settings directly. One-time tweak; survives across launches
until the game updates and replaces the file. A backup is written next to it.

Usage:
    python tools/patch-run-in-background.py
    python tools/patch-run-in-background.py --restore   # revert to backup

Requires: pip install UnityPy
"""
import argparse
import shutil
from pathlib import Path

import UnityPy

GAME_DIR = Path(r"C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu")
DATA_DIR = GAME_DIR / "JinGu_Data"
# This build bundles everything into data.unity3d instead of separate globalgamemanagers
CANDIDATES = [DATA_DIR / "globalgamemanagers", DATA_DIR / "data.unity3d"]
GGM      = next((p for p in CANDIDATES if p.exists()), CANDIDATES[0])
BACKUP   = GGM.with_name(GGM.name + ".bak")


def patch():
    if not GGM.exists():
        raise SystemExit(f"globalgamemanagers not found at {GGM}")
    if not BACKUP.exists():
        shutil.copy2(GGM, BACKUP)
        print(f"backup written: {BACKUP}")
    else:
        print(f"backup already exists: {BACKUP}")

    env = UnityPy.load(str(GGM))
    touched = False
    for obj in env.objects:
        if obj.type.name != "PlayerSettings":
            continue
        tree = obj.read_typetree()
        current = tree.get("runInBackground", None)
        if current is None:
            # Newer Unity versions may use a different field name
            for k in tree:
                if k.lower().endswith("runinbackground"):
                    current = tree[k]
                    key = k
                    break
            else:
                raise SystemExit("PlayerSettings has no runInBackground field. "
                                 "Dump fields with --dump to investigate.")
        else:
            key = "runInBackground"

        print(f"  current PlayerSettings.{key} = {current}")
        if current in (1, True):
            print("  already enabled. No change needed.")
            return
        tree[key] = 1
        obj.save_typetree(tree)
        touched = True
        print(f"  set {key} = 1")

    if not touched:
        raise SystemExit("No PlayerSettings asset found in globalgamemanagers")

    new_bytes = env.file.save()
    GGM.write_bytes(new_bytes)
    print(f"wrote patched file: {GGM}")
    print("Done. Launch the game; Unity will now keep Update ticking when the window loses focus.")


def restore():
    if not BACKUP.exists():
        raise SystemExit(f"No backup found at {BACKUP}")
    shutil.copy2(BACKUP, GGM)
    print(f"restored {GGM} from {BACKUP}")


def dump():
    env = UnityPy.load(str(GGM))
    for obj in env.objects:
        if obj.type.name == "PlayerSettings":
            tree = obj.read_typetree()
            for k, v in tree.items():
                if isinstance(v, (int, bool, str, float)):
                    print(f"  {k} = {v}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--restore", action="store_true", help="Restore from backup")
    parser.add_argument("--dump", action="store_true", help="Dump PlayerSettings fields")
    args = parser.parse_args()
    if args.restore:   restore()
    elif args.dump:    dump()
    else:              patch()
