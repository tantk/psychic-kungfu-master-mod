using DBLoad;
using UnityEngine;

namespace JinGuCheats;

// Unity-attached MonoBehaviour that runs every frame.
// MelonLoader's MelonMod.OnUpdate doesn't fire reliably in this game build,
// so we attach this component to a DontDestroyOnLoad GameObject and let
// Unity's own runtime tick it directly.
internal sealed class UpdateDriver : MonoBehaviour
{
    private int _frameCount;
    private int _lastSceneIdx = -999;
    private bool _autoLoadTriggered;
    private float _lastHeartbeat;

    private void Awake()    { Plugin.Log.Msg("[Driver] Awake() — Unity instantiated component"); }
    private void OnEnable() { Plugin.Log.Msg("[Driver] OnEnable()"); }
    private void Start()    { Plugin.Log.Msg("[Driver] Start() — first Unity frame after enable"); }
    private void OnDisable(){ Plugin.Log.Msg("[Driver] OnDisable()"); }
    private void OnDestroy(){ Plugin.Log.Msg("[Driver] OnDestroy() — Unity tore us down"); }

    private void Update()
    {
        _frameCount++;

        // Heartbeat every 5 seconds so we can see in the log that we're ticking
        float now = Time.realtimeSinceStartup;
        if (now - _lastHeartbeat > 5f)
        {
            _lastHeartbeat = now;
            Plugin.Log.Msg($"[Driver] tick (frame {_frameCount}, scene {_lastSceneIdx})");
        }

        // Log scene transitions
        int idx;
        try { idx = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex; }
        catch { return; }
        if (idx != _lastSceneIdx)
        {
            Plugin.Log.Msg($"[Driver] scene {_lastSceneIdx} → {idx} (frame {_frameCount})");
            _lastSceneIdx = idx;
        }

        // 1. Drain queued main-thread work from pipe handlers
        MainThreadDispatcher.Pump();

        // 2. Refresh state snapshot (read by pipe handlers without marshalling)
        Cheats.RefreshCache();

        // 3. Auto-load — env var JINGU_AUTOLOAD=1 OR [Test] AutoLoadSave config
        if (!_autoLoadTriggered)
        {
            // Multiple ways to opt in: config flag, env var, OR marker file next to the exe.
            // The marker file is the most reliable across Steam-relaunch scenarios where
            // the env var doesn't propagate.
            bool autoLoad = Plugin.CfgAutoLoadSave.Value
                || System.Environment.GetEnvironmentVariable("JINGU_AUTOLOAD") == "1"
                || System.IO.File.Exists(System.IO.Path.Combine(
                       UnityEngine.Application.dataPath, "..", "JINGU_AUTOLOAD"));
            if (autoLoad && idx == 1 && SaveManager.Instance != null)   // 1 = SceneEnum.登录
            {
                try
                {
                    var recent = SaveManager.Instance.Recent;
                    if (recent != null)
                    {
                        Plugin.Log.Msg($"[AutoLoad] loading save '{recent.m_name}' (saveTime={recent.m_saveTime})");
                        SaveManager.Instance.Load(recent);
                        _autoLoadTriggered = true;
                    }
                    else
                    {
                        Plugin.Log.Warning("[AutoLoad] no recent save found");
                        _autoLoadTriggered = true;
                    }
                }
                catch (System.Exception e) { ErrorLog.Record("auto-load", e); _autoLoadTriggered = true; }
            }
        }

        // 4. Auto-publish/collect sect commissions (no-op unless toggle is on
        // AND the game-Turn has advanced — Cheats.AutoWeiTuoTick self-throttles).
        if (idx > 1 && SaveManager.Instance != null)
        {
            try { Cheats.AutoWeiTuoTick(); }
            catch (System.Exception e) { ErrorLog.Record("auto_wei_tuo:tick", e); }
        }

        // 5. Hotkeys — only fire if user has bound a key
        if (Plugin.CfgKeyMaxMoney.Value    != KeyCode.None && Hotkey.Pressed(Plugin.CfgKeyMaxMoney.Value))    Plugin.TogglePublic(Plugin.CfgMaxMoney,    "Max Money");
        if (Plugin.CfgKeyFreeActions.Value != KeyCode.None && Hotkey.Pressed(Plugin.CfgKeyFreeActions.Value)) Plugin.TogglePublic(Plugin.CfgFreeActions, "Free Actions");
        if (Plugin.CfgKeyGodMode.Value     != KeyCode.None && Hotkey.Pressed(Plugin.CfgKeyGodMode.Value))     Plugin.TogglePublic(Plugin.CfgGodMode,     "God Mode");
        if (Plugin.CfgKeyFreezeTime.Value  != KeyCode.None && Hotkey.Pressed(Plugin.CfgKeyFreezeTime.Value))  Plugin.TogglePublic(Plugin.CfgFreezeTime,  "Freeze Time");
    }
}
