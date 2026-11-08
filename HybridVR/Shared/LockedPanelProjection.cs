using System;

namespace HybridVR.Shared
{
    internal struct ProjectedPanel
    {
        internal double X, Y, Width, Height;
    }

    internal static class LockedPanelProjection
    {
        // Match the original camera's viewport inside the full captured framebuffer.
        // Unity camera space looks down -Z; the panel uses forward +Z.
        internal static bool TryFit(double distance, double m00, double m11, double m02, double m12,
            double rectX, double rectY, double rectW, double rectH, out ProjectedPanel panel)
        {
            panel = default;
            foreach (double v in new[] { distance, m00, m11, m02, m12, rectX, rectY, rectW, rectH })
                if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            if (distance <= 0 || m00 <= .0001 || m11 <= .0001 || rectW <= .0001 || rectH <= .0001) return false;
            double width = 2 * distance / m00, height = 2 * distance / m11;
            panel.Width = width / rectW; panel.Height = height / rectH;
            panel.X = distance * m02 / m00 + panel.Width * (.5 - rectX - rectW * .5);
            panel.Y = distance * m12 / m11 + panel.Height * (.5 - rectY - rectH * .5);
            return panel.Width < distance * 100 && panel.Height < distance * 100;
        }
    }
}
