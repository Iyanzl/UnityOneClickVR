using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Uuvr.OpenVR;

namespace HybridVR.MonoRuntime;

internal static class MonoCameraDiagnostics
{
    // Bind-time read only. Do not infer blur from eye resolution or disable every effect.
    internal static void Write(Camera camera)
    {
        try
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            OpenVrDiagnostics.Write($"camera capabilities: pipeline={(pipeline == null ? "BuiltIn" : pipeline.GetType().FullName)}, mask=0x{camera.cullingMask:X8}, rect={camera.rect}, hdr={camera.allowHDR}, msaa={camera.allowMSAA}, dynamic={camera.allowDynamicResolution}, qualityAA={QualitySettings.antiAliasing}");
            foreach (var component in camera.GetComponents<Component>())
            {
                if (component == null) continue;
                var type = component.GetType();
                string detail = "camera component: " + type.FullName;
                if (component is Behaviour behaviour) detail += ", enabled=" + behaviour.enabled;
                foreach (string name in new[] { "antialiasing", "antialiasingMode", "antialiasingQuality", "allowDynamicResolution" })
                {
                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (field == null && (property == null || property.GetIndexParameters().Length != 0)) continue;
                    try { detail += ", " + name + "=" + (field != null ? field.GetValue(component) : property!.GetValue(component)); }
                    catch { detail += ", " + name + "=unavailable"; }
                }
                OpenVrDiagnostics.Write(detail);
            }
        }
        catch (Exception e) { OpenVrDiagnostics.Write("camera capability diagnostic unavailable: " + e.Message); }
    }
}
