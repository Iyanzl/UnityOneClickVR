using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Opt-in equivalent of the Mono per-render skin-matrix workaround.
// No extra rendering, bone writes, physics changes or per-eye discovery.
internal sealed class SkinnedMatrixCompatibility : IDisposable
{
    private readonly PropertyInfo? _property = typeof(SkinnedMeshRenderer).GetProperty("forceMatrixRecalculationPerRender");
    private readonly Dictionary<int, SkinnedMeshRenderer> _changed = new();
    private readonly HashSet<int> _seen = new();
    private bool _enabled, _failed;
    private float _nextScan;

    internal void Update(bool enabled, float now)
    {
        if (!enabled)
        {
            if (_enabled) Dispose();
            return;
        }
        if (!_enabled)
        {
            _enabled = true;
            _failed = _property?.PropertyType != typeof(bool) || !_property.CanRead || !_property.CanWrite;
            _nextScan = now;
            Uuvr.OpenVR.OpenVrDiagnostics.Write("skinned matrices: requested=True, supported=" + !_failed);
        }
        if (_failed || now < _nextScan) return;
        _nextScan = now + 2;
        try
        {
            var dead = new List<int>();
            foreach (var entry in _changed) if (entry.Value == null) dead.Add(entry.Key);
            foreach (int id in dead) { _changed.Remove(id); _seen.Remove(id); }
            int added = 0;
            foreach (var skin in Object.FindObjectsOfType<SkinnedMeshRenderer>())
            {
                if (skin == null || !skin.enabled || _seen.Count >= 1024 || !_seen.Add(skin.GetInstanceID())) continue;
                if (_property!.GetValue(skin) is true) continue;
                // Register before mutation so an exception still has a restoration path.
                _changed.Add(skin.GetInstanceID(), skin);
                _property.SetValue(skin, true);
                added++;
            }
            if (added > 0) Uuvr.OpenVR.OpenVrDiagnostics.Write($"skinned matrices: changed={added}, owned={_changed.Count}");
        }
        catch (Exception error)
        {
            Uuvr.OpenVR.OpenVrDiagnostics.Write("skinned matrices unavailable; restoring: " + error.Message);
            Restore();
            _failed = true;
        }
    }

    private void Restore()
    {
        foreach (var entry in _changed)
        {
            try
            {
                if (entry.Value != null && _property!.GetValue(entry.Value) is true)
                    _property.SetValue(entry.Value, false);
            }
            catch (Exception error) { Uuvr.OpenVR.OpenVrDiagnostics.Write("skinned matrix restore failed: " + error.Message); }
        }
        _changed.Clear(); _seen.Clear();
    }

    public void Dispose()
    {
        Restore();
        if (_enabled) Uuvr.OpenVR.OpenVrDiagnostics.Write("skinned matrices: disabled, original values restored where available");
        _enabled = false; _failed = false;
    }
}
