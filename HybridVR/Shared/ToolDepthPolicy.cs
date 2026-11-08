using System;

namespace HybridVR.Shared;

// Scene depth and its projection stay paired. Only front-layer tools get a separate
// clip range, so a game's distant near plane cannot cut a hand-held panel in half.
internal static class ToolDepthPolicy
{
    internal static bool UsesSceneDepth(bool pinned, bool locked, bool menu) => pinned && !locked && !menu;

    internal static bool IsCurrentEyeRender(bool active, bool completed, int scopeFrame, int frame,
        bool sameCamera, bool sameEye, bool cameraTargetsEye) =>
        active && completed && scopeFrame == frame && sameCamera && sameEye && cameraTargetsEye;

    internal static bool MatricesMatch(float viewDelta, float projectionDelta) =>
        viewDelta >= 0 && viewDelta <= .0005f && projectionDelta >= 0 && projectionDelta <= .0005f;

    internal static bool CanBindEyeDepth(int scopeFrame, int frame, bool sameEye, bool selfBind,
        bool created, bool texture2D, int samples, int eyeSamples, int depthBits,
        int width, int height, int eyeWidth, int eyeHeight) =>
        CanCopyEyeDepth(scopeFrame, frame, sameEye, selfBind, created, texture2D, samples,
            width, height, eyeWidth, eyeHeight) && eyeSamples == 1 && depthBits > 0;

    internal static bool CanCopyEyeDepth(int scopeFrame, int frame, bool sameEye, bool selfCopy,
        bool created, bool texture2D, int samples, int width, int height, int eyeWidth, int eyeHeight) =>
        scopeFrame == frame && sameEye && !selfCopy && created && texture2D && samples == 1 &&
        width > 0 && height > 0 && width == eyeWidth && height == eyeHeight;

    internal static void OverlayDepth(float units, out float m22, out float m23)
    {
        if (float.IsNaN(units) || float.IsInfinity(units) || units <= 0) units = 1;
        units = Math.Max(.0001f, Math.Min(10000, units));
        float near = .005f * units, far = 1000f * units;
        // The manual renderer supplies OpenVR's API_DirectX projection. Keep its
        // asymmetric X/Y frustum and handedness; replace only D3D depth coefficients.
        m22 = -far / (far - near);
        m23 = -far * near / (far - near);
    }
}
