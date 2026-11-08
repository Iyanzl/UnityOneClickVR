using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Uuvr.OpenVR;

internal static class OpenVrNativeSubmitBridge
{
#if HYBRID_MONO
    private const string LibraryName = "Uuvr.OpenVR.NativeBridge.dll";
#else
    private const string LibraryName = "Uuvr.OpenVR.NativeBridge";
#endif

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_GetAbiVersion")]
    public static extern int GetAbiVersion();

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_GetRenderEventFunc")]
    public static extern IntPtr GetRenderEventFunc();

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_ConfigureStereo")]
    public static extern IntPtr ConfigureStereo(
        IntPtr leftTexture,
        IntPtr rightTexture,
        float uMin,
        float vMin,
        float uMax,
        float vMax,
        int colorSpace,
        uint submitFlags);

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_QueueRenderPose")]
    public static extern IntPtr QueueRenderPose(IntPtr generation, ref HmdMatrix34_t pose);

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_GetLastError")]
    public static extern int GetLastError(int eye);

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_GetSubmitCount")]
    public static extern ulong GetSubmitCount(int eye);

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_TryStop")]
    public static extern int TryStop();

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_IsActive")]
    public static extern int IsActive();

    [DllImport(LibraryName, EntryPoint = "UuvrOpenVR_GetDiagnostics", CharSet = CharSet.Ansi)]
    public static extern int GetDiagnostics(StringBuilder buffer, int capacity);

    public static string ReadDiagnostics()
    {
        var buffer = new StringBuilder(2048);
        GetDiagnostics(buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
