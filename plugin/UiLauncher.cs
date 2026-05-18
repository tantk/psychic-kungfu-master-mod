using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace JinGuCheats;

// Spawns the external Tauri UI process when the plugin loads, and kills it when the plugin
// unloads. The UI is useless without the plugin (no pipe to talk to), so we tie their
// lifetimes together — no orphan windows after the game closes.
internal static class UiLauncher
{
    private const string UiExeName = "jingu-cheats-ui.exe";
    private static Process? _process;

    public static void Launch()
    {
        Plugin.Log.Msg("[UiLauncher] Launch() entered");
        try
        {
            // Skip if any UI is already running
            var existing = Process.GetProcessesByName("jingu-cheats-ui");
            Plugin.Log.Msg($"[UiLauncher] existing 'jingu-cheats-ui' processes: {existing.Length}");
            if (existing.Length > 0)
            {
                Plugin.Log.Msg($"[UiLauncher] UI already running (pid {existing[0].Id}) — not spawning another.");
                foreach (var p in existing) p.Dispose();
                return;
            }

            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Plugin.Log.Msg($"[UiLauncher] plugin dir: {pluginDir ?? "(null)"}");

            var exePath = ResolveExePath();
            Plugin.Log.Msg($"[UiLauncher] resolved exe path: {exePath ?? "(not found)"}");
            if (exePath == null)
            {
                Plugin.Log.Warning($"[UiLauncher] UI exe not found. Searched: {string.Join(", ", CandidatePaths())}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                UseShellExecute = true,
                CreateNoWindow = false,
            };
            Plugin.Log.Msg($"[UiLauncher] starting process: {exePath}");
            _process = Process.Start(psi);
            if (_process != null)
                Plugin.Log.Msg($"[UiLauncher] OK — launched UI (pid {_process.Id})");
            else
                Plugin.Log.Error("[UiLauncher] Process.Start returned null");
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[UiLauncher] FAILED: {e.GetType().Name}: {e.Message}");
            Plugin.Log.Error($"[UiLauncher] stack: {e.StackTrace}");
        }
    }

    public static void Shutdown()
    {
        if (_process == null) return;
        try
        {
            if (!_process.HasExited)
            {
                Plugin.Log.Msg($"Closing UI (pid {_process.Id}) along with game.");
                // Try graceful close first, then kill if it didn't exit
                if (!_process.CloseMainWindow() || !_process.WaitForExit(800))
                    _process.Kill();
            }
        }
        catch { /* process may have died on its own */ }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private static string? ResolveExePath() =>
        CandidatePaths().FirstOrDefault(File.Exists);

    private static string[] CandidatePaths()
    {
        var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        // For MelonLoader: plugin lives in <game>/Mods/, UI exe also in Mods/.
        // For BepInEx legacy: <game>/BepInEx/plugins/. Try a few variants.
        return new[]
        {
            Path.Combine(pluginDir, UiExeName),
            Path.Combine(pluginDir, "JinGuCheats", UiExeName),
            Path.Combine(pluginDir, "..", UiExeName),
        };
    }
}
