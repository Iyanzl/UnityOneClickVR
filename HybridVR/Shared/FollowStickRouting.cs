using System;

namespace HybridVR.Shared
{
    // Owns only the left analog axes. No mouse buttons, triggers or VR chords
    // may leak into the game's character controls through this route.
    public sealed class FollowStickRouting
    {
        private bool _ready;
        private int _target;
        private FollowViewMode _mode;
        private long _session, _frame = -1;
        private double _seconds;

        public void Reset() { _ready = false; _frame = -1; }

        public static bool CanControl(bool enabled, bool active, bool locked, bool menu, bool xbox,
            ControllerSample sample, InteractionActions actions) => enabled && active && sample.Valid &&
            !locked && !menu && !xbox && !actions.Blocked && !actions.GrabLeft && !actions.GrabRight &&
            !actions.Pitch && !actions.ScaleHeld && !actions.ResetScale && !sample.L3 && !sample.R3;

        public static ControllerSample CameraSample(ControllerSample sample, bool characterOwned)
        {
            if (characterOwned) sample.LeftStickX = sample.LeftStickY = 0;
            return sample;
        }

        public ControllerSample Update(ControllerSample sample, FollowViewMode mode, int target, bool allowed)
        {
            bool changed = _target != target || _mode != mode || _session != sample.Session || sample.Seconds < _seconds;
            if (!allowed || !sample.Valid || mode == FollowViewMode.GameCamera || changed) Reset();
            _target = target; _mode = mode; _session = sample.Session; _seconds = sample.Seconds;
            if (!allowed || !sample.Valid || mode == FollowViewMode.GameCamera || sample.Frame <= _frame) return default;
            _frame = sample.Frame;
            double length = Math.Sqrt(sample.LeftStickX * sample.LeftStickX + sample.LeftStickY * sample.LeftStickY);
            if (double.IsNaN(length) || double.IsInfinity(length)) { Reset(); return default; }
            if (!_ready)
            {
                _ready = length < .18;
                return default;
            }
            return new ControllerSample { Valid = true, Frame = sample.Frame, Session = sample.Session, Seconds = sample.Seconds,
                LeftStickX = length < .18 ? 0 : Math.Max(-1, Math.Min(1, sample.LeftStickX)),
                LeftStickY = length < .18 ? 0 : Math.Max(-1, Math.Min(1, sample.LeftStickY)) };
        }
    }
}
