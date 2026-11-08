using System;
using System.IO;
using System.Text;
using BepInEx;

namespace Uuvr.OpenVR;

public static class OpenVrDiagnostics
{
    private static readonly object SyncRoot = new();
    private static string? _logPath;

    public static string LogPath
    {
        get
        {
            EnsureLogPath();
            return _logPath!;
        }
    }

    public static void StartSession(string build)
    {
        lock (SyncRoot)
        {
            EnsureLogPath();
            HybridVR.Shared.SessionLogArchive.Preserve(_logPath!);
            try
            {
                File.WriteAllText(
                    _logPath!,
#if HYBRID_MONO
                    $"HybridVR Mono diagnostics started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
                    $"build={build}\r\n" +
                    $"process={System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName}\r\n",
#else
                    $"UUVR IL2CPP diagnostics started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
                    $"build={build}\r\n" +
                    $"process={Environment.ProcessPath}\r\n",
#endif
                    new UTF8Encoding(false));
            }
            catch
            {
                // Standard BepInEx logging remains available if the dedicated file cannot be opened.
            }
        }
    }

    public static void Write(string message)
    {
        lock (SyncRoot)
        {
            EnsureLogPath();
            try
            {
                File.AppendAllText(
                    _logPath!,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n",
                    new UTF8Encoding(false));
            }
            catch
            {
                // Diagnostics must never prevent VR initialization.
            }
        }
    }

    private static void EnsureLogPath()
    {
        if (!string.IsNullOrEmpty(_logPath)) return;

        try
        {
#if HYBRID_MONO
            _logPath = Path.Combine(Paths.BepInExRootPath, "HybridVR.Mono.latest.log");
#else
            _logPath = Path.Combine(Paths.BepInExRootPath, "UUVR.Il2Cpp.latest.log");
#endif
        }
        catch
        {
#if HYBRID_MONO
            _logPath = Path.Combine(AppContext.BaseDirectory, "HybridVR.Mono.latest.log");
#else
            _logPath = Path.Combine(AppContext.BaseDirectory, "UUVR.Il2Cpp.latest.log");
#endif
        }
    }
}
