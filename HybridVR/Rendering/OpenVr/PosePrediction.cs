using System;

namespace Uuvr.OpenVR;

internal static class PosePrediction
{
    public static float SecondsToPhotons(bool timingAvailable, float frequency,
        float sinceVsync, float vsyncToPhotons)
    {
        // Missing or stale driver timing must not extrapolate arbitrarily far.
#if HYBRID_MONO
        if (!timingAvailable || !HybridVR.Adapters.Mono.FrameworkCompatibility.IsFinite(frequency) || frequency < 20f ||
            !HybridVR.Adapters.Mono.FrameworkCompatibility.IsFinite(sinceVsync) || sinceVsync < 0f || sinceVsync > 0.1f ||
            !HybridVR.Adapters.Mono.FrameworkCompatibility.IsFinite(vsyncToPhotons) || vsyncToPhotons < 0f)
            return 0f;
        return HybridVR.Adapters.Mono.FrameworkCompatibility.Clamp(1f / frequency - sinceVsync + vsyncToPhotons, 0f, 0.05f);
#else
        if (!timingAvailable || !float.IsFinite(frequency) || frequency < 20f ||
            !float.IsFinite(sinceVsync) || sinceVsync < 0f || sinceVsync > 0.1f ||
            !float.IsFinite(vsyncToPhotons) || vsyncToPhotons < 0f)
            return 0f;
        return Math.Clamp(1f / frequency - sinceVsync + vsyncToPhotons, 0f, 0.05f);
#endif
    }
}
