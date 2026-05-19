"""Build a self-contained HTML viewer for the dialog-graph branch maps.

Reads:
  data/branch-maps/*.md         (1,737 Mermaid flowcharts)
  data/branch-maps/_summary.md  (achievement-keyed index)
  data/achieve.json             (achievement names, for annotation)
  data/item.json                (item names, for cross-reference)

Writes:
  data/branch-viewer/index.html

The output is a single ~5 MB HTML file with every graph embedded as JSON.
Open it with `python -m http.server` in that folder, or just double-click
(file:// works because we never call fetch()).
"""
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
MAPS_DIR = REPO / "data" / "branch-maps"
ACHIEVE = REPO / "data" / "achieve.json"
ITEM = REPO / "data" / "item.json"
OUT_DIR = REPO / "data" / "branch-viewer"
OUT_FILE = OUT_DIR / "index.html"

MERMAID_BLOCK = re.compile(r"```mermaid\n(.*?)\n```", re.S)
FILENAME_RE = re.compile(r"^(.+?)_pid(-?\d+)\.md$")


def main() -> None:
    if not MAPS_DIR.exists():
        sys.exit(f"{MAPS_DIR} not found — run extract + parse first")
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    achieve_names = {}
    if ACHIEVE.exists():
        for r in json.loads(ACHIEVE.read_text(encoding="utf-8")):
            if r.get("name"):
                achieve_names[r["id"]] = r["name"]

    item_names = {}
    if ITEM.exists():
        for r in json.loads(ITEM.read_text(encoding="utf-8")):
            if r.get("name"):
                item_names[r["id"]] = r["name"]

    graphs = []
    for fp in sorted(MAPS_DIR.glob("*.md")):
        if fp.name == "_summary.md":
            continue
        m = FILENAME_RE.match(fp.name)
        if not m:
            continue
        name = m.group(1)
        path_id = int(m.group(2))
        text = fp.read_text(encoding="utf-8")
        mer = MERMAID_BLOCK.search(text)
        if not mer:
            continue
        mermaid_src = mer.group(1)
        # Quick metrics: node count + branch count (lines with `-->`)
        node_count = len(re.findall(r"^\s*N\d+\[", mermaid_src, re.M))
        edge_count = mermaid_src.count("-->")
        # Heuristic: a graph "branches" if any node has >1 outgoing edge.
        # Counting edges per source N{id} → if any source appears >1×, it's branchy.
        sources = re.findall(r"^\s*(N\d+)\s+-->", mermaid_src, re.M)
        from collections import Counter
        sc = Counter(sources)
        branch_points = sum(1 for v in sc.values() if v > 1)
        # Choice / Check / Achieve counts for filter facets
        choice_count = mermaid_src.count('"Choice:')
        check_count = mermaid_src.count('"Check')
        achieve_count = mermaid_src.count('"Achieve_or_ChangeCamp:')
        graphs.append({
            "name": name,
            "pid": path_id,
            "mermaid": mermaid_src,
            "nodes": node_count,
            "edges": edge_count,
            "branches": branch_points,
            "choices": choice_count,
            "checks": check_count,
            "achievements": achieve_count,
        })

    print(f"loaded {len(graphs)} graphs")

    # Parse _summary.md into achievement → [(graph_name, path), ...]
    summary_data = {}
    summary_file = MAPS_DIR / "_summary.md"
    if summary_file.exists():
        text = summary_file.read_text(encoding="utf-8")
        cur_ach = None
        for line in text.splitlines():
            m = re.match(r"^### Achievement / Camp-change `#(\d+)`", line)
            if m:
                cur_ach = int(m.group(1))
                summary_data.setdefault(cur_ach, [])
                continue
            m = re.match(r"^- `(.+?)` :\s+(.+)$", line)
            if m and cur_ach is not None:
                summary_data[cur_ach].append({"graph": m.group(1), "path": m.group(2)})

    # Convert achievement keys to a list with names + paths
    achievements = []
    for aid, hits in sorted(summary_data.items()):
        achievements.append({
            "id": aid,
            "name": achieve_names.get(aid, ""),  # may be empty if id is actually a Book/Npc id (ChangeCampNode)
            "item_name": item_names.get(aid, ""),  # secondary guess
            "hits": hits,
        })
    print(f"loaded {len(achievements)} achievement/camp-change entries from summary")

    html = build_html(graphs, achievements)
    OUT_FILE.write_text(html, encoding="utf-8")
    size_mb = OUT_FILE.stat().st_size / 1e6
    print(f"\nwrote {OUT_FILE}  ({size_mb:.1f} MB)")
    print("\nopen with: file://" + str(OUT_FILE).replace('\\', '/'))
    print("(or `python -m http.server` in that folder and visit http://localhost:8000/)")


def build_html(graphs, achievements):
    # Embed data as JSON inside a script tag — JSON.parse is faster than JS literal
    graphs_json = json.dumps(graphs, ensure_ascii=False, separators=(',', ':'))
    achievements_json = json.dumps(achievements, ensure_ascii=False, separators=(',', ':'))
    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<title>今古群侠传 · 剧情分支查看器 (private)</title>
<style>
  * {{ box-sizing: border-box; }}
  html, body {{ margin: 0; height: 100%; font-family: 'Segoe UI', 'Noto Sans SC', system-ui, sans-serif; background: #1a1612; color: #d6c596; font-size: 14px; }}
  #app {{ display: grid; grid-template-columns: 360px 1fr; height: 100vh; }}
  .sidebar {{ background: #221c14; border-right: 1px solid #4a3a1f; overflow: hidden; display: flex; flex-direction: column; }}
  .sidebar h1 {{ margin: 0; padding: 14px 16px; font-size: 14px; color: #c9893d; border-bottom: 1px solid #4a3a1f; letter-spacing: 0.04em; }}
  .filters {{ padding: 10px 12px; border-bottom: 1px solid #3a2f1f; display: flex; flex-direction: column; gap: 8px; }}
  .filters input, .filters select {{ background: #14110a; color: #e9dab2; border: 1px solid #4a3a1f; border-radius: 3px; padding: 6px 8px; font-size: 13px; outline: none; }}
  .filters input:focus, .filters select:focus {{ border-color: #c9893d; }}
  .mode-tabs {{ display: flex; gap: 2px; padding: 0 12px 8px; }}
  .mode-tabs button {{ background: transparent; color: #8e7a4e; border: 1px solid #4a3a1f; padding: 4px 10px; font-size: 12px; cursor: pointer; flex: 1; }}
  .mode-tabs button.active {{ color: #c9893d; border-color: #c9893d; }}
  .list {{ overflow-y: auto; flex: 1; }}
  .item {{ padding: 8px 14px; border-bottom: 1px solid rgba(74,58,31,0.4); cursor: pointer; }}
  .item:hover {{ background: rgba(201,137,61,0.08); }}
  .item.active {{ background: rgba(201,137,61,0.15); border-left: 3px solid #c9893d; padding-left: 11px; }}
  .item .name {{ color: #e9dab2; font-weight: 500; }}
  .item .meta {{ font-size: 11px; color: #8e7a4e; margin-top: 2px; font-family: monospace; }}
  .ach-item {{ padding: 8px 14px; border-bottom: 1px solid rgba(74,58,31,0.4); cursor: pointer; }}
  .ach-item:hover {{ background: rgba(201,137,61,0.08); }}
  .ach-item .ach-id {{ color: #c9893d; font-weight: 600; font-family: monospace; }}
  .ach-item .ach-name {{ color: #e9dab2; margin-left: 6px; }}
  .ach-item .ach-count {{ float: right; color: #8e7a4e; font-size: 11px; }}
  .main {{ overflow: auto; padding: 24px; }}
  .header {{ display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 16px; }}
  .header h2 {{ margin: 0; color: #c9893d; font-size: 22px; }}
  .header .stats {{ color: #8e7a4e; font-size: 12px; font-family: monospace; }}
  .render-area {{ background: #14110a; border: 1px solid #4a3a1f; border-radius: 4px; padding: 20px; min-height: 200px; overflow: auto; }}
  .render-area svg {{ max-width: 100%; height: auto; }}
  .empty {{ color: #6a553a; font-style: italic; text-align: center; padding: 40px; }}
  .badge {{ display: inline-block; padding: 1px 7px; margin: 0 3px; border-radius: 8px; background: rgba(201,137,61,0.15); color: #c9893d; font-size: 11px; font-family: monospace; }}
  .badge.choice {{ background: rgba(152,194,168,0.12); color: #98c2a8; }}
  .badge.check {{ background: rgba(193,70,58,0.13); color: #d96a5d; }}
  .badge.achieve {{ background: rgba(224,168,90,0.18); color: #e0a85a; }}
  .achievement-detail {{ padding: 12px 0; }}
  .ach-path-list {{ margin-top: 12px; }}
  .ach-path {{ margin: 6px 0; padding: 8px 12px; background: rgba(20,17,10,0.6); border-left: 3px solid #c9893d; font-family: monospace; font-size: 12px; line-height: 1.5; cursor: pointer; }}
  .ach-path:hover {{ background: rgba(201,137,61,0.08); }}
  .ach-path .graph-name {{ color: #c9893d; font-weight: 600; }}
  .ach-path .path-text {{ color: #b6c4b0; }}
  .count {{ color: #8e7a4e; padding: 6px 12px; font-size: 11px; border-bottom: 1px solid #3a2f1f; font-family: monospace; }}
</style>
<script src="https://cdn.jsdelivr.net/npm/mermaid@10.9.1/dist/mermaid.min.js"></script>
</head>
<body>
<div id="app">
  <div class="sidebar">
    <h1>今古群侠传 · 剧情分支</h1>
    <div class="mode-tabs">
      <button class="active" data-mode="graphs">分支图 ({len(graphs)})</button>
      <button data-mode="achievements">成就路径 ({len(achievements)})</button>
    </div>
    <div class="filters" id="filters-graphs">
      <input id="q-graphs" placeholder="搜索图名 / id...">
      <select id="sort-graphs">
        <option value="branches">按分支点数排序</option>
        <option value="nodes">按节点数排序</option>
        <option value="name">按名称排序</option>
        <option value="achievements">按成就触发数排序</option>
      </select>
      <label style="font-size:12px;color:#8e7a4e">
        <input type="checkbox" id="only-branchy"> 只显示有分支的
      </label>
    </div>
    <div class="filters" id="filters-ach" style="display:none">
      <input id="q-ach" placeholder="搜索成就 ID 或名称...">
    </div>
    <div class="count" id="count">—</div>
    <div class="list" id="list"></div>
  </div>
  <div class="main">
    <div id="render"><div class="empty">← pick a graph or achievement from the sidebar</div></div>
  </div>
</div>

<script>
const GRAPHS = JSON.parse(document.getElementById('graph-data').textContent);
const ACHIEVEMENTS = JSON.parse(document.getElementById('ach-data').textContent);

mermaid.initialize({{ startOnLoad: false, theme: 'dark', themeVariables: {{
  primaryColor: '#221c14', primaryTextColor: '#e9dab2',
  primaryBorderColor: '#c9893d', lineColor: '#8e7a4e',
  secondaryColor: '#2a2117', tertiaryColor: '#3a2f1f',
}}, flowchart: {{ useMaxWidth: false, htmlLabels: true, curve: 'basis' }} }});

let mode = 'graphs';
let activeIdx = null;

function $(s) {{ return document.querySelector(s); }}
function $$(s) {{ return Array.from(document.querySelectorAll(s)); }}

function renderSidebar() {{
  const list = $('#list');
  const count = $('#count');
  list.innerHTML = '';
  if (mode === 'graphs') {{
    const q = $('#q-graphs').value.toLowerCase().trim();
    const sort = $('#sort-graphs').value;
    const onlyBranchy = $('#only-branchy').checked;
    let rows = GRAPHS.filter(g => {{
      if (onlyBranchy && g.branches === 0) return false;
      if (!q) return true;
      return g.name.toLowerCase().includes(q) || String(g.pid).includes(q);
    }});
    rows.sort((a, b) => {{
      if (sort === 'name') return a.name.localeCompare(b.name);
      return b[sort] - a[sort];
    }});
    count.textContent = `${{rows.length}} / ${{GRAPHS.length}} graphs`;
    rows.forEach((g, idx) => {{
      const el = document.createElement('div');
      el.className = 'item';
      if (activeIdx === g.pid) el.classList.add('active');
      el.innerHTML = `
        <div class="name">${{escapeHtml(g.name)}}</div>
        <div class="meta">pid ${{g.pid}}
          <span class="badge">${{g.nodes}} nodes</span>
          ${{g.branches ? `<span class="badge">${{g.branches}} br</span>` : ''}}
          ${{g.choices ? `<span class="badge choice">${{g.choices}} choice</span>` : ''}}
          ${{g.checks ? `<span class="badge check">${{g.checks}} check</span>` : ''}}
          ${{g.achievements ? `<span class="badge achieve">${{g.achievements}} ach</span>` : ''}}
        </div>`;
      el.addEventListener('click', () => {{ activeIdx = g.pid; selectGraph(g); renderSidebar(); }});
      list.appendChild(el);
    }});
  }} else {{
    const q = $('#q-ach').value.toLowerCase().trim();
    let rows = ACHIEVEMENTS.filter(a => {{
      if (!q) return true;
      return String(a.id).includes(q) || (a.name && a.name.toLowerCase().includes(q));
    }});
    count.textContent = `${{rows.length}} / ${{ACHIEVEMENTS.length}} achievements`;
    rows.forEach((a) => {{
      const el = document.createElement('div');
      el.className = 'ach-item';
      if (activeIdx === 'ach-' + a.id) el.classList.add('active');
      const name = a.name || a.item_name || '<i style="color:#6a553a">(unnamed — likely ChangeCamp NPC id)</i>';
      el.innerHTML = `
        <span class="ach-count">${{a.hits.length}} path${{a.hits.length === 1 ? '' : 's'}}</span>
        <span class="ach-id">#${{a.id}}</span>
        <span class="ach-name">${{name}}</span>`;
      el.addEventListener('click', () => {{ activeIdx = 'ach-' + a.id; selectAchievement(a); renderSidebar(); }});
      list.appendChild(el);
    }});
  }}
}}

function selectGraph(g) {{
  const main = $('#render');
  main.innerHTML = `
    <div class="header">
      <h2>${{escapeHtml(g.name)}}</h2>
      <div class="stats">pid ${{g.pid}}  ·  ${{g.nodes}} nodes  ·  ${{g.edges}} edges  ·  ${{g.branches}} branch points</div>
    </div>
    <div class="render-area"><div id="mermaid-target" class="mermaid">${{escapeHtml(g.mermaid)}}</div></div>`;
  // Render the mermaid block
  try {{
    mermaid.run({{ nodes: [document.getElementById('mermaid-target')] }});
  }} catch (e) {{
    $('#render .render-area').innerHTML = `<div style="color:#d96a5d">渲染失败 (graph too large or invalid Mermaid syntax): ${{e.message}}</div><pre style="font-size:11px;color:#8e7a4e;white-space:pre-wrap">${{escapeHtml(g.mermaid)}}</pre>`;
  }}
}}

function selectAchievement(a) {{
  const main = $('#render');
  const name = a.name || a.item_name || '<i>(unnamed — possibly a ChangeCamp NPC id)</i>';
  main.innerHTML = `
    <div class="header">
      <h2>#${{a.id}}  ${{name}}</h2>
      <div class="stats">${{a.hits.length}} dialog path${{a.hits.length === 1 ? '' : 's'}} trigger this</div>
    </div>
    <div class="achievement-detail">
      <p style="color:#8e7a4e;font-size:13px">Each row below is one dialog graph + the sequence of choices that lead to this achievement (or NPC camp-change). Click to render that graph.</p>
      <div class="ach-path-list" id="ach-paths"></div>
    </div>`;
  const container = $('#ach-paths');
  a.hits.forEach(hit => {{
    const div = document.createElement('div');
    div.className = 'ach-path';
    div.innerHTML = `<span class="graph-name">${{escapeHtml(hit.graph)}}</span>: <span class="path-text">${{escapeHtml(hit.path)}}</span>`;
    div.addEventListener('click', () => {{
      // Find the matching graph by name (first match wins; duplicates may exist)
      const found = GRAPHS.find(g => g.name === hit.graph);
      if (found) {{ activeIdx = found.pid; selectGraph(found); renderSidebar(); }}
    }});
    container.appendChild(div);
  }});
}}

function escapeHtml(s) {{
  return String(s ?? '').replace(/[&<>"']/g, c => ({{ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }}[c]));
}}

// Mode tabs
$$('.mode-tabs button').forEach(btn => btn.addEventListener('click', () => {{
  $$('.mode-tabs button').forEach(b => b.classList.remove('active'));
  btn.classList.add('active');
  mode = btn.dataset.mode;
  $('#filters-graphs').style.display = mode === 'graphs' ? 'flex' : 'none';
  $('#filters-ach').style.display = mode === 'achievements' ? 'flex' : 'none';
  activeIdx = null;
  renderSidebar();
}}));

['q-graphs', 'sort-graphs', 'only-branchy', 'q-ach'].forEach(id => {{
  const el = document.getElementById(id);
  if (el) el.addEventListener('input', renderSidebar);
  if (el && el.tagName === 'SELECT') el.addEventListener('change', renderSidebar);
}});

renderSidebar();
</script>
<script id="graph-data" type="application/json">{graphs_json}</script>
<script id="ach-data" type="application/json">{achievements_json}</script>
</body>
</html>
"""


if __name__ == "__main__":
    main()
