using System;
using System.Collections.Generic;

namespace JinGuCheats;

// Ring buffer of the last N errors so the UI can show them on demand.
// Thread-safe because pipe handlers and main-thread work can both report errors.
internal static class ErrorLog
{
    private const int Capacity = 50;
    private static readonly object _lock = new();
    private static readonly LinkedList<Entry> _entries = new();
    private static long _nextId = 1;

    public sealed class Entry
    {
        public long Id;
        public string Time = "";     // ISO-8601 local time, formatted for display
        public string Kind = "";     // "error" | "warning"
        public string Source = "";   // e.g. "cmd:give_money", "patch:Injured", "pipe"
        public string Message = "";
        public string Stack = "";
    }

    public static void Record(string source, Exception e, string kind = "error")
    {
        lock (_lock)
        {
            if (_entries.Count >= Capacity) _entries.RemoveFirst();
            _entries.AddLast(new Entry
            {
                Id      = _nextId++,
                Time    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Kind    = kind,
                Source  = source ?? "",
                Message = e.Message ?? "(no message)",
                Stack   = e.StackTrace ?? "",
            });
        }
        // Also surface to MelonLoader's own log
        try { Plugin.Log?.Error($"[{source}] {e.Message}\n{e.StackTrace}"); } catch { /* ignored */ }
    }

    public static void Record(string source, string message, string kind = "warning")
    {
        lock (_lock)
        {
            if (_entries.Count >= Capacity) _entries.RemoveFirst();
            _entries.AddLast(new Entry
            {
                Id      = _nextId++,
                Time    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Kind    = kind,
                Source  = source ?? "",
                Message = message,
                Stack   = "",
            });
        }
        try { Plugin.Log?.Warning($"[{source}] {message}"); } catch { /* ignored */ }
    }

    public static Entry[] Snapshot(long sinceId = 0)
    {
        lock (_lock)
        {
            var result = new List<Entry>(_entries.Count);
            foreach (var e in _entries)
                if (e.Id > sinceId) result.Add(e);
            return result.ToArray();
        }
    }

    public static int Count() { lock (_lock) return _entries.Count; }

    public static void Clear() { lock (_lock) { _entries.Clear(); } }
}
