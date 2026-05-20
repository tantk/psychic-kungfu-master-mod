"""Extract DialogGraph ScriptableObjects (and the nodes they contain) from JinGu's
data.unity3d Unity bundle.

Approach:
1. UnityPy.load() the bundle — slow but unavoidable; this builds the file index.
2. Walk env.container — the path-to-object map — and pick only entries whose path
   starts with "assets/resources/dialog/" (where ResManager.Load looks for
   DialogGraph assets, per decomp/UIUtlils.cs).
3. For each MonoBehaviour matching the filter, dump its typetree to JSON.

Output: data/dialog-graphs/<asset-name>.json — one file per DialogGraph asset.

We don't try to be clever about partial parsing. UnityPy reads the whole bundle
header to enumerate objects, but the per-object .read_typetree() is cheap enough
that ~hundreds of dialog graphs finish in a few minutes once the index is built.
"""
import json
import os
import sys
import time
from pathlib import Path

import UnityPy

REPO = Path(__file__).resolve().parent.parent
BUNDLE = Path(r"C:\Program Files (x86)\Steam\steamapps\common\JinGu\JinGu\JinGu_Data\data.unity3d")
OUT_DIR = REPO / "data" / "dialog-graphs"

DIALOG_PATH_PREFIX = "assets/resources/dialog/"


def main() -> None:
    if not BUNDLE.exists():
        sys.exit(f"bundle not found: {BUNDLE}")
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    print(f"loading bundle ({BUNDLE.stat().st_size / 1e9:.1f} GB)... this can take a few minutes")
    env = UnityPy.load(str(BUNDLE))
    t1 = time.time()
    print(f"  loaded in {t1 - t0:.1f}s — {len(env.container)} container paths total")

    # Filter to dialog assets
    targets = [
        (path, obj)
        for path, obj in env.container.items()
        if path.lower().startswith(DIALOG_PATH_PREFIX)
    ]
    print(f"  {len(targets)} match {DIALOG_PATH_PREFIX}*")

    # Index by PathID so we can resolve cross-references (XNode connections point at
    # other Node objects via their PPtr file_id + path_id).
    all_objs_by_path_id = {obj.path_id: obj for obj in env.objects}

    saved = 0
    skipped = 0
    errors = 0
    for asset_path, obj in targets:
        try:
            tree = obj.read_typetree()
        except Exception as e:
            print(f"  ERROR reading {asset_path}: {e}", file=sys.stderr)
            errors += 1
            continue

        # tree is the raw serialized form. For a DialogGraph this is a dict with
        # at least { "nodes": [PPtr, ...], "m_Name": ..., "m_Script": PPtr }.
        # Pull each PPtr-referenced node's typetree too so the whole graph is in one file.
        resolved = expand_pptrs(tree, all_objs_by_path_id, depth=0, max_depth=4)

        # Use the asset basename for the filename
        name = Path(asset_path).stem
        out = OUT_DIR / f"{name}.json"
        with open(out, "w", encoding="utf-8") as f:
            json.dump(
                {"path": asset_path, "name": name, "data": resolved},
                f,
                ensure_ascii=False,
                indent=2,
                default=_json_default,
            )
        saved += 1
        if saved % 25 == 0:
            print(f"  saved {saved}/{len(targets)}")

    t2 = time.time()
    print(f"done in {t2 - t1:.1f}s · saved {saved}, skipped {skipped}, errors {errors}")
    print(f"output: {OUT_DIR}")


def expand_pptrs(node, by_path_id, depth, max_depth):
    """Walk a typetree dict and inline any PPtr<MonoBehaviour> references by reading the
    target object's typetree too. Bounded depth to prevent runaway recursion on
    self-referential graphs."""
    if depth >= max_depth:
        return node
    if isinstance(node, dict):
        # PPtr looks like { "m_FileID": 0, "m_PathID": <int> }
        if set(node.keys()) >= {"m_PathID", "m_FileID"} and len(node) <= 3:
            path_id = node.get("m_PathID", 0)
            if path_id and path_id in by_path_id:
                target = by_path_id[path_id]
                if target.type.name == "MonoBehaviour":
                    try:
                        return {
                            "__ref": path_id,
                            "__type": "MonoBehaviour",
                            "data": expand_pptrs(target.read_typetree(), by_path_id, depth + 1, max_depth),
                        }
                    except Exception:
                        return {"__ref": path_id, "__error": "read failed"}
                return {"__ref": path_id, "__type": target.type.name}
            return {"__ref": path_id, "__missing": True}
        return {k: expand_pptrs(v, by_path_id, depth, max_depth) for k, v in node.items()}
    if isinstance(node, list):
        return [expand_pptrs(v, by_path_id, depth, max_depth) for v in node]
    return node


def _json_default(o):
    if isinstance(o, bytes):
        return o.decode("utf-8", errors="replace")
    return str(o)


if __name__ == "__main__":
    main()
