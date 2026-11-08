using System;

namespace HybridVR.Shared
{
    public struct ScaleUpdate
    {
        public bool Changed, Commit;
        public double Value;
    }

    public sealed class WorldScaleGesture
    {
        private bool _active;
        private long _generation;
        private double _distance, _scale, _last;

        public ScaleUpdate Update(long generation, bool valid, bool held, bool reset,
            double distanceMeters, double currentScale, double maximum)
        {
            maximum = Finite(maximum) ? Math.Max(1, Math.Min(100, maximum)) : 8;
            currentScale = Finite(currentScale) ? Math.Max(.1, Math.Min(maximum, currentScale)) : 1;
            var result = new ScaleUpdate { Value = currentScale };
            if (!valid || generation != _generation)
            {
                result.Commit = _active;
                _active = false;
                _generation = generation;
                // Never apply a distance delta across a tracking-origin transition.
                return result;
            }
            if (reset)
            {
                _active = false;
                return new ScaleUpdate { Value = 1, Changed = currentScale != 1, Commit = true };
            }
            if (!held || !Finite(distanceMeters) || distanceMeters < .05)
            {
                result.Commit = _active;
                _active = false;
                return result;
            }
            if (!_active)
            {
                _active = true;
                _distance = distanceMeters;
                _scale = _last = currentScale;
            }
            result.Value = Math.Max(.1, Math.Min(maximum, _scale * distanceMeters / _distance));
            result.Changed = Math.Abs(result.Value - _last) > .000001;
            _last = result.Value;
            return result;
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
