#if HYBRID_MONO
namespace HybridVR.Adapters.Mono;

internal static class FrameworkCompatibility
{
    internal static int Clamp(int v, int min, int max) => System.Math.Max(min, System.Math.Min(max, v));
    internal static float Clamp(float v, float min, float max) => System.Math.Max(min, System.Math.Min(max, v));
    internal static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
#endif
