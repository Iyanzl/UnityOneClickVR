using System;

namespace HybridVR.Shared
{
    internal readonly struct PanelCaptureLayout
    {
        internal readonly int ScreenWidth, ScreenHeight, TextureWidth, TextureHeight;
        internal readonly PanelRect ScreenRect, TextureUv;
        internal readonly bool FlipTextureY, IsValid;
        internal readonly double PresentationAspect;

        private PanelCaptureLayout(int sw, int sh, int tw, int th, PanelRect screen, PanelRect uv, bool flipY, double aspect)
        {
            ScreenWidth = sw; ScreenHeight = sh; TextureWidth = tw; TextureHeight = th;
            ScreenRect = screen; TextureUv = uv; FlipTextureY = flipY; IsValid = true;
            PresentationAspect = aspect;
        }

        internal static bool TryCreate(int sw, int sh, int tw, int th, PanelRect screen,
            PanelRect textureUv, bool flipY, out PanelCaptureLayout layout, double presentationAspect = 0)
        {
            layout = default;
            if (sw <= 0 || sh <= 0 || tw <= 0 || th <= 0 || !Inside(screen, sw, sh) ||
                !Inside(textureUv, 1, 1)) return false;
            if (presentationAspect == 0) presentationAspect = screen.Width / screen.Height;
            if (!Finite(presentationAspect) || presentationAspect <= 0) return false;
            layout = new PanelCaptureLayout(sw, sh, tw, th, screen, textureUv, flipY, presentationAspect);
            return true;
        }

        internal bool TryMap(double u, double v, bool dragging, out double x, out double y)
        {
            x = y = 0;
            // The material corrects sampling orientation; screen input is always logical bottom-left.
            return IsValid && PanelCoordinates.TryUvToScreen(u, v, PanelRect.Unit, ScreenRect, dragging, out x, out y);
        }

        internal bool TryPlaneSize(double fullScreenWidth, out double width, out double height)
        {
            width = height = 0;
            if (!IsValid || !Finite(fullScreenWidth) || fullScreenWidth <= 0) return false;
            width = fullScreenWidth * ScreenRect.Width / ScreenWidth;
            height = width / PresentationAspect;
            return Finite(width) && Finite(height) && width > 0 && height > 0;
        }

        internal void TextureTransform(out double sx, out double sy, out double ox, out double oy)
        {
            sx = TextureUv.Width; ox = TextureUv.X;
            sy = FlipTextureY ? -TextureUv.Height : TextureUv.Height;
            oy = FlipTextureY ? TextureUv.Y + TextureUv.Height : TextureUv.Y;
        }

        internal bool SameAs(PanelCaptureLayout other) => IsValid == other.IsValid &&
            ScreenWidth == other.ScreenWidth && ScreenHeight == other.ScreenHeight &&
            TextureWidth == other.TextureWidth && TextureHeight == other.TextureHeight &&
            SameRect(ScreenRect, other.ScreenRect) && SameRect(TextureUv, other.TextureUv) &&
            FlipTextureY == other.FlipTextureY && PresentationAspect == other.PresentationAspect;

        internal static bool TryBoundedTextureSize(int width, int height, int maxDimension, out int tw, out int th)
        {
            tw = th = 0;
            if (width <= 0 || height <= 0 || maxDimension <= 0) return false;
            double scale = Math.Min(1d, (double)maxDimension / Math.Max(width, height));
            tw = Math.Max(1, (int)Math.Round(width * scale));
            th = Math.Max(1, (int)Math.Round(height * scale));
            return true;
        }

        private static bool Inside(PanelRect r, double w, double h) =>
            Finite(r.X) && Finite(r.Y) && Finite(r.Width) && Finite(r.Height) &&
            r.X >= 0 && r.Y >= 0 && r.Width > 0 && r.Height > 0 &&
            r.X + r.Width <= w && r.Y + r.Height <= h;
        private static bool SameRect(PanelRect a, PanelRect b) =>
            a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }

    internal sealed class PanelCaptureState
    {
        internal PanelCaptureLayout Layout { get; private set; }
        internal bool HasLayout { get; private set; }
        internal int Generation { get; private set; }
        private long _producer, _target;

        internal bool Publish(PanelCaptureLayout layout, long producer, long target)
        {
            if (!layout.IsValid || target == 0) { Invalidate(); return false; }
            bool changed = !HasLayout || !_target.Equals(target) || !_producer.Equals(producer) || !Layout.SameAs(layout);
            if (changed) Generation++;
            Layout = layout; _producer = producer; _target = target; HasLayout = true;
            return changed;
        }

        internal bool TryGet(int sw, int sh, long target, out PanelCaptureLayout layout)
        {
            layout = default;
            if (!HasLayout || target != _target || sw != Layout.ScreenWidth || sh != Layout.ScreenHeight) return false;
            layout = Layout;
            return true;
        }

        internal void Invalidate() { HasLayout = false; Layout = default; }
    }
}
