using System;
using System.Runtime.InteropServices;
using HybridVR.Shared;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed class WindowsKeyboardOutput : IVirtualKeyboardOutput
{
    // Both supported runtimes are x64: INPUT is 40 bytes, union starts at offset 8.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal ushort VirtualKey;
        [FieldOffset(10)] internal ushort Scan;
        [FieldOffset(12)] internal uint Flags;
        [FieldOffset(24)] internal UIntPtr Extra;
    }
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint map);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    private bool _reported;
    public bool Send(VirtualKey key, bool down)
    {
        uint scan = MapVirtualKey((uint)key.Code, 4);
        bool virtualOnly = key.Code == 0x13 || scan == 0;
        var input = new Input { Type = 1, VirtualKey = virtualOnly ? (ushort)key.Code : (ushort)0,
            Scan = virtualOnly ? (ushort)0 : (ushort)(scan & 255),
            Flags = (down ? 0u : 2u) | (virtualOnly ? 0u : 8u) | (key.Extended ? 1u : 0u),
            Extra = new UIntPtr(DesktopMouseObserver.InjectionTag) };
        bool ok = SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) == 1;
        if (!ok && !_reported) { _reported = true; Uuvr.OpenVR.OpenVrDiagnostics.Write("virtual keyboard SendInput failed: " + Marshal.GetLastWin32Error()); }
        return ok;
    }
}
