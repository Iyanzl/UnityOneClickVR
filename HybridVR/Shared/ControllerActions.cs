namespace HybridVR.Shared
{
    public struct ControllerSample
    {
        public long Frame, Session;
        public double Seconds;
        public double LeftStickX, LeftStickY, RightStickX, RightStickY;
        public bool Valid, X, Y, A, B, L3, R3, LeftTrigger, RightTrigger, LeftGrip, RightGrip;
        public bool SticksNeutral => System.Math.Abs(LeftStickX) < .18 && System.Math.Abs(LeftStickY) < .18 &&
            System.Math.Abs(RightStickX) < .18 && System.Math.Abs(RightStickY) < .18;
        public bool Neutral => SticksNeutral && !(X || Y || A || B || L3 || R3 || LeftTrigger || RightTrigger || LeftGrip || RightGrip);
    }

    public struct ControllerActions
    {
        public bool ToggleMenu, TogglePanel, ToggleAnchor, ToggleFollow;
        public bool MouseLeft, MouseRight, MouseMiddle, GrabLeft, GrabRight;
        public bool AllowMovement;
    }

    // The adapter supplies physical controls only. No game names or runtime APIs belong here.
    public sealed class ControllerActionRouter
    {
        private ControllerSample _last;
        private bool _ready, _chordConsumed, _movementReady;
        private double _r3Since;
        private long _lastFrame = -1;
        public void Suspend() { _ready = false; _movementReady = false; _chordConsumed = false; _lastFrame = -1; }

        public ControllerActions Update(ControllerSample sample, bool menuOpen)
        {
            var actions = new ControllerActions();
            if (!sample.Valid || sample.Session != _last.Session)
            {
                _ready = false;
                _movementReady = false;
                _chordConsumed = false;
                _lastFrame = -1;
            }
            if (!sample.Valid || !_ready)
            {
                _ready = sample.Valid && sample.Neutral;
                _last = sample;
                _lastFrame = sample.Frame;
                return actions;
            }
            if (sample.Frame <= _lastFrame) return actions;
            _lastFrame = sample.Frame;
            if (sample.R3 && !_last.R3) _r3Since = sample.Seconds;
            if (sample.L3 && sample.R3 && !_chordConsumed)
            {
                actions.ToggleMenu = true;
                _chordConsumed = true;
            }
            if (!menuOpen && !actions.ToggleMenu)
            {
                actions.TogglePanel = sample.X && !_last.X;
                actions.ToggleAnchor = sample.Y && !_last.Y;
                // Defer L3 until release so opening settings never also changes camera-follow mode.
                actions.ToggleFollow = !sample.L3 && _last.L3 && !_chordConsumed;
                actions.MouseLeft = sample.A || (sample.RightTrigger && !sample.RightGrip);
                actions.MouseRight = sample.B;
                actions.MouseMiddle = sample.R3 && !sample.L3 && !_chordConsumed && sample.Seconds - _r3Since >= .15;
                actions.GrabLeft = sample.LeftGrip && sample.LeftTrigger;
                actions.GrabRight = sample.RightGrip && sample.RightTrigger;
            }
            if (menuOpen || actions.ToggleMenu) _movementReady = false;
            else if (sample.SticksNeutral && !(sample.LeftGrip || sample.RightGrip || sample.LeftTrigger || sample.RightTrigger)) _movementReady = true;
            actions.AllowMovement = _movementReady && !menuOpen && !actions.ToggleMenu;
            if (!sample.L3 && !sample.R3) _chordConsumed = false;
            _last = sample;
            return actions;
        }
    }

    public sealed class MenuPress
    {
        private int _pressed = -1;
        private bool _held = true;
        public void Cancel() { _pressed = -1; _held = true; }
        public int Update(bool allowed, bool held, int hovered)
        {
            if (!allowed) { Cancel(); return -1; }
            int result = -1;
            if (held && !_held) _pressed = hovered;
            if (!held && _held)
            {
                if (_pressed >= 0 && _pressed == hovered) result = hovered;
                _pressed = -1;
            }
            _held = held;
            return result;
        }
    }
}
