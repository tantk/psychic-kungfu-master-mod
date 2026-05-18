using System;
using System.Collections.Concurrent;

namespace JinGuCheats;

// HttpListener callbacks fire on worker threads, but Unity APIs are not thread-safe.
// HTTP handlers enqueue Action<>s here; Plugin.Update() drains the queue on the main thread.
internal static class MainThreadDispatcher
{
    private static readonly ConcurrentQueue<Action> _queue = new();

    public static void Run(Action a) => _queue.Enqueue(a);

    // Synchronously dispatch an action and wait for its result. Used by HTTP handlers
    // that need to return a value computed on the Unity main thread.
    public static T Get<T>(Func<T> f, int timeoutMs = 2000)
    {
        T result = default!;
        Exception? err = null;
        var done = new System.Threading.ManualResetEventSlim(false);
        _queue.Enqueue(() =>
        {
            try { result = f(); }
            catch (Exception e) { err = e; }
            finally { done.Set(); }
        });
        if (!done.Wait(timeoutMs))
            throw new TimeoutException($"Main-thread dispatch timed out after {timeoutMs} ms");
        if (err != null) throw err;
        return result;
    }

    public static void Pump()
    {
        while (_queue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { ErrorLog.Record("main-thread", e); }
        }
    }
}
