using System;

namespace HybridVR.Shared;

internal enum PanelRegion { None, Content, Move, Resize }

internal static class PanelHitRegion
{
    internal const float MarginMetres = .025f;
    internal static PanelRegion Classify(double u, double v, double width, double height, double units)
    {
        if (double.IsNaN(u) || double.IsNaN(v) || width <= 0 || height <= 0 || units <= 0) return PanelRegion.None;
        if (u >= 0 && u <= 1 && v >= 0 && v <= 1) return PanelRegion.Content;
        double x = Math.Abs(u - .5) * width, y = Math.Abs(v - .5) * height;
        double margin = MarginMetres * units;
        if (x > width * .5 + margin || y > height * .5 + margin) return PanelRegion.None;
        return x >= width * .5 && y >= height * .5 ? PanelRegion.Resize : PanelRegion.Move;
    }
}
