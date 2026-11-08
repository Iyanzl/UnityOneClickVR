using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal readonly struct DesktopMouseEvents
{
    internal readonly long Sequence;
    internal readonly int Buttons;
    internal readonly long LeftPresses, RightPresses, MiddlePresses, InjectedPresses;
    internal DesktopMouseEvents(long sequence, int buttons, long left = 0, long right = 0, long middle = 0, long injected = 0)
    { Sequence = sequence; Buttons = buttons; LeftPresses = left; RightPresses = right; MiddlePresses = middle; InjectedPresses = injected; }
}

// Observe only mouse ownership while this game is foreground. Never suppress or
// replay events, never observe keyboard input, and never call Unity on this thread.
internal sealed class DesktopMouseObserver : IDisposable
{
    internal const ulong InjectionTag = 0x4856524D;
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseEvent
    { internal Point Point; internal uint Data, Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { internal IntPtr Window; internal uint Id; internal UIntPtr W; internal IntPtr L; internal uint Time; internal Point Point; internal uint Private; }
    private delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, Hook hook, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    private readonly uint _process = (uint)Process.GetCurrentProcess().Id;
    private readonly Hook _callback;
    private readonly Thread _thread;
    private long _sequence;
    private long _leftPresses, _rightPresses, _middlePresses, _injectedPresses;
    private int _buttons;
    private uint _threadId;
    private volatile bool _stopping;
    private volatile string _status = "starting";
    internal volatile DesktopClickTraceBuffer? Trace;
    internal string Status => _status;
    internal DesktopMouseEvents Read() => new(Interlocked.Read(ref _sequence), Volatile.Read(ref _buttons),
        Interlocked.Read(ref _leftPresses), Interlocked.Read(ref _rightPresses), Interlocked.Read(ref _middlePresses), Interlocked.Read(ref _injectedPresses));
    internal DesktopMouseObserver(bool start = true)
    {
        _callback = Observe;
        _thread = new Thread(Run) { IsBackground = true, Name = "HybridVR desktop mouse ownership" };
        if (start) _thread.Start();
    }
    private void Run()
    {
        IntPtr hook = IntPtr.Zero;
        try
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            if (_stopping) return;
            hook = SetWindowsHookEx(14, _callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) { _status = "unavailable:" + Marshal.GetLastWin32Error(); return; }
            _status = "active";
            while (!_stopping && GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        }
        catch (Exception error) { _status = "unavailable:" + error.GetType().Name; }
        finally { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); }
    }
    private IntPtr Observe(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !_stopping)
        {
            try
            {
                var input = Marshal.PtrToStructure<MouseEvent>(data);
                GetWindowThreadProcessId(GetForegroundWindow(), out uint process);
                Trace?.Add(new DesktopClickTrace((int)message, input.Point.X, input.Point.Y,
                    input.Flags, input.Extra.ToUInt64(), Stopwatch.GetTimestamp()), process == _process);
                ObserveEvent((int)message, input.Extra.ToUInt64(), process == _process, (input.Flags & 1) != 0);
            }
            catch { /* Observation must never interfere with the original event. */ }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    internal void ObserveEvent(int message, ulong tag, bool foreground, bool injected = false)
    {
        if (tag == InjectionTag) return;
        int down = message == 0x201 ? 1 : message == 0x204 ? 2 : message == 0x207 ? 4 : 0;
        int up = message == 0x202 ? 1 : message == 0x205 ? 2 : message == 0x208 ? 4 : 0;
        // Releases outside the game must still clear an earlier desktop hold.
#if HYBRID_MONO
        if (up != 0) UpdateButtons(up, false);
#else
        if (up != 0) Interlocked.And(ref _buttons, ~up);
#endif
        if (!foreground) return;
#if HYBRID_MONO
        if (down != 0) UpdateButtons(down, true);
#else
        if (down != 0) Interlocked.Or(ref _buttons, down);
#endif
        if (down == 1) Interlocked.Increment(ref _leftPresses);
        if (down == 2) Interlocked.Increment(ref _rightPresses);
        if (down == 4) Interlocked.Increment(ref _middlePresses);
        if (down != 0 && injected) Interlocked.Increment(ref _injectedPresses);
        if (message >= 0x200 && message <= 0x20E) Interlocked.Increment(ref _sequence);
    }
#if HYBRID_MONO
    private void UpdateButtons(int mask, bool down)
    {
        int previous;
        do { previous = Volatile.Read(ref _buttons); }
        while (Interlocked.CompareExchange(ref _buttons, down ? previous | mask : previous & ~mask, previous) != previous);
    }
#endif
    public void Dispose()
    {
        _stopping = true;
        if (_threadId != 0) PostThreadMessage(_threadId, 0x12, UIntPtr.Zero, IntPtr.Zero);
        if (_thread.IsAlive) _thread.Join(500);
    }
}
