using System.Collections.Generic;
using System.Runtime.InteropServices;
using DBLoad;
using Fight;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(JinGuCheats.Plugin), JinGuCheats.Plugin.PluginName, JinGuCheats.Plugin.PluginVersion, "tantk")]
[assembly: MelonGame("金十四工作室", "JinGu")]

namespace JinGuCheats;

public class Plugin : MelonMod
{
    public const string PluginName    = "JinGu Cheats";
    public const string PluginVersion = "0.2.1";

    internal static MelonLogger.Instance Log = null!;

    internal static MelonPreferences_Entry<bool> CfgMaxMoney    = null!;
    internal static MelonPreferences_Entry<bool> CfgFreeActions = null!;
    internal static MelonPreferences_Entry<bool> CfgGodMode     = null!;
    internal static MelonPreferences_Entry<bool> CfgFreezeTime  = null!;
    internal static MelonPreferences_Entry<KeyCode> CfgKeyMaxMoney    = null!;
    internal static MelonPreferences_Entry<KeyCode> CfgKeyFreeActions = null!;
    internal static MelonPreferences_Entry<KeyCode> CfgKeyGodMode     = null!;
    internal static MelonPreferences_Entry<KeyCode> CfgKeyFreezeTime  = null!;
    internal static MelonPreferences_Entry<bool> CfgAutoLaunchUi = null!;
    internal static MelonPreferences_Entry<bool> CfgAutoLoadSave = null!;
    internal static MelonPreferences_Entry<bool> CfgAutoWeiTuo  = null!;
    internal static MelonPreferences_Entry<bool> CfgMapTeleport = null!;

    private PipeServer? _server;
    private static UpdateDriver? _driver;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Log.Msg($"=== STEP 1: OnInitializeMelon entered — Plugin {PluginVersion} ===");
        var envAutoLoad = System.Environment.GetEnvironmentVariable("JINGU_AUTOLOAD");
        Log.Msg($"env JINGU_AUTOLOAD = {(envAutoLoad ?? "<unset>")}");

        LogEnvironment();

        // Critical: keep Unity's Update loop running when the game window loses focus.
        // Without this, clicking the cheat UI pauses the game's main thread, which
        // makes every pipe request time out at the main-thread dispatcher.
        // The setter is hidden in the reference assembly so we set it via reflection.
        try
        {
            var prop = typeof(Application).GetProperty("runInBackground",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(null, true);
                Log.Msg($"Application.runInBackground set to true (was {Application.runInBackground})");
            }
            else
            {
                Log.Warning("Application.runInBackground setter not accessible — pipe state polls will pause when game loses focus");
            }
        }
        catch (System.Exception e) { Log.Warning($"runInBackground set failed: {e.Message}"); }

        try
        {
            Log.Msg("STEP 2: creating Cheats preferences...");
            var cheats = MelonPreferences.CreateCategory("Cheats");
            CfgMaxMoney    = cheats.CreateEntry("MaxMoney",    false, description: "Spoof money item count to 9,999,999 (no inventory change)");
            CfgFreeActions = cheats.CreateEntry("FreeActions", false, description: "CostAction calls do nothing");
            CfgGodMode     = cheats.CreateEntry("GodMode",     false, description: "Friendly Roles take 0 damage");
            CfgFreezeTime  = cheats.CreateEntry("FreezeTime",  false, description: "Stop world clock from advancing");
            CfgAutoWeiTuo  = cheats.CreateEntry("AutoWeiTuo",  false, description: "Auto-publish + auto-collect 发布委托 sect commissions on each game-time tick");
            CfgMapTeleport = cheats.CreateEntry("MapTeleport", false, description: "Click any scene-exit or task icon on the in-game map to fast-travel there");

            Log.Msg("STEP 3: creating Hotkeys preferences (no defaults — user binds via UI)...");
            var keys = MelonPreferences.CreateCategory("Hotkeys");
            // Defaults are intentionally None — the user picks their own keys.
            // Anything left at None means the hotkey is simply inactive.
            CfgKeyMaxMoney    = keys.CreateEntry("ToggleMaxMoney",    KeyCode.None);
            CfgKeyFreeActions = keys.CreateEntry("ToggleFreeActions", KeyCode.None);
            CfgKeyGodMode     = keys.CreateEntry("ToggleGodMode",     KeyCode.None);
            CfgKeyFreezeTime  = keys.CreateEntry("ToggleFreezeTime",  KeyCode.None);

            Log.Msg("STEP 4: creating UI preferences...");
            var ui = MelonPreferences.CreateCategory("UI");
            CfgAutoLaunchUi = ui.CreateEntry("AutoLaunch", true,
                description: "Spawn the external UI window when the game starts");

            var test = MelonPreferences.CreateCategory("Test");
            CfgAutoLoadSave = test.CreateEntry("AutoLoadSave", false,
                description: "Auto-load the most recent save once the login scene shows. For test/CI use only.");

            Log.Msg("STEP 5: applying Harmony patches...");
            VerifyPatchTargets();
            HarmonyInstance.PatchAll(typeof(Patches));
            try
            {
                int patched = 0;
                foreach (var _ in HarmonyInstance.GetPatchedMethods()) patched++;
                Log.Msg($"STEP 5 OK: PatchAll completed, {patched} method(s) patched");
            }
            catch (System.Exception e) { Log.Warning($"could not count patched methods: {e.Message}"); }

            Log.Msg("STEP 6: starting pipe server...");
            _server = new PipeServer();
            _server.Start();
            Log.Msg($"STEP 6 OK: pipe listening at \\\\.\\pipe\\{PipeServer.PipeName}");

            Log.Msg($"STEP 7: AutoLaunchUi config = {CfgAutoLaunchUi.Value}");
            if (CfgAutoLaunchUi.Value)
            {
                Log.Msg("STEP 8: spawning UI process...");
                UiLauncher.Launch();
                Log.Msg("STEP 8 returned");
            }

            // The MonoBehaviour driver gets installed in OnSceneWasInitialized below —
            // installing here (during MelonLoader init, before Unity's main loop)
            // produces a GameObject Unity doesn't tick.

            Log.Msg($"=== {PluginName} {PluginVersion} READY. Hotkeys: bind via UI Settings — none assigned by default ===");
        }
        catch (System.Exception e)
        {
            Log.Error($"FATAL during OnInitializeMelon: {e}");
            throw;
        }
    }

    public override void OnApplicationStart()
    {
        Log.Msg("[Plugin] OnApplicationStart fired (Unity main loop reached)");
    }

    public override void OnApplicationLateStart()
    {
        Log.Msg("[Plugin] OnApplicationLateStart fired");
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Log.Msg($"[Plugin] OnSceneWasInitialized({buildIndex}, '{sceneName}')");
        EnsureDriver();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Log.Msg($"[Plugin] OnSceneWasLoaded({buildIndex}, '{sceneName}')");
    }

    // Dump runtime environment so we can diagnose stripped corlibs, mismatched
    // game versions, or missing assemblies from a user's Latest.log alone.
    private static void LogEnvironment()
    {
        try
        {
            Log.Msg($"--- ENV: OS = {System.Environment.OSVersion.VersionString}, 64-bit process = {System.Environment.Is64BitProcess}");
            Log.Msg($"--- ENV: CLR = {System.Environment.Version}, Mono = {(System.Type.GetType("Mono.Runtime") != null ? "yes" : "no")}");
        }
        catch (System.Exception e) { Log.Warning($"ENV os/clr query failed: {e.Message}"); }

        try
        {
            Log.Msg($"--- ENV: Unity = {Application.unityVersion}, platform = {Application.platform}");
            Log.Msg($"--- ENV: dataPath = {Application.dataPath}");
            Log.Msg($"--- ENV: persistentDataPath = {Application.persistentDataPath}");
        }
        catch (System.Exception e) { Log.Warning($"ENV unity query failed: {e.Message}"); }

        // Corlib + Assembly-CSharp sanity — most common cause of MelonLoader failures
        // on this game is Steam reverting a patched corlib back to its stripped form.
        // Logging file sizes lets a reporter (or us) spot a partial revert at a glance.
        try
        {
            var managed = System.IO.Path.Combine(Application.dataPath, "Managed");
            string[] watch = {
                "mscorlib.dll", "System.dll", "System.Core.dll", "System.Runtime.dll",
                "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll",
                "UnityEngine.CoreModule.dll"
            };
            foreach (var name in watch)
            {
                var p = System.IO.Path.Combine(managed, name);
                if (System.IO.File.Exists(p))
                {
                    var fi = new System.IO.FileInfo(p);
                    Log.Msg($"--- ENV: {name} = {fi.Length:N0} bytes (mtime {fi.LastWriteTime:yyyy-MM-dd HH:mm})");
                }
                else
                {
                    Log.Warning($"--- ENV: {name} MISSING at {p}");
                }
            }

            // Expected baselines for the patched corlibs — bundled MonoBleedingEdgePatches
            // give mscorlib ~4.6 MB. If we see <3 MB, Steam reverted it.
            try
            {
                var msc = System.IO.Path.Combine(managed, "mscorlib.dll");
                if (System.IO.File.Exists(msc))
                {
                    var sz = new System.IO.FileInfo(msc).Length;
                    if (sz < 3_000_000)
                        Log.Error($"--- ENV: mscorlib.dll looks STRIPPED ({sz:N0} bytes < 3 MB). Run JinGu-Doctor.bat or pre-launch.ps1 to restore the patched corlibs.");
                }
            }
            catch { }
        }
        catch (System.Exception e) { Log.Warning($"ENV managed-dir query failed: {e.Message}"); }
    }

    // Pre-check each method we're about to patch. If a method has been renamed or
    // removed by a game update, PatchAll throws an opaque error — this log tells
    // us exactly which target is missing so we know what to re-find in the decomp.
    private static readonly (System.Type t, string m)[] _patchTargets =
    {
        (typeof(SaveData),                  nameof(SaveData.GetItemNum)),
        (typeof(SaveData),                  nameof(SaveData.CostAction)),
        (typeof(Role),                      nameof(Role.Injured)),
        (typeof(SaveManager),               "AddTime"),
        (typeof(SaveManager),               "Awake"),
        (typeof(UnityEngine.Application),   "get_runInBackground"),
        (typeof(MapWindow),                 "OnOpen"),
    };

    private static void VerifyPatchTargets()
    {
        int ok = 0, missing = 0;
        foreach (var (t, m) in _patchTargets)
        {
            try
            {
                var info = HarmonyLib.AccessTools.Method(t, m);
                if (info != null)
                {
                    ok++;
                }
                else
                {
                    Log.Warning($"--- PATCH-TARGET MISSING: {t.FullName}.{m} — game version may have changed");
                    missing++;
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"--- PATCH-TARGET ERROR: {t.FullName}.{m} — {e.Message}");
                missing++;
            }
        }
        Log.Msg($"--- patch-target preflight: {ok} resolved, {missing} missing/error");
    }

    public override void OnDeinitializeMelon()
    {
        _server?.Dispose();
        _server = null;
        UiLauncher.Shutdown();
    }

    public override void OnUpdate()
    {
        // MelonLoader's OnUpdate isn't firing in this game/runtime combo. We instead
        // install a MonoBehaviour (UpdateDriver) once and let Unity call its Update()
        // directly. If MelonLoader DID start working we'd get double-ticks; the driver
        // self-deduplicates to prevent that.
        EnsureDriver();
    }

    private static void EnsureDriver()
    {
        if (_driver != null) return;
        try
        {
            var go = new GameObject("JinGuCheatsDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<UpdateDriver>();
            Log.Msg("[Driver] UpdateDriver MonoBehaviour installed");
        }
        catch (System.Exception e) { Log.Error($"failed to install UpdateDriver: {e.Message}"); }
    }

    // Public surface so the Patches class can call it from SaveManager.Awake postfix
    internal static void EnsureDriverPublic() => EnsureDriver();

    // Hotkeys are now polled by UpdateDriver, not OnUpdate.
    private static void Toggle(MelonPreferences_Entry<bool> e, string label)
    {
        e.Value = !e.Value;
        Log.Msg($"{label} = {(e.Value ? "ON" : "OFF")}");
    }
    // Public surface for the UpdateDriver MonoBehaviour
    internal static void TogglePublic(MelonPreferences_Entry<bool> e, string label) => Toggle(e, label);
}

// Win32-based key-down edge detection. Game's Input.GetKeyDown was stripped during build
// because the new Input System replaced it.
internal static class Hotkey
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static readonly Dictionary<int, bool> _wasDown = new();

    public static bool Pressed(KeyCode kc)
    {
        int vk = ToVk(kc);
        if (vk == 0) return false;
        bool isDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
        _wasDown.TryGetValue(vk, out bool last);
        _wasDown[vk] = isDown;
        return isDown && !last;
    }

    private static int ToVk(KeyCode kc) => kc switch
    {
        KeyCode.F1 => 0x70, KeyCode.F2 => 0x71, KeyCode.F3 => 0x72, KeyCode.F4 => 0x73,
        KeyCode.F5 => 0x74, KeyCode.F6 => 0x75, KeyCode.F7 => 0x76, KeyCode.F8 => 0x77,
        KeyCode.F9 => 0x78, KeyCode.F10 => 0x79, KeyCode.F11 => 0x7A, KeyCode.F12 => 0x7B,
        _ => (kc >= KeyCode.A && kc <= KeyCode.Z) ? (int)kc - (int)KeyCode.A + 0x41
           : (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9) ? (int)kc - (int)KeyCode.Alpha0 + 0x30
           : 0
    };
}

internal static class Patches
{
    // ---- F2: Max money — spoof GetItemNum for item 10001 (money) ----
    [HarmonyPatch(typeof(SaveData), nameof(SaveData.GetItemNum))]
    [HarmonyPostfix]
    private static void GetItemNum_Postfix(int itemId, ref int __result)
    {
        if (Plugin.CfgMaxMoney.Value && itemId == 10001)
            __result = 9_999_999;
    }

    // ---- F3: Free actions ----
    [HarmonyPatch(typeof(SaveData), nameof(SaveData.CostAction))]
    [HarmonyPrefix]
    private static bool CostAction_Prefix() => !Plugin.CfgFreeActions.Value;

    // ---- F4: God mode — friendly roles take 0 damage ----
    [HarmonyPatch(typeof(Role), nameof(Role.Injured))]
    [HarmonyPrefix]
    private static void Injured_Prefix(Role __instance, ref float delta)
    {
        if (Plugin.CfgGodMode.Value && __instance != null && __instance.m_camp == CampType.Friend)
            delta = 0f;
    }

    // ---- F5: Freeze time — skip the SaveManager 1Hz tick ----
    [HarmonyPatch(typeof(SaveManager), "AddTime")]
    [HarmonyPrefix]
    private static bool AddTime_Prefix() => !Plugin.CfgFreezeTime.Value;

    // ---- Install UpdateDriver MonoBehaviour from SaveManager.Awake ----
    // OnInitializeMelon and OnSceneWasInitialized both fail to give us a tickable
    // GameObject in this build (former: too early, latter: MelonLoader's scene
    // pipeline is broken on stripped UnityEngine.CoreModule). SaveManager.Awake
    // runs from inside Unity's main loop after a scene is fully loaded — a
    // GameObject created here gets registered with the update scheduler and ticked.
    [HarmonyPatch(typeof(SaveManager), "Awake")]
    [HarmonyPostfix]
    private static void SaveManagerAwake_Postfix()
    {
        Plugin.Log.Msg("[Patches] SaveManager.Awake fired — installing UpdateDriver from here");
        Plugin.EnsureDriverPublic();
    }

    // ---- Force Application.runInBackground to read as true ----
    // The setter is missing in this Unity build, and Unity pauses Update when the game
    // window loses focus. Harmony-patching the getter so it always returns true *may*
    // be enough — if Unity's native focus-pause logic re-reads the C# property each
    // frame. (If not, see tools/patch-run-in-background.ps1 for the permanent fix that
    // modifies globalgamemanagers directly.)
    [HarmonyPatch(typeof(UnityEngine.Application), "get_runInBackground")]
    [HarmonyPostfix]
    private static void RunInBackground_Postfix(ref bool __result) => __result = true;

    // ---- Click-to-teleport on the in-game map ----
    // MapWindow.OnOpen lays out scene-exit icons and task icons but doesn't add click
    // handlers (only the world-map caravan icons have one — see MapWindow.cs:174).
    // When CfgMapTeleport is on, we re-iterate the rendered scene-trigger children
    // post-OnOpen and attach handlers that mirror the game's own SceneTrigger.OnEnter
    // flow: save return point, load target scene, place player at the trigger spawn.
    private static readonly System.Reflection.FieldInfo F_mapScenesRect =
        HarmonyLib.AccessTools.Field(typeof(MapWindow), "m_scenesRect");
    private static readonly System.Reflection.FieldInfo F_mapTasksRect =
        HarmonyLib.AccessTools.Field(typeof(MapWindow), "m_tasksRect");

    [HarmonyPatch(typeof(MapWindow), "OnOpen")]
    [HarmonyPostfix]
    private static void MapWindow_OnOpen_Postfix(MapWindow __instance)
    {
        if (!Plugin.CfgMapTeleport.Value) return;
        try
        {
            // Re-find scene triggers in the same order/filter MapWindow uses, so the
            // rendered child icons line up with our trigger list by index.
            var allTriggers = UnityEngine.Object.FindObjectsOfType<SceneTrigger>();
            var visible = new System.Collections.Generic.List<SceneTrigger>();
            foreach (var t in allTriggers)
            {
                var sceneData = DBLoad.Scene.Get((int)t.m_sceneEnum);
                if (sceneData != null && sceneData.m_type > 0) visible.Add(t);
            }

            var scenesRect = F_mapScenesRect?.GetValue(__instance) as UnityEngine.RectTransform;
            if (scenesRect == null || scenesRect.childCount == 0) return;

            int n = System.Math.Min(scenesRect.childCount, visible.Count);
            for (int i = 0; i < n; i++)
            {
                var child = scenesRect.GetChild(i);
                var trigger = visible[i];  // captured for the closure
                var btn = child.GetComponent<UIButton>();
                if (btn == null) continue;
                btn.SetLeftClickEvent(() => MapClickTeleport(__instance, trigger));
            }
        }
        catch (System.Exception e)
        {
            ErrorLog.Record("map_teleport_hook", e);
        }
    }

    private static void MapClickTeleport(MapWindow win, SceneTrigger t)
    {
        try
        {
            var save = MonoSingleton<SaveManager>.Instance?.SaveData;
            if (save == null) return;
            save.SavePos();
            MonoSingleton<SceneMgr>.Instance.LoadScene(t.m_sceneEnum, () =>
            {
                MonoSingleton<PlayerControl>.Instance.SetPos(t.m_pos);
                UIUtlils.OpenSceneTips(t.m_sceneEnum);
            });
            win.OnClose();
        }
        catch (System.Exception e) { ErrorLog.Record("map_teleport_click", e); }
    }
}
