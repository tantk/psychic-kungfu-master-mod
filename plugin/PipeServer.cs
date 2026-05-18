using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace JinGuCheats;

// Replaces HttpServer with a Windows Named Pipe. No port, no firewall prompt, no file location.
//
// Wire format (same as cheatengine-mcp-rs):
//   request:  4-byte LE length | UTF-8 JSON bytes
//   response: 4-byte LE length | UTF-8 JSON bytes
//
// Pipe name embeds protocol version: bumping v1 -> v2 invalidates old clients automatically.
// FirstPipeInstance ensures only one server can claim the name at a time.
internal sealed class PipeServer : IDisposable
{
    public const string PipeName = "JinGuCheats.v1";
    public const int    Protocol = 1;
    private const int   MaxMessageBytes = 16 * 1024 * 1024;

    private Thread? _thread;
    private volatile bool _running;
    private NamedPipeServerStream? _current;

    public void Start()
    {
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "JinGuCheats-Pipe" };
        _thread.Start();
        Plugin.Log.Msg($"Pipe server listening on \\\\.\\pipe\\{PipeName}");
    }

    public void Dispose()
    {
        _running = false;
        try { _current?.Disconnect(); } catch { /* ignore */ }
        try { _current?.Dispose();    } catch { /* ignore */ }
    }

    private void Loop()
    {
        while (_running)
        {
            NamedPipeServerStream server;
            try
            {
                // maxNumberOfServerInstances=1 — only one server in the system can hold this name.
                // If another JinGu instance already owns the pipe, this constructor throws
                // (typically IOException or UnauthorizedAccessException) and we exit cleanly:
                // hotkeys still work, but the UI can only attach to whichever instance won.
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                    inBufferSize:  64 * 1024,
                    outBufferSize: 64 * 1024);
            }
            catch (UnauthorizedAccessException)
            {
                Plugin.Log.Warning(
                    $"Pipe \\\\.\\pipe\\{PipeName} already in use — another JinGu instance is running. " +
                    "Hotkeys (F2-F5) still work but the UI cannot connect to this instance.");
                return;
            }
            catch (IOException e) when ((uint)e.HResult == 0x800700E7) // ERROR_PIPE_BUSY
            {
                Plugin.Log.Warning(
                    $"Pipe \\\\.\\pipe\\{PipeName} busy — another JinGu instance is running. Hotkeys still work.");
                return;
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Pipe server creation failed: {e}");
                return;
            }

            _current = server;
            try
            {
                server.WaitForConnection();
                ServeClient(server);
            }
            catch (Exception e)
            {
                if (_running) Plugin.Log.Error($"Pipe server loop: {e.Message}");
            }
            finally
            {
                try { server.Disconnect(); } catch { /* ignore */ }
                server.Dispose();
                _current = null;
            }
        }
    }

    private static void ServeClient(NamedPipeServerStream pipe)
    {
        var header = new byte[4];
        while (pipe.IsConnected)
        {
            // Read 4-byte length prefix
            if (!ReadExact(pipe, header, 0, 4)) return;
            int len = (int)BitConverter.ToUInt32(header, 0);
            if (len <= 0 || len > MaxMessageBytes) { Plugin.Log.Error($"pipe: bad message length {len}"); return; }

            // Read body
            var body = new byte[len];
            if (!ReadExact(pipe, body, 0, len)) return;
            string requestJson = Encoding.UTF8.GetString(body);

            // Dispatch (reuses HttpServer handlers)
            string responseJson;
            try { responseJson = Dispatcher.Handle(requestJson); }
            catch (Exception e) { responseJson = $"{{\"ok\":false,\"error\":{Json.Quote(e.Message)}}}"; }

            // Write response
            var respBytes = Encoding.UTF8.GetBytes(responseJson);
            var respHeader = BitConverter.GetBytes((uint)respBytes.Length);
            try
            {
                pipe.Write(respHeader, 0, 4);
                pipe.Write(respBytes, 0, respBytes.Length);
                pipe.Flush();
            }
            catch (IOException)   { return; }   // client disconnected mid-write
            catch (ObjectDisposedException) { return; }
        }
    }

    private static bool ReadExact(Stream s, byte[] buf, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n;
            try { n = s.Read(buf, offset + read, count - read); }
            catch (IOException)            { return false; }
            catch (ObjectDisposedException) { return false; }
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }
}
