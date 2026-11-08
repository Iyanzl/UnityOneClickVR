using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

// A shared manual camera has no independent desktop/eye motion history. Bypass
// this effect for the VR session, including the source pass captured by the panel.
internal sealed class MotionBlurSuppression : IDisposable
{
    private static readonly (string Type, string Method)[] Targets =
    {
        ("UnityEngine.Rendering.PostProcessing.MotionBlur, Unity.Postprocessing.Runtime", "IsEnabledAndSupported"),
        ("UnityEngine.Rendering.Universal.MotionBlur, Unity.RenderPipelines.Universal.Runtime", "IsActive"),
        ("UnityEngine.Rendering.HighDefinition.MotionBlur, Unity.RenderPipelines.HighDefinition.Runtime", "IsActive")
    };
    private readonly Harmony _patches = new("codex.hybrid.motion-blur." + Guid.NewGuid().ToString("N"));
    private readonly HashSet<MethodInfo> _attempted = new();
    private readonly List<MethodInfo> _installed = new();
    private readonly Func<string, Type?> _resolve;
    private readonly Action<string> _log;
    private static MotionBlurSuppression? _current;
    private bool _enabled, _disposed;
    private volatile bool _discoveryPending = true;
    private float _nextDiscovery;
    private long _evaluations, _active, _suppressed;
    internal string Status => $"motion blur: disabled={_enabled}, hooks={_installed.Count}, evaluations={_evaluations}, active={_active}, suppressed={_suppressed}; scope=VR-session-eyes-and-capture";

    internal MotionBlurSuppression() : this(name => Type.GetType(name, false), OpenVrDiagnostics.Write) { }
    internal MotionBlurSuppression(Func<string, Type?> resolve, Action<string> log)
    {
        _resolve = resolve; _log = log;
        AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;
    }

    private void AssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
    {
        string? name = args.LoadedAssembly.GetName().Name;
        if (name is "Unity.Postprocessing.Runtime" or "Unity.RenderPipelines.Universal.Runtime" or "Unity.RenderPipelines.HighDefinition.Runtime")
            _discoveryPending = true;
    }

    internal static MethodInfo? FindMethod(Type? type, string name)
    {
        if (type == null) return null;
        MethodInfo? found = null;
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (method.Name != name || method.ReturnType != typeof(bool) || method.IsAbstract || method.ContainsGenericParameters) continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 0 && !(name == "IsEnabledAndSupported" && parameters.Length == 1 &&
                parameters[0].ParameterType.FullName == "UnityEngine.Rendering.PostProcessing.PostProcessRenderContext")) continue;
            if (found != null) return null;
            found = method;
        }
        return found;
    }

    internal void Update(bool enabled, float now)
    {
        if (_disposed) return;
        _current = this;
        _enabled = enabled;
        if (!enabled || !_discoveryPending || now < _nextDiscovery) return;
        _nextDiscovery = now + 5;
        _discoveryPending = false;
        foreach (var target in Targets)
        {
            try
            {
                var method = FindMethod(_resolve(target.Type), target.Method);
                if (method == null || !_attempted.Add(method)) continue;
                _patches.Patch(method, postfix: new HarmonyMethod(typeof(MotionBlurSuppression), nameof(Filter)));
                _installed.Add(method);
                _log("motion blur hook installed: " + method.DeclaringType!.FullName + "." + method.Name);
            }
            catch (System.IO.FileNotFoundException) { /* Optional pipeline absent. */ }
            catch (Exception error) { _log("motion blur hook unavailable: " + target.Type + ": " + error.Message); }
        }
    }

    internal static void Filter(ref bool __result)
    {
        var current = _current;
        if (current == null || current._disposed) return;
        current._evaluations++;
        if (__result) current._active++;
        if (!current._enabled) return;
        if (__result) current._suppressed++;
        __result = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _enabled = false;
        AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
        if (_current == this) _current = null;
        foreach (var method in _installed)
        {
            try { _patches.Unpatch(method, HarmonyPatchType.All, _patches.Id); }
            catch (Exception error) { _log("motion blur hook cleanup: " + error.Message); }
        }
        _installed.Clear();
    }
}
