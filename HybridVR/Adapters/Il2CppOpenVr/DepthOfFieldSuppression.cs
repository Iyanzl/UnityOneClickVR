using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Optional pipeline capabilities; no scene scans or extra render work.
internal sealed class DepthOfFieldSuppression : IDisposable
{
    private static readonly (string Type, string Method)[] Targets =
    {
        ("UnityEngine.Rendering.PostProcessing.DepthOfField, Unity.Postprocessing.Runtime", "IsEnabledAndSupported"),
        ("UnityEngine.Rendering.Universal.DepthOfField, Unity.RenderPipelines.Universal.Runtime", "IsActive"),
        ("UnityEngine.Rendering.HighDefinition.DepthOfField, Unity.RenderPipelines.HighDefinition.Runtime", "IsActive")
    };
    private readonly Harmony _patches = new("codex.hybrid-il2cpp.dof." + Guid.NewGuid().ToString("N"));
    private readonly HashSet<MethodInfo> _attempted = new();
    private readonly List<MethodInfo> _installed = new();
    private readonly BeautifyDepthOfFieldGate _beautify = new();
    private static DepthOfFieldSuppression? _current;
    private bool _enabled, _disposed;
    private volatile bool _discoveryPending = true;
    private float _nextDiscovery;
    private long _evaluations, _suppressed;
    private int _discoveries;
    private double _discoveryMs;
    internal int Discoveries => _discoveries;
    internal string Status => $"depth of field: disabled={_enabled}, hooks={_installed.Count}, evaluations={_evaluations}, suppressed={_suppressed}, discoveries={_discoveries}, discoveryMs={_discoveryMs:0.00}; {_beautify.Status}";

    internal DepthOfFieldSuppression() => AppDomain.CurrentDomain.AssemblyLoad += AssemblyLoaded;

    private void AssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
    {
        string? name = args.LoadedAssembly.GetName().Name;
        if (name is "Unity.Postprocessing.Runtime" or "Unity.RenderPipelines.Universal.Runtime" or "Unity.RenderPipelines.HighDefinition.Runtime" or "Beautify.Universal.Runtime")
            _discoveryPending = true;
    }

    internal void Update(bool enabled, float now)
    {
        if (_disposed) return;
        _current = this;
        _enabled = enabled;
        _beautify.SetEnabled(enabled);
        if (!enabled || !_discoveryPending || now < _nextDiscovery) return;
        _nextDiscovery = now + 5;
        // Missing pipelines must not cause recurring assembly resolution / file
        // exceptions on the game thread. Retry only when a pipeline is loaded.
        _discoveryPending = false;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _discoveries++;
        foreach (var target in Targets)
        {
            try
            {
                // Optional wrappers resolve through BepInEx. No hard dependency on a particular pipeline version.
                var type = Type.GetType(target.Type, false);
                var method = type?.GetMethod(target.Method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (method == null || method.ReturnType != typeof(bool) || !_attempted.Add(method)) continue;
                _patches.Patch(method, postfix: new HarmonyMethod(typeof(DepthOfFieldSuppression), nameof(Filter)));
                _installed.Add(method);
                OpenVrDiagnostics.Write("depth of field hook installed: " + type!.FullName + "." + method.Name);
            }
            catch (System.IO.FileNotFoundException) { /* Optional pipeline absent. */ }
            catch (Exception error)
            {
                OpenVrDiagnostics.Write("depth of field hook unavailable: " + target.Type + ": " + error.Message);
            }
        }
        DiscoverBeautify();
        _discoveryMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
    }

    private void DiscoverBeautify()
    {
        try
        {
            var type = Type.GetType("Beautify.Universal.BeautifyRendererFeature+BeautifyRenderPass, Beautify.Universal.Runtime", false);
            if (type == null) return;
            foreach (string name in new[] { "UpdateMaterialProperties", "Execute" })
            {
                var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method == null || method.ReturnType != typeof(void) || !_attempted.Add(method)) continue;
                try
                {
                    _patches.Patch(method,
                        prefix: new HarmonyMethod(typeof(DepthOfFieldSuppression), nameof(BeautifyPrefix)),
                        finalizer: new HarmonyMethod(typeof(DepthOfFieldSuppression), nameof(BeautifyFinally)));
                    _installed.Add(method);
                    OpenVrDiagnostics.Write("depth of field hook installed: " + type.FullName + "." + name);
                }
                catch (Exception error) { OpenVrDiagnostics.Write("Beautify DoF hook unavailable: " + name + ": " + error.Message); }
            }
        }
        catch (System.IO.FileNotFoundException) { /* Optional effect absent. */ }
        catch (Exception error) { OpenVrDiagnostics.Write("Beautify DoF discovery unavailable: " + error.Message); }
    }

    internal static void BeautifyPrefix(object __instance, out BeautifyDepthOfFieldGate.Scope? __state)
        => __state = _current?._beautify.Enter(__instance);

    internal static Exception? BeautifyFinally(Exception? __exception, BeautifyDepthOfFieldGate.Scope? __state)
    {
        __state?.Dispose();
        return __exception;
    }

    internal static void Filter(ref bool __result)
    {
        var current = _current;
        if (current == null || !current._enabled || current._disposed) return;
        current._evaluations++;
        if (__result) current._suppressed++;
        __result = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppDomain.CurrentDomain.AssemblyLoad -= AssemblyLoaded;
        _enabled = false;
        _beautify.SetEnabled(false);
        if (_current == this) _current = null;
        foreach (var method in _installed)
        {
            try { _patches.Unpatch(method, HarmonyPatchType.All, _patches.Id); }
            catch (Exception error) { OpenVrDiagnostics.Write("depth of field hook cleanup: " + error.Message); }
        }
        _installed.Clear();
    }
}
