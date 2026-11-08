using System;

namespace HybridVR.Shared
{
    internal enum MoveStatus
    {
        Idle, Moving, DuplicateSample, Blocked, AwaitingNeutral, InvalidSample, HeadingUnavailable
    }

    // All directions and deltas are in world space, with Unity's +Y as ground normal.
    // The adapter supplies the tracked HMD forward, never the untracked game camera.
    internal struct MoveSample
    {
        internal long FrameId;
        internal int TrackingGeneration;
        internal bool Allowed;
        internal bool HasHeadOrientation;
        internal float HeadForwardX, HeadForwardY, HeadForwardZ;
        internal float StickX, StickY;
        internal float DeltaSeconds, Speed, Deadzone;
    }

    internal struct MoveResult
    {
        internal float WorldX, WorldZ;
        internal MoveStatus Status;
        internal MoveResult(MoveStatus status, float x = 0, float z = 0)
        {
            Status = status;
            WorldX = x;
            WorldZ = z;
        }
    }

    internal sealed class HorizontalLocomotion
    {
        private bool _initialized;
        private int _generation;
        private long _lastFrame;
        private bool _awaitNeutral = true;
        private bool _hasHeading;
        private double _forwardX, _forwardZ;

        internal void Suspend()
        {
            _awaitNeutral = true;
            _hasHeading = false;
        }

        internal MoveResult Update(MoveSample sample)
        {
            if (!_initialized || _generation != sample.TrackingGeneration)
            {
                _initialized = true;
                _generation = sample.TrackingGeneration;
                _lastFrame = long.MinValue;
                Suspend();
            }
            // Even a second callback in this frame can revoke permission to move.
            if (!sample.Allowed)
            {
                Suspend();
                _lastFrame = Math.Max(_lastFrame, sample.FrameId);
                return new MoveResult(MoveStatus.Blocked);
            }
            if (sample.FrameId <= _lastFrame) return new MoveResult(MoveStatus.DuplicateSample);
            _lastFrame = sample.FrameId;

            if (!sample.HasHeadOrientation || !Finite(sample.HeadForwardX) ||
                !Finite(sample.HeadForwardY) || !Finite(sample.HeadForwardZ) ||
                !Finite(sample.StickX) || !Finite(sample.StickY) ||
                !Finite(sample.Speed) || sample.Speed < 0 || sample.Speed > 20 ||
                !Finite(sample.Deadzone) || sample.Deadzone < 0 || sample.Deadzone >= 1 ||
                !Finite(sample.DeltaSeconds) || sample.DeltaSeconds < 0 || sample.DeltaSeconds > 0.25f)
            {
                Suspend();
                return new MoveResult(MoveStatus.InvalidSample);
            }

            double hx = sample.HeadForwardX, hy = sample.HeadForwardY, hz = sample.HeadForwardZ;
            double horizontalSquared = hx * hx + hz * hz;
            double lengthSquared = horizontalSquared + hy * hy;
            if (lengthSquared < 1e-12)
            {
                Suspend();
                return new MoveResult(MoveStatus.HeadingUnavailable);
            }
            // Looking almost straight up/down retains the last trustworthy heading.
            // A scene/recenter change clears it instead of inventing world-forward.
            if (horizontalSquared / lengthSquared >= 0.0025)
            {
                double inverse = 1 / Math.Sqrt(horizontalSquared);
                _forwardX = hx * inverse;
                _forwardZ = hz * inverse;
                _hasHeading = true;
            }

            double x = sample.StickX, y = sample.StickY;
            double magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= sample.Deadzone)
            {
                _awaitNeutral = false;
                return new MoveResult(MoveStatus.Idle);
            }
            if (_awaitNeutral) return new MoveResult(MoveStatus.AwaitingNeutral);
            if (!_hasHeading) return new MoveResult(MoveStatus.HeadingUnavailable);
            double gain = sample.Speed * sample.DeltaSeconds / Math.Max(1, magnitude);
            return new MoveResult(MoveStatus.Moving,
                (float)((_forwardX * y + _forwardZ * x) * gain),
                (float)((_forwardZ * y - _forwardX * x) * gain));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
