using System;
using BepInEx.Configuration;
using HybridVR.Shared;
using UnityEngine;
using Uuvr.OpenVR;

namespace HybridVR.MonoRuntime;

internal sealed class MonoTemporalAaCompatibility : IDisposable
{
    internal static ConfigEntry<bool>? EnabledSetting;
    private Component? _data;
    private TemporalAaOverride? _change;
    private bool _failed;

    internal MonoTemporalAaCompatibility(Camera camera)
    {
        try
        {
            // This adapter is only for a manual renderer sharing one Camera across
            // desktop and both eyes. Native XR's per-eye histories are not touched.
            foreach (var component in camera.GetComponents<Component>())
            {
                if (component == null || component.GetType().FullName != "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData") continue;
                _data = component;
                _change = TemporalAaOverride.TryCreate(component, OpenVrDiagnostics.Write);
                OpenVrDiagnostics.Write("shared camera TAA capability: camera=" + camera.name + ", supported=" + (_change != null));
                break;
            }
        }
        catch (Exception e) { Fail(e); }
    }

    internal void Update()
    {
        if (_failed || _data == null || _change == null) return;
        try { _change.Update(EnabledSetting?.Value ?? true); }
        catch (Exception e) { Fail(e); }
    }

    private void Fail(Exception error)
    {
        _failed = true;
        OpenVrDiagnostics.Write("shared camera TAA compatibility unavailable: " + error.Message);
        Dispose();
    }

    public void Dispose()
    {
        try { if (_data != null) _change?.Dispose(); }
        catch (Exception e) { OpenVrDiagnostics.Write("shared camera TAA restore failed: " + e.Message); }
        finally { _change = null; _data = null; }
    }
}
