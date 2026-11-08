using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Uuvr.OpenVR;

// Independent of Unity Update, with one bounded latest snapshot and no XR calls on this thread.
internal sealed class NativeSubmitDiagnostics : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Timer _timer;
    private bool _disposed;

    public NativeSubmitDiagnostics(string path)
    {
        _path = path;
        _timer = new Timer(_ => WriteSnapshot(), null, Timeout.Infinite, Timeout.Infinite);
        WriteSnapshot();
        _timer.Change(1000, 1000);
    }

    private void WriteSnapshot()
    {
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            if (_disposed) return;
            var snapshot = OpenVrNativeSubmitBridge.ReadDiagnostics();
            File.WriteAllText(_path + ".tmp",
                $"IL01-F native submit snapshot {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
#if HYBRID_MONO
                $"pid={System.Diagnostics.Process.GetCurrentProcess().Id}; attempted=call entered; returned=call completed; accepted=success\n" +
#else
                $"pid={Environment.ProcessId}; attempted=call entered; returned=call completed; accepted=success\n" +
#endif
                snapshot, new UTF8Encoding(false));
#if HYBRID_MONO
            if (File.Exists(_path)) File.Replace(_path + ".tmp", _path, null);
            else File.Move(_path + ".tmp", _path);
#else
            File.Move(_path + ".tmp", _path, true);
#endif
        }
        catch
        {
            // Diagnostic I/O cannot stop rendering or flood the game log.
        }
        finally { Monitor.Exit(_gate); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            WriteSnapshot();
            _disposed = true;
            _timer.Dispose();
        }
    }
}
