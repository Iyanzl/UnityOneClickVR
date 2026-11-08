namespace HybridVR.Shared
{
    internal enum TrackingProvider { Unknown, UnityXr, SharedBridge, NativeOpenXr }
    internal enum OrientationSource { Unknown, DesktopCamera, TrackedOrigin, TrackedHeadAnchor }
    internal enum TrackingStatus { Ready, MissingTracking, UntrustedOrientation, StaleSample, InvalidOrientation }

    // Identity is separate from pose: ordinary head/camera motion must not reset input.
    internal struct TrackingSample
    {
        internal TrackingProvider Provider;
        internal OrientationSource Source;
        internal long CurrentFrame, PoseFrame;
        internal long GameCameraId, OriginId, SessionId, OriginEpoch;
        internal bool HasOrientation;
        internal float ForwardX, ForwardY, ForwardZ;
    }

    internal struct TrackingEvaluation
    {
        internal TrackingStatus Status;
        internal int Generation;
        internal bool IsReady => Status == TrackingStatus.Ready;
    }

    internal sealed class TrackingContext
    {
        private bool _ready;
        private int _generation;
        private TrackingSample _identity;

        internal void Invalidate() => _ready = false;

        internal static bool IsUnitQuaternion(float x, float y, float z, float w)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(w)) return false;
            double length = (double)x * x + (double)y * y + (double)z * z + (double)w * w;
            return length >= 0.999 && length <= 1.001;
        }

        internal TrackingEvaluation Update(TrackingSample sample)
        {
            TrackingStatus status = Validate(sample);
            if (status != TrackingStatus.Ready)
            {
                Invalidate();
                return new TrackingEvaluation { Status = status, Generation = _generation };
            }
            if (!_ready || !SameIdentity(sample, _identity))
            {
                unchecked { _generation++; }
                _identity = sample;
            }
            _ready = true;
            return new TrackingEvaluation { Status = status, Generation = _generation };
        }

        private static TrackingStatus Validate(TrackingSample s)
        {
            bool knownProvider = s.Provider == TrackingProvider.UnityXr || s.Provider == TrackingProvider.SharedBridge || s.Provider == TrackingProvider.NativeOpenXr;
            if (!s.HasOrientation || !knownProvider)
                return TrackingStatus.MissingTracking;
            if (s.Source != OrientationSource.TrackedOrigin && s.Source != OrientationSource.TrackedHeadAnchor)
                return TrackingStatus.UntrustedOrientation;
            if (s.OriginId == 0 || s.SessionId == 0) return TrackingStatus.MissingTracking;
            if (s.PoseFrame != s.CurrentFrame) return TrackingStatus.StaleSample;
            if (!Finite(s.ForwardX) || !Finite(s.ForwardY) || !Finite(s.ForwardZ)) return TrackingStatus.InvalidOrientation;
            double x = s.ForwardX, y = s.ForwardY, z = s.ForwardZ;
            double length = x * x + y * y + z * z;
            return length >= 0.999 && length <= 1.001 ? TrackingStatus.Ready : TrackingStatus.InvalidOrientation;
        }

        private static bool SameIdentity(TrackingSample a, TrackingSample b) =>
            a.Provider == b.Provider && a.Source == b.Source && a.GameCameraId == b.GameCameraId &&
            a.OriginId == b.OriginId && a.SessionId == b.SessionId && a.OriginEpoch == b.OriginEpoch;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
