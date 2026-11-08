using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal static class RenderPipelineIdentity
{
    private static readonly PropertyInfo? Current = typeof(GraphicsSettings).GetProperty("currentRenderPipeline");
    private static readonly PropertyInfo? Quality = typeof(QualitySettings).GetProperty("renderPipeline");
    private static readonly PropertyInfo? Default = typeof(GraphicsSettings).GetProperty("defaultRenderPipeline") ??
        typeof(GraphicsSettings).GetProperty("renderPipelineAsset");

    internal static string Read()
    {
        foreach (var property in new[] { Current, Quality, Default })
        {
            var value = property?.GetValue(null);
            if (value == null) continue;
            // IL2CPP CLR wrappers can report only their declared base type. Read
            // the native object's type, not System.Object.ToString().
#if HYBRID_MONO
            return value.GetType().FullName ?? value.GetType().Name;
#else
            if (value is UnityEngine.Object native)
                return native.GetIl2CppType().FullName ?? "unknown-native-pipeline";
            return value.GetType().FullName ?? "unknown-pipeline";
#endif
        }
        return "BuiltIn";
    }
}
