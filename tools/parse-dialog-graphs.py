"""Walk AssetStudioMod-dumped MonoBehaviour typetree files and emit branch maps.

Input: data/dialog-graphs/<assetName> @<pathID>.txt
  - Unity-style indented typetree text, one file per MonoBehaviour.
  - The PathID at the end of the filename uniquely identifies the asset within
    its source serialized file (file 0 in our bundle).

Outputs:
  data/branch-maps/<dialogName>.md   - Mermaid flowchart per DialogGraph
  data/branch-maps/_summary.md       - aggregate of every Achieve / dead-end
"""
import re
import sys
from pathlib import Path
from collections import defaultdict

REPO = Path(__file__).resolve().parent.parent
IN_DIR = REPO / "data" / "dialog-graphs"
OUT_DIR = REPO / "data" / "branch-maps"

FILENAME_RE = re.compile(r"^(.+?)\s+@(-?\d+)\.txt$")

# ---------------------------------------------------------------------------
# typetree text parser
# ---------------------------------------------------------------------------

def parse_typetree(text: str) -> dict:
    """Parse AssetStudioMod's typetree-dump text into a nested dict.

    AssetStudio's format has a quirk: for List/Array fields, it emits

        List`1 myfield         <- at indent N
        \tArray Array           <- indent N+1  (the array container marker)
        \tint size = K          <- indent N+1  (sibling-level scalar with the count)
        \t\t[0]                  <- indent N+2  (first array element)
        \t\t<type> data          <- indent N+2  (the element's value — usually `data`)
        \t\t\t...                  <- indent N+3  (sub-fields of the element)
        \t\t[1]
        ...

    Note `Array Array` and `int size` are at the SAME indent (both children of
    `myfield`), but the array elements `[N]` are at indent N+2 — children of the
    `Array Array` marker rather than the outer `myfield`. We special-case this.
    """
    lines = text.splitlines()
    # Drop the "<Type> Base" wrapper line and shift everything left one indent.
    while lines and not lines[0].strip():
        lines.pop(0)
    if lines and lines[0].endswith(" Base"):
        lines = lines[1:]
        lines = [l[1:] if l.startswith("\t") else l for l in lines]
    pos = [0]

    def get_indent(line_str: str) -> int:
        stripped = line_str.lstrip("\t")
        return len(line_str) - len(stripped)

    def parse_block(min_indent: int):
        """Read children at exactly `min_indent` until we hit a smaller indent.
        Handles the Array/size/[N] quirk so the caller gets a clean dict/list."""
        result = {}
        # If we encounter an "Array Array" marker at min_indent, we'll collect
        # the [N] elements that follow at min_indent+1 into this list.
        pending_array = None  # None | list

        while pos[0] < len(lines):
            line = lines[pos[0]]
            stripped = line.lstrip("\t")
            indent = get_indent(line)

            if not stripped:
                pos[0] += 1
                continue
            if indent < min_indent:
                break

            # Lines deeper than min_indent: array elements when we're in array mode
            if indent > min_indent:
                if pending_array is not None and indent == min_indent + 1:
                    # Read this and following [N] markers as elements
                    m_arr = re.match(r"^\[(\d+)\]$", stripped)
                    if m_arr:
                        pos[0] += 1
                        # Element content: next field(s) at the same indent until next [N+1]
                        element = {}
                        while pos[0] < len(lines):
                            nxt = lines[pos[0]]
                            nxt_stripped = nxt.lstrip("\t")
                            nxt_indent = get_indent(nxt)
                            if not nxt_stripped:
                                pos[0] += 1
                                continue
                            if nxt_indent < min_indent + 1:
                                break
                            if nxt_indent == min_indent + 1 and re.match(r"^\[(\d+)\]$", nxt_stripped):
                                break
                            if nxt_indent > min_indent + 1:
                                pos[0] += 1
                                continue
                            # one field at indent min_indent+1
                            if "=" in nxt_stripped:
                                lhs, _, rhs = nxt_stripped.partition("=")
                                parts = lhs.strip().rsplit(None, 1)
                                name = parts[-1] if parts else lhs.strip()
                                element[name] = _coerce(rhs)
                                pos[0] += 1
                            else:
                                parts = nxt_stripped.rsplit(None, 1)
                                name = parts[-1] if len(parts) == 2 else nxt_stripped
                                pos[0] += 1
                                child = parse_block(min_indent + 2)
                                element[name] = _simplify(child)
                        # Most array elements have a single "data" wrapper field — unwrap it
                        if set(element.keys()) == {"data"}:
                            pending_array.append(element["data"])
                        else:
                            pending_array.append(element)
                        continue
                # Unrelated deeper line — skip
                pos[0] += 1
                continue

            # indent == min_indent here

            # Special marker: "Array Array" — switch into array-collection mode
            if stripped == "Array Array":
                if pending_array is None:
                    pending_array = []
                pos[0] += 1
                continue

            # Field with value
            if "=" in stripped:
                lhs, _, rhs = stripped.partition("=")
                parts = lhs.strip().rsplit(None, 1)
                name = parts[-1] if parts else lhs.strip()
                result[name] = _coerce(rhs)
                pos[0] += 1
            else:
                # Field with nested container
                parts = stripped.rsplit(None, 1)
                name = parts[-1] if len(parts) == 2 else stripped
                pos[0] += 1
                child = parse_block(min_indent + 1)
                result[name] = _simplify(child)

        # If we collected array elements in this block, return them as a list.
        # Otherwise return the dict of fields.
        if pending_array is not None:
            return pending_array
        return result

    return parse_block(0)


def _coerce(s: str):
    s = s.strip().strip('"')
    if re.match(r"^-?\d+$", s):
        try:
            return int(s)
        except ValueError:
            return s
    if re.match(r"^-?\d+\.\d+([eE][+-]?\d+)?$", s):
        try:
            return float(s)
        except ValueError:
            return s
    return s


def _simplify(block):
    """If block looks like { 'Array': [...], 'size': N }, return the list directly."""
    if isinstance(block, dict):
        if "Array" in block and isinstance(block["Array"], list):
            return block["Array"]
    return block


# ---------------------------------------------------------------------------
# Node classification — same as before, semantically based on port names
# ---------------------------------------------------------------------------

def get_ports(node: dict) -> dict:
    """Return a dict port_name -> port-dict. XNode's ports field serializes as
    a Unity SerializedDictionary: { keys: [name, ...], values: [NodePort, ...] }."""
    ports = node.get("ports")
    if isinstance(ports, dict):
        keys = ports.get("keys")
        values = ports.get("values")
        if isinstance(keys, list) and isinstance(values, list):
            return dict(zip(keys, values))
        # Fallback shapes
        if all(isinstance(k, str) for k in ports.keys() if k not in ("keys", "values")):
            return {k: v for k, v in ports.items() if isinstance(k, str)}
    return {}


def get_connections(port) -> list:
    """A NodePort has 'connections' = list of { node: PPtr, fieldName: str }."""
    if not isinstance(port, dict):
        return []
    conns = port.get("connections")
    if not isinstance(conns, list):
        return []
    out = []
    for c in conns:
        if isinstance(c, dict):
            node_ref = c.get("node")
            if isinstance(node_ref, dict):
                pid = node_ref.get("m_PathID", 0)
                if pid:
                    out.append(int(pid))
    return out


def classify(node: dict) -> tuple:
    """Return (kind, label) for a node, inferred from port names + extra fields."""
    ports = get_ports(node)
    pnames = set(ports.keys())

    if pnames >= {"Right", "Wrong"}:
        if "m_effectId" in node and "m_value" in node:
            return ("CheckEffectLv", f"check {node.get('m_effectId')} ≥ {node.get('m_value')}?")
        if "m_id" in node and "m_value" in node:
            return ("CheckCamp", f"check npc#{node.get('m_id')} camp == {node.get('m_value')}?")
        if "m_id" in node:
            return ("Check", f"check #{node.get('m_id')}?")
        return ("TwoNode", "if ... ?")

    dyn = [p for p in pnames if p.startswith("m_list ")]
    if dyn:
        ml = node.get("m_list") or []
        if "m_npcId" in node:
            return ("CheckFavo", f"favo npc#{node.get('m_npcId')} ≥ {ml}?")
        if all(isinstance(x, str) for x in ml):
            return ("Choice", "pick: " + " / ".join(str(x)[:20] for x in ml[:5]))
        return ("MultiNode", f"branch ({len(ml)} opts)")

    if pnames == {"Out"}:
        return ("Start", "▶ start")

    if pnames >= {"In", "Out"}:
        if "m_id" in node and "m_value" not in node and "m_list" not in node:
            return ("Achieve_or_ChangeCamp", f"id={node.get('m_id')}")
        if "m_list" in node:
            ml = node.get("m_list") or []
            if ml and isinstance(ml[0], dict) and ("m_id" in ml[0] or "id" in ml[0]):
                return ("AddItem", f"+items ×{len(ml)}")
            return ("Dialog", "💬 dialog")
        return ("Simple", "(simple)")
    return ("Unknown", str(pnames))


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    if not IN_DIR.exists():
        sys.exit(f"input dir not found: {IN_DIR}")
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    files = list(IN_DIR.glob("*.txt"))
    print(f"indexing {len(files)} asset files by path id...")
    by_path_id = {}
    for fp in files:
        m = FILENAME_RE.match(fp.name)
        if m:
            by_path_id[int(m.group(2))] = fp
    print(f"  indexed {len(by_path_id)} files")

    print("first pass: scan for DialogGraph wrappers (assets with `nodes` field)...")
    dialog_graphs = []
    cache = {}  # pathID -> parsed dict
    for path_id, fp in by_path_id.items():
        try:
            text = fp.read_text(encoding="utf-8", errors="replace")
        except Exception:
            continue
        # Cheap pre-filter: only assets with `nodes` near the top are graphs
        if "List`1 nodes" not in text and "\tList`1 nodes\n" not in text:
            continue
        try:
            data = parse_typetree(text)
        except Exception as e:
            continue
        cache[path_id] = data
        nodes = data.get("nodes")
        if isinstance(nodes, list) and nodes:
            dialog_graphs.append((path_id, data))
    print(f"  found {len(dialog_graphs)} DialogGraph wrappers")

    if not dialog_graphs:
        print("\nno graphs found. Showing first asset to verify parser output:")
        first = next(iter(by_path_id.values()))
        text = first.read_text(encoding="utf-8")[:600]
        print(text)
        return

    all_achievements = defaultdict(list)
    emitted = 0
    for graph_id, gdata in dialog_graphs:
        try:
            md, ach, name = render_graph(graph_id, gdata, by_path_id, cache)
            safe = re.sub(r'[^\w一-鿿\-]', '_', name)[:60]
            # Append PathID to disambiguate — many graphs share the default name "DialogGragh"
            (OUT_DIR / f"{safe}_pid{graph_id}.md").write_text(md, encoding="utf-8")
            emitted += 1
            for aid, p in ach:
                all_achievements[aid].append((name, p))
            if emitted % 50 == 0:
                print(f"  emitted {emitted}/{len(dialog_graphs)}")
        except Exception as e:
            print(f"  ERROR on graph {graph_id}: {e}", file=sys.stderr)

    lines = [f"# Dialog branch summary\n\nGenerated from {emitted} DialogGraphs in `data/dialog-graphs/`.\n\n## Achievements unlocked through dialog choices\n\n"]
    for aid, hits in sorted(all_achievements.items()):
        lines.append(f"### Achievement / Camp-change `#{aid}`\n\n")
        for name, p in hits[:10]:
            lines.append(f"- `{name}` :  {p}\n")
        if len(hits) > 10:
            lines.append(f"- ... ({len(hits) - 10} more graphs trigger this)\n")
        lines.append("\n")
    (OUT_DIR / "_summary.md").write_text("".join(lines), encoding="utf-8")
    print(f"\ndone — {emitted} per-graph maps + 1 summary in {OUT_DIR}")


def render_graph(graph_id, graph_data, by_path_id, cache):
    name = graph_data.get("m_Name") or f"dialog_{graph_id}"
    out = [f"# `{name}` (pathId {graph_id})\n\n```mermaid\nflowchart TD\n"]

    nodes_pptr = graph_data.get("nodes") or []
    node_data = {}
    for pptr in nodes_pptr:
        pid = pptr.get("data", {}).get("m_PathID", 0) if isinstance(pptr, dict) else 0
        if not pid:
            # AssetStudio sometimes flattens PPtr to just {m_FileID, m_PathID}
            if isinstance(pptr, dict):
                pid = pptr.get("m_PathID", 0)
        if pid and pid in by_path_id:
            if pid not in cache:
                try:
                    cache[pid] = parse_typetree(by_path_id[pid].read_text(encoding="utf-8", errors="replace"))
                except Exception:
                    continue
            node_data[pid] = cache[pid]

    # Find start node
    start_id = None
    for pid, nd in node_data.items():
        if set(get_ports(nd).keys()) == {"Out"}:
            start_id = pid
            break
    if start_id is None and node_data:
        start_id = next(iter(node_data.keys()))

    visited = set()
    ach = []
    walk(start_id, node_data, out, visited, ach, 0, "")
    out.append("```\n")
    return "".join(out), ach, name


def walk(node_id, node_data, out, visited, achievements, depth, path_desc):
    if node_id is None or node_id in visited or depth > 200:
        return
    visited.add(node_id)
    node = node_data.get(node_id)
    if not node:
        return
    kind, label = classify(node)
    safe = label.replace('"', "'").replace("|", "/")[:80]
    out.append(f'  N{node_id}["{kind}: {safe}"]\n')

    if kind == "Achieve_or_ChangeCamp" and "m_id" in node:
        achievements.append((node["m_id"], path_desc or "(start)"))

    ports = get_ports(node)
    for pname in sorted(p for p in ports if p != "In"):
        for target_id in get_connections(ports[pname]):
            if target_id not in node_data:
                continue
            edge = pname
            if pname.startswith("m_list "):
                idx = pname.split(" ")[-1]
                ml = node.get("m_list") or []
                try:
                    edge = f"[{idx}] {str(ml[int(idx)])[:30]}"
                except (IndexError, ValueError):
                    pass
            safe_edge = edge.replace("|", "/").replace('"', "'")[:40]
            out.append(f"  N{node_id} -->|{safe_edge}| N{target_id}\n")
            walk(target_id, node_data, out, visited, achievements, depth + 1,
                 (path_desc + " > " + safe_edge) if path_desc else safe_edge)


if __name__ == "__main__":
    main()
