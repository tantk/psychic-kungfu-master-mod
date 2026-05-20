using System;

namespace JinGuCheats;

// Pure request -> response JSON dispatch, transport-agnostic.
// Every dispatch is wrapped so errors land in ErrorLog and are returned to the caller —
// no silent failures, the UI always sees what went wrong.
internal static class Dispatcher
{
    public static string Handle(string requestJson)
    {
        Json.Reader r;
        string cmd;
        try
        {
            r = new Json.Reader(requestJson);
            cmd = r.GetString("cmd") ?? "";
        }
        catch (Exception e)
        {
            ErrorLog.Record("pipe:parse", e);
            return Fail("malformed request", e);
        }

        try
        {
            return DispatchInner(cmd, r);
        }
        catch (Exception e)
        {
            ErrorLog.Record($"cmd:{cmd}", e);
            return Fail($"cmd '{cmd}' threw", e);
        }
    }

    private static string DispatchInner(string cmd, Json.Reader r)
    {
        switch (cmd)
        {
            case "hello":
                return new Json.Obj()
                    .Add("ok", true)
                    .Add("protocol", PipeServer.Protocol)
                    .Add("plugin", Plugin.PluginName)
                    .Add("version", Plugin.PluginVersion)
                    .ToString();

            case "state":
            {
                // No main-thread marshal — read the cached snapshot updated by Plugin.OnUpdate().
                // This means state polls succeed even when the game window has lost focus.
                var snap = Cheats.GetCached();
                var hotkeys = Cheats.Hotkeys();
                return new Json.Obj()
                    .Add("ok", true)
                    .Add("in_game", snap.in_game)
                    .Add("money", snap.money)
                    .Add("game_time", snap.game_time)
                    .Add("real_time", snap.real_time)
                    .Add("leader_name", snap.leader_name)
                    .Add("leader_family", snap.leader_family)
                    .Add("difficulty", snap.difficulty)
                    .Add("jingli", snap.jingli)
                    .Add("tili", snap.tili)
                    .Add("jingli_max", snap.jingli_max)
                    .Add("tili_max", snap.tili_max)
                    .Add("error_count", ErrorLog.Count())
                    .AddDict("toggles", snap.toggles)
                    .AddDict("hotkeys", hotkeys)
                    .AddDict("effects", snap.effects)
                    .AddStringList("wei_tuo_log", snap.wei_tuo_log)
                    .ToString();
            }

            case "toggle":
            {
                // Toggles just write MelonPreferences entries — no Unity API touched, no marshal needed.
                var name = r.GetString("name") ?? "";
                var value = r.GetBool("value") ?? false;
                return Ok(Cheats.SetToggle(name, value));
            }

            case "give_money":
                return Ok(MainThreadDispatcher.Get(() => Cheats.GiveMoney(r.GetInt("amount") ?? 100000)));

            case "give_item":
                return Ok(MainThreadDispatcher.Get(() => Cheats.GiveItem(r.GetInt("id") ?? 0, r.GetInt("num") ?? 0)));

            case "add_effect":
                return Ok(MainThreadDispatcher.Get(() => Cheats.AddEffect(r.GetInt("id") ?? 0, r.GetFloat("value") ?? 0)));

            case "set_effect":
                return Ok(MainThreadDispatcher.Get(() => Cheats.SetEffect(r.GetInt("id") ?? 0, r.GetFloat("value") ?? 0)));

            case "add_hour":
                return Ok(MainThreadDispatcher.Get(() => Cheats.AddHour(r.GetInt("hours") ?? 1)));

            case "max_all_wuxue":
                return Ok(MainThreadDispatcher.Get(Cheats.MaxAllWuXue));

            case "heal_team":
                return Ok(MainThreadDispatcher.Get(Cheats.HealTeam));

            case "refill_energy":
                return Ok(MainThreadDispatcher.Get(() => Cheats.RefillEnergy(
                    r.GetBool("jingli") ?? true,
                    r.GetBool("tili") ?? true)));

            case "errors":
            {
                long since = r.GetInt("since") ?? 0;
                var entries = ErrorLog.Snapshot(since);
                var obj = new Json.Obj().Add("ok", true).Add("count", entries.Length);
                // Build the entries array manually
                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < entries.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(new Json.Obj()
                        .Add("id", entries[i].Id)
                        .Add("time", entries[i].Time)
                        .Add("kind", entries[i].Kind)
                        .Add("source", entries[i].Source)
                        .Add("message", entries[i].Message)
                        .Add("stack", entries[i].Stack)
                        .ToString());
                }
                sb.Append(']');
                obj.AddRaw("entries", sb.ToString());
                return obj.ToString();
            }

            case "clear_errors":
                ErrorLog.Clear();
                return Ok("error log cleared");

            case "clear_wei_tuo_log":
                Cheats.ClearWeiTuoLog();
                return Ok("auto-委托 log cleared");

            case "set_hotkey":
            {
                var name = r.GetString("name") ?? "";
                var keyCode = r.GetInt("key") ?? 0;
                return Ok(Cheats.SetHotkey(name, keyCode));
            }

            // ---- NPC editor ----
            case "npc_get":
                return MainThreadDispatcher.Get(() => Cheats.NpcGet(r.GetInt("id") ?? 0));

            case "npc_set":
                return Ok(MainThreadDispatcher.Get(() => Cheats.NpcSet(
                    r.GetInt("id") ?? 0,
                    r.GetInt("money"),
                    r.GetInt("favo"),
                    r.GetInt("friend_lv"),
                    r.GetBool("robed"),
                    r.GetBool("show"),
                    r.GetBool("deaded"),
                    r.GetInt("camp"))));

            case "character_get":
                // Character.Get reads from a static dict — safe from any thread.
                return Cheats.CharacterGet(r.GetInt("id") ?? 0);

            case "character_set_stat":
                return Ok(MainThreadDispatcher.Get(() => Cheats.CharacterSetStat(
                    r.GetInt("id") ?? 0, r.GetString("stat") ?? "", r.GetInt("value") ?? 0)));

            case "character_add_skill":
                return Ok(MainThreadDispatcher.Get(() => Cheats.CharacterAddSkill(
                    r.GetInt("id") ?? 0, r.GetInt("skill") ?? 0, r.GetBool("passive") ?? false)));

            case "character_remove_skill":
                return Ok(MainThreadDispatcher.Get(() => Cheats.CharacterRemoveSkill(
                    r.GetInt("id") ?? 0, r.GetInt("skill") ?? 0, r.GetBool("passive") ?? false)));

            case "character_reset":
                return Ok(MainThreadDispatcher.Get(() => Cheats.CharacterReset(r.GetInt("id") ?? 0)));

            // ---- Unlock + sect editor ----
            case "unlock_all_features":
                return Ok(MainThreadDispatcher.Get(Cheats.UnlockAllFeatures));

            case "sect_get":
                return MainThreadDispatcher.Get(() => Cheats.SectGet(r.GetInt("id") ?? 0));

            case "sect_set":
                return Ok(MainThreadDispatcher.Get(() => Cheats.SectSet(
                    r.GetInt("id") ?? 0,
                    r.GetBool("is_member"),
                    r.GetInt("mass_favor"))));

            // ---- Mini-game skip ----
            case "skip_alchemy":
                return Ok(MainThreadDispatcher.Get(() => Cheats.SkipAlchemy(
                    r.GetInt("recipe") ?? 0, r.GetInt("tier") ?? 2)));

            case "skip_forging":
                return Ok(MainThreadDispatcher.Get(() => Cheats.SkipForging(r.GetInt("recipe") ?? 0)));

            case "skip_fishing":
                // fish_id = 0 → random pick from Fish.Dic
                return Ok(MainThreadDispatcher.Get(() => Cheats.SkipFishing(
                    r.GetInt("fish_id") ?? 0, r.GetInt("count") ?? 1)));

            case "skip_hunting":
                return Ok(MainThreadDispatcher.Get(() => Cheats.SkipHunting(
                    r.GetInt("hunt_id") ?? 0, r.GetInt("count") ?? 1)));

            case "skip_taming_all":
                return Ok(MainThreadDispatcher.Get(Cheats.SkipTamingMaxAll));

            case "skip_taming_one":
                return Ok(MainThreadDispatcher.Get(() => Cheats.SkipTamingOne(r.GetInt("npc_id") ?? 0)));

            default:
                ErrorLog.Record("dispatcher", $"unknown cmd: {cmd}");
                return new Json.Obj().Add("ok", false).Add("message", $"unknown cmd: {cmd}").ToString();
        }
    }

    private static string Ok(string message) =>
        new Json.Obj().Add("ok", true).Add("message", message).ToString();

    private static string Fail(string message, Exception e) =>
        new Json.Obj()
            .Add("ok", false)
            .Add("message", message)
            .Add("error", e.Message ?? "")
            .Add("stack", e.StackTrace ?? "")
            .ToString();
}
