using System;

namespace HybridVR.Shared;

internal readonly struct EyeCameraPolicy
{
    internal readonly bool RenderSource;
    internal readonly float Near, Far;

    internal EyeCameraPolicy(bool orthographic, float near, float far) : this(orthographic, near, far, 1) { }

    internal EyeCameraPolicy(bool orthographic, float near, float far, float units)
    {
        // Signed orthographic UI clip ranges cannot be used as an HMD perspective
        // frustum. Keep those menus in the captured panel, with a valid tool frustum.
        RenderSource = !(orthographic && near <= 0);
        if (!Finite(units) || units <= 0) units = 1;
        float maximumNear = .01f * Math.Max(.0001f, Math.Min(10000, units));
        Near = RenderSource && Finite(near) && near > 0 ? Math.Min(near, maximumNear) : maximumNear;
        Far = RenderSource && Finite(far) && far > Near ? far : Math.Max(1000, Near + 1000);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
