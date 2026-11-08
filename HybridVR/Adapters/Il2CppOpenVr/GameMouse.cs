using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HybridVR.Shared;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal struct MouseViewport
{
    internal IntPtr Window;
    internal int Width, Height, Left, Top, DesktopLeft, DesktopTop, DesktopWidth, DesktopHeight;
}
internal interface IGameMousePlatform
{
    bool Foreground { get; }
    bool KeyHeld(int key);
    bool Viewport(out MouseViewport viewport);
    bool Send(uint flags, int x, int y);
    bool Cursor(out int x, out int y);
    DesktopMouseEvents DesktopEvents { get; }
    string ObserverStatus { get; }
    string Error { get; }
}
internal sealed class WindowsGameMouse : IGameMousePlatform, IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { internal int X, Y; internal uint Data, Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal MouseInput Mouse; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    private readonly uint _process = (uint)Process.GetCurrentProcess().Id;
    private readonly DesktopMouseObserver _observer = new();
    public DesktopMouseEvents DesktopEvents => _observer.Read();
    public string ObserverStatus => _observer.Status;
    internal void AttachTrace(DesktopClickTraceBuffer? trace) => _observer.Trace = trace;
    public void Dispose() => _observer.Dispose();
    public bool Foreground
    {
        get { var w = GetForegroundWindow(); GetWindowThreadProcessId(w, out uint pid); return w != IntPtr.Zero && pid == _process; }
    }
    public bool KeyHeld(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public bool Cursor(out int x, out int y) { bool ok = GetCursorPos(out var point); x = point.X; y = point.Y; return ok; }
    public bool Viewport(out MouseViewport viewport)
    {
        viewport = default;
        var w = GetForegroundWindow(); GetWindowThreadProcessId(w, out uint pid);
        if (w == IntPtr.Zero || pid != _process || !GetClientRect(w, out var rect)) return false;
        var point = new Point();
        if (!ClientToScreen(w, ref point)) return false;
        viewport = new MouseViewport { Window = w, Width = rect.R - rect.L, Height = rect.B - rect.T,
            Left = point.X, Top = point.Y, DesktopLeft = GetSystemMetrics(76), DesktopTop = GetSystemMetrics(77),
            DesktopWidth = GetSystemMetrics(78), DesktopHeight = GetSystemMetrics(79) };
        return viewport.DesktopWidth > 1 && viewport.DesktopHeight > 1;
    }
    public string Error { get; private set; } = "";
    public bool Send(uint flags, int x, int y)
    {
        bool ok = SendInput(1, new[] { new Input { Mouse = new MouseInput { X = x, Y = y, Flags = flags, Extra = new UIntPtr(DesktopMouseObserver.InjectionTag) } } }, Marshal.SizeOf<Input>()) == 1;
        Error = ok ? "" : "SendInput failed: " + Marshal.GetLastWin32Error();
        return ok;
    }
}
internal sealed class GameMouse : IDisposable
{
    private readonly IGameMousePlatform _platform;
    internal GameMouse() : this(new WindowsGameMouse()) { }
    internal GameMouse(IGameMousePlatform platform) { _platform = platform; }
    private readonly PointerClickMotion _motion = new();
    private readonly bool[] _down = new bool[3];
    private readonly double[] _releaseGrace = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
    private IntPtr _window;
    private PointerOwner _owner;
    private bool _hasOwner, _armed, _keyHeld;
    private bool _cursorKnown;
    private int _cursorX, _cursorY;
    private bool _pendingMove;
    private int _expectedX, _expectedY;
    private double _desktopUntil, _now;
    private long _desktopSequence;
    internal string OwnershipStatus
    {
        get
        {
            var events = _platform.DesktopEvents;
            return $"observer={_platform.ObserverStatus}, externalEvents={_desktopSequence}, desktopDowns={events.LeftPresses}/{events.RightPresses}/{events.MiddlePresses}, injectedDowns={events.InjectedPresses}, vrHeld={Holding}";
        }
    }
    internal bool DesktopInUse => _now < _desktopUntil;
    internal void AttachTrace(DesktopClickTraceBuffer? trace)
    { if (_platform is WindowsGameMouse windows) windows.AttachTrace(trace); }
    // VD desktop input can leave the game foreground. A cursor move not emitted by
    // this adapter, or an external held button, yields ownership without releasing
    // physical buttons. Our own held buttons are tracked and released separately.
    internal void ObserveDesktop(double seconds)
    {
        _now = seconds;
        ObserveEvents(seconds);
        if (_platform.Cursor(out int x, out int y))
        {
            bool ownQueuedMove = _pendingMove && Math.Abs(x - _expectedX) <= 2 && Math.Abs(y - _expectedY) <= 2;
            if (_cursorKnown && !ownQueuedMove && (Math.Abs(x - _cursorX) > 2 || Math.Abs(y - _cursorY) > 2)) _desktopUntil = seconds + .75;
            if (ownQueuedMove) _pendingMove = false;
            _cursorX = x; _cursorY = y; _cursorKnown = true;
        }
        if ((!_down[0] && seconds > _releaseGrace[0] && _platform.KeyHeld(1)) ||
            (!_down[1] && seconds > _releaseGrace[1] && _platform.KeyHeld(2)) ||
            (!_down[2] && seconds > _releaseGrace[2] && _platform.KeyHeld(4)))
            _desktopUntil = seconds + .75;
        if (DesktopInUse) Release();
    }
    private void ObserveEvents(double seconds)
    {
        var events = _platform.DesktopEvents;
        if (events.Sequence != _desktopSequence || events.Buttons != 0)
        {
            _desktopSequence = events.Sequence;
            _desktopUntil = seconds + .75;
            // Do not send an UP over another owner's DOWN on the same OS button.
            for (int i = 0; i < 3; i++) if ((events.Buttons & (1 << i)) != 0) _down[i] = false;
        }
    }
    internal bool Shortcut(int key)
    {
        bool held = Foreground && _platform.KeyHeld(key);
        bool down = held && !_keyHeld; _keyHeld = held; return down;
    }
    internal bool Foreground => _platform.Foreground;
    internal bool MenuPointer(int width, int height, out float x, out float y, out bool held)
    {
        x=y=0; held=false;
        if (width < 1 || height < 1 || !_platform.Foreground || !_platform.Viewport(out var viewport) ||
            viewport.Width < 1 || viewport.Height < 1 || !_platform.Cursor(out int px,out int py)) return false;
        x=(px-viewport.Left)*(float)width/viewport.Width;
        y=(py-viewport.Top)*(float)height/viewport.Height;
        held=_platform.KeyHeld(1);
        return x>=0 && y>=0 && x<width && y<height;
    }
    internal bool Holding => _down[0] || _down[1] || _down[2];
    internal string Status { get; private set; } = "Mouse ready";

    internal void Update(bool hit, float x, float y, int width, int height, bool left, bool right, bool middle, PointerOwner owner)
    {
        ObserveEvents(_now);
        if (DesktopInUse || !hit || !_platform.Viewport(out var viewport))
        { Release(); return; }
        if (_window != viewport.Window) { Release(); _window = viewport.Window; }
        if (!_hasOwner || !_owner.SameAs(owner)) { Release(); _owner = owner; _hasOwner = true; }
        if (!left && !right && !middle) _armed = true;
        var point = _motion.Update(owner, x, y, width, height, left || right || middle, 12);
        if (!PanelCoordinates.TryScreenToClient(point.X, point.Y, width, height, viewport.Width, viewport.Height, out int px, out int py))
        { Release(); return; }
        if (!Send(0x8000 | 0x4000 | 0x0001,
            (int)Math.Round((px + viewport.Left - viewport.DesktopLeft) * 65535d / (viewport.DesktopWidth - 1)),
            (int)Math.Round((py + viewport.Top - viewport.DesktopTop) * 65535d / (viewport.DesktopHeight - 1))))
        { Release(); return; }
        _expectedX = px + viewport.Left; _expectedY = py + viewport.Top; _pendingMove = true;
        if (_armed) { Button(0, left); Button(1, right); Button(2, middle); }
    }
    private void Button(int button, bool held)
    {
        if (_down[button] == held) return;
        uint flag = button == 0 ? (held ? 2u : 4u) : button == 1 ? (held ? 8u : 16u) : (held ? 32u : 64u);
        if (Send(flag)) { _down[button] = held; if (!held) _releaseGrace[button] = _now + .12; }
    }
    private bool Send(uint flags, int x = 0, int y = 0)
    {
        bool ok = _platform.Send(flags, x, y);
        if (ok && (flags & 1) != 0 && _platform.Cursor(out _cursorX, out _cursorY)) _cursorKnown = true;
        Status = ok ? "Mouse ready" : _platform.Error;
        return ok;
    }
    internal void Release() { ObserveEvents(_now); for (int i = 0; i < 3; ++i) Button(i, false); _motion.Reset(); _armed = false; }
    public void Dispose() { Release(); if (_platform is IDisposable disposable) disposable.Dispose(); }
}
