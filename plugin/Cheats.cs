using System.Collections.Generic;
using DBLoad;
using UnityEngine;

namespace JinGuCheats;

// All game-state mutations live here. Every method must be called on the Unity main thread
// (via MainThreadDispatcher.Run/Get). HTTP handlers MUST NOT touch these directly.
internal static class Cheats
{
    private const int MONEY_ITEM_ID = 10001;

    // CAREFUL: SaveManager.SaveData auto-creates a placeholder ("虾米/大") when accessed
    // before a real save is loaded. So we gate on the scene first.
    private static bool IsInGame()
    {
        if (SaveManager.Instance == null) return false;
        try
        {
            // SceneEnum.启动场景 = 0 (splash), 登录 = 1 (login). Anything else = in a save.
            int idx = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            return idx > 1;
        }
        catch { return false; }
    }

    private static SaveData? Save =>
        IsInGame() ? SaveManager.Instance.SaveData : null;

    // Latest snapshot, refreshed each frame on the main thread by Plugin.OnUpdate().
    // Pipe handlers read this directly (no main-thread marshalling) so /api/state never
    // times out — even when the game window loses focus and Update stops ticking, the
    // last-known snapshot stays accessible.
    private static StateSnapshot? _cached;
    private static readonly object _cacheLock = new();

    public static void RefreshCache()
    {
        try
        {
            var snap = Snapshot();
            lock (_cacheLock) _cached = snap;
        }
        catch (System.Exception e) { ErrorLog.Record("cache-refresh", e); }
    }

    public static StateSnapshot GetCached()
    {
        lock (_cacheLock)
        {
            if (_cached != null) return _cached;
        }
        // First-call path: synth an "out-of-game" snapshot so the UI has something to render.
        return new StateSnapshot
        {
            in_game = false,
            toggles = new System.Collections.Generic.Dictionary<string, bool>
            {
                ["max_money"]       = Plugin.CfgMaxMoney.Value,
                ["free_actions"]    = Plugin.CfgFreeActions.Value,
                ["god_mode"]        = Plugin.CfgGodMode.Value,
                ["freeze_time"]     = Plugin.CfgFreezeTime.Value,
                ["auto_launch_ui"]  = Plugin.CfgAutoLaunchUi.Value,
                ["auto_wei_tuo"]    = Plugin.CfgAutoWeiTuo.Value,
                ["map_teleport"]    = Plugin.CfgMapTeleport.Value,
            }
        };
    }

    // ---------- State snapshot ----------
    public sealed class StateSnapshot
    {
        public bool in_game;
        public int money;
        public int game_time;
        public int real_time;
        public string leader_name = "";
        public string leader_family = "";
        public int difficulty;
        public int jingli;       // current 精力 (wilderness stamina), 0..jingli_max
        public int tili;         // current 体力 (in-town stamina), 0..tili_max
        public int jingli_max;   // EffectId.精力上限 (601), cached separately for the UI badge
        public int tili_max;     // EffectId.体力上限 (602)
        public Dictionary<string, bool> toggles = new();
        // Effect values surfaced for inline live-value badges in the UI (Option A from the
        // value-audit table). Each id here gets a single GetEffectValue() per poll — cheap.
        public Dictionary<int, float> effects = new();
        // Rolling auto-委托 collection log so the player can read intel notes returned
        // by their disciples without manually opening each item.
        public List<string> wei_tuo_log = new();
    }

    // EffectIds whose live value the state snapshot exposes — drives the UI's inline
    // current-value badges next to each cheat row. Add an id here = it becomes pollable.
    private static readonly int[] PolledEffectIds = new[]
    {
        // Direct XP pools (each feeds GetEffectLv to drive a skill level)
        301, 302, 303, 304, 305, 306, 307, 308,  // 8 living arts direct XP
        501, 503,                                 // 内功 direct XP, 实战 direct XP

        // Six pillars (raw stat values)
        101, 102, 103, 104, 105, 106,

        // Six weapon mastery
        201, 202, 203, 204, 205, 206,

        // Reputation
        401, 402, 403,                            // 善恶 / 名望 / 情缘

        // Energy caps (current values live on save.JingLi / save.TiLi properties, exposed separately)
        601, 602,                                 // 精力上限 / 体力上限

        // XP rate boost multipliers (% rate per EffectId convention — game divides by 100 at use)
        20, 21, 717,                              // 实战经验, 武学经验, 内功经验
        720, 721, 722, 723, 724, 725, 726, 732,  // 8 living arts rate boosts
    };

    public static StateSnapshot Snapshot()
    {
        var s = new StateSnapshot
        {
            toggles = new Dictionary<string, bool>
            {
                ["max_money"]       = Plugin.CfgMaxMoney.Value,
                ["free_actions"]    = Plugin.CfgFreeActions.Value,
                ["god_mode"]        = Plugin.CfgGodMode.Value,
                ["freeze_time"]     = Plugin.CfgFreezeTime.Value,
                ["auto_launch_ui"]  = Plugin.CfgAutoLaunchUi.Value,
                ["auto_wei_tuo"]    = Plugin.CfgAutoWeiTuo.Value,
                ["map_teleport"]    = Plugin.CfgMapTeleport.Value,
            }
        };

        // Only touch SaveData if a real save is actually loaded — otherwise
        // accessing the getter would create a "虾米" placeholder and corrupt our signal.
        if (!IsInGame()) { s.in_game = false; return s; }
        var save = Save;
        if (save == null) { s.in_game = false; return s; }
        s.in_game       = true;
        s.money         = save.GetItemNum(MONEY_ITEM_ID);
        s.game_time     = save.m_gameTime;
        s.real_time     = save.RealTime;
        s.leader_name   = save.m_leaderName ?? "";
        s.leader_family = save.m_leaderFamily ?? "";
        s.difficulty    = (int)save.m_diffucultEnum;
        s.jingli        = save.JingLi;
        s.tili          = save.TiLi;
        s.jingli_max    = save.GetEffectValueInt(EffectId.精力上限);
        s.tili_max      = save.GetEffectValueInt(EffectId.体力上限);
        foreach (var id in PolledEffectIds)
            s.effects[id] = save.GetEffectValue((EffectId)id);
        s.wei_tuo_log = SnapshotWeiTuoLog();
        return s;
    }

    // ---------- Commands ----------
    public static string GiveMoney(int amount)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        save.AddItems(new[] { MONEY_ITEM_ID }, new[] { amount });
        return $"gave {amount:N0} 银两";
    }

    public static string GiveItem(int id, int num)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        save.AddItems(new[] { id }, new[] { num });
        return $"gave {num} of item {id}";
    }

    // For these "direct XP" effects, SaveData.AddEffectValue silently multiplies the add
    // amount by (rate / 100) — see SaveData.cs:1322 switch. That means "+1000 钓鱼 XP" with
    // 钓鱼经验 = 500 actually adds 5000. We want the slider to behave literally, so we
    // pin the rate to 100 (1.0×) around the call and restore it after.
    // 实战 (503) and 武学 are NOT in that switch (their multipliers are applied elsewhere
    // in FightResultWindow / XiuLianWindow at battle/training time), so they need no pin.
    private static readonly Dictionary<EffectId, EffectId> DirectXpRateMultiplier = new()
    {
        [EffectId.驯兽] = EffectId.驯兽经验,
        [EffectId.挖掘] = EffectId.挖掘经验,
        [EffectId.钓鱼] = EffectId.钓鱼经验,
        [EffectId.打猎] = EffectId.打猎经验,
        [EffectId.炼丹] = EffectId.炼丹经验,
        [EffectId.下棋] = EffectId.下棋经验,
        [EffectId.打造] = EffectId.打造经验,
        [EffectId.采集] = EffectId.采集经验,
        [EffectId.内功] = EffectId.内功经验,
    };

    public static string AddEffect(int effectId, float value)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        var id = (EffectId)effectId;

        // Pin the rate multiplier to 100 (= 1.0×) so AddEffectValue adds exactly `value`.
        float restoreDelta = 0f;
        EffectId rateId = default;
        bool pinned = false;
        if (DirectXpRateMultiplier.TryGetValue(id, out rateId))
        {
            float cur = save.GetEffectValue(rateId);
            if (System.Math.Abs(cur - 100f) > 0.0001f)
            {
                save.AddEffectValue(rateId, 100f - cur, needTips: false);
                restoreDelta = cur - 100f;
                pinned = true;
            }
        }
        try
        {
            save.AddEffectValue(id, value, needTips: true);
        }
        finally
        {
            if (pinned) save.AddEffectValue(rateId, restoreDelta, needTips: false);
        }
        return $"added {value} to effect {effectId} ({id})";
    }

    // SetEffect assigns an absolute value to an EffectId — the game has no SetEffectValue,
    // so we read the current value and apply the delta via AddEffectValue. Used by the UI's
    // XP-rate-boost toggles: toggle-on sends value = slider, toggle-off sends value = 0.
    public static string SetEffect(int effectId, float value)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        var id = (EffectId)effectId;
        float cur = save.GetEffectValue(id);
        float delta = value - cur;
        if (System.Math.Abs(delta) < 0.0001f) return $"effect {effectId} already {value}";
        save.AddEffectValue(id, delta, needTips: false);
        return $"effect {effectId} set to {value} (was {cur:0.##})";
    }

    public static string AddHour(int hours)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        save.AddHour(hours);
        return $"advanced {hours} hours";
    }

    public static string MaxAllWuXue()
    {
        var save = Save;
        if (save == null) return "no save loaded";
        int count = 0;
        foreach (var kv in WuXue.Dic)
        {
            var wd = kv.Value;
            int currentLv = save.GetWuXueLv(wd.m_id);
            if (currentLv >= wd.m_lvMax) continue;
            int totalExpNeeded = wd.m_lvMax * wd.m_exp;
            save.AddWuXueExp(wd.m_id, totalExpNeeded, needTips: false);
            count++;
        }
        return $"maxed {count} wuxue skills";
    }

    // Rolling log of what auto-委托 has collected — surfaced to the UI so the player
    // can read the new intel notes / loot without opening every 读物 item by hand.
    // Bounded queue (oldest entries drop off). Reset only on plugin reload.
    private static readonly System.Collections.Generic.Queue<string> _weiTuoLog = new();
    private const int WEI_TUO_LOG_MAX = 60;
    public static void ClearWeiTuoLog() { lock (_weiTuoLog) _weiTuoLog.Clear(); }
    public static System.Collections.Generic.List<string> SnapshotWeiTuoLog()
    {
        lock (_weiTuoLog) return new System.Collections.Generic.List<string>(_weiTuoLog);
    }
    private static void AppendWeiTuoLog(string msg)
    {
        lock (_weiTuoLog)
        {
            _weiTuoLog.Enqueue(msg);
            while (_weiTuoLog.Count > WEI_TUO_LOG_MAX) _weiTuoLog.Dequeue();
        }
    }

    // 发布委托 auto-tick: collects everything ready, then dispatches up to the limit.
    // Driven from UpdateDriver. No throttle — work is cheap (6 commission ids in the
    // table) and the operations are idempotent: PostReceived guards re-dispatch and
    // the completion check is a Turn comparison. We DO throttle the diagnostic log to
    // avoid spam, and only log when the toggle is on (to avoid noise from off-state).
    private static float _lastWeiTuoLog;
    private static int _lastWeiTuoDispatched;
    private static int _lastWeiTuoCollected;
    public static void AutoWeiTuoTick()
    {
        if (!Plugin.CfgAutoWeiTuo.Value) return;
        var save = Save;
        if (save == null) return;
        int turn = save.Turn;

        var dic = DBLoad.ReceivePost.Dic;
        if (dic == null || dic.Count == 0)
        {
            MaybeLog($"[auto_wei_tuo] ReceivePost.Dic is empty (count={(dic?.Count ?? 0)}) — table not loaded yet?");
            return;
        }

        // Stable id snapshot — ReceivePost / GetPost mutate save state but not this table.
        var ids = new System.Collections.Generic.List<int>(dic.Keys);

        // Pass 1: collect anything completed (frees up dispatch slots first).
        // For each collection we capture the reward by diffing inventory counts on the
        // commission's possible reward+trash ids before and after the call — the changed
        // entry is what GetPost gave us. Then we append a log line the UI can read.
        int collected = 0;
        foreach (var id in ids)
        {
            if (save.PostReceived(id))
            {
                var info = DBLoad.ReceivePost.Get(id);
                if (info != null && turn - save.GetPostBegin(id) >= info.m_time)
                {
                    // Pre-snapshot of candidate item counts (rewards + trash)
                    var watchIds = new System.Collections.Generic.HashSet<int>();
                    if (info.m_rewardId != null) foreach (var w in info.m_rewardId) watchIds.Add(w);
                    if (info.m_trashId != null)  foreach (var w in info.m_trashId)  watchIds.Add(w);
                    var before = new System.Collections.Generic.Dictionary<int, int>();
                    foreach (var wid in watchIds) before[wid] = save.GetItemNum(wid);

                    try
                    {
                        save.GetPost(id);
                        collected++;
                        LogCollectionDiff(info, watchIds, before, save);
                    }
                    catch (System.Exception e) { ErrorLog.Record("auto_wei_tuo:collect", e); }
                }
            }
        }

        // Pass 2: dispatch any idle commission, up to the simultaneous limit.
        int limit = save.GetEffectValueInt(EffectId.同时发布委托个数);
        int active = 0;
        foreach (var id in ids) if (save.PostReceived(id)) active++;
        int dispatched = 0;
        foreach (var id in ids)
        {
            if (active >= limit) break;
            if (save.PostReceived(id)) continue;
            try
            {
                if (save.ReceivePost(id)) { active++; dispatched++; }
            }
            catch (System.Exception e) { ErrorLog.Record("auto_wei_tuo:dispatch", e); }
        }

        // Log when something actually happened, OR periodically state-dump for diagnostics.
        if (dispatched > 0 || collected > 0)
        {
            Plugin.Log.Msg($"[auto_wei_tuo] turn {turn}: collected {collected}, dispatched {dispatched} (active {active}/{limit})");
            _lastWeiTuoDispatched += dispatched;
            _lastWeiTuoCollected += collected;
        }
        else
        {
            // Once every 10 seconds of wall time, log the current state so we can see why nothing's happening
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastWeiTuoLog > 10f)
            {
                _lastWeiTuoLog = now;
                Plugin.Log.Msg($"[auto_wei_tuo] turn {turn}, table={ids.Count}, active={active}/{limit}, totals collected={_lastWeiTuoCollected} dispatched={_lastWeiTuoDispatched}");
            }
        }
    }

    private static void MaybeLog(string msg)
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now - _lastWeiTuoLog > 5f)
        {
            _lastWeiTuoLog = now;
            Plugin.Log.Msg(msg);
        }
    }

    // Given the pre-call snapshot of candidate item counts, find what GetPost added and
    // build a human-readable log entry. For 江湖情报 (and any item with an Information
    // table entry), we inline the actual lore text so the player can read it directly.
    private static void LogCollectionDiff(
        DBLoad.ReceivePostData info,
        System.Collections.Generic.HashSet<int> watchIds,
        System.Collections.Generic.Dictionary<int, int> before,
        SaveData save)
    {
        string commissionName = SafeStr(info.m_name);
        foreach (var wid in watchIds)
        {
            int after = save.GetItemNum(wid);
            int delta = after - (before.TryGetValue(wid, out var b) ? b : 0);
            if (delta <= 0) continue;

            string itemName = "?";
            var itemData = DBLoad.Item.Get(wid);
            if (itemData != null) itemName = SafeStr(itemData.m_name);

            string content = "";
            var infoEntry = DBLoad.Information.Get(wid);
            if (infoEntry != null && !string.IsNullOrEmpty(infoEntry.m_desc))
            {
                string desc = SafeStr(infoEntry.m_desc);
                // Trim newlines so the log row stays one logical entry
                content = "  →  " + desc.Replace("\n", " · ").Replace("\r", "");
            }

            string stamp = System.DateTime.Now.ToString("HH:mm:ss");
            string msg = $"[{stamp}] {commissionName}  ⇒  {itemName} #{wid} ×{delta}{content}";
            AppendWeiTuoLog(msg);
            // Also mirror to the MelonLoader log for offline review
            Plugin.Log.Msg($"[auto_wei_tuo] {msg}");
        }
    }

    private static string SafeStr(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        try { return LanguageUtils.GetStr(raw) ?? raw; }
        catch { return raw; }
    }

    public static string RefillEnergy(bool jingli, bool tili)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        int filled = 0;
        if (jingli) { save.JingLiPer = 1f; filled++; }
        if (tili)   { save.TiLi = save.GetEffectValueInt(EffectId.体力上限); filled++; }
        return $"refilled {filled} energy pool(s)";
    }

    public static string HealTeam()
    {
        var rm = RoleManager.Instance;
        if (rm == null) return "no battle in progress";
        int healed = 0;
        foreach (var role in rm.m_list)
        {
            if (role == null || role.m_camp != Fight.CampType.Friend) continue;
            role.CurHp = role.MaxHp;
            role.CurMp = role.MaxMp;
            healed++;
        }
        return $"healed {healed} friendly roles";
    }

    // ---------- Toggle setter ----------
    public static string SetToggle(string name, bool value)
    {
        switch (name)
        {
            case "max_money":      Plugin.CfgMaxMoney.Value     = value; break;
            case "free_actions":   Plugin.CfgFreeActions.Value  = value; break;
            case "god_mode":       Plugin.CfgGodMode.Value      = value; break;
            case "freeze_time":    Plugin.CfgFreezeTime.Value   = value; break;
            case "auto_launch_ui": Plugin.CfgAutoLaunchUi.Value = value; break;
            case "auto_wei_tuo":   Plugin.CfgAutoWeiTuo.Value   = value; break;
            case "map_teleport":   Plugin.CfgMapTeleport.Value  = value; break;
            default: return $"unknown toggle '{name}'";
        }
        return $"{name} = {value}";
    }

    // ---------- Hotkey setter (user-bound via UI) ----------
    // Pass keyCode = 0 (KeyCode.None) to clear the binding.
    public static string SetHotkey(string name, int keyCode)
    {
        var kc = (UnityEngine.KeyCode)keyCode;
        switch (name)
        {
            case "max_money":    Plugin.CfgKeyMaxMoney.Value    = kc; break;
            case "free_actions": Plugin.CfgKeyFreeActions.Value = kc; break;
            case "god_mode":     Plugin.CfgKeyGodMode.Value     = kc; break;
            case "freeze_time":  Plugin.CfgKeyFreezeTime.Value  = kc; break;
            default: return $"unknown hotkey '{name}'";
        }
        return kc == UnityEngine.KeyCode.None
            ? $"{name}: cleared"
            : $"{name}: bound to {kc}";
    }

    // Used by /api/state so the UI knows what's currently bound (and can render it).
    public static System.Collections.Generic.Dictionary<string, int> Hotkeys() => new()
    {
        ["max_money"]    = (int)Plugin.CfgKeyMaxMoney.Value,
        ["free_actions"] = (int)Plugin.CfgKeyFreeActions.Value,
        ["god_mode"]     = (int)Plugin.CfgKeyGodMode.Value,
        ["freeze_time"]  = (int)Plugin.CfgKeyFreezeTime.Value,
    };

    // ============================================================
    // NPC editor support — runtime state in save, plus static template edits
    // ============================================================

    // Baseline cache of original CharacterData so we can offer "reset to template"
    private static readonly System.Collections.Generic.Dictionary<int, CharacterData> _originalChar = new();

    private static System.Reflection.FieldInfo? _field(System.Type t, string name) =>
        t.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

    public static string NpcGet(int npcId)
    {
        var save = Save;
        if (save == null) return new Json.Obj().Add("ok", false).Add("message", "no save loaded").ToString();

        var npcDic = save.NpcDic;
        npcDic.TryGetValue(npcId, out var npc);
        var data = Npc.Get(npcId);
        if (data == null) return new Json.Obj().Add("ok", false).Add("message", $"unknown npc {npcId}").ToString();

        // Read private NpcInfo fields via reflection (m_robed, m_show, m_deaded, m_favo, m_camp, m_book)
        bool robed = false, show = true, deaded = false; int favo = data.m_favor, book = 0, camp = (int)NpcCamp.牛家村;
        int money = data.m_money;
        if (npc != null) {
            money  = npc.m_money;
            camp   = (int)npc.m_camp;
            var t  = typeof(NpcInfo);
            robed  = (bool)(_field(t, "m_robed")?.GetValue(npc)  ?? false);
            show   = (bool)(_field(t, "m_show")?.GetValue(npc)   ?? true);
            deaded = (bool)(_field(t, "m_deaded")?.GetValue(npc) ?? false);
            favo   = (int)(_field(t, "m_favo")?.GetValue(npc)    ?? data.m_favor);
            book   = (int)(_field(t, "m_book")?.GetValue(npc)    ?? 0);
        }
        int friendLv = save.GetFriendLv(npcId);
        return new Json.Obj()
            .Add("ok", true)
            .Add("id", npcId)
            .Add("character_id", data.m_characterId)
            .Add("money", money)
            .Add("favo", favo)
            .Add("camp", camp)
            .Add("robed", robed)
            .Add("show", show)
            .Add("deaded", deaded)
            .Add("book", book)
            .Add("friend_lv", friendLv)
            .Add("group", data.m_group)
            .Add("base_favor", data.m_favor)
            .Add("base_money", data.m_money)
            .ToString();
    }

    public static string NpcSet(int npcId, int? money, int? favo, int? friendLv, bool? robed, bool? show, bool? deaded, int? camp)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        if (Npc.Get(npcId) == null) return $"unknown npc {npcId}";

        // Force the NpcInfo to exist by reading via the dic accessor
        var dic = save.NpcDic;
        if (!dic.TryGetValue(npcId, out var npc))
        {
            // Construct via reflection — NpcInfo has a public ctor(int id)
            var ctor = typeof(NpcInfo).GetConstructor(new[] { typeof(int) });
            npc = (NpcInfo)ctor!.Invoke(new object[] { npcId });
            dic[npcId] = npc;
        }
        if (money != null)  npc.m_money = money.Value;
        if (camp  != null)  npc.m_camp  = (NpcCamp)camp.Value;
        var t = typeof(NpcInfo);
        if (favo   != null) _field(t, "m_favo")?.SetValue(npc, favo.Value);
        if (robed  != null) _field(t, "m_robed")?.SetValue(npc, robed.Value);
        if (show   != null) _field(t, "m_show")?.SetValue(npc, show.Value);
        if (deaded != null) _field(t, "m_deaded")?.SetValue(npc, deaded.Value);
        if (friendLv != null) save.SetFriendLv(npcId, friendLv.Value);
        return $"npc {npcId} updated";
    }

    public static string CharacterGet(int charId)
    {
        var cd = Character.Get(charId);
        if (cd == null) return new Json.Obj().Add("ok", false).Add("message", $"unknown character {charId}").ToString();
        var o = new Json.Obj()
            .Add("ok", true)
            .Add("id", cd.m_id)
            .Add("res", cd.m_res)
            .Add("book", cd.m_book)
            .Add("chengHao", cd.m_chengHao ?? "")
            .Add("hp", cd.m_hp).Add("mp", cd.m_mp)
            .Add("damage", cd.m_damage).Add("atk", cd.m_atk).Add("def", cd.m_def)
            .Add("crt", cd.m_crt).Add("eva", cd.m_eva)
            .Add("speed", cd.m_speed).Add("move", cd.m_move).Add("range", cd.m_range)
            .Add("sizeType", cd.m_sizeType)
            .Add("behaviorTree", cd.m_behaviorTree ?? "")
            .Add("kind", cd.m_kind)
            .Add("modified", _originalChar.ContainsKey(charId));
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < cd.m_skills.Length; i++) { if (i > 0) sb.Append(','); sb.Append(cd.m_skills[i]); }
        sb.Append(']');
        o.AddRaw("skills", sb.ToString());
        sb.Clear().Append('[');
        for (int i = 0; i < cd.m_passives.Length; i++) { if (i > 0) sb.Append(','); sb.Append(cd.m_passives[i]); }
        sb.Append(']');
        o.AddRaw("passives", sb.ToString());
        return o.ToString();
    }

    // Build a new CharacterData with selected fields overridden + optional skill/passive list replacement.
    private static CharacterData MutateCharacter(int charId, System.Collections.Generic.Dictionary<string, object?> patch)
    {
        var cur = Character.Get(charId);
        if (cur == null) throw new System.ArgumentException($"unknown character {charId}");
        if (!_originalChar.ContainsKey(charId)) _originalChar[charId] = cur;   // snapshot once

        int get(string k, int defVal) => patch.TryGetValue(k, out var v) && v is int i ? i : defVal;
        string getS(string k, string defVal) => patch.TryGetValue(k, out var v) && v is string s ? s : defVal;
        int[] getA(string k, int[] defVal) => patch.TryGetValue(k, out var v) && v is int[] a ? a : defVal;

        return new CharacterData(
            cur.m_id, get("res", cur.m_res), get("book", cur.m_book), getS("chengHao", cur.m_chengHao ?? ""),
            get("hp", cur.m_hp), get("mp", cur.m_mp), get("damage", cur.m_damage),
            get("atk", cur.m_atk), get("def", cur.m_def),
            get("crt", cur.m_crt), get("eva", cur.m_eva),
            get("speed", cur.m_speed), get("move", cur.m_move), get("range", cur.m_range),
            get("sizeType", cur.m_sizeType),
            getA("skills", cur.m_skills), getA("passives", cur.m_passives),
            getS("behaviorTree", cur.m_behaviorTree ?? ""), get("kind", cur.m_kind));
    }

    public static string CharacterSetStat(int charId, string stat, int value)
    {
        if (Character.Get(charId) == null) return $"unknown character {charId}";
        var patch = new System.Collections.Generic.Dictionary<string, object?> { [stat] = value };
        Character.Dic[charId] = MutateCharacter(charId, patch);
        return $"character {charId} {stat} = {value}";
    }

    public static string CharacterAddSkill(int charId, int skillId, bool asPassive)
    {
        var cur = Character.Get(charId);
        if (cur == null) return $"unknown character {charId}";
        var arr = asPassive ? cur.m_passives : cur.m_skills;
        foreach (var x in arr) if (x == skillId) return $"already present";
        var next = new int[arr.Length + 1];
        System.Array.Copy(arr, next, arr.Length);
        next[arr.Length] = skillId;
        var patch = new System.Collections.Generic.Dictionary<string, object?> { [asPassive ? "passives" : "skills"] = next };
        Character.Dic[charId] = MutateCharacter(charId, patch);
        return $"added {(asPassive ? "passive" : "skill")} {skillId} to character {charId}";
    }

    public static string CharacterRemoveSkill(int charId, int skillId, bool asPassive)
    {
        var cur = Character.Get(charId);
        if (cur == null) return $"unknown character {charId}";
        var arr = asPassive ? cur.m_passives : cur.m_skills;
        var next = new System.Collections.Generic.List<int>(arr.Length);
        bool removed = false;
        foreach (var x in arr) { if (!removed && x == skillId) { removed = true; continue; } next.Add(x); }
        if (!removed) return $"not present";
        var patch = new System.Collections.Generic.Dictionary<string, object?> { [asPassive ? "passives" : "skills"] = next.ToArray() };
        Character.Dic[charId] = MutateCharacter(charId, patch);
        return $"removed {(asPassive ? "passive" : "skill")} {skillId} from character {charId}";
    }

    public static string CharacterReset(int charId)
    {
        if (!_originalChar.TryGetValue(charId, out var original)) return $"character {charId} not modified";
        Character.Dic[charId] = original;
        _originalChar.Remove(charId);
        return $"character {charId} reset to template";
    }

    // ============================================================
    // Unlock-all-features (closest equivalent to "reveal map" — this game gates
    // features like alchemy / forging / meditation / sect tasks behind progression)
    // ============================================================
    public static string UnlockAllFeatures()
    {
        var save = Save;
        if (save == null) return "no save loaded";
        int added = 0;
        for (int id = 1; id <= 9; id++) { if (save.Unlock(id)) added++; }
        return $"unlocked {added} new feature(s); total {9} now available";
    }

    // ============================================================
    // Sect / Faction editor
    // ============================================================
    public static string SectGet(int campId)
    {
        var save = Save;
        if (save == null) return new Json.Obj().Add("ok", false).Add("message", "no save loaded").ToString();
        var camp = (NpcCamp)campId;
        bool isMember = save.CampHash.Contains(camp);

        // Count NPCs in this faction + how many alive vs dead, plus total/avg current Favo
        int total = 0, alive = 0, dead = 0;
        long sumFavo = 0;
        foreach (var kv in Npc.Dic)
        {
            // m_camp lives on NpcInfo (runtime), but NpcData has m_group (the static template camp)
            // We need to filter by current NpcInfo camp if exists, else by template group.
            int group = kv.Value.m_group;
            var info = save.GetNpcInfo(kv.Key);
            var effectiveCamp = info != null ? (int)info.m_camp : group;
            if (effectiveCamp != campId) continue;
            total++;
            if (info != null && info.Deaded) dead++;
            else alive++;
            if (info != null) sumFavo += info.Favo;
        }
        int avgFavo = total > 0 ? (int)(sumFavo / total) : 0;
        return new Json.Obj()
            .Add("ok", true)
            .Add("camp_id", campId)
            .Add("is_member", isMember)
            .Add("npc_total", total)
            .Add("npc_alive", alive)
            .Add("npc_dead", dead)
            .Add("avg_favo", avgFavo)
            .ToString();
    }

    // ============================================================
    // Mini-game skip — animal taming
    // ============================================================
    // Animal "level" derives from how much exp the player has fed it (m_animalExpDic[npcId]).
    // Each level has an exp threshold in Animal.Dic (rows keyed by npcId × 1000 + level).
    public static string SkipTamingMaxAll()
    {
        var save = Save;
        if (save == null) return "no save loaded";
        // Build npcId → max exp threshold across all levels
        var maxExpByNpc = new System.Collections.Generic.Dictionary<int, int>();
        foreach (var ad in DBLoad.Animal.Dic.Values)
        {
            maxExpByNpc.TryGetValue(ad.m_npcId, out var cur);
            if (ad.m_exp > cur) maxExpByNpc[ad.m_npcId] = ad.m_exp;
        }
        // Mutate the private dictionary via reflection (same pattern as NpcInfo edits)
        var fld = typeof(SaveData).GetField("m_animalExpDic",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dic = fld?.GetValue(save) as System.Collections.Generic.IDictionary<int, int>;
        if (dic == null) return "could not access m_animalExpDic";
        foreach (var kv in maxExpByNpc) dic[kv.Key] = kv.Value;
        return $"maxed bond with {maxExpByNpc.Count} animals";
    }

    public static string SkipTamingOne(int npcId)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        int maxExp = 0;
        foreach (var ad in DBLoad.Animal.Dic.Values)
            if (ad.m_npcId == npcId && ad.m_exp > maxExp) maxExp = ad.m_exp;
        if (maxExp == 0) return $"unknown animal npcId {npcId} (or only level 1 exists)";
        var fld = typeof(SaveData).GetField("m_animalExpDic",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dic = fld?.GetValue(save) as System.Collections.Generic.IDictionary<int, int>;
        if (dic == null) return "could not access m_animalExpDic";
        dic[npcId] = maxExp;
        return $"animal {npcId} bond → max (exp {maxExp})";
    }

    // ============================================================
    // Mini-game skip — fishing + hunting
    // (random reward variant: omit id, plugin picks randomly from the table.
    //  hunt outcomes also weighted-rolled by m_itemValue — same as real gameplay.)
    // ============================================================
    public static string SkipFishing(int fishId, int count)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        count = System.Math.Max(1, System.Math.Min(99, count));
        var rand = new System.Random();
        var pool = System.Linq.Enumerable.ToArray(DBLoad.Fish.Dic.Values);
        if (pool.Length == 0) return "no fish data";

        var caught = new System.Collections.Generic.Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            int id = fishId > 0 ? fishId : pool[rand.Next(pool.Length)].m_id;
            caught.TryGetValue(id, out var cur);
            caught[id] = cur + 1;
        }
        // Give all in one call; add XP scaled
        var ids = new int[caught.Count]; var nums = new int[caught.Count];
        int k = 0; foreach (var kv in caught) { ids[k] = kv.Key; nums[k] = kv.Value; k++; }
        save.AddItems(ids, nums);
        save.AddEffectValue(EffectId.钓鱼, 30 * count, needTips: false);
        var summary = string.Join(", ", System.Linq.Enumerable.Select(caught, kv => $"{kv.Value}×{kv.Key}"));
        return $"fishing skip ({count}): {summary} + {30 * count} 钓鱼 EXP";
    }

    public static string SkipHunting(int huntId, int count)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        count = System.Math.Max(1, System.Math.Min(99, count));
        var rand = new System.Random();
        var pool = System.Linq.Enumerable.ToArray(DBLoad.Hunt.Dic.Values);
        if (pool.Length == 0) return "no hunt data";

        var got = new System.Collections.Generic.Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            HuntData hunt = huntId > 0 ? DBLoad.Hunt.Get(huntId) : pool[rand.Next(pool.Length)];
            if (hunt == null) continue;
            // Weighted random pick from m_itemValue (the game's own loot weights)
            int total = 0;
            foreach (var w in hunt.m_itemValue) total += System.Math.Max(0, w);
            int idx = 0;
            if (total > 0)
            {
                int roll = rand.Next(total), cum = 0;
                for (int j = 0; j < hunt.m_itemValue.Length; j++)
                {
                    cum += System.Math.Max(0, hunt.m_itemValue[j]);
                    if (roll < cum) { idx = j; break; }
                }
            }
            if (idx < hunt.m_itemId.Length && idx < hunt.m_itemNum.Length)
            {
                int item = hunt.m_itemId[idx], qty = hunt.m_itemNum[idx];
                got.TryGetValue(item, out var cur); got[item] = cur + qty;
            }
        }
        var ids = new int[got.Count]; var nums = new int[got.Count];
        int k = 0; foreach (var kv in got) { ids[k] = kv.Key; nums[k] = kv.Value; k++; }
        save.AddItems(ids, nums);
        save.AddEffectValue(EffectId.打猎, 30 * count, needTips: false);
        var summary = string.Join(", ", System.Linq.Enumerable.Select(got, kv => $"{kv.Value}×{kv.Key}"));
        return $"hunt skip ({count}): {summary} + {30 * count} 打猎 EXP";
    }

    // ============================================================
    // Mini-game skip — alchemy + forging (recipe-based outputs)
    // ============================================================
    public static string SkipAlchemy(int recipeId, int tier)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        var recipe = DanFang.Get(recipeId);
        if (recipe == null) return $"unknown danfang recipe {recipeId}";
        tier = System.Math.Max(0, System.Math.Min(2, tier));    // 0..2 only
        int pillId = recipe.m_danYao[tier];
        int expGain = recipe.m_exp[tier];
        save.AddItems(new[] { pillId }, new[] { 1 });
        save.AddEffectValue(EffectId.炼丹, expGain, needTips: false);
        return $"alchemy skip: gave pill {pillId} (tier {tier}) + {expGain} 炼丹 EXP";
    }

    public static string SkipForging(int recipeId)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        var recipe = DaZao.Get(recipeId);
        if (recipe == null) return $"unknown dazao recipe {recipeId}";
        int itemId = recipe.m_get;
        int expGain = recipe.m_exp;
        save.AddItems(new[] { itemId }, new[] { 1 });
        save.AddEffectValue(EffectId.打造, expGain, needTips: false);
        return $"forging skip: gave item {itemId} + {expGain} 打造 EXP";
    }

    public static string SectSet(int campId, bool? isMember, int? massFavor)
    {
        var save = Save;
        if (save == null) return "no save loaded";
        var camp = (NpcCamp)campId;
        var changes = new System.Collections.Generic.List<string>();
        if (isMember != null)
        {
            if (isMember.Value && !save.CampHash.Contains(camp)) { save.CampHash.Add(camp); changes.Add("joined"); }
            else if (!isMember.Value && save.CampHash.Contains(camp)) { save.CampHash.Remove(camp); changes.Add("left"); }
        }
        if (massFavor != null && massFavor.Value != 0)
        {
            save.ChangeGroupFavo(campId, massFavor.Value);
            changes.Add($"favor {(massFavor.Value > 0 ? "+" : "")}{massFavor.Value}");
        }
        return changes.Count > 0 ? $"sect {campId}: {string.Join(", ", changes)}" : "no changes";
    }
}
