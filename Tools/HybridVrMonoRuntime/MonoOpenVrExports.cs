using System;
using System.Runtime.InteropServices;
using Uuvr.OpenVR;

namespace HybridVR.MonoRuntime;

// Unity Mono can resolve a short P/Invoke name through the game's Plugins folder.
// Bind exports from the exact Windows module shared with both native bridges.
internal static class MonoOpenVrExports
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint Init(ref EVRInitError error, EVRApplicationType type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint Init2(ref EVRInitError error, EVRApplicationType type,
        [MarshalAs(UnmanagedType.LPStr)] string startupInfo);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Shutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool QueryBool();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr ErrorText(EVRInitError error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr Interface([MarshalAs(UnmanagedType.LPStr)] string version, ref EVRInitError error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal delegate bool InterfaceValid([MarshalAs(UnmanagedType.LPStr)] string version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint Token();

    internal static Init InitInternal = null!;
    internal static Shutdown ShutdownInternal = null!;
    internal static QueryBool IsHmdPresent = null!, IsRuntimeInstalled = null!;
    internal static ErrorText GetStringForHmdError = null!;
    internal static Interface GetGenericInterface = null!;
    internal static InterfaceValid IsInterfaceVersionValid = null!;
    internal static Token GetInitToken = null!;
    internal static IntPtr Module { get; private set; }

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    internal static void Bind(IntPtr module)
    {
        if (module == IntPtr.Zero) throw new ArgumentException("Missing OpenVR module", nameof(module));
        if (Module != IntPtr.Zero && Module != module) throw new InvalidOperationException("Cannot change OpenVR library while the runtime is loaded.");
        T Export<T>(string name) where T : Delegate
        {
            var address = GetProcAddress(module, name);
            if (address == IntPtr.Zero) throw new EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        var originalInit = Export<Init>("VR_InitInternal");
        var init2Address = GetProcAddress(module, "VR_InitInternal2");
        var extendedInit = init2Address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<Init2>(init2Address);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        string startupInfo = "{\"log_file_name\":\"vrclient_HybridVR_" + process.Id + "\"}";
        InitInternal = (ref EVRInitError error, EVRApplicationType type) =>
            InitializeWithLogFallback(originalInit, extendedInit, ref error, type, startupInfo, OpenVrDiagnostics.Write);
        ShutdownInternal = Export<Shutdown>("VR_ShutdownInternal");
        IsHmdPresent = Export<QueryBool>("VR_IsHmdPresent");
        IsRuntimeInstalled = Export<QueryBool>("VR_IsRuntimeInstalled");
        GetStringForHmdError = Export<ErrorText>("VR_GetStringForHmdError");
        GetGenericInterface = Export<Interface>("VR_GetGenericInterface");
        IsInterfaceVersionValid = Export<InterfaceValid>("VR_IsInterfaceVersionValid");
        GetInitToken = Export<Token>("VR_GetInitToken");
        Module = module;
    }

    internal static uint InitializeWithLogFallback(Init original, Init2? extended, ref EVRInitError error,
        EVRApplicationType type, string startupInfo, Action<string> log)
    {
        uint token = original(ref error, type);
        if (error != EVRInitError.Init_NoLogPath || extended == null) return token;
        // Some clients fail to open a Unicode EXE-derived log name. This optional
        // runtime parameter was verified with a Utility client before using it here.
        log("OpenVR 112: retry once with ASCII client log name; no global path changes");
        error = EVRInitError.None;
        token = extended(ref error, type, startupInfo);
        log("OpenVR ASCII-log retry: error=" + error);
        return token;
    }
}
