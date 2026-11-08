using System;

namespace HybridVR.Shared;

// A gesture owns the complete press, even after the ray leaves the keyboard.
internal sealed class KeyboardResizeGesture
{
    internal bool Active { get; private set; }
    internal float Scale { get; private set; } = 1;
    private bool _armed;
    private float _x, _y, _startScale;
    internal const float PanelEdgeClearance = PanelHitRegion.MarginMetres + .01f;
    internal static void CentreFromBottomLeft(float width, float height, out float x, out float y)
    { x = width * .5f; y = height * .5f; }
    internal static float Clamp(float value) => float.IsNaN(value) || float.IsInfinity(value)
        ? 1 : Math.Max(.5f, Math.Min(2.5f, value));
    internal static bool Edge(float u, float v) =>
        u >= 0 && u <= 1 && v >= 0 && v <= 1 &&
        (u < .008f || u > .992f || v < .028f || v > .972f) &&
        VirtualKeyboardLayout.Hit(u, v) < 0;

    internal bool Update(bool allowed, bool held, bool edge, bool pointValid,
        float x, float y, float configuredScale, out bool committed)
    {
        committed = false;
        if (!allowed) { Reset(); return false; }
        if (!held)
        {
            bool owned = Active;
            committed = Active;
            Active = false; _armed = true;
            return owned;
        }
        bool press = _armed;
        _armed = false;
        bool finitePoint = pointValid && !float.IsNaN(x) && !float.IsNaN(y) &&
            !float.IsInfinity(x) && !float.IsInfinity(y);
        if (!Active && press && edge && finitePoint && x*x + y*y > .000001f)
        {
            Active = true; _x = x; _y = y;
            Scale = _startScale = Clamp(configuredScale);
        }
        if (Active && finitePoint)
            Scale = Clamp(_startScale * (x*_x + y*_y) / (_x*_x + _y*_y));
        return Active;
    }
    internal void Reset() { Active = false; _armed = false; }
}
