// JinGu data reference — static GitHub Pages browser.
// All data is fetched from docs/data/*.json at load time and cross-referenced
// for human-readable names. No network calls beyond the initial JSON loads.

const $ = (s) => document.querySelector(s);
const $$ = (s) => document.querySelectorAll(s);

// === Game-defined categories (mirror in-game inventory taxonomy) ===
const ITEM_TYPES = [
  { id: 0, name: "全部 All" },
  { id: 1, name: "武器 Weapons" },
  { id: 2, name: "防具 Armor" },
  { id: 3, name: "秘籍 Manuals" },
  { id: 4, name: "消耗 Consumables" },
  { id: 5, name: "杂物 Misc" },
];
const ITEM_TYPE2S = [
  { id: 0, parent: -1, name: "全部 All" },
  { id: 1, parent: 0,  name: "资源 Resource" },
  { id: 2, parent: 1,  name: "刀 Knife" },
  { id: 3, parent: 1,  name: "剑 Sword" },
  { id: 4, parent: 1,  name: "拳掌 Fist & Palm" },
  { id: 5, parent: 1,  name: "枪棍 Spear & Staff" },
  { id: 6, parent: 1,  name: "暗器 Hidden Weapon" },
  { id: 7, parent: 1,  name: "琴 Instrument" },
  { id: 8, parent: 2,  name: "衣服 Clothes" },
  { id: 9, parent: 2,  name: "鞋子 Footwear" },
  { id: 10, parent: 2, name: "饰品 Accessory" },
  { id: 11, parent: 3, name: "武学 Martial Manual" },
  { id: 12, parent: 3, name: "丹方 Alchemy Recipe" },
  { id: 13, parent: 3, name: "图纸 Forging Blueprint" },
  { id: 14, parent: 4, name: "战斗 Combat" },
  { id: 15, parent: 4, name: "使用 Usable" },
  { id: 16, parent: 4, name: "精力 Energy" },
  { id: 17, parent: 4, name: "恢复 Recovery" },
  { id: 18, parent: 5, name: "读物 Reading" },
  { id: 19, parent: 5, name: "任务 Quest" },
  { id: 20, parent: 5, name: "饲育 Animal Food" },
  { id: 21, parent: 5, name: "炼丹 Alchemy Material" },
  { id: 22, parent: 5, name: "打造 Forging Material" },
  { id: 23, parent: 5, name: "工具 Tool" },
  { id: 24, parent: 5, name: "礼物 Gift" },
];
const TYPE_NAME = Object.fromEntries(ITEM_TYPES.map(t => [t.id, t.name]));
const TYPE2_NAME = Object.fromEntries(ITEM_TYPE2S.map(t => [t.id, t.name]));

// Gift category names — keyed by gift.m_type / npc.giftTypePrefer values (1-9)
const GIFT_TYPE_NAME = {
  1: "料理 Cuisine", 2: "酒 Wine", 3: "茶 Tea",
  4: "武学 Manuals", 5: "装备 Equipment", 6: "珍宝 Treasure",
  7: "风月 Pleasures", 8: "文玩 Antiques", 9: "妆饰 Cosmetics",
};

// NPC faction / sect — from decomp/DBLoad/NpcCamp.cs. The `group` field on each NPC
// is the integer id of their camp. Id 17 is intentionally skipped in the enum; 101
// is a runtime-only marker for romance interests so no pre-assigned NPCs use it.
const GROUP_NAME = {
  1: "牛家村 Niu Village", 2: "灵九宫 Lingjiu Palace", 3: "少林 Shaolin",
  4: "青衫派 Qingshan Sect", 5: "铸剑山庄 Sword-Forge Manor", 6: "银武卫 Silver Guard",
  7: "铁尸门 Iron-Corpse Sect", 8: "七宝门 Seven Treasures Sect", 9: "朝廷 Imperial Court",
  10: "雪山派 Snow Mountain Sect", 11: "豢龙会 Dragon-Keepers", 12: "唐门 Tang Clan",
  13: "剑庐 Sword Hut", 14: "松山剑派 Pine Mountain Sword Sect", 15: "丐帮 Beggars' Sect",
  16: "东海道心派 East-Sea Daoxin Sect", 18: "海鲨帮 Sea Shark Gang",
  19: "万兽山庄 Myriad-Beast Manor", 20: "岭南四煞 Lingnan Four Fiends",
  21: "红袖居 Red Sleeves Residence", 22: "红莲苦海教 Red Lotus Bitter Sea Cult",
  23: "楞伽金刚宗 Lankavajra Sect", 24: "漕运帮 Canal Transport Gang",
  25: "泾水城 Jingshui City", 26: "合欢宗 Joy Sect", 27: "东林书阁 Eastlin Library",
  28: "方歆武馆 Fang Xin Dojo", 29: "大理城 Dali City", 30: "五毒教 Five Poisons Cult",
  31: "七煞山庄 Seven Fiends Manor", 32: "金武卫 Gold Guard", 33: "百匪谷 Hundred-Bandits Valley",
  34: "漠北 Northern Desert", 35: "神都城 Sacred Capital", 36: "大理王宫 Dali Royal Palace",
  37: "静禅寺 Stillness Zen Temple", 38: "八通商局 Eight Trade Bureau",
  101: "情缘 Romance", 102: "宠物 Pets", 103: "江湖 Wanderers",
};

const ITEM_COLUMNS = ["id", "name", "quality", "type", "type2", "value", "describe"];
const MAX_ROWS = 200;

// === Data ===
const data = {};  // populated by loadAll(): items, gifts, npcs, characters, books, ...
let itemById = null;
let bookById = null;
let charById = null;

async function loadAll() {
  const tables = ["item", "gift", "npc", "character", "book"];
  const loaded = await Promise.all(tables.map(t =>
    fetch(`data/${t}.json`).then(r => {
      if (!r.ok) throw new Error(`${t}.json: ${r.status}`);
      return r.json();
    })
  ));
  tables.forEach((t, i) => { data[t] = loaded[i]; });
  itemById = Object.fromEntries(data.item.map(r => [r.id, r]));
  bookById = Object.fromEntries(data.book.map(r => [r.id, r]));
  charById = Object.fromEntries(data.character.map(r => [r.id, r]));
}

// Resolve NPC display name via the same path as NpcInfo.Name in the game:
// npc.characterId → character.book → book.name. Falls back to "?" if any step is missing.
function npcDisplayName(npc) {
  const ch = charById?.[npc.characterId];
  const book = ch ? bookById?.[ch.book] : null;
  return book?.name && book.name !== "虾米" ? book.name : `#${npc.id}`;
}
function itemName(id) {
  return itemById?.[id]?.name || `#${id}`;
}

// =========================================================================
// Items panel: filter + sort + search + modal
// =========================================================================
const items = {
  type: 0, type2: 0, q: "",
  sortKey: null, sortDir: "asc",
  filtered: [],
};

function rebuildType2Select(mainType) {
  const sel = $("#item-type2-select");
  const prev = parseInt(sel.value, 10);
  sel.innerHTML = "";
  const valid = ITEM_TYPE2S.filter(t => t.id === 0 || mainType === 0 || t.parent === mainType);
  for (const t of valid) {
    const opt = document.createElement("option");
    opt.value = t.id; opt.textContent = t.name;
    sel.appendChild(opt);
  }
  if (valid.some(t => t.id === prev)) sel.value = String(prev);
  else { sel.value = "0"; items.type2 = 0; }
}

function applyItemFilter() {
  const q = items.q.toLowerCase().trim();
  let rows = data.item.filter(r => {
    if (items.type !== 0 && r.type !== items.type) return false;
    if (items.type2 !== 0 && r.type2 !== items.type2) return false;
    if (!q) return true;
    for (const v of Object.values(r)) {
      if (v == null || typeof v === "object") continue;
      if (String(v).toLowerCase().includes(q)) return true;
    }
    return false;
  });
  // Sort the full filtered set so the visible top-200 reflects the true top of the sort.
  if (items.sortKey) {
    const sign = items.sortDir === "asc" ? 1 : -1;
    const k = items.sortKey;
    rows = [...rows].sort((a, b) => {
      const va = a[k], vb = b[k];
      if (va == null && vb == null) return 0;
      if (va == null) return 1;
      if (vb == null) return -1;
      if (typeof va === "number" && typeof vb === "number") return (va - vb) * sign;
      return String(va).localeCompare(String(vb), undefined, { numeric: true }) * sign;
    });
  }
  items.filtered = rows;
  renderItemRows(rows);
  $("#item-count").textContent = rows.length === data.item.length
    ? `${rows.length} 条`
    : (rows.length > MAX_ROWS ? `${MAX_ROWS}/${rows.length} 显示 · ${data.item.length} 总` : `${rows.length}/${data.item.length} 条`);
}

function renderItemRows(rows) {
  $("#item-head").innerHTML = ITEM_COLUMNS.map(c => {
    const active = c === items.sortKey;
    const arrow = active ? (items.sortDir === "asc" ? " ▲" : " ▼") : "";
    return `<th data-col="${c}"${active ? ' class="sorted"' : ""}>${c}${arrow}</th>`;
  }).join("");
  const body = $("#item-body");
  body.innerHTML = "";
  $("#item-empty").hidden = rows.length > 0;
  rows.slice(0, MAX_ROWS).forEach((row, idx) => {
    const tr = document.createElement("tr");
    tr.dataset.idx = idx;
    for (const c of ITEM_COLUMNS) {
      const td = document.createElement("td");
      const klass = c === "id" ? "col-id"
                  : c === "name" ? "col-name"
                  : c === "describe" ? "col-desc"
                  : "";
      if (klass) td.className = klass;
      let display;
      if (c === "type")  display = TYPE_NAME[row.type]   ?? row.type ?? "—";
      else if (c === "type2") display = TYPE2_NAME[row.type2] ?? row.type2 ?? "—";
      else display = row[c] == null ? "—" : String(row[c]);
      td.textContent = display;
      td.title = display;
      tr.appendChild(td);
    }
    body.appendChild(tr);
  });
}

function bindItems() {
  const mainSel = $("#item-type-select");
  for (const t of ITEM_TYPES) {
    const opt = document.createElement("option");
    opt.value = t.id; opt.textContent = t.name;
    mainSel.appendChild(opt);
  }
  rebuildType2Select(0);
  mainSel.addEventListener("change", () => {
    items.type = parseInt(mainSel.value, 10);
    rebuildType2Select(items.type);
    items.type2 = parseInt($("#item-type2-select").value, 10);
    applyItemFilter();
  });
  $("#item-type2-select").addEventListener("change", (e) => {
    items.type2 = parseInt(e.target.value, 10);
    applyItemFilter();
  });
  $("#item-search").addEventListener("input", (e) => {
    items.q = e.target.value;
    applyItemFilter();
  });
  $("#item-head").addEventListener("click", (e) => {
    const th = e.target.closest("th[data-col]");
    if (!th) return;
    const col = th.dataset.col;
    if (items.sortKey === col) {
      items.sortDir = items.sortDir === "asc" ? "desc" : "asc";
    } else {
      items.sortKey = col;
      // Default numeric columns to descending — "biggest first" is what people want.
      items.sortDir = ["value", "quality", "type", "type2", "id"].includes(col) ? "desc" : "asc";
    }
    applyItemFilter();
  });
  $("#item-body").addEventListener("click", (e) => {
    const tr = e.target.closest("tr[data-idx]");
    if (!tr) return;
    const row = items.filtered[+tr.dataset.idx];
    if (row) showModal(`#${row.id} ${row.name ?? ""}`, row);
  });
  applyItemFilter();
}

// =========================================================================
// Gifts panel: NPC-centric view of who likes/dislikes what
// =========================================================================
const gifts = { q: "", group: 0 };  // group=0 means "all"

function npcHasGiftData(npc) {
  return (npc.giftTypePrefer?.length || 0) > 0
      || (npc.giftIdPrefer?.length || 0) > 0
      || (npc.giftHate?.length || 0) > 0;
}

// Build the group <select> from groups actually present among named gift-able NPCs.
// (Includes count per group, sorted by id. Unknown enum values get a "? unknown" label.)
function populateGroupSelect() {
  const sel = $("#npc-group-select");
  sel.innerHTML = "";
  // baseline pool: same filter as the grid, minus the group filter itself
  const pool = data.npc
    .filter(npcHasGiftData)
    .map(n => ({ ...n, _name: npcDisplayName(n) }))
    .filter(n => n._name !== `#${n.id}`);
  const counts = new Map();
  for (const n of pool) counts.set(n.group, (counts.get(n.group) || 0) + 1);

  const allOpt = document.createElement("option");
  allOpt.value = "0";
  allOpt.textContent = `全部门派 All groups (${pool.length})`;
  sel.appendChild(allOpt);

  const sortedGroups = [...counts.keys()].sort((a, b) => a - b);
  for (const g of sortedGroups) {
    const opt = document.createElement("option");
    opt.value = String(g);
    const name = GROUP_NAME[g] || `${g} ? unknown`;
    opt.textContent = `${name} (${counts.get(g)})`;
    sel.appendChild(opt);
  }
}

function renderNpcGrid() {
  const grid = $("#npc-grid");
  grid.innerHTML = "";
  const q = gifts.q.toLowerCase().trim();
  const npcs = data.npc
    .filter(npcHasGiftData)
    .map(n => ({ ...n, _name: npcDisplayName(n) }))
    .filter(n => n._name !== `#${n.id}`)
    .filter(n => gifts.group === 0 || n.group === gifts.group)
    .filter(n => !q || n._name.toLowerCase().includes(q) || String(n.id).includes(q))
    .sort((a, b) => a._name.localeCompare(b._name, "zh-Hans-u-co-pinyin"));

  $("#npc-count").textContent = `${npcs.length} 位 NPC`;

  if (npcs.length === 0) {
    grid.innerHTML = `<p class="hint">没有匹配的 NPC.</p>`;
    return;
  }

  for (const n of npcs) {
    const card = document.createElement("div");
    card.className = "npc-card";
    card.addEventListener("click", () => showModal(`${n._name} #${n.id}`, n));

    const groupLabel = GROUP_NAME[n.group] || `${n.group} ? unknown`;
    const likes = (n.giftTypePrefer || []).map(t =>
      `<span class="tag-good">${GIFT_TYPE_NAME[t] || `?${t}`}</span>`).join("");
    const hates = (n.giftHate || []).map(t =>
      `<span class="tag-bad">${GIFT_TYPE_NAME[t] || `?${t}`}</span>`).join("");
    const loves = (n.giftIdPrefer || []).map(id =>
      `<span class="tag-special" title="item ${id}">${itemName(id)}</span>`).join("");

    card.innerHTML = `
      <div class="npc-name">${n._name}</div>
      <div class="npc-meta">id ${n.id}  ·  evil ${n.evil ?? 0}</div>
      <div class="npc-group">${groupLabel}</div>
      ${likes ? `<div class="npc-likes"><span class="npc-likes-label">喜欢 Likes:</span>${likes}</div>` : ""}
      ${loves ? `<div class="npc-loves"><span class="npc-loves-label">尤爱 Loves:</span>${loves}</div>` : ""}
      ${hates ? `<div class="npc-hates"><span class="npc-hates-label">讨厌 Hates:</span>${hates}</div>` : ""}
    `;
    grid.appendChild(card);
  }
}

function bindGifts() {
  populateGroupSelect();
  $("#npc-group-select").addEventListener("change", (e) => {
    gifts.group = parseInt(e.target.value, 10) || 0;
    renderNpcGrid();
  });
  $("#npc-search").addEventListener("input", (e) => {
    gifts.q = e.target.value;
    renderNpcGrid();
  });
}

// =========================================================================
// Modal
// =========================================================================
function showModal(title, obj) {
  $("#modal-title").textContent = title;
  $("#modal-body").textContent = JSON.stringify(obj, (k, v) => k.startsWith("_") ? undefined : v, 2);
  $("#modal").hidden = false;
}
function bindModal() {
  $("#modal-close").addEventListener("click", () => $("#modal").hidden = true);
  $("#modal").addEventListener("click", (e) => {
    if (e.target.id === "modal") e.currentTarget.hidden = true;
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && !$("#modal").hidden) $("#modal").hidden = true;
  });
}

// =========================================================================
// Tabs
// =========================================================================
function bindTabs() {
  $$(".tab[data-tab]").forEach(tab => {
    tab.addEventListener("click", () => {
      $$(".tab").forEach(t => t.classList.remove("active"));
      $$(".panel").forEach(p => p.classList.remove("active"));
      tab.classList.add("active");
      $(`#panel-${tab.dataset.tab}`).classList.add("active");
      // Lazy-render gifts on first activation (a bit slower than items since it cross-references)
      if (tab.dataset.tab === "gifts" && !$("#npc-grid").children.length) renderNpcGrid();
    });
  });
}

// =========================================================================
// Init
// =========================================================================
(async () => {
  try {
    await loadAll();
    bindTabs();
    bindItems();
    bindGifts();
    bindModal();
  } catch (e) {
    document.body.innerHTML = `<div style="padding:32px;color:#c1463a;font-family:system-ui">
      <h1>数据加载失败 · Data load failed</h1>
      <pre>${e.message}</pre>
      <p>If you're viewing this locally, you need a local web server (browsers block fetch() over file://). Try: <code>python -m http.server</code> in the docs/ folder.</p>
    </div>`;
  }
})();
