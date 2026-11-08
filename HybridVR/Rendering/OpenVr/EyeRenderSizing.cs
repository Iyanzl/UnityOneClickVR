using System;

namespace Uuvr.OpenVR;

internal static class EyeRenderSizing
{
    public static float Normalize(float scale) => float.IsNaN(scale) || float.IsInfinity(scale)
#if HYBRID_MONO
        ? 1f : HybridVR.Adapters.Mono.FrameworkCompatibility.Clamp(scale, 0.5f, 1f);
#else
        ? 1f : Math.Clamp(scale, 0.5f, 1f);
#endif

    public static void Apply(ref uint width, ref uint height, float scale)
    {
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid recommended eye size");
        scale = Normalize(scale);
        width = Math.Max(1u, (uint)Math.Round(width * (double)scale, MidpointRounding.AwayFromZero));
        height = Math.Max(1u, (uint)Math.Round(height * (double)scale, MidpointRounding.AwayFromZero));
    }
}
