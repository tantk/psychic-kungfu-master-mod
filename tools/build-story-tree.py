"""Build the master story-tree Mermaid diagram for 今古群侠传.

Strategy (derived from the data we already have):

  The game's story flows linearly through ~40 "Interlude_N" chapter cutscenes.
  Most are linear (no choices). The ones that DO have choices are the major
  divergence points. The very last (Interlude_36) is the master ending
  cutscene which fans out into 9 distinct endings based on accumulated state
  (which NPCs are romanced, alignment, etc.).

This script:
  1. Reads the Mermaid renderings of every Interlude_* graph
  2. Extracts choice text + achievement IDs from each
  3. Reads achieve.json for ending names
  4. Reads book.json for romance-interest NPC names
  5. Emits one big Mermaid flowchart that:
     - Lays out chapters in order
     - Shows choice splits where they occur
     - Annotates each chapter with the achievements unlocked there
     - Fans out into the 9 endings at the bottom

Output: data/branch-viewer/story-tree.json  (consumed by the viewer)
"""
import json
import re
import sys
from pathlib import Path
from collections import defaultdict

REPO = Path(__file__).resolve().parent.parent
MAPS_DIR = REPO / "data" / "branch-maps"
ACHIEVE = REPO / "data" / "achieve.json"
BOOK = REPO / "data" / "book.json"
OUT_FILE = REPO / "data" / "branch-viewer" / "story-tree.json"


def main() -> None:
    if not MAPS_DIR.exists():
        sys.exit(f"{MAPS_DIR} not found — run parse-dialog-graphs.py first")

    achieve_names = {}
    if ACHIEVE.exists():
        for r in json.loads(ACHIEVE.read_text(encoding="utf-8")):
            if r.get("name"):
                achieve_names[r["id"]] = r["name"]

    book_names = {}
    if BOOK.exists():
        for r in json.loads(BOOK.read_text(encoding="utf-8")):
            name = r.get("name")
            if name and name != "虾米":
                book_names[r["id"]] = name

    # Find every Interlude_N graph; key by N for ordering.
    interlude_files = {}
    for fp in MAPS_DIR.glob("Interlude_*.md"):
        m = re.match(r"^Interlude_(\d+)_pid", fp.name)
        if m:
            interlude_files[int(m.group(1))] = fp

    if not interlude_files:
        sys.exit("no Interlude_* graphs found in branch-maps/")

    chapters = []
    for n in sorted(interlude_files):
        info = analyze_chapter(n, interlude_files[n], achieve_names, book_names)
        chapters.append(info)

    # Build the Mermaid source for the master tree
    mermaid = render_tree(chapters, achieve_names, book_names)

    OUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    OUT_FILE.write_text(json.dumps({
        "mermaid": mermaid,
        "chapters": chapters,
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"wrote {OUT_FILE}")
    print(f"chapters analyzed: {len(chapters)}")
    print(f"total mermaid lines: {mermaid.count(chr(10)) + 1}")


def analyze_chapter(n, path, achieve_names, book_names):
    """Extract chapter metadata: choices, checks, achievements triggered."""
    text = path.read_text(encoding="utf-8")
    # Parse Mermaid nodes
    nodes = {}
    edges = []
    for line in text.splitlines():
        m = re.match(r'^\s*(N\d+)\["([^:]+):\s*([^"]*)"\]\s*$', line)
        if m:
            nodes[m.group(1)] = (m.group(2), m.group(3))
            continue
        m = re.match(r'^\s*(N\d+)\s+-->\|([^|]+)\|\s*(N\d+)', line)
        if m:
            edges.append((m.group(1), m.group(2), m.group(3)))

    choices = [(nid, label) for nid, (kind, label) in nodes.items() if kind == "Choice"]
    # Each Choice has multiple outgoing m_list edges (the choice options)
    choice_details = []
    for nid, label in choices:
        opts = [e for e in edges if e[0] == nid]
        choice_details.append({
            "label": label,
            "options": [e[1] for e in opts],
        })

    checks = []
    npc_check_ids = []
    for nid, (kind, label) in nodes.items():
        if kind.startswith("Check"):
            checks.append({"kind": kind, "label": label})
            # Parse CheckCamp npc id
            m = re.search(r"npc#(\d+)", label)
            if m:
                npc_check_ids.append(int(m.group(1)))

    # Achievements triggered
    achievements = []
    for nid, (kind, label) in nodes.items():
        if kind == "Achieve_or_ChangeCamp":
            m = re.search(r"id=(\d+)", label)
            if m:
                aid = int(m.group(1))
                achievements.append({
                    "id": aid,
                    "name": achieve_names.get(aid, ""),
                })

    return {
        "n": n,
        "name": f"Interlude_{n}",
        "node_count": len(nodes),
        "choices": choice_details,
        "checks": checks,
        "achievements": achievements,
        "romance_npcs": [book_names.get(nid, f"NPC#{nid}") for nid in sorted(set(npc_check_ids))],
    }


# Manually curated ending list — IDs verified via achieve.json + summary cross-ref.
# Names taken from data/achieve.json. Order chosen by alignment + outcome.
ENDINGS = [
    (200217, "浩气长存", "Righteous Spirit Endures"),
    (200216, "情圣·正", "Lover · Righteous"),
    (200218, "清风在世", "Pure Wind Manifests"),
    (200212, "四海升平", "Four Seas Peace"),
    (200211, "圣威盖世", "Sacred Authority Unparalleled"),
    (200210, "情圣·王", "Lover · 福王 Path"),
    (200215, "凶威赫赫", "Fearsome Authority"),
    (200213, "情圣·邪", "Lover · Evil"),
    (200214, "魔焰滔天", "Demonic Flames"),
]


def render_tree(chapters, achieve_names, book_names):
    """Render a Mermaid flowchart of the full story tree."""
    lines = [
        "flowchart TD",
        "  classDef start fill:#c1463a,stroke:#fff,color:#fff",
        "  classDef chapter fill:#3a2f1f,stroke:#c9893d,color:#e9dab2",
        "  classDef choice fill:#5d4524,stroke:#e0a85a,color:#e9dab2",
        "  classDef ending fill:#8a4e1a,stroke:#fff,color:#fff,stroke-width:2px",
        "  classDef ach fill:#1f4257,stroke:#98c2a8,color:#b6c4b0",
        "",
        '  START["▶ 新晋掌门<br/>Game Start<br/>(Niu Family Village)"]:::start',
    ]

    # Walk chapters in order, connecting them linearly. At each chapter with
    # choices, emit a Choice node (or two) and split.
    last_node = "START"
    for ch in chapters:
        node_id = f"CH{ch['n']}"
        ach_summary = ""
        if ch['achievements']:
            # Show up to 3 achievement names
            named = [a for a in ch['achievements'] if a['name']]
            if named:
                ach_summary = "<br/><small>🏆 " + " · ".join(a['name'] for a in named[:3])
                if len(named) > 3:
                    ach_summary += f" +{len(named) - 3}"
                ach_summary += "</small>"

        # Special handling for Interlude_36 (the final cutscene with 9 endings)
        if ch['n'] == 36:
            lines.append(f'  {node_id}["💎 Interlude_36 — 终章 Final Cutscene<br/><small>(state-driven branching:<br/>'
                         f'14 NPC romance checks +<br/>3 stat checks + 1 final 愿意 choice)</small>"]:::ending')
            lines.append(f"  {last_node} --> {node_id}")
            # Fan out to endings
            for aid, name_zh, name_en in ENDINGS:
                eid = f"END{aid}"
                lines.append(f'  {eid}["🏁 {name_zh}<br/>{name_en}"]:::ending')
                lines.append(f"  {node_id} ==> {eid}")
            # Add side-endings from Interlude_40 / 42 / 44 if they have unique achievements
            continue

        # Regular chapter
        title = f"Interlude_{ch['n']}"
        # Pick a one-line summary based on choices
        choice_summary = ""
        if ch['choices']:
            first_opts = ch['choices'][0]['options']
            opts_short = " / ".join(o[:18] for o in first_opts[:3])
            choice_summary = f"<br/><small>⚖️ {opts_short}</small>"

        label = f"{title}{choice_summary}{ach_summary}"
        cls = "choice" if ch['choices'] else "chapter"
        lines.append(f'  {node_id}["{label}"]:::{cls}')
        lines.append(f"  {last_node} --> {node_id}")

        # If multiple choices, emit each as a labeled branch (visual only — they recombine downstream).
        # Keep last_node as the chapter so the next chapter follows it directly.
        last_node = node_id

    return "\n".join(lines)


if __name__ == "__main__":
    main()
