namespace HybridVR.Shared
{
    internal enum PointerCommand { None, Down, Up }

    internal struct PointerOwner
    {
        internal long SourceId, RigId, CameraId, SurfaceId;
        internal int HandId;
        internal int LayoutGeneration;
        internal HandPoseKind PoseKind;
        internal bool SameAs(PointerOwner other) => SourceId == other.SourceId &&
            RigId == other.RigId && CameraId == other.CameraId &&
            SurfaceId == other.SurfaceId && HandId == other.HandId && LayoutGeneration == other.LayoutGeneration &&
            PoseKind == other.PoseKind;
    }

    internal sealed class PointerButton
    {
        private bool _hasOwner, _armed, _pressed, _releasePending;
        private long _lastFrame = long.MinValue;
        private PointerOwner _owner;
        private PointerCommand _pending;
        internal bool IsDown { get; private set; }
        internal bool MustReleaseBeforeMove(PointerOwner owner) => IsDown &&
            (_releasePending || !_hasOwner || !_owner.SameAs(owner));

        // Sending an event is a transaction: only a successful acknowledgement changes OS-held state.
        internal PointerCommand Update(long frame, PointerOwner owner, bool eligible, bool pressed)
        {
            bool changed = !_hasOwner || !_owner.SameAs(owner);
            if (changed || !eligible)
            {
                Cancel();
                _hasOwner = true;
                _owner = owner;
            }
            if (frame <= _lastFrame) return PointerCommand.None;
            _lastFrame = frame;
            if (_releasePending) return Request(PointerCommand.Up);
            if (!eligible) return PointerCommand.None;
            if (!_armed)
            {
                if (!pressed) { _armed = true; _pressed = false; }
                return PointerCommand.None;
            }
            bool downEdge = pressed && !_pressed;
            _pressed = pressed;
            if (!pressed && IsDown) return Request(PointerCommand.Up);
            if (downEdge && !IsDown) return Request(PointerCommand.Down);
            return PointerCommand.None;
        }

        internal PointerCommand Cancel()
        {
            _armed = false;
            _pressed = false;
            _releasePending = IsDown;
            _pending = PointerCommand.None;
            return IsDown ? Request(PointerCommand.Up) : PointerCommand.None;
        }

        internal void Acknowledge(PointerCommand command, bool sent)
        {
            if (command == PointerCommand.None || command != _pending) return;
            _pending = PointerCommand.None;
            if (command == PointerCommand.Down)
            {
                IsDown = sent;
                if (!sent) _armed = false;
            }
            else
            {
                IsDown = !sent;
                _releasePending = !sent;
                if (!sent) _armed = false;
            }
        }

        private PointerCommand Request(PointerCommand command)
        { _pending = command; return command; }
    }
}
