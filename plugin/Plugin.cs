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
    public const string PluginVersion = "0.1.0";

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

    private PipeServer? _server;
    private static UpdateDriver? _driver;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Log.Msg("=== STEP 1: OnInitializeMelon entered ===");
        var envAutoLoad = System.Environment.GetEnvironmentVariable("JINGU_AUTOLOAD");
        Log.Msg($"env JINGU_AUTOLOAD = {(envAutoLoad ?? "<unset>")}");

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
            HarmonyInstance.PatchAll(typeof(Patches));

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

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Log.Msg($"[Plugin] OnSceneWasInitialized({buildIndex}, '{sceneName}')");
        EnsureDriver();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Log.Msg($"[Plugin] OnSceneWasLoaded({buildIndex}, '{sceneName}')");
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
}
