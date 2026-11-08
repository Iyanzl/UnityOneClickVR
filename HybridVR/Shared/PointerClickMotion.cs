using System;

namespace HybridVR.Shared
{
    internal struct PointerMotionPoint
    {
        internal double X, Y, Travel;
        internal bool Protected, Dragging;
    }

    // Not a hover smoother: keep a click still until intentional drag travel is reached.
    // Once dragging, never snap back, even when the pointer returns near the press point.
    internal sealed class PointerClickMotion
    {
        private PointerOwner _owner;
        private bool _known, _held, _dragging;
        private int _width, _height;
        private double _x, _y;
        internal void Reset() { _known = _held = _dragging = false; }

        internal PointerMotionPoint Update(PointerOwner owner, double x, double y,
            int width, int height, bool held, double radiusAt1080)
        {
            var result = new PointerMotionPoint { X = x, Y = y };
            if (!Finite(x) || !Finite(y) || width <= 0 || height <= 0) { Reset(); return result; }
            if (!_known || !_owner.SameAs(owner) || width != _width || height != _height) Reset();
            _known = true; _owner = owner; _width = width; _height = height;
            if (held && !_held) { _x = x; _y = y; _dragging = false; }
            if (held || _held)
            {
                result.Travel = Math.Sqrt((x-_x)*(x-_x)+(y-_y)*(y-_y));
                double radius = Finite(radiusAt1080) ? Math.Max(0, Math.Min(40, radiusAt1080)) * height / 1080d : 0;
                if (radius == 0 || result.Travel > radius) _dragging = true;
                result.Dragging = _dragging;
                result.Protected = !_dragging;
                if (!_dragging) { result.X = _x; result.Y = _y; }
            }
            // Protect the release position as well; resume unsmoothed hover next frame.
            _held = held;
            return result;
        }
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
