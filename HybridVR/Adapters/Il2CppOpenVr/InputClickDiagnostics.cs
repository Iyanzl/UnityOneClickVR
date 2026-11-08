extern alias LegacyInput;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using Uuvr.OpenVR;
using Input = LegacyInput::UnityEngine.Input;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Opt-in, read-only sampling. Do not patch EventSystem, replay clicks, or change focus.
internal sealed class InputClickDiagnostics : IDisposable
{
    internal const string Argument = "--hybrid-input-diagnostics";
    private readonly GameMouse _mouse;
    private readonly DesktopClickTraceBuffer _edges = new();
    private readonly StreamWriter _writer;
    private readonly long _start = Stopwatch.GetTimestamp();
    private double _lastLate, _nextSample, _burstUntil, _nextFlush;
    private int _lastButtons, _lastTouches, _lines;
    private bool _disposed, _legacyUnavailable, _uiUnavailable;
    private bool _newInputChecked;
    private PropertyInfo? _newMouseCurrent;
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    internal static InputClickDiagnostics? Create(GameMouse mouse)
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), Argument) < 0) return null;
        try { return new InputClickDiagnostics(mouse); }
        catch (Exception e) { OpenVrDiagnostics.Write("input diagnostic unavailable: " + e.Message); return null; }
    }
    private InputClickDiagnostics(GameMouse mouse)
    {
        _mouse = mouse;
        string path = Path.Combine(Path.GetDirectoryName(OpenVrDiagnostics.LogPath)!, "HybridVR.InputTrace.latest.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
        Write("GC05 input diagnosis; read-only; limit=600 seconds/6000 records; no keyboard text or click replay");
        _writer.Flush();
        _mouse.AttachTrace(_edges);
        OpenVrDiagnostics.Write("input diagnostic enabled: " + path);
    }
    private double Seconds => (Stopwatch.GetTimestamp() - _start) / (double)Stopwatch.Frequency;
    private bool Ready()
    {
        if (_disposed) return false;
        if (Seconds < 600 && _lines < 6000) return true;
        Dispose(); return false;
    }
    internal void Sample(bool late, bool menu)
    {
        if (!Ready()) return;
        try
        {
            double now = Seconds;
            while (_edges.TryRead(out var edge))
            {
                Write($"OS t={(edge.Timestamp - _start) / (double)Stopwatch.Frequency:0.000000} msg=0x{edge.Message:X} source={edge.Source} flags=0x{edge.Flags:X} extra=0x{edge.Extra:X} xy={edge.X},{edge.Y}");
                _burstUntil = now + .15;
            }
            int held = 0, down = 0, up = 0, touches = 0;
            Vector3 position = default;
            if (!_legacyUnavailable)
            {
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (Input.GetMouseButton(i)) held |= 1 << i;
                        if (Input.GetMouseButtonDown(i)) down |= 1 << i;
                        if (Input.GetMouseButtonUp(i)) up |= 1 << i;
                    }
                    touches = Input.touchCount; position = Input.mousePosition;
                }
                catch (Exception e) { _legacyUnavailable = true; Write("legacy unavailable: " + e.GetType().Name + ":" + e.Message); }
            }
            double gap = late && _lastLate > 0 ? (now - _lastLate) * 1000 : 0;
            if (late) _lastLate = now;
            bool changed = held != _lastButtons || touches != _lastTouches || down != 0 || up != 0;
            _lastButtons = held; _lastTouches = touches;
            if (changed) _burstUntil = now + .15;
            if (changed || now < _burstUntil || late && now >= _nextSample)
            {
                int osHeld = (GetAsyncKeyState(1) < 0 ? 1 : 0) | (GetAsyncKeyState(2) < 0 ? 2 : 0) | (GetAsyncKeyState(4) < 0 ? 4 : 0);
                Write($"{(late ? "Late" : "Update")} t={now:0.000000} frame={Time.frameCount} gapMs={gap:0.0} unityFocus={Application.isFocused} winFocus={_mouse.Foreground} desktopOwner={_mouse.DesktopInUse} vrHeld={_mouse.Holding} menu={menu} osHeld={osHeld} legacyHeld={held} down={down} up={up} pos={position} touchCount={touches} size={Screen.width}x{Screen.height} timeScale={Time.timeScale:0.###} dropped={_edges.Dropped}");
                if (late)
                {
                    Write(DesktopTarget());
                    Write(UiState(touches));
                    Write(NewInputState());
                    _nextSample = now + (held != 0 || touches > 0 ? .125 : 2);
                }
                if (touches > 0 && !_legacyUnavailable)
                    for (int i = 0; i < Math.Min(touches, 4); i++)
                    {
                        var touch = Input.GetTouch(i);
                        Write($"touch id={touch.fingerId} phase={touch.phase} type={touch.type} position={touch.position} delta={touch.deltaPosition}");
                    }
            }
            if (late && gap > 100) Write($"STALL t={now:0.000000} gapMs={gap:0.0}; update interval, not GPU timing");
            if (now >= _nextFlush) { _writer.Flush(); _nextFlush = now + 1; }
        }
        catch (Exception e) { Fail(e); }
    }
    internal void Gui(bool menu)
    {
        if (!Ready()) return;
        try
        {
            var e = Event.current;
            if (e == null || e.rawType is not (EventType.MouseDown or EventType.MouseUp)) return;
            Write($"IMGUI t={Seconds:0.000000} frame={Time.frameCount} raw={e.rawType} type={e.type} button={e.button} pos={e.mousePosition} menu={menu}");
        }
        catch (Exception e) { Fail(e); }
    }
    private string UiState(int touches)
    {
        if (_uiUnavailable) return "uGUI=unavailable";
        try
        {
            var system = EventSystem.current;
            if (system == null) return "uGUI=no EventSystem";
            var module = system.currentInputModule;
#if HYBRID_MONO
            string text = $"uGUI system={Name(system.gameObject)} focused={system.isFocused} dragThreshold={system.pixelDragThreshold} selected={Name(system.currentSelectedGameObject)} module={module?.GetType().FullName ?? "none"} inputOverride={module?.inputOverride?.GetType().FullName ?? "none"}";
            var pointer = module as PointerInputModule;
#else
            string text = $"uGUI system={Name(system.gameObject)} focused={system.isFocused} dragThreshold={system.pixelDragThreshold} selected={Name(system.currentSelectedGameObject)} module={(module == null ? "none" : module.GetIl2CppType().FullName)} inputOverride={(module?.inputOverride == null ? "none" : module.inputOverride.GetIl2CppType().FullName)}";
            var pointer = module?.TryCast<PointerInputModule>();
#endif
            if (pointer == null) return text + "; pointer cache unavailable for this module";
            // Reads existing pointer data only; never GetPointerData(create:true) or RaycastAll.
            for (int id = -1; id >= -3; id--) text += Describe(ReadPointer(pointer, id), id);
            for (int i = 0; i < Math.Min(touches, 4); i++)
            { int id = Input.GetTouch(i).fingerId; text += Describe(ReadPointer(pointer, id), id); }
            return text;
        }
        catch (Exception e) { _uiUnavailable = true; return "uGUI unavailable: " + e.GetType().Name + ":" + e.Message; }
    }
    private static PointerEventData? ReadPointer(PointerInputModule module, int id)
    {
#if HYBRID_MONO
        return typeof(PointerInputModule).GetMethod("GetLastPointerEventData", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(module, new object[] { id }) as PointerEventData;
#else
        return module.GetLastPointerEventData(id);
#endif
    }
    private static string Describe(PointerEventData? data, int id)
    {
        if (data == null) return $"; p{id}=none";
        return $"; p{id}[xy={data.position} pressXY={data.pressPosition} enter={Name(data.pointerEnter)} hit={Name(data.pointerCurrentRaycast.gameObject)} press={Name(data.pointerPress)} rawPress={Name(data.rawPointerPress)} drag={Name(data.pointerDrag)} dragging={data.dragging} eligible={data.eligibleForClick} clicks={data.clickCount}]";
    }
    private static string Name(GameObject? value) => value == null ? "none" : value.name + "#" + value.GetInstanceID();
    private string NewInputState()
    {
        try
        {
            if (!_newInputChecked)
            {
                _newInputChecked = true;
                _newMouseCurrent = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem", false)?.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
            }
            if (_newMouseCurrent == null) return "newInput=unavailable";
            var mouse = _newMouseCurrent.GetValue(null);
            if (mouse == null) return "newInput mouse=none";
            object? Read(object? owner, string property) => owner?.GetType().GetProperty(property)?.GetValue(owner);
            var position = Read(mouse, "position");
            string state = "newInput position=" + position?.GetType().GetMethod("ReadValue", Type.EmptyTypes)?.Invoke(position, null);
            foreach (string button in new[] { "leftButton", "rightButton", "middleButton" })
            {
                var control = Read(mouse, button);
                state += $" {button}={Read(control, "isPressed")}/{Read(control, "wasPressedThisFrame")}/{Read(control, "wasReleasedThisFrame")}";
            }
            return state;
        }
        catch (Exception e) { _newMouseCurrent = null; return "newInput unavailable: " + e.GetBaseException().Message; }
    }
    private static string DesktopTarget()
    {
        if (!GetCursorPos(out var point)) return "windowAtCursor=unknown";
        var window = WindowFromPoint(point); GetWindowThreadProcessId(window, out uint process);
        var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
        return $"windowAtCursor=0x{window.ToInt64():X} pid={process} class={name} physicalXY={point.X},{point.Y}; hit-test metadata is not proof of message delivery";
    }
    private void Write(string text) { _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {text}"); _lines++; }
    private void Fail(Exception error)
    {
        try { Write("diagnostic stopped: " + error.GetType().Name + ":" + error.Message); } catch { }
        Dispose();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _mouse.AttachTrace(null);
        try { Write($"diagnostic end: seconds={Seconds:0.0} records={_lines} dropped={_edges.Dropped}"); _writer.Dispose(); }
        catch { /* Diagnostic failure cannot disable interaction. */ }
    }
}
