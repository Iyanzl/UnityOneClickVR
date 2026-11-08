using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

// GC06 is opt-in. Never change tracking, source transforms, volumes, render
// textures or compositor timing to diagnose a perceived head-motion defect.
internal sealed class HeadMotionDiagnostics : IDisposable
{
    private static readonly (string Type, string Method)[] Targets =
    {
        ("UnityEngine.Rendering.PostProcessing.MotionBlur, Unity.Postprocessing.Runtime", "IsEnabledAndSupported"),
        ("UnityEngine.Rendering.Universal.MotionBlur, Unity.RenderPipelines.Universal.Runtime", "IsActive"),
        ("UnityEngine.Rendering.HighDefinition.MotionBlur, Unity.RenderPipelines.HighDefinition.Runtime", "IsActive")
    };
    private readonly Harmony _patches = new("codex.hybrid-il2cpp.head-motion." + Guid.NewGuid().ToString("N"));
    private readonly HashSet<MethodInfo> _attempted = new();
    private readonly List<MethodInfo> _installed = new();
    private readonly EyeMotionBlurGate _gate;
    private readonly StreamWriter _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SkinRenderDiagnostics? _skins;
    private readonly bool _skinOnly;
    private static HeadMotionDiagnostics? _current;
    private volatile bool _discoveryPending = true;
    private bool _disposed, _known;
    private double _nextReport = 5, _lastFrame;
    private int _frames, _eyeSamples, _poseChanged, _viewChanged, _projectionChanged, _cameraId;
    private Vector3 _previousHead, _previousSource;
    private Quaternion _previousHeadRotation, _previousSourceRotation;
    private float _maxHeadDistance, _maxHeadAngle, _maxSourceDistance, _maxSourceAngle;
    private float _maxRenderDistance, _maxRenderAngle, _maxViewDelta, _maxProjectionDelta;
    private double _maxInterval;

    internal static HeadMotionDiagnostics? TryCreate()
    {
        var args = Environment.GetCommandLineArgs();
        bool suppress = Array.Exists(args, a => a == "--hybrid-disable-eye-motion-blur");
        bool skin = Array.Exists(args, a => a == "--hybrid-skin-diagnostics");
        bool motion = suppress || Array.Exists(args, a => a == "--hybrid-head-motion-diagnostics");
        if (!motion && !skin) return null;
        try { return new HeadMotionDiagnostics(suppress, skin, !motion); }
        catch (Exception e) { OpenVrDiagnostics.Write("head-motion probe unavailable: " + e.Message); return null; }
    }

    private HeadMotionDiagnostics(bool suppress, bool skin, bool skinOnly)
    {
        _gate = new EyeMotionBlurGate(Environment.CurrentManagedThreadId, suppress);
        string path = Path.Combine(Path.GetDirectoryName(OpenVrDiagnostics.LogPath)!, "HybridVR.HeadMotion.latest.log");
        _log = new StreamWriter(path, false) { AutoFlush = true };
        _skinOnly = skinOnly;
        if (skin) _skins = new SkinRenderDiagnostics(Write);
        Write($"GC06 head-motion probe; eyeMotionBlurSuppressed={suppress}; observationSeconds=600; no tracking/input/config changes");
        Write("Counters describe CPU render callbacks, not displayed headset FPS. Active effects are original IsActive results. No queries does not mean no blur.");
        AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
        _current = this;
        OpenVrDiagnostics.Write("GC06 head-motion probe: " + path + "; suppressEyeMotionBlur=" + suppress);
    }

    private void AssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
    {
        string? name = args.LoadedAssembly.GetName().Name;
        if (name is "Unity.Postprocessing.Runtime" or "Unity.RenderPipelines.Universal.Runtime" or "Unity.RenderPipelines.HighDefinition.Runtime")
            _discoveryPending = true;
    }

    private void Discover()
    {
        if (!_discoveryPending) return;
        _discoveryPending = false;
        foreach (var target in Targets)
        {
            try
            {
                var type = Type.GetType(target.Type, false);
                var method = type?.GetMethod(target.Method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (method == null || method.ReturnType != typeof(bool) || !_attempted.Add(method)) continue;
                _patches.Patch(method, postfix: new HarmonyMethod(typeof(HeadMotionDiagnostics), nameof(FilterMotionBlur)));
                _installed.Add(method);
                Write("motion-blur hook installed: " + type!.FullName + "." + method.Name);
            }
            catch (FileNotFoundException) { /* Optional pipeline absent. */ }
            catch (Exception e) { Write("motion-blur hook unavailable: " + target.Type + ": " + e.Message); }
        }
    }

    internal static void FilterMotionBlur(ref bool __result)
        => _current?._gate.Filter(Environment.CurrentManagedThreadId, ref __result);

    internal void SampleFrame(Utils.RigidTransform head, Camera source)
    {
        if (_disposed) return;
        try
        {
            if (!_skinOnly) Discover();
            double now = _clock.Elapsed.TotalSeconds;
            _skins?.Frame(source, now);
            if (now >= 600) { Write("Observation complete. Eye motion-blur selection remains until process exit; aggregate sampling stopped."); _samplingStopped = true; return; }
            var p = source.transform.position;
            var r = source.transform.rotation;
            int id = source.GetInstanceID();
            if (_known)
            {
                _maxHeadDistance = Mathf.Max(_maxHeadDistance, Vector3.Distance(head.pos, _previousHead));
                _maxHeadAngle = Mathf.Max(_maxHeadAngle, Quaternion.Angle(head.rot, _previousHeadRotation));
                _maxInterval = Math.Max(_maxInterval, (now - _lastFrame) * 1000);
                if (_cameraId == id)
                {
                    _maxSourceDistance = Mathf.Max(_maxSourceDistance, Vector3.Distance(p, _previousSource));
                    _maxSourceAngle = Mathf.Max(_maxSourceAngle, Quaternion.Angle(r, _previousSourceRotation));
                }
            }
            if (!_known || id != _cameraId) Write($"source camera id={id}, name={source.name}");
            _known = true; _cameraId = id; _previousHead = head.pos; _previousHeadRotation = head.rot;
            _previousSource = p; _previousSourceRotation = r; _lastFrame = now; _frames++;
            if (now >= _nextReport) { Report(); _nextReport = now + 5; }
        }
        catch (Exception e) { Fail(e); }
    }
    private bool _samplingStopped;
    internal void ObserveFrame(Utils.RigidTransform head, Camera source)
    {
        if (!_samplingStopped) SampleFrame(head, source);
    }

    internal int BeginEye()
    {
        if (_disposed) return -1;
        _skins?.BeginEye();
        return _gate.Enter(Environment.CurrentManagedThreadId);
    }
    internal void EndEye(int scope)
    {
        _gate.Exit(scope);
        if (!_disposed) _skins?.EndEye();
    }

    internal void ObserveEye(Camera camera, Vector3 position, Quaternion rotation, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_disposed || _samplingStopped) return;
        try
        {
            _eyeSamples++;
            float distance = Vector3.Distance(position, camera.transform.position);
            float angle = Quaternion.Angle(rotation, camera.transform.rotation);
            float viewDelta = MatrixDelta(view, camera.worldToCameraMatrix);
            float projectionDelta = MatrixDelta(projection, camera.projectionMatrix);
            if (distance > 0.0001f || angle > 0.02f) _poseChanged++;
            if (viewDelta > 0.0001f) _viewChanged++;
            if (projectionDelta > 0.0001f) _projectionChanged++;
            _maxRenderDistance = Mathf.Max(_maxRenderDistance, distance); _maxRenderAngle = Mathf.Max(_maxRenderAngle, angle);
            _maxViewDelta = Mathf.Max(_maxViewDelta, viewDelta); _maxProjectionDelta = Mathf.Max(_maxProjectionDelta, projectionDelta);
        }
        catch (Exception e) { Fail(e); }
    }

    private static float MatrixDelta(Matrix4x4 a, Matrix4x4 b)
    {
        float maximum = 0;
        for (int i = 0; i < 16; i++) maximum = Mathf.Max(maximum, Mathf.Abs(a[i] - b[i]));
        return maximum;
    }

    private void Report()
    {
        Write($"window frames={_frames}, eyeSamples={_eyeSamples}, maxIntervalMs={_maxInterval:0.00}, " +
            $"headStepMaxMeters={_maxHeadDistance:0.0000}, headStepMaxDeg={_maxHeadAngle:0.000}, " +
            $"sourceStepMaxUnits={_maxSourceDistance:0.0000}, sourceStepMaxDeg={_maxSourceAngle:0.000}, " +
            $"renderPoseChanged={_poseChanged}, renderViewChanged={_viewChanged}, renderProjectionChanged={_projectionChanged}, " +
            $"renderDeltaMaxUnits={_maxRenderDistance:0.0000}, renderDeltaMaxDeg={_maxRenderAngle:0.000}, viewDeltaMax={_maxViewDelta:0.0000}, projectionDeltaMax={_maxProjectionDelta:0.0000}");
        Write($"motionBlur cumulative: hooks={_installed.Count}, eyeQueries={_gate.EyeQueries}, eyeActive={_gate.EyeActive}, eyeSuppressed={_gate.EyeSuppressed}, outsideQueries={_gate.OutsideQueries}, outsideActive={_gate.OutsideActive}");
        _frames = _eyeSamples = _poseChanged = _viewChanged = _projectionChanged = 0;
        _maxHeadDistance = _maxHeadAngle = _maxSourceDistance = _maxSourceAngle = 0;
        _maxRenderDistance = _maxRenderAngle = _maxViewDelta = _maxProjectionDelta = 0; _maxInterval = 0;
    }

    private void Write(string text) => _log.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {text}");
    private void Fail(Exception error)
    {
        OpenVrDiagnostics.Write("head-motion probe stopped, renderer retained: " + error.Message);
        Dispose();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _gate.Dispose();
        AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
        if (_current == this) _current = null;
        foreach (var method in _installed)
        {
            try { _patches.Unpatch(method, HarmonyPatchType.All, _patches.Id); }
            catch (Exception e) { OpenVrDiagnostics.Write("head-motion hook cleanup: " + e.Message); }
        }
        try { Report(); _log.Dispose(); } catch { /* Diagnostics cannot stop rendering. */ }
    }
}
