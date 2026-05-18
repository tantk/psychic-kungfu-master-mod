// JinGu Cheats UI — frontend logic
// Talks to BepInEx plugin over named pipe via Tauri's invoke() bridge.
// No HTTP, no ports, no firewall prompt — pipe handshake happens in Rust.

const state = {
  connected: false,
  protocol: null,
  pluginVersion: null,
};

const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => document.querySelectorAll(sel);

// EffectIds whose stored value is a %-rate multiplier (the game divides by 100 at use,
// so the stored 100 = 1.0× = baseline). Cur-val shows these as multipliers (e.g. "现:1.0×")
// to avoid the misleading "现:100%" reading which users interpreted as "+100% boost".
const RATE_EFFECT_IDS = new Set([20, 21, 717, 720, 721, 722, 723, 724, 725, 726, 732]);

function formatLiveValue(idStr, v) {
  const id = parseInt(idStr, 10);
  const n = Number(v) || 0;
  if (RATE_EFFECT_IDS.has(id)) return `现:${(n / 100).toFixed(1)}×`;
  return `现:${Math.round(n)}`;
}

function updateCurField(key, display) {
  const els = document.querySelectorAll(`[data-cur="${key}"]`);
  for (const el of els) el.textContent = display;
}

// ---------- Tauri invoke wrapper ----------
const tauri = window.__TAURI__;
function invoke(cmd, args) {
  if (!tauri) return Promise.reject(new Error("Tauri runtime not available"));
  return tauri.core.invoke(cmd, args || {});
}

// ---------- API calls ----------
async function apiHello()           { return invoke("api_hello"); }
async function apiState()           { return invoke("api_state"); }
async function apiToggle(name, value) { return invoke("api_toggle", { name, value }); }
async function apiCmd(payload)      { return invoke("api_cmd", { payload }); }
async function apiSetHotkey(name, keyCode) {
  return apiCmd({ cmd: "set_hotkey", name, key: keyCode });
}
async function apiGetErrors(since = 0) {
  return apiCmd({ cmd: "errors", since });
}
async function apiClearErrors() {
  return apiCmd({ cmd: "clear_errors" });
}

// ---------- Status bar ----------
function setStatus(msg, kind = "") {
  const el = $("#status-msg");
  el.textContent = msg;
  el.classList.remove("ok", "err");
  if (kind) el.classList.add(kind);
  if (kind === "ok") fireInkSplash();
}

let inkSplashTimer = null;
function fireInkSplash() {
  const el = $("#ink-splash");
  if (!el) return;
  el.classList.remove("fire");
  // Force a reflow so re-adding the class restarts the animation
  void el.offsetWidth;
  el.classList.add("fire");
  clearTimeout(inkSplashTimer);
  inkSplashTimer = setTimeout(() => el.classList.remove("fire"), 900);
}

function setConnected(ok, label) {
  state.connected = ok;
  $("#conn").classList.toggle("ok", ok);
  $("#conn-text").textContent = label || (ok ? "已连接" : "未连接");
}

// ---------- Polling ----------
let pollTimer = null;
function startPolling() {
  if (pollTimer) clearInterval(pollTimer);
  pollTimer = setInterval(poll, 600);
  poll();
}

async function poll() {
  try {
    const s = await apiState();
    setConnected(true, s.in_game ? "已连接 · 存档载入" : "已连接 · 标题画面");
    if (s.in_game) {
      const family = s.leader_family || "";
      const name   = s.leader_name || "(无名)";
      $("#leader-name").textContent = `${family}${name}`;
      // Corner seal: first character of the family name (or leader name as fallback)
      $("#leader-seal").textContent = (family || name).charAt(0) || "侠";
      $("#stat-money").textContent  = Number(s.money).toLocaleString("en-US");
      $("#stat-gametime").textContent = s.game_time.toLocaleString("en-US");
      $("#stat-diff").textContent   = ["简单","普通","困难","噩梦"][s.difficulty] || s.difficulty;
    } else {
      $("#leader-name").textContent = "尚未载入存档";
      $("#leader-seal").textContent = "—";
      $("#stat-money").textContent  = "—";
      $("#stat-gametime").textContent = "—";
      $("#stat-diff").textContent   = "—";
    }
    if (s.toggles) {
      for (const [k, v] of Object.entries(s.toggles)) {
        const cb = document.querySelector(`input[data-toggle="${k}"]`);
        if (cb && cb.checked !== v) cb.checked = v;
      }
    }
    // Effect toggles intentionally do NOT auto-sync from poll: a lingering save-side effect
    // value (e.g. from prior stacking-Apply sessions) would show toggle-on every launch,
    // which the user found confusing. Toggle state is user-driven only — clicking applies
    // or unapplies. Mismatch with save-side value is acceptable.

    // Live-value badges — update every [data-cur-effect="ID"] and [data-cur="key"] span.
    // Values come from state.effects (effect ids) and direct snapshot fields (jingli, tili).
    if (s.in_game) {
      if (s.effects) {
        for (const [k, v] of Object.entries(s.effects)) {
          const els = document.querySelectorAll(`[data-cur-effect="${k}"]`);
          if (!els.length) continue;
          const display = formatLiveValue(k, v);
          for (const el of els) {
            el.textContent = display;
            if (el.classList.contains("cur-val")) {
              el.classList.toggle("active", Number(v) !== 0);
            }
          }
        }
      }
      // Energy: format as current/max
      updateCurField("jingli", s.jingli != null && s.jingli_max != null ? `${s.jingli}/${s.jingli_max}` : "—");
      updateCurField("tili",   s.tili   != null && s.tili_max   != null ? `${s.tili}/${s.tili_max}`   : "—");
    } else {
      // Out of game — wipe all live-value badges back to placeholder.
      document.querySelectorAll("[data-cur-effect],[data-cur]").forEach(el => {
        if (el.classList.contains("cur-val")) {
          const id = parseInt(el.dataset.curEffect, 10);
          const suf = RATE_EFFECT_IDS.has(id) ? "×" : "";
          el.textContent = "现:—" + suf;
          el.classList.remove("active");
        } else {
          el.textContent = "—";
        }
      });
    }
    if (s.hotkeys) {
      for (const [k, v] of Object.entries(s.hotkeys)) renderHotkey(k, v);
    }
    if (typeof s.error_count === "number") renderErrorBadge(s.error_count);
  } catch (e) {
    setConnected(false, "未连接");
    $("#leader-name").textContent = "— 等待游戏 —";
    $("#stat-money").textContent  = "—";
    $("#stat-gametime").textContent = "—";
    $("#stat-diff").textContent   = "—";
  }
}

// ---------- Handshake (run once on startup) ----------
async function handshake() {
  try {
    const h = await apiHello();
    state.protocol = h.protocol;
    state.pluginVersion = h.version;
    $("#about-version").textContent = `${h.plugin || "plugin"} ${h.version || ""}  ·  protocol v${h.protocol}`;
    setStatus(`已握手 · 插件 ${h.version}`, "ok");
  } catch (e) {
    $("#about-version").textContent = "未连接到插件";
    setStatus(`握手失败 — 检查游戏是否运行: ${e}`, "err");
  }
}

// ---------- Tabs ----------
function bindTabs() {
  $$(".tab").forEach((tab) => {
    tab.addEventListener("click", () => {
      $$(".tab").forEach((t) => t.classList.remove("active"));
      $$(".panel").forEach((p) => p.classList.remove("active"));
      tab.classList.add("active");
      const id = "panel-" + tab.dataset.tab;
      document.getElementById(id)?.classList.add("active");
    });
  });
}

// ---------- Buttons ----------
function bindButtons() {
  $$("button[data-cmd]").forEach((btn) => {
    btn.addEventListener("click", async () => {
      const cmd = btn.dataset.cmd;
      let args = {};
      if (btn.dataset.args) {
        try { args = JSON.parse(btn.dataset.args); }
        catch { return setStatus(`参数解析失败 (${cmd})`, "err"); }
      }
      // `data-arg-from` maps argName → inputId. The input's numeric value is multiplied
      // by the corresponding `data-args` entry (used as the sign for ± buttons). e.g.
      //   data-args='{"value":-1}' + data-arg-from='{"value":"rep-amt"}' + input=10 → value=-10
      if (btn.dataset.argFrom) {
        try {
          const map = JSON.parse(btn.dataset.argFrom);
          for (const [argName, inputId] of Object.entries(map)) {
            const el = document.getElementById(inputId);
            const n = parseFloat(el?.value);
            if (!Number.isFinite(n)) return setStatus(`需要有效数值 (${inputId})`, "err");
            args[argName] = (args[argName] ?? 1) * n;
          }
        } catch (e) { return setStatus(`参数读取失败 (${cmd}): ${e}`, "err"); }
      }
      btn.disabled = true;
      try {
        // payload = { cmd, ...args }  →  plugin reads via Json.Reader (flat key lookup)
        const res = await apiCmd({ cmd, ...args });
        setStatus(res.message || cmd, res.ok ? "ok" : "err");
      } catch (e) {
        setStatus(`${cmd}: ${e}`, "err");
      } finally {
        btn.disabled = false;
      }
    });
  });

  // Live-update <output> elements next to range sliders (data-output="output-id")
  $$("input[type='range'][data-output]").forEach((slider) => {
    const out = document.getElementById(slider.dataset.output);
    if (!out) return;
    const sync = () => { out.textContent = slider.value; };
    slider.addEventListener("input", sync);
    sync();
    // If this slider drives an effect toggle and the toggle is currently on, re-apply on commit
    // (use 'change' so we only fire once when the user releases the thumb, not on every pixel).
    slider.addEventListener("change", () => {
      const cb = document.querySelector(`input[data-effect-toggle][data-amt-from="${slider.id}"]`);
      if (cb?.checked) applyEffectToggle(cb);
    });
  });

  // Effect-toggle checkboxes — toggle on = set_effect(id, slider value), toggle off = set_effect(id, 0)
  $$("input[data-effect-toggle]").forEach((cb) => {
    cb.addEventListener("change", () => applyEffectToggle(cb));
  });
}

// Rate-boost effects (20, 21, 717, 720-732) are defined with m_initValue = 100 in the
// game's Effect.cs — meaning 100 = 1.0× = "normal, no boost". Toggling off must restore
// to 100, NOT 0 — 0 would multiply XP gain by zero and silently break the activity.
const RATE_EFFECT_DEFAULT = 100;

async function applyEffectToggle(cb) {
  const id = parseInt(cb.dataset.effectToggle, 10);
  const slider = document.getElementById(cb.dataset.amtFrom);
  const amt = parseFloat(slider?.value || 0);
  const value = cb.checked ? amt : RATE_EFFECT_DEFAULT;
  try {
    const res = await apiCmd({ cmd: "set_effect", id, value });
    setStatus(res.message || `effect ${id} = ${value}`, res.ok ? "ok" : "err");
  } catch (e) {
    setStatus(`set_effect ${id}: ${e}`, "err");
    cb.checked = !cb.checked;  // revert the visual on failure
  }
}

function bindToggles() {
  $$("input[data-toggle]").forEach((cb) => {
    cb.addEventListener("change", async () => {
      const name = cb.dataset.toggle;
      const value = cb.checked;
      try {
        const res = await apiToggle(name, value);
        setStatus(res.message || `${name} = ${value}`, "ok");
      } catch (e) {
        setStatus(`${name}: ${e}`, "err");
        cb.checked = !value;
      }
    });
  });
}

// ---------- Window controls ----------
function bindWindowControls() {
  if (!tauri) {
    $(".window-controls").style.display = "none";
    return;
  }
  const win = tauri.window.getCurrentWindow();
  $("#btn-min").addEventListener("click",   () => win.minimize());
  $("#btn-max").addEventListener("click",   () => win.toggleMaximize());
  $("#btn-close").addEventListener("click", () => win.close());
}

// ---------- Hotkey binding ----------
// Map browser KeyboardEvent codes to Unity KeyCode enum values.
// Only the keys most useful for hotkeys; unknowns fall back to a friendly error.
const UNITY_KEYCODE = (() => {
  const m = {
    "Escape": 27, "Space": 32, "Tab": 9, "Enter": 13, "Backquote": 96,
    "Minus": 45, "Equal": 61, "BracketLeft": 91, "BracketRight": 93,
    "Semicolon": 59, "Quote": 39, "Backslash": 92, "Comma": 44, "Period": 46, "Slash": 47,
    "ArrowUp": 273, "ArrowDown": 274, "ArrowRight": 275, "ArrowLeft": 276,
    "Insert": 277, "Home": 278, "End": 279, "PageUp": 280, "PageDown": 281,
  };
  // Function keys F1..F15 -> KeyCode.F1 (282) .. F15 (296)
  for (let i = 1; i <= 15; i++) m[`F${i}`] = 281 + i;
  // Letters A..Z -> 97..122 (Unity KeyCode is lowercase)
  for (let c = 0; c < 26; c++) m[`Key${String.fromCharCode(65 + c)}`] = 97 + c;
  // Digits 0..9 (top row) -> 48..57
  for (let d = 0; d < 10; d++) m[`Digit${d}`] = 48 + d;
  // Numpad 0..9 -> 256..265
  for (let d = 0; d < 10; d++) m[`Numpad${d}`] = 256 + d;
  return m;
})();

function keyCodeToLabel(code) {
  if (!code || code === 0) return "未绑定";
  // Reverse lookup against UNITY_KEYCODE
  for (const [browserCode, unityCode] of Object.entries(UNITY_KEYCODE)) {
    if (unityCode === code) {
      if (browserCode.startsWith("Key"))    return browserCode.slice(3);
      if (browserCode.startsWith("Digit"))  return browserCode.slice(5);
      if (browserCode.startsWith("Numpad")) return "Num " + browserCode.slice(6);
      return browserCode;
    }
  }
  return `KeyCode(${code})`;
}

let capturing = null;  // { name, btn } when in hotkey capture mode
function startCapture(name, btn) {
  capturing = { name, btn };
  btn.classList.add("capturing");
  btn.textContent = "按键中…";
  $("#hotkey-target-name").textContent = name;
  $("#hotkey-modal").hidden = false;
}
function endCapture() {
  if (capturing) {
    capturing.btn.classList.remove("capturing");
  }
  capturing = null;
  $("#hotkey-modal").hidden = true;
}

function onCaptureKey(e) {
  if (!capturing) return;
  e.preventDefault();
  e.stopPropagation();
  if (e.code === "Escape") { endCapture(); return; }
  const unityCode = UNITY_KEYCODE[e.code];
  if (!unityCode) {
    setStatus(`不支持的按键: ${e.code}`, "err");
    endCapture();
    return;
  }
  const name = capturing.name;
  endCapture();
  apiSetHotkey(name, unityCode).then((res) => {
    setStatus(res.message || `${name} 已绑定`, "ok");
    // Optimistic UI; poll will refresh anyway
    renderHotkey(name, unityCode);
  }).catch((err) => setStatus(`绑定失败: ${err}`, "err"));
}

function renderHotkey(name, keyCode) {
  const row = document.querySelector(`.hotkey-row[data-hotkey="${name}"]`);
  if (!row) return;
  const btn = row.querySelector(".hotkey-btn");
  btn.textContent = keyCodeToLabel(keyCode);
  btn.classList.toggle("bound", keyCode && keyCode !== 0);
}

function bindHotkeys() {
  $$(".hotkey-row").forEach((row) => {
    const name = row.dataset.hotkey;
    row.querySelector('[data-action="bind"]').addEventListener("click", (e) => {
      startCapture(name, e.currentTarget);
    });
    row.querySelector('[data-action="clear"]').addEventListener("click", () => {
      apiSetHotkey(name, 0).then((res) => {
        setStatus(res.message || `${name} 已清除`, "ok");
        renderHotkey(name, 0);
      }).catch((err) => setStatus(`清除失败: ${err}`, "err"));
    });
  });
  // Global keydown listens for capture
  window.addEventListener("keydown", onCaptureKey, true);
}

// ---------- Error log ----------
function renderErrorBadge(count) {
  const el = $("#error-badge");
  if (!el) return;
  el.textContent = String(count);
  el.classList.toggle("zero", count === 0);
}

async function showErrors() {
  try {
    const res = await apiGetErrors(0);
    const list = $("#error-list");
    list.innerHTML = "";
    if (!res.entries || res.entries.length === 0) {
      $("#error-modal").hidden = false;
      return;
    }
    // Most-recent first
    [...res.entries].reverse().forEach((e) => {
      const div = document.createElement("div");
      div.className = "error-entry" + (e.kind === "warning" ? " warning" : "");
      div.innerHTML = `
        <div class="error-entry-head">
          <span class="error-entry-time">${e.time}</span>
          <span class="error-entry-source">[${e.source}]</span>
        </div>
        <div class="error-entry-msg"></div>
        ${e.stack ? '<pre class="error-entry-stack"></pre>' : ""}
      `;
      div.querySelector(".error-entry-msg").textContent = e.message;
      if (e.stack) div.querySelector(".error-entry-stack").textContent = e.stack;
      list.appendChild(div);
    });
    $("#error-modal").hidden = false;
  } catch (err) {
    setStatus(`无法读取错误日志: ${err}`, "err");
  }
}

function bindErrorLog() {
  $("#btn-show-errors").addEventListener("click", showErrors);
  $("#btn-clear-errors").addEventListener("click", async () => {
    try {
      await apiClearErrors();
      setStatus("错误日志已清空", "ok");
      renderErrorBadge(0);
    } catch (e) { setStatus(`清空失败: ${e}`, "err"); }
  });
  $("#error-modal-close").addEventListener("click", () => {
    $("#error-modal").hidden = true;
  });
  // Click backdrop to close
  $("#error-modal").addEventListener("click", (e) => {
    if (e.target.id === "error-modal") e.currentTarget.hidden = true;
  });
}

// ============================================================
// NPC editor
// ============================================================

// Faction id → 门派 name. Source: DBLoad/NpcCamp.cs enum.
const FACTION_NAMES = {
  1: "牛家村", 2: "灵九宫", 3: "少林", 4: "青衫派", 5: "铸剑山庄",
  6: "银武卫", 7: "铁尸门", 8: "七宝门", 9: "朝廷", 10: "雪山派",
  11: "豢龙会", 12: "唐门", 13: "剑庐", 14: "松山剑派", 15: "丐帮",
  16: "东海道心派", 17: "万兽山庄", 18: "驼龙寨", 19: "天下", 20: "苦海教",
  21: "金刚宗", 22: "合欢宗", 23: "漕运帮", 24: "五毒教", 25: "白家",
  26: "海鲨帮", 27: "黑风寨", 28: "百匪谷", 29: "七煞山庄",
};

const npcState = {
  npcs: [],         // npc.json
  characters: {},   // characterId -> CharacterData (from JSON)
  skills: {},       // skillId -> SkillData
  passives: {},     // passiveId -> PassiveData
  skillUseCount: {},     // skillId -> count of characters using it
  passiveUseCount: {},
  current: null,    // currently-edited NPC
  currentChar: null, // currently-edited character data (live, may be mutated)
};

async function loadNpcDataset() {
  if (npcState.npcs.length) return;
  const [npcs, chars, skills, passives] = await Promise.all([
    loadDataTable("npc"),
    loadDataTable("character"),
    loadDataTable("skill"),
    loadDataTable("passive"),
  ]);
  npcState.npcs = npcs;
  for (const c of chars) npcState.characters[c.id] = c;
  for (const s of skills) npcState.skills[s.id] = s;
  for (const p of passives) npcState.passives[p.id] = p;
  // Pre-compute usage counts so we can show "used by N other characters" in the picker
  for (const c of chars) {
    for (const sid of (c.skills   || [])) npcState.skillUseCount[sid]   = (npcState.skillUseCount[sid]   || 0) + 1;
    for (const pid of (c.passives || [])) npcState.passiveUseCount[pid] = (npcState.passiveUseCount[pid] || 0) + 1;
  }
}

function npcDisplayName(npc) {
  // npc.json has no `name` field — pull from item.json via book id (NPCs use character → m_book → Book.m_name).
  // We don't have book.json in bundle; fall back to character chengHao or "NPC <id>".
  const ch = npcState.characters[npc.characterId];
  return (ch && ch.chengHao) ? ch.chengHao : `NPC #${npc.id}`;
}

function renderNpcGrid() {
  const grid = $("#npc-grid");
  grid.innerHTML = "";
  const q = $("#npc-search").value.trim().toLowerCase();
  const f = $("#npc-faction").value;
  const filtered = npcState.npcs.filter(n => {
    if (f && String(n.group) !== f) return false;
    if (q) {
      const name = npcDisplayName(n).toLowerCase();
      if (!name.includes(q) && !String(n.id).includes(q)) return false;
    }
    return true;
  });
  $("#npc-count").textContent = `${filtered.length}/${npcState.npcs.length} 角色`;
  filtered.slice(0, 200).forEach(npc => {
    const card = document.createElement("div");
    card.className = "npc-card";
    card.dataset.id = npc.id;
    const name    = npcDisplayName(npc);
    const faction = FACTION_NAMES[npc.group] || `门派#${npc.group}`;
    card.innerHTML =
      `<span class="npc-id">${npc.id}</span>` +
      `<span class="npc-name"></span>` +
      `<span class="npc-faction"></span>`;
    card.querySelector(".npc-name").textContent = name;
    card.querySelector(".npc-faction").textContent = faction;
    card.addEventListener("click", () => openNpcEditor(npc));
    grid.appendChild(card);
  });
}

function populateFactionFilter() {
  const sel = $("#npc-faction");
  const present = new Set(npcState.npcs.map(n => n.group));
  for (const [id, name] of Object.entries(FACTION_NAMES)) {
    if (!present.has(parseInt(id, 10))) continue;
    const opt = document.createElement("option");
    opt.value = id; opt.textContent = `${name} (${id})`;
    sel.appendChild(opt);
  }
}

// ----- Edit modal -----
async function openNpcEditor(npc) {
  npcState.current = npc;
  $("#npc-modal-title").textContent =
    `${npcDisplayName(npc)} · NPC#${npc.id} → Character#${npc.characterId}`;
  $("#np-status").textContent = "";
  $("#np-status").className = "give-status";

  // Static alignment info — from bundled npc.json (no plugin call needed)
  $("#np-static-evil").value        = npc.evil ?? 0;
  $("#np-static-groupfavor").value  = npc.groupFavor ?? 0;
  $("#np-static-basefavor").value   = npc.favor ?? 0;

  // Fetch live state from plugin (best-effort; UI still works if not connected)
  try {
    const state = await apiCmd({ cmd: "npc_get", id: npc.id });
    if (state.ok) {
      $("#np-friend-lv").value = state.friend_lv ?? 0;
      $("#np-favo").value      = state.favo ?? 0;
      $("#np-money").value     = state.money ?? 0;
      $("#np-deaded").checked  = !!state.deaded;
      $("#np-robed").checked   = !!state.robed;
      $("#np-show").checked    = state.show !== false;
    } else {
      setStatus(`npc_get: ${state.message}`, "err");
    }
  } catch (e) {
    setStatus(`npc_get failed: ${e}`, "err");
  }

  // Fetch character stats (also works offline since Character.Dic is in-memory)
  try {
    const cd = await apiCmd({ cmd: "character_get", id: npc.characterId });
    if (cd.ok) {
      npcState.currentChar = cd;
      $("#ns-hp").value     = cd.hp;
      $("#ns-mp").value     = cd.mp;
      $("#ns-atk").value    = cd.atk;
      $("#ns-def").value    = cd.def;
      $("#ns-crt").value    = cd.crt;
      $("#ns-eva").value    = cd.eva;
      $("#ns-speed").value  = cd.speed;
      $("#ns-move").value   = cd.move;
      $("#ns-range").value  = cd.range;
      $("#np-modified-flag").hidden = !cd.modified;
      renderSkillList(cd.skills,   false);
      renderSkillList(cd.passives, true);
    }
  } catch (e) {
    // Fall back to bundled JSON if not connected
    const cd = npcState.characters[npc.characterId];
    if (cd) {
      npcState.currentChar = { ...cd, modified: false };
      $("#ns-hp").value=cd.hp; $("#ns-mp").value=cd.mp;
      $("#ns-atk").value=cd.atk; $("#ns-def").value=cd.def;
      $("#ns-crt").value=cd.crt; $("#ns-eva").value=cd.eva;
      $("#ns-speed").value=cd.speed; $("#ns-move").value=cd.move; $("#ns-range").value=cd.range;
      $("#np-modified-flag").hidden = true;
      renderSkillList(cd.skills,   false);
      renderSkillList(cd.passives, true);
    }
  }

  $("#npc-modal").hidden = false;
}

function renderSkillList(ids, asPassive) {
  const list = asPassive ? $("#np-passive-list") : $("#np-active-list");
  const counter = asPassive ? $("#np-passive-count") : $("#np-active-count");
  const dict = asPassive ? npcState.passives : npcState.skills;
  list.innerHTML = "";
  counter.textContent = ids.length;
  ids.forEach(sid => {
    const s = dict[sid] || {};
    const li = document.createElement("li");
    li.className = "skill-item";
    const name = s.name || "?";
    const mp   = s.mpCost ?? s.mp ?? "—";
    const cd   = s.CD ?? s.cd ?? "—";
    const maxMp = npcState.currentChar?.mp ?? 0;
    const warn = (typeof mp === "number" && mp > maxMp)
      ? `<span class="swarn" title="MP cost exceeds NPC max MP — will never cast">⚠ ${mp}>${maxMp}MP</span>` : "";
    li.innerHTML =
      `<span class="sid">${sid}</span>` +
      `<span class="sname"></span>` +
      `<span class="smeta">${asPassive ? "" : `MP ${mp} · CD ${cd}`}</span>` +
      warn +
      `<button class="del" title="remove">×</button>`;
    li.querySelector(".sname").textContent = name;
    li.querySelector(".del").addEventListener("click", () => removeSkill(sid, asPassive));
    list.appendChild(li);
  });
}

async function removeSkill(skillId, asPassive) {
  const cid = npcState.current?.characterId;
  if (!cid) return;
  try {
    const r = await apiCmd({ cmd: "character_remove_skill", id: cid, skill: skillId, passive: asPassive });
    if (r.ok) {
      // Refresh from server
      const cd = await apiCmd({ cmd: "character_get", id: cid });
      if (cd.ok) {
        npcState.currentChar = cd;
        $("#np-modified-flag").hidden = !cd.modified;
        renderSkillList(cd.skills, false);
        renderSkillList(cd.passives, true);
      }
      setStatus(r.message, "ok");
    } else {
      setStatus(r.message, "err");
    }
  } catch (e) { setStatus(`${e}`, "err"); }
}

// ----- Skill picker submodal -----
let pickingAsPassive = false;
function openSkillPicker(asPassive) {
  pickingAsPassive = asPassive;
  $("#skill-picker-title").textContent = asPassive ? "选择被动技能" : "选择主动技能";
  $("#skill-picker-search").value = "";
  renderSkillPicker();
  $("#skill-picker-modal").hidden = false;
}

function renderSkillPicker() {
  const dict = pickingAsPassive ? npcState.passives : npcState.skills;
  const useCount = pickingAsPassive ? npcState.passiveUseCount : npcState.skillUseCount;
  const maxMp = npcState.currentChar?.mp ?? 0;
  const all = Object.values(dict);
  const q = $("#skill-picker-search").value.trim().toLowerCase();
  const filtered = q ? all.filter(s => {
    if (String(s.id).includes(q)) return true;
    if (s.name && s.name.toLowerCase().includes(q)) return true;
    return false;
  }) : all;
  $("#skill-picker-count").textContent =
    filtered.length > 200 ? `200/${filtered.length} 显示 · ${all.length} 总` : `${filtered.length} 条`;

  const headCols = pickingAsPassive
    ? ["id", "name", "quality", "used by", ""]
    : ["id", "name", "MP", "CD", "used by", ""];
  $("#skill-picker-head").innerHTML = headCols.map(c => `<th>${c}</th>`).join("");
  const body = $("#skill-picker-body");
  body.innerHTML = "";
  filtered.slice(0, 200).forEach(s => {
    const tr = document.createElement("tr");
    const uses = useCount[s.id] || 0;
    const mp = s.mpCost ?? s.mp ?? 0;
    const cd = s.CD ?? s.cd ?? "—";
    const mpBad = !pickingAsPassive && typeof mp === "number" && mp > maxMp;
    if (pickingAsPassive) {
      tr.innerHTML =
        `<td class="col-id">${s.id}</td>` +
        `<td class="col-name"></td>` +
        `<td>${s.quality ?? "—"}</td>` +
        `<td class="col-uses">${uses}×</td>` +
        `<td class="col-add"><button>添加</button></td>`;
    } else {
      tr.innerHTML =
        `<td class="col-id">${s.id}</td>` +
        `<td class="col-name"></td>` +
        `<td class="col-mp${mpBad ? " bad" : ""}" title="${mpBad ? "exceeds NPC max MP " + maxMp : ""}">${mp}</td>` +
        `<td>${cd}</td>` +
        `<td class="col-uses">${uses}×</td>` +
        `<td class="col-add"><button>添加</button></td>`;
    }
    tr.querySelector(".col-name").textContent = s.name || "?";
    tr.querySelector(".col-add button").addEventListener("click", () => addSkill(s.id));
    body.appendChild(tr);
  });
}

async function addSkill(skillId) {
  const cid = npcState.current?.characterId;
  if (!cid) return;
  try {
    const r = await apiCmd({ cmd: "character_add_skill", id: cid, skill: skillId, passive: pickingAsPassive });
    if (r.ok) {
      const cd = await apiCmd({ cmd: "character_get", id: cid });
      if (cd.ok) {
        npcState.currentChar = cd;
        $("#np-modified-flag").hidden = !cd.modified;
        renderSkillList(cd.skills, false);
        renderSkillList(cd.passives, true);
      }
      $("#skill-picker-modal").hidden = true;
      setStatus(r.message, "ok");
    } else {
      setStatus(r.message, "err");
    }
  } catch (e) { setStatus(`${e}`, "err"); }
}

// ----- Save (state + stats) -----
async function saveNpcEdits() {
  const npc = npcState.current; if (!npc) return;
  const setStatusBtn = (msg, kind) => {
    $("#np-status").textContent = msg;
    $("#np-status").className = "give-status " + (kind || "");
  };

  // State patch
  try {
    const r1 = await apiCmd({
      cmd: "npc_set",
      id: npc.id,
      money:     parseInt($("#np-money").value, 10),
      favo:      parseInt($("#np-favo").value,  10),
      friend_lv: parseInt($("#np-friend-lv").value, 10),
      deaded:    $("#np-deaded").checked,
      robed:     $("#np-robed").checked,
      show:      $("#np-show").checked,
    });
    if (!r1.ok) { setStatusBtn(`state: ${r1.message}`, "err"); return; }
  } catch (e) { setStatusBtn(`state save failed: ${e}`, "err"); return; }

  // Stat patches — one per changed value
  const fields = [
    ["hp","ns-hp"], ["mp","ns-mp"], ["atk","ns-atk"], ["def","ns-def"],
    ["crt","ns-crt"], ["eva","ns-eva"], ["speed","ns-speed"], ["move","ns-move"], ["range","ns-range"],
  ];
  const cd = npcState.currentChar;
  let nStats = 0;
  for (const [stat, elId] of fields) {
    const v = parseInt($("#" + elId).value, 10);
    if (!Number.isFinite(v)) continue;
    if (cd && cd[stat] === v) continue;
    try {
      const r = await apiCmd({ cmd: "character_set_stat", id: npc.characterId, stat, value: v });
      if (r.ok) nStats++;
    } catch {}
  }
  setStatusBtn(`✓ saved state + ${nStats} stat changes`, "ok");
  setStatus(`npc#${npc.id} saved`, "ok");
}

async function resetCharacter() {
  const cid = npcState.current?.characterId; if (!cid) return;
  try {
    const r = await apiCmd({ cmd: "character_reset", id: cid });
    if (r.ok) {
      const cd = await apiCmd({ cmd: "character_get", id: cid });
      if (cd.ok) {
        npcState.currentChar = cd;
        $("#np-modified-flag").hidden = !cd.modified;
        $("#ns-hp").value=cd.hp; $("#ns-mp").value=cd.mp;
        $("#ns-atk").value=cd.atk; $("#ns-def").value=cd.def;
        $("#ns-crt").value=cd.crt; $("#ns-eva").value=cd.eva;
        $("#ns-speed").value=cd.speed; $("#ns-move").value=cd.move; $("#ns-range").value=cd.range;
        renderSkillList(cd.skills, false);
        renderSkillList(cd.passives, true);
      }
      $("#np-status").textContent = r.message;
      $("#np-status").className = "give-status ok";
    } else {
      $("#np-status").textContent = r.message;
      $("#np-status").className = "give-status err";
    }
  } catch (e) {
    $("#np-status").textContent = `${e}`;
    $("#np-status").className = "give-status err";
  }
}

async function bindNpcEditor() {
  await loadNpcDataset();
  populateFactionFilter();
  renderNpcGrid();

  $("#npc-search").addEventListener("input", renderNpcGrid);
  $("#npc-faction").addEventListener("change", renderNpcGrid);

  $("#npc-modal-close").addEventListener("click", () => $("#npc-modal").hidden = true);
  $("#npc-modal").addEventListener("click", (e) => {
    if (e.target.id === "npc-modal") e.currentTarget.hidden = true;
  });

  $("#np-save").addEventListener("click", saveNpcEdits);
  $("#np-reset-char").addEventListener("click", resetCharacter);
  $("#np-add-active").addEventListener("click",  () => openSkillPicker(false));
  $("#np-add-passive").addEventListener("click", () => openSkillPicker(true));

  $("#skill-picker-close").addEventListener("click", () => $("#skill-picker-modal").hidden = true);
  $("#skill-picker-modal").addEventListener("click", (e) => {
    if (e.target.id === "skill-picker-modal") e.currentTarget.hidden = true;
  });
  $("#skill-picker-search").addEventListener("input", renderSkillPicker);
}

// ============================================================
// Sect editor
// ============================================================

// Full NpcCamp enum (from DBLoad/NpcCamp.cs). Skipping 101/102/103 which are
// meta groupings (情缘/宠物/江湖), not real factions.
const SECTS = [
  { id: 1,  cn: "牛家村",   en: "Niu Family Village" },
  { id: 2,  cn: "灵九宫",   en: "Lingjiu Palace" },
  { id: 3,  cn: "少林",     en: "Shaolin" },
  { id: 4,  cn: "青衫派",   en: "Qingshan Sect" },
  { id: 5,  cn: "铸剑山庄", en: "Sword-Forging Manor" },
  { id: 6,  cn: "银武卫",   en: "Silver Guard" },
  { id: 7,  cn: "铁尸门",   en: "Iron Corpse Gate" },
  { id: 8,  cn: "七宝门",   en: "Seven Treasures" },
  { id: 9,  cn: "朝廷",     en: "Imperial Court" },
  { id: 10, cn: "雪山派",   en: "Snow Mountain Sect" },
  { id: 11, cn: "豢龙会",   en: "Dragon-Raising Society" },
  { id: 12, cn: "唐门",     en: "Tang Clan" },
  { id: 13, cn: "剑庐",     en: "Sword Forge" },
  { id: 14, cn: "松山剑派", en: "Songshan Sword Sect" },
  { id: 15, cn: "丐帮",     en: "Beggars' Guild" },
  { id: 16, cn: "东海道心派",en: "East Sea Daoxin Sect" },
  { id: 18, cn: "海鲨帮",   en: "Sea Shark Gang" },
  { id: 19, cn: "万兽山庄", en: "Ten-Thousand-Beasts Manor" },
  { id: 20, cn: "岭南四煞", en: "Lingnan Four Evils" },
  { id: 21, cn: "红袖居",   en: "Red Sleeves Pavilion" },
  { id: 22, cn: "红莲苦海教",en: "Red Lotus Bitter Sea Cult" },
  { id: 23, cn: "楞伽金刚宗",en: "Lankavajra Sect" },
  { id: 24, cn: "漕运帮",   en: "Cao Yun Gang" },
  { id: 25, cn: "泾水城",   en: "Jingshui City" },
  { id: 26, cn: "合欢宗",   en: "Hehuan Sect" },
  { id: 27, cn: "东林书阁", en: "Donglin Pavilion" },
  { id: 28, cn: "方歆武馆", en: "Fang Xin Martial Hall" },
  { id: 29, cn: "大理城",   en: "Dali City" },
  { id: 30, cn: "五毒教",   en: "Five Poisons Cult" },
  { id: 31, cn: "七煞山庄", en: "Seven Evils Manor" },
  { id: 32, cn: "金武卫",   en: "Golden Guard" },
  { id: 33, cn: "百匪谷",   en: "Hundred Bandits Valley" },
  { id: 34, cn: "漠北",     en: "Northern Desert" },
  { id: 35, cn: "神都城",   en: "Capital City" },
  { id: 36, cn: "大理王宫", en: "Dali Royal Palace" },
  { id: 37, cn: "静禅寺",   en: "Quiet Zen Temple" },
  { id: 38, cn: "八通商局", en: "Eight Trade Guild" },
];

let sectState = { current: null };

function renderSectGrid() {
  const grid = $("#sect-grid");
  grid.innerHTML = "";
  const q = $("#sect-search").value.trim().toLowerCase();
  const filtered = SECTS.filter(s => !q || s.cn.includes(q) || s.en.toLowerCase().includes(q) || String(s.id).includes(q));
  $("#sect-count").textContent = `${filtered.length}/${SECTS.length} 门派`;
  filtered.forEach(s => {
    const card = document.createElement("div");
    card.className = "npc-card";
    card.dataset.id = s.id;
    card.innerHTML =
      `<span class="npc-id">${s.id}</span>` +
      `<span class="npc-name"></span>` +
      `<span class="npc-faction"></span>`;
    card.querySelector(".npc-name").textContent = s.cn;
    card.querySelector(".npc-faction").textContent = s.en;
    card.addEventListener("click", () => openSectEditor(s));
    grid.appendChild(card);
  });
}

async function openSectEditor(sect) {
  sectState.current = sect;
  $("#sect-modal-title").textContent = `${sect.cn} · ${sect.en} (id ${sect.id})`;
  $("#sc-id").value = sect.id;
  $("#sc-status").textContent = "";
  $("#sc-status").className = "give-status";

  try {
    const r = await apiCmd({ cmd: "sect_get", id: sect.id });
    if (r.ok) {
      $("#sc-total").value = r.npc_total ?? 0;
      $("#sc-alive").value = r.npc_alive ?? 0;
      $("#sc-dead").value = r.npc_dead ?? 0;
      $("#sc-avg-favo").value = r.avg_favo ?? 0;
      $("#sc-member").checked = !!r.is_member;
    } else {
      setStatus(`sect_get: ${r.message}`, "err");
    }
  } catch (e) {
    setStatus(`sect_get failed: ${e}`, "err");
  }
  $("#sect-modal").hidden = false;
}

async function applyMassFavor(amount) {
  const sect = sectState.current; if (!sect) return;
  try {
    const r = await apiCmd({ cmd: "sect_set", id: sect.id, mass_favor: amount });
    if (r.ok) {
      $("#sc-status").textContent = `${r.message}`;
      $("#sc-status").className = "give-status ok";
      // Refresh stats so user sees updated avg_favo
      const r2 = await apiCmd({ cmd: "sect_get", id: sect.id });
      if (r2.ok) $("#sc-avg-favo").value = r2.avg_favo;
    } else {
      $("#sc-status").textContent = r.message;
      $("#sc-status").className = "give-status err";
    }
  } catch (e) {
    $("#sc-status").textContent = `${e}`;
    $("#sc-status").className = "give-status err";
  }
}

async function saveSectMembership() {
  const sect = sectState.current; if (!sect) return;
  try {
    const r = await apiCmd({ cmd: "sect_set", id: sect.id, is_member: $("#sc-member").checked });
    if (r.ok) {
      $("#sc-status").textContent = r.message;
      $("#sc-status").className = "give-status ok";
      setStatus(`sect ${sect.cn}: ${r.message}`, "ok");
    } else {
      $("#sc-status").textContent = r.message;
      $("#sc-status").className = "give-status err";
    }
  } catch (e) {
    $("#sc-status").textContent = `${e}`;
    $("#sc-status").className = "give-status err";
  }
}

function bindSectEditor() {
  renderSectGrid();
  $("#sect-search").addEventListener("input", renderSectGrid);
  $("#sect-modal-close").addEventListener("click", () => $("#sect-modal").hidden = true);
  $("#sect-modal").addEventListener("click", (e) => {
    if (e.target.id === "sect-modal") e.currentTarget.hidden = true;
  });
  $("#sc-save").addEventListener("click", saveSectMembership);
  $("#sc-favo-m100").addEventListener("click", () => applyMassFavor(-100));
  $("#sc-favo-m50").addEventListener("click",  () => applyMassFavor(-50));
  $("#sc-favo-m10").addEventListener("click",  () => applyMassFavor(-10));
  $("#sc-favo-p10").addEventListener("click",  () => applyMassFavor(10));
  $("#sc-favo-p50").addEventListener("click",  () => applyMassFavor(50));
  $("#sc-favo-p100").addEventListener("click", () => applyMassFavor(100));
  $("#sc-favo-p999").addEventListener("click", () => applyMassFavor(999));
  $("#sc-favo-apply").addEventListener("click", () => {
    const v = parseInt($("#sc-favo-custom").value, 10);
    if (Number.isFinite(v) && v !== 0) applyMassFavor(v);
  });
  $("#sc-favo-custom").addEventListener("keydown", (e) => {
    if (e.key === "Enter") { e.preventDefault(); $("#sc-favo-apply").click(); }
  });
}

// ---------- Appearance (theme + font + size) ----------
const APPEARANCE = {
  themes: ["ink", "paper", "celadon", "crimson", "bamboo"],
  fonts:  ["noto", "notosans", "xiaowei", "kuaile", "qingke", "brush", "longcang", "liujian", "zhimang", "system"],
  defaults: { theme: "ink", font: "xiaowei", size: "1.00" },
};

function loadAppearance() {
  return {
    theme: localStorage.getItem("ui.theme") || APPEARANCE.defaults.theme,
    font:  localStorage.getItem("ui.font")  || APPEARANCE.defaults.font,
    size:  localStorage.getItem("ui.size")  || APPEARANCE.defaults.size,
  };
}

function applyAppearance(a) {
  document.documentElement.setAttribute("data-theme", a.theme);
  document.documentElement.setAttribute("data-font", a.font);
  document.documentElement.style.setProperty("--ui-zoom", a.size);
  // Update active states in the picker UI
  $$(".theme-card").forEach((c) => c.classList.toggle("active", c.dataset.theme === a.theme));
  const fontSel = $("#font-select"); if (fontSel) fontSel.value = a.font;
  $$("[data-size]").forEach((c) => c.classList.toggle("active", c.dataset.size  === a.size));
}

function setAppearance(partial) {
  const cur = loadAppearance();
  const next = { ...cur, ...partial };
  if (partial.theme) localStorage.setItem("ui.theme", partial.theme);
  if (partial.font)  localStorage.setItem("ui.font",  partial.font);
  if (partial.size)  localStorage.setItem("ui.size",  partial.size);
  applyAppearance(next);
}

function bindAppearance() {
  $$(".theme-card").forEach((c) => c.addEventListener("click", () => setAppearance({ theme: c.dataset.theme })));
  const fontSel = $("#font-select");
  if (fontSel) fontSel.addEventListener("change", (e) => setAppearance({ font: e.target.value }));
  $$("[data-size]").forEach((c) => c.addEventListener("click", () => setAppearance({ size:  c.dataset.size  })));
}

// ---------- Items browser ----------
// Categories sourced from the game's own enums: DBLoad/ItemType.cs + ItemType2.cs.
// Every item in item.json carries a `type` (1-5) and `type2` (1-24); the two enums
// define the in-game inventory tab hierarchy, so mirroring them here makes the UI
// match what the player sees in-game.
const ITEM_TYPES = [
  { id: 0, name: "全部 All" },
  { id: 1, name: "武器 Weapons" },
  { id: 2, name: "防具 Armor" },
  { id: 3, name: "秘籍 Manuals" },
  { id: 4, name: "消耗 Consumables" },
  { id: 5, name: "杂物 Misc" },
];
const ITEM_TYPE2S = [
  { id: 0,  parent: -1, name: "全部 All" },
  { id: 1,  parent: 0,  name: "资源 Resource" },
  { id: 2,  parent: 1,  name: "刀 Knife" },
  { id: 3,  parent: 1,  name: "剑 Sword" },
  { id: 4,  parent: 1,  name: "拳掌 Fist & Palm" },
  { id: 5,  parent: 1,  name: "枪棍 Spear & Staff" },
  { id: 6,  parent: 1,  name: "暗器 Hidden Weapon" },
  { id: 7,  parent: 1,  name: "琴 Instrument" },
  { id: 8,  parent: 2,  name: "衣服 Clothes" },
  { id: 9,  parent: 2,  name: "鞋子 Footwear" },
  { id: 10, parent: 2,  name: "饰品 Accessory" },
  { id: 11, parent: 3,  name: "武学 Martial Manual" },
  { id: 12, parent: 3,  name: "丹方 Alchemy Recipe" },
  { id: 13, parent: 3,  name: "图纸 Forging Blueprint" },
  { id: 14, parent: 4,  name: "战斗 Combat" },
  { id: 15, parent: 4,  name: "使用 Usable" },
  { id: 16, parent: 4,  name: "精力 Energy" },
  { id: 17, parent: 4,  name: "恢复 Recovery" },
  { id: 18, parent: 5,  name: "读物 Reading" },
  { id: 19, parent: 5,  name: "任务 Quest" },
  { id: 20, parent: 5,  name: "饲育 Animal Food" },
  { id: 21, parent: 5,  name: "炼丹 Alchemy Material" },
  { id: 22, parent: 5,  name: "打造 Forging Material" },
  { id: 23, parent: 5,  name: "工具 Tool" },
  { id: 24, parent: 5,  name: "礼物 Gift" },
];
const TYPE2_NAME = Object.fromEntries(ITEM_TYPE2S.map(t => [t.id, t.name]));
const TYPE_NAME = Object.fromEntries(ITEM_TYPES.map(t => [t.id, t.name]));

// Columns shown in the items table — fixed set since we always source from item.json.
const ITEM_COLUMNS = ["id", "name", "quality", "type", "type2", "value", "describe"];

const dataCache = {};
// State of the items browser: current filter selections.
let currentTypeFilter = 0;
let currentType2Filter = 0;
let currentRows = [];          // filtered+sorted view used by the modal indexer
let allItems = [];             // full item.json
const MAX_ROWS = 200;

async function loadDataTable(key) {
  if (dataCache[key]) return dataCache[key];
  const res = await fetch(`data/${key}.json`);
  if (!res.ok) throw new Error(`failed to load ${key}.json: ${res.status}`);
  const data = await res.json();
  dataCache[key] = data;
  return data;
}

function renderCell(value) {
  if (value === null || value === undefined) return "—";
  if (Array.isArray(value))  return `[${value.length} items]`;
  if (typeof value === "object") return "{…}";
  return String(value);
}

function renderItemRows(rows) {
  $("#data-table-head").innerHTML = ITEM_COLUMNS.map(c => `<th>${c}</th>`).join("");
  const body = $("#data-table-body");
  body.innerHTML = "";
  $("#data-empty").hidden = rows.length > 0;
  rows.slice(0, MAX_ROWS).forEach((row, idx) => {
    const tr = document.createElement("tr");
    tr.dataset.idx = idx;
    ITEM_COLUMNS.forEach(c => {
      const td = document.createElement("td");
      const klass = c === "id" ? "col-id"
                  : c === "name" ? "col-name"
                  : (c === "describe" || c === "desc" || c === "des") ? "col-desc"
                  : "";
      if (klass) td.className = klass;
      // Show the enum name for type/type2 instead of the raw int.
      let display;
      if (c === "type")  display = TYPE_NAME[row.type]   ?? row.type;
      else if (c === "type2") display = TYPE2_NAME[row.type2] ?? row.type2;
      else display = renderCell(row[c]);
      td.textContent = display;
      td.title = display;
      tr.appendChild(td);
    });
    body.appendChild(tr);
  });
}

function filterItems(rows, type, type2, query) {
  const q = (query || "").toLowerCase().trim();
  return rows.filter(row => {
    if (type !== 0 && row.type !== type) return false;
    if (type2 !== 0 && row.type2 !== type2) return false;
    if (!q) return true;
    for (const v of Object.values(row)) {
      if (v === null || v === undefined) continue;
      if (typeof v === "object") continue;
      if (String(v).toLowerCase().includes(q)) return true;
    }
    return false;
  });
}

function updateDataCount(shown, total) {
  $("#data-count").textContent =
    total === shown ? `${total} 条` :
    shown > MAX_ROWS ? `${MAX_ROWS}/${shown} 显示 · ${total} 总` :
    `${shown}/${total} 条`;
}

function applyDataFilter() {
  const q = $("#data-search").value;
  currentRows = filterItems(allItems, currentTypeFilter, currentType2Filter, q);
  renderItemRows(currentRows);
  updateDataCount(Math.min(currentRows.length, MAX_ROWS), allItems.length);
}

// Rebuild the subcategory <select> to only include children of the chosen main category
// (plus 全部 at the top). When main = 0 ("全部 All"), show every subcategory.
function rebuildType2Select(mainType) {
  const sel = $("#item-type2-select");
  const prev = parseInt(sel.value, 10);
  sel.innerHTML = "";
  const valid = ITEM_TYPE2S.filter(t => t.id === 0 || mainType === 0 || t.parent === mainType);
  for (const t of valid) {
    const opt = document.createElement("option");
    opt.value = t.id;
    opt.textContent = t.name;
    sel.appendChild(opt);
  }
  // Preserve prior selection if still valid, else reset to 全部
  if (valid.some(t => t.id === prev)) sel.value = String(prev);
  else { sel.value = "0"; currentType2Filter = 0; }
}

let givePendingRow = null;
function showDataDetail(row) {
  givePendingRow = row;
  $("#data-detail-title").textContent =
    `#${row.id ?? "?"} ${row.name ?? ""}`.trim();
  $("#data-detail-pre").textContent = JSON.stringify(row, null, 2);
  // Every row in the items tab is a real item — give-to-inventory always applies.
  const showGive = row.id != null;
  $("#give-action").hidden = !showGive;
  if (showGive) {
    $("#give-qty").value = 1;
    $("#give-status").textContent = "";
    $("#give-status").className = "give-status";
  }
  // Alchemy skip — 丹方 (recipe book) rows have type2 == 12
  const showAlchemy = row.type2 === 12 && row.id != null;
  $("#alchemy-skip-action").hidden = !showAlchemy;
  if (showAlchemy) { $("#alchemy-skip-status").textContent = ""; $("#alchemy-skip-status").className = "give-status"; }
  // Forging skip — 图纸 (blueprint) rows have type2 == 13
  const showForging = row.type2 === 13 && row.id != null;
  $("#forging-skip-action").hidden = !showForging;
  if (showForging) { $("#forging-skip-status").textContent = ""; $("#forging-skip-status").className = "give-status"; }
  // Fishing/hunting skip actions live in the Mini-games tab — keeping two paths confused users.

  $("#data-detail-modal").hidden = false;
}

async function doSkipAlchemy(tier) {
  const row = givePendingRow; if (!row) return;
  const statusEl = $("#alchemy-skip-status");
  statusEl.textContent = "发送中…"; statusEl.className = "give-status";
  try {
    const res = await apiCmd({ cmd: "skip_alchemy", recipe: row.id, tier });
    if (res.ok) {
      statusEl.textContent = `✓ ${res.message}`;
      statusEl.className = "give-status ok";
      setStatus(`alchemy skip tier ${tier} (recipe ${row.id})`, "ok");
    } else {
      statusEl.textContent = `× ${res.message}`;
      statusEl.className = "give-status err";
    }
  } catch (e) {
    statusEl.textContent = `× ${e}`; statusEl.className = "give-status err";
  }
}

async function doSkipForging() {
  const row = givePendingRow; if (!row) return;
  const statusEl = $("#forging-skip-status");
  statusEl.textContent = "发送中…"; statusEl.className = "give-status";
  try {
    const res = await apiCmd({ cmd: "skip_forging", recipe: row.id });
    if (res.ok) {
      statusEl.textContent = `✓ ${res.message}`;
      statusEl.className = "give-status ok";
      setStatus(`forging skip (recipe ${row.id})`, "ok");
    } else {
      statusEl.textContent = `× ${res.message}`;
      statusEl.className = "give-status err";
    }
  } catch (e) {
    statusEl.textContent = `× ${e}`; statusEl.className = "give-status err";
  }
}

async function doGive() {
  const row = givePendingRow;
  if (!row) return;
  const qty = parseInt($("#give-qty").value, 10);
  if (!Number.isFinite(qty) || qty < 1) {
    $("#give-status").textContent = "数量无效"; $("#give-status").className = "give-status err"; return;
  }
  const btn = $("#give-btn"); btn.disabled = true;
  $("#give-status").textContent = "发送中…"; $("#give-status").className = "give-status";
  try {
    const res = await apiCmd({ cmd: "give_item", id: row.id, num: qty });
    if (res.ok) {
      $("#give-status").textContent = `✓ ${res.message ?? "added"}`;
      $("#give-status").className = "give-status ok";
      setStatus(`+ ${qty} × ${row.name ?? row.id}`, "ok");
    } else {
      $("#give-status").textContent = `× ${res.message ?? "failed"}`;
      $("#give-status").className = "give-status err";
    }
  } catch (e) {
    $("#give-status").textContent = `× ${e}`;
    $("#give-status").className = "give-status err";
  } finally {
    btn.disabled = false;
  }
}

async function bindDataBrowser() {
  // Always source from item.json — every game item is in here. Load once, then filter.
  $("#data-table-body").innerHTML = "<tr><td colspan='10'>加载中…</td></tr>";
  try {
    allItems = await loadDataTable("item");
  } catch (e) {
    $("#data-table-body").innerHTML = `<tr><td colspan='10'>加载失败: ${e.message}</td></tr>`;
    return;
  }

  // Populate main category <select> from ITEM_TYPES
  const mainSel = $("#item-type-select");
  for (const t of ITEM_TYPES) {
    const opt = document.createElement("option");
    opt.value = t.id; opt.textContent = t.name;
    mainSel.appendChild(opt);
  }
  // Initial subcategory list (all)
  rebuildType2Select(0);

  mainSel.addEventListener("change", () => {
    currentTypeFilter = parseInt(mainSel.value, 10);
    rebuildType2Select(currentTypeFilter);
    currentType2Filter = parseInt($("#item-type2-select").value, 10);
    applyDataFilter();
  });
  $("#item-type2-select").addEventListener("change", (e) => {
    currentType2Filter = parseInt(e.target.value, 10);
    applyDataFilter();
  });

  $("#data-search").addEventListener("input", () => applyDataFilter());

  $("#data-table-body").addEventListener("click", (e) => {
    const tr = e.target.closest("tr[data-idx]");
    if (!tr) return;
    const row = currentRows[+tr.dataset.idx];
    if (row) showDataDetail(row);
  });

  $("#data-detail-close").addEventListener("click", () => {
    $("#data-detail-modal").hidden = true;
  });
  $("#data-detail-modal").addEventListener("click", (e) => {
    if (e.target.id === "data-detail-modal") e.currentTarget.hidden = true;
  });

  // Give action — quantity input + Add button
  $("#give-btn").addEventListener("click", doGive);
  $("#give-qty").addEventListener("keydown", (e) => {
    if (e.key === "Enter") { e.preventDefault(); doGive(); }
  });

  // Alchemy / forging skip buttons
  $("#alchemy-skip-0").addEventListener("click", () => doSkipAlchemy(0));
  $("#alchemy-skip-1").addEventListener("click", () => doSkipAlchemy(1));
  $("#alchemy-skip-2").addEventListener("click", () => doSkipAlchemy(2));
  $("#forging-skip-btn").addEventListener("click", doSkipForging);

  // Animal taming — per-animal skip
  $("#tame-one-btn").addEventListener("click", async () => {
    const id = parseInt($("#tame-npc-id").value, 10);
    const statusEl = $("#tame-status");
    if (!Number.isFinite(id) || id <= 0) {
      statusEl.textContent = "需要有效的 npcId"; statusEl.className = "give-status err"; return;
    }
    try {
      const r = await apiCmd({ cmd: "skip_taming_one", npc_id: id });
      statusEl.textContent = r.ok ? `✓ ${r.message}` : `× ${r.message}`;
      statusEl.className = "give-status " + (r.ok ? "ok" : "err");
    } catch (e) { statusEl.textContent = `× ${e}`; statusEl.className = "give-status err"; }
  });

  // Initial render with no filter (全部 / 全部)
  applyDataFilter();
}

// ---------- Init ----------
window.addEventListener("DOMContentLoaded", async () => {
  // Apply saved appearance immediately so the UI never flashes default theme
  applyAppearance(loadAppearance());

  bindTabs();
  bindButtons();
  bindToggles();
  bindHotkeys();
  bindErrorLog();
  bindAppearance();
  bindDataBrowser();
  bindNpcEditor();
  bindSectEditor();
  bindWindowControls();
  await handshake();
  startPolling();
});
