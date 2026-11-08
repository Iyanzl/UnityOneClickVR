using System;

namespace HybridVR.Shared
{
    internal struct PanelRect
    {
        internal double X, Y, Width, Height;
        internal PanelRect(double x, double y, double width, double height)
        { X = x; Y = y; Width = width; Height = height; }
        internal static PanelRect Unit => new PanelRect(0, 0, 1, 1);
    }

    internal static class PanelCoordinates
    {
        // Both rectangles use bottom-left origins and continuous edges, not last-pixel indices.
        // A letterboxed capture supplies its actual content UV rect, never an inferred aspect fit.
        internal static bool TryUvToScreen(double u, double v, PanelRect contentUv,
            PanelRect screenRect, bool capturedDrag, out double x, out double y)
        {
            x = y = 0;
            if (!Finite(u) || !Finite(v) || !Valid(contentUv) || !Valid(screenRect)) return false;
            if (contentUv.X < 0 || contentUv.Y < 0 || contentUv.X + contentUv.Width > 1 ||
                contentUv.Y + contentUv.Height > 1) return false;
            double nx = (u - contentUv.X) / contentUv.Width;
            double ny = (v - contentUv.Y) / contentUv.Height;
            if (!capturedDrag && (nx < 0 || nx > 1 || ny < 0 || ny > 1)) return false;
            x = screenRect.X + nx * screenRect.Width;
            y = screenRect.Y + ny * screenRect.Height;
            return Finite(x) && Finite(y);
        }

        // Convert once from Unity bottom-left pixels to Win32 top-left client pixels.
        internal static bool TryScreenToClient(double x, double y, int screenWidth, int screenHeight,
            int clientWidth, int clientHeight, out int clientX, out int clientY)
        {
            clientX = clientY = 0;
            if (!Finite(x) || !Finite(y) || screenWidth <= 0 || screenHeight <= 0 ||
                clientWidth <= 0 || clientHeight <= 0) return false;
            double nx = Math.Max(0, Math.Min(1, x / screenWidth));
            double ny = Math.Max(0, Math.Min(1, y / screenHeight));
            clientX = (int)Math.Min(clientWidth - 1, Math.Floor(nx * clientWidth));
            clientY = (int)Math.Min(clientHeight - 1, Math.Floor((1 - ny) * clientHeight));
            return true;
        }

        private static bool Valid(PanelRect r) => Finite(r.X) && Finite(r.Y) &&
            Finite(r.Width) && Finite(r.Height) && r.Width > 0 && r.Height > 0 &&
            Finite(r.X + r.Width) && Finite(r.Y + r.Height);
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }
}
