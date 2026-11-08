using System;

namespace HybridVR.Shared
{
    public enum MouseBinding { None, Left, Right, Middle }

    public sealed class InteractionBindings
    {
        public MouseBinding A = MouseBinding.Left, B = MouseBinding.Right, R3 = MouseBinding.Middle;
        public bool XboxEnabled = true, ScaleEnabled = true;
        public bool HorizonControlsEnabled;
    }

    public struct InteractionActions
    {
        public bool ToggleMenu, TogglePanel, ToggleAnchor, ToggleDefaultView, ToggleXbox;
        public bool MouseLeft, MouseRight, MouseMiddle, GrabLeft, GrabRight, Pitch;
        public bool Roll, ResetHorizon;
        public bool ScaleHeld, ResetScale, AllowMovement, SubmitXbox, Blocked;
    }

    // No Unity, rendering backend, UI process or input injection owns this state.
    public sealed class InteractionSession
    {
        private ControllerSample _last;
        private long _frame = -1;
        private bool _ready, _menuChord, _l3Cancelled, _r3Cancelled, _exclusiveRelease, _scaleConsumed;
        private double _xboxSince = -1, _lastScaleTap = double.NegativeInfinity, _r3Since;
        private bool _menuWasOpen;
        private bool _resetScaleHold;
        public bool XboxActive { get; private set; }
        public InteractionBindings Bindings { get; } = new InteractionBindings();

        public void SetXboxActive(bool enabled)
        {
            XboxActive = enabled;
            _exclusiveRelease = true;
        }

        public void Suspend()
        {
            _ready = false;
            _frame = -1;
            _xboxSince = -1;
            _lastScaleTap = double.NegativeInfinity;
            _menuChord = _l3Cancelled = _r3Cancelled = _exclusiveRelease = _scaleConsumed = false;
            _resetScaleHold = false;
        }

        public InteractionActions Update(ControllerSample s, bool menuOpen)
        {
            var a = new InteractionActions { Blocked = true };
            if (!s.Valid || s.Session != _last.Session || s.Seconds < _last.Seconds)
                Suspend();
            if (s.Frame <= _frame) return a;
            _frame = s.Frame;
            if (!s.Valid || !_ready)
            {
                _ready = s.Valid && s.Neutral;
                _last = s;
                _menuWasOpen = menuOpen;
                return a;
            }

            if (s.R3 && !_last.R3) { _r3Since = s.Seconds; _r3Cancelled = false; }
            if (s.L3 && !_last.L3) _l3Cancelled = false;
            if (menuOpen != _menuWasOpen)
            {
                _exclusiveRelease = true;
                _l3Cancelled = _r3Cancelled = true;
            }
            _menuWasOpen = menuOpen;
            bool menuChord = s.L3 && s.R3;
            if (menuChord && !_menuChord)
            {
                a.ToggleMenu = true;
                _exclusiveRelease = _l3Cancelled = _r3Cancelled = true;
                _lastScaleTap = double.NegativeInfinity;
            }
            _menuChord = menuChord || (_menuChord && (s.L3 || s.R3));

            bool xboxChord = s.LeftGrip && s.RightGrip && s.LeftTrigger && s.RightTrigger;
            if (!menuOpen && !a.ToggleMenu && !_exclusiveRelease && Bindings.XboxEnabled && xboxChord)
            {
                if (_xboxSince < 0) _xboxSince = s.Seconds;
                if (s.Seconds - _xboxSince >= .5)
                {
                    a.ToggleXbox = true;
                    _exclusiveRelease = true;
                }
                _l3Cancelled = _r3Cancelled = true;
            }
            else _xboxSince = -1;

            if (menuOpen || a.ToggleMenu || _exclusiveRelease || (xboxChord && Bindings.XboxEnabled))
            {
                if (!menuOpen && !a.ToggleMenu && s.Neutral) _exclusiveRelease = false;
                _last = s;
                return a;
            }
            if (XboxActive)
            {
                a.SubmitXbox = true;
                _last = s;
                return a;
            }

            bool scaleChord = Bindings.ScaleEnabled &&
                ((s.LeftTrigger && s.RightTrigger) || (s.LeftGrip && s.RightGrip));
            if (scaleChord && s.A && !_last.A)
            {
                a.ResetScale = s.Seconds - _lastScaleTap <= .4;
                _resetScaleHold = a.ResetScale;
                _lastScaleTap = a.ResetScale ? double.NegativeInfinity : s.Seconds;
                _scaleConsumed = true;
            }
            if (_scaleConsumed)
            {
                if (!s.A) _resetScaleHold = false;
                a.ScaleHeld = scaleChord && s.A && !_resetScaleHold;
                _l3Cancelled = _r3Cancelled = true;
                // Release in any order without leaking A, grip or trigger actions.
                if (!s.A && !s.LeftTrigger && !s.RightTrigger && !s.LeftGrip && !s.RightGrip)
                    _scaleConsumed = false;
                _last = s;
                return a;
            }

            a.Blocked = false;
            a.TogglePanel = s.X && !_last.X;
            a.ToggleAnchor = s.Y && !_last.Y;
            a.ToggleDefaultView = !s.L3 && _last.L3 && !_l3Cancelled;
            a.GrabLeft = s.LeftGrip && s.LeftTrigger;
            a.GrabRight = s.RightGrip && s.RightTrigger;
            a.Pitch = s.RightGrip && !s.RightTrigger && !a.GrabLeft;
            a.Roll = Bindings.HorizonControlsEnabled && a.Pitch;
            if (a.Roll && s.R3)
            {
                a.ResetHorizon = !s.L3 && (!_last.R3 || !_last.RightGrip);
                _r3Cancelled = true;
            }
            bool middle = s.R3 && !s.L3 && !_r3Cancelled && s.Seconds - _r3Since >= .15;
            // A short R3 click gets one press frame on release; chords never leak it.
            middle |= !s.R3 && _last.R3 && !_r3Cancelled && s.Seconds - _r3Since < .15;
            Map(ref a, Bindings.A, s.A);
            Map(ref a, Bindings.B, s.B);
            Map(ref a, Bindings.R3, middle);
            a.MouseLeft |= s.RightTrigger && !s.RightGrip;
            a.AllowMovement = true;
            _last = s;
            return a;
        }

        private static void Map(ref InteractionActions actions, MouseBinding binding, bool held)
        {
            if (!held) return;
            if (binding == MouseBinding.Left) actions.MouseLeft = true;
            if (binding == MouseBinding.Right) actions.MouseRight = true;
            if (binding == MouseBinding.Middle) actions.MouseMiddle = true;
        }
    }
}
