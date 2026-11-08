using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Presence in an assembly is not evidence of an active UI. Check live components,
// at a bounded cadence, before calling a uGUI-only replay a complete capture.
internal sealed class UiSystemCoverage
{
    private readonly List<Type> _types = new();
    private int _assemblyCount;
    private float _nextScan;
    private string? _reason;
    internal string? UnsupportedActiveSystem()
    {
        if (Time.unscaledTime < _nextScan) return _reason;
        _nextScan = Time.unscaledTime + 2;
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (assemblies.Length != _assemblyCount)
            {
                _assemblyCount = assemblies.Length; _types.Clear();
                foreach (var assembly in assemblies)
                    foreach (string name in new[] { "UIDrawCall", "UnityEngine.UIElements.UIDocument" })
                    {
                        var type = assembly.GetType(name, false);
                        if (type != null && typeof(Behaviour).IsAssignableFrom(type)) _types.Add(type);
                    }
            }
            MethodInfo? find = null;
            foreach (var method in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (method.Name == "FindObjectsOfType" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                { find = method; break; }
            _reason = null;
            foreach (var type in _types)
            {
                if (find == null) return _reason = "UI coverage scan unavailable: " + type.FullName;
                var objects = find.MakeGenericMethod(type).Invoke(null, null) as IEnumerable;
                if (objects == null) return _reason = "UI coverage result unavailable: " + type.FullName;
                foreach (var item in objects)
                    if (item is Behaviour behaviour && behaviour != null && behaviour.isActiveAndEnabled)
                        return _reason = "active " + type.FullName + " is not captured by the uGUI adapter";
            }
        }
        catch (Exception e) { _reason = "UI coverage unknown: " + e.GetBaseException().Message; }
        return _reason;
    }
}
