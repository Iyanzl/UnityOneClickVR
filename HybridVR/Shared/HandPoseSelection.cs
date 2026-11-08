namespace HybridVR.Shared
{
    internal enum HandPoseKind { Unavailable, Grip, Aim }

    // Position and orientation are one sample; never assemble them from different pose kinds.
    internal readonly struct HandPose
    {
        internal readonly float X, Y, Z, Qx, Qy, Qz, Qw;
        internal readonly bool IsValid;

        internal HandPose(bool available, float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            X = x; Y = y; Z = z; Qx = qx; Qy = qy; Qz = qz; Qw = qw;
            IsValid = available && Finite(x) && Finite(y) && Finite(z) &&
                TrackingContext.IsUnitQuaternion(qx, qy, qz, qw);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal readonly struct HandPoseSelection
    {
        internal readonly HandPose Panel, Pointer;
        internal readonly HandPoseKind PanelKind, PointerKind;

        internal HandPoseSelection(HandPose grip, HandPose aim)
        {
            PanelKind = grip.IsValid ? HandPoseKind.Grip : aim.IsValid ? HandPoseKind.Aim : HandPoseKind.Unavailable;
            PointerKind = aim.IsValid ? HandPoseKind.Aim : grip.IsValid ? HandPoseKind.Grip : HandPoseKind.Unavailable;
            Panel = PanelKind == HandPoseKind.Grip ? grip : PanelKind == HandPoseKind.Aim ? aim : default;
            Pointer = PointerKind == HandPoseKind.Aim ? aim : PointerKind == HandPoseKind.Grip ? grip : default;
        }
    }

    internal sealed class PanelFollowPose
    {
        private HandPose _last;
        internal void Reset() => _last = default;

        // Retain a world-space pose during tracking loss, not a head-relative fallback.
        internal bool Update(HandPose current, out HandPose held)
        {
            if (current.IsValid) _last = current;
            held = _last;
            return held.IsValid;
        }
    }
}
