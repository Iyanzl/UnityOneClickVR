using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
#if HYBRID_MONO
using Newtonsoft.Json;
#else
using System.Text.Json;
#endif
using HybridVR.Shared;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

[StructLayout(LayoutKind.Sequential)]
internal struct HandInput
{
    internal uint Valid, Buttons;
    internal float Trigger, Grip, X, Y;
    internal HmdMatrix34_t GripPose, AimPose;
    internal uint AimValid, Active;
}
[StructLayout(LayoutKind.Sequential)]
internal struct InputFrame { internal HandInput Left, Right; }

internal sealed class OpenVrActions : IDisposable
{
#if HYBRID_MONO
    private const string Library = "Uuvr.OpenVR.InputBridge.dll";
#else
    private const string Library = "Uuvr.OpenVR.InputBridge";
#endif
    [DllImport(Library)] internal static extern int HybridInput_Abi();
    [DllImport(Library)] private static extern int HybridInput_Initialize([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Library)] private static extern int HybridInput_IdentifyApplication(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string manifest, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, uint processId);
    [DllImport(Library)] internal static extern int HybridInput_Read(out InputFrame frame, uint size, int origin);
    [DllImport(Library)] private static extern void HybridInput_Reset();
    private readonly string _manifest;
    private readonly OpenVrApplicationIdentity _identity;
    private readonly uint _processId;
    private double _retry;
    private bool _ready;
    private bool _identified, _inactiveWarning;
    private double _inactiveSince = -1;
    private long _session = 1;
    private readonly InputReadinessRecovery _readiness = new();
    internal string Status { get; private set; } = "Input initializing";
    internal InputFrame Frame;

    internal OpenVrActions(string configDirectory)
    {
        int abi;
        try { abi = HybridInput_Abi(); }
        finally { NativeLibraryDiagnostics.WriteOnce(); }
        if (Marshal.SizeOf<InputFrame>() != 256 || abi != 2)
            throw new InvalidOperationException("Input bridge ABI mismatch");
        _manifest = CreateManifest(configDirectory);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        _processId = (uint)process.Id;
        _identity = new OpenVrApplicationIdentity(process.MainModule!.FileName!, _manifest);
    }
    internal ControllerSample Read(long frame, double seconds, int origin)
    {
        if (!_ready && seconds >= _retry)
        {
            _retry = seconds + 2;
            if (!_identified)
            {
                int identityError = HybridInput_IdentifyApplication(_identity.Manifest, _identity.Key, _processId);
                Status = $"SteamVR input identity={identityError}, key={_identity.Key}";
                OpenVrDiagnostics.Write(Status);
                if (identityError != 0) { Frame = default; return default; }
                _identified = true;
            }
            int error = HybridInput_Initialize(_manifest);
            _ready = error == 0;
            Status = "SteamVR input init=" + error;
            OpenVrDiagnostics.Write(Status);
        }
        int result = _ready ? HybridInput_Read(out Frame, 256, origin) : -1;
        if (result != 0) Frame = default;
        Status = $"Input={result}; L={Frame.Left.Valid}/{Frame.Left.Active:X2}, R={Frame.Right.Valid}/{Frame.Right.Active:X2}";
        if (_ready && result == 0 && (Frame.Left.Active | Frame.Right.Active) == 0)
        {
            if (_inactiveSince < 0) _inactiveSince = seconds;
            if (!_inactiveWarning && seconds - _inactiveSince >= 5)
            {
                _inactiveWarning = true;
                OpenVrDiagnostics.Write("Input handles ready but no active actions for 5s; check controller connection/focus/bindings. " +
                    $"key={_identity.Key}, manifest={_manifest}. No camera fallback or global binding reset performed.");
            }
        }
        else _inactiveSince = -1;
        if (_ready && _readiness.ShouldRetry(seconds, result, Frame.Left.Active | Frame.Right.Active))
        {
            // Manifest registration can precede SteamVR binding readiness on a
            // fresh install. Refresh handles, not the renderer's OpenVR session.
            _ready = _identified = false;
            _retry = seconds + .25;
            Frame = default;
            OpenVrDiagnostics.Write("input startup recovery: refreshing application/action handles after 5s without active actions; at most three retries; bindings and VR session preserved");
        }
        return new ControllerSample
        {
            Frame = frame, Session = _session, Seconds = seconds,
            Valid = result == 0 && Frame.Left.Valid != 0 && Frame.Right.Valid != 0,
            X = (Frame.Left.Buttons & 1) != 0, Y = (Frame.Left.Buttons & 2) != 0,
            A = (Frame.Right.Buttons & 1) != 0, B = (Frame.Right.Buttons & 2) != 0,
            L3 = (Frame.Left.Buttons & 4) != 0, R3 = (Frame.Right.Buttons & 4) != 0,
            LeftTrigger = Frame.Left.Trigger > .55f, RightTrigger = Frame.Right.Trigger > .55f,
            LeftGrip = Frame.Left.Grip > .55f, RightGrip = Frame.Right.Grip > .55f,
            LeftStickX = Frame.Left.X, LeftStickY = Frame.Left.Y,
            RightStickX = Frame.Right.X, RightStickY = Frame.Right.Y
        };
    }
    public void Dispose() { HybridInput_Reset(); _ready = _identified = false; _session++; Frame = default; }

    internal static string CreateManifest(string root)
    {
        var directory = Path.Combine(root, "HybridVR.OpenVR");
        Directory.CreateDirectory(directory);
        var actions = new List<object>();
        foreach (string hand in new[] { "left", "right" })
            foreach (string name in new[] { "pose", "aim", "primary", "secondary", "click", "trigger", "grip", "stick" })
                actions.Add(new { name = $"/actions/hybrid/in/{hand}_{name}", type = name == "pose" || name == "aim" ? "pose" :
                    name == "stick" ? "vector2" : name == "trigger" || name == "grip" ? "vector1" : "boolean", requirement = "suggested" });
        foreach (string controller in new[] { "oculus_touch", "knuckles" })
        {
            var sources = new List<object>();
            var poses = new List<object>();
            foreach (string hand in new[] { "left", "right" })
            {
                string output = $"/actions/hybrid/in/{hand}_";
                void Source(string mode, string path, params (string input, string action)[] mappings)
                {
                    var inputs = new Dictionary<string, object>();
                    foreach (var m in mappings) inputs.Add(m.input, new { output = output + m.action });
                    sources.Add(new { mode, path = $"/user/hand/{hand}/input/{path}", inputs });
                }
                Source("button", hand == "left" && controller == "oculus_touch" ? "x" : "a", ("click", "primary"));
                Source("button", hand == "left" && controller == "oculus_touch" ? "y" : "b", ("click", "secondary"));
                Source("joystick", "joystick", ("position", "stick"), ("click", "click"));
                Source("trigger", "trigger", ("pull", "trigger"));
                Source(controller == "knuckles" ? "force_sensor" : "trigger", "grip", (controller == "knuckles" ? "force" : "pull", "grip"));
                poses.Add(new { output = output + "pose", path = $"/user/hand/{hand}/pose/grip" });
                poses.Add(new { output = output + "aim", path = $"/user/hand/{hand}/pose/tip" });
            }
            WriteDefault(Path.Combine(directory, controller + ".json"), new
            {
                controller_type = controller, name = "HybridVR shared interaction", description = "HybridVR default physical controls",
                bindings = new Dictionary<string, object> { ["/actions/hybrid"] = new { sources, poses } }
            });
        }
        var manifest = Path.Combine(directory, "actions.json");
        WriteDefault(manifest, new { action_sets = new[] { new { name = "/actions/hybrid", usage = "leftright" } }, actions,
            default_bindings = new[] { new { controller_type = "oculus_touch", binding_url = "oculus_touch.json" },
                new { controller_type = "knuckles", binding_url = "knuckles.json" } } });
        return manifest;
    }
    private static void WriteDefault(string path, object value)
    {
        // Preserve user edits and SteamVR's per-game bindings on subsequent starts.
#if HYBRID_MONO
        if (!File.Exists(path)) File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
#else
        if (!File.Exists(path)) File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
#endif
    }
}
