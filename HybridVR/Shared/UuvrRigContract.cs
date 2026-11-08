using System;

namespace HybridVR.Shared
{
    // Observations only: a matching hierarchy does not grant permission to write it.
    public struct UuvrRigObservation
    {
        public string Mode;
        public bool SessionEnabled, HeadDriverEnabled, SourceOverrideActive;
        public int SourceId, OwnerParentId, HeadId, HeadParentId, OriginId, OriginParentId;
        public double ScaleX, ScaleY, ScaleZ;
    }

    public enum UuvrRigStatus
    {
        SessionDisabled, UnsupportedMode, MissingSource, ParentMismatch,
        IncompleteHierarchy, MissingHeadDriver, InvalidRigScale, SourceOverridden,
        ObservedAwaitingHandoff
    }

    public static class UuvrRigContract
    {
        public static UuvrRigStatus Observe(UuvrRigObservation value)
        {
            if (!value.SessionEnabled) return UuvrRigStatus.SessionDisabled;
            if (value.Mode != "Child" && value.Mode != "RelativeTransform") return UuvrRigStatus.UnsupportedMode;
            if (value.SourceId == 0) return UuvrRigStatus.MissingSource;
            if (value.OwnerParentId != value.SourceId) return UuvrRigStatus.ParentMismatch;
            if (value.HeadId == 0 || value.OriginId == 0 || value.HeadId == value.SourceId ||
                value.OriginId == value.SourceId || value.HeadId == value.OriginId ||
                value.HeadParentId != value.OriginId || value.OriginParentId != value.SourceId)
                return UuvrRigStatus.IncompleteHierarchy;
            if (!value.HeadDriverEnabled) return UuvrRigStatus.MissingHeadDriver;
            if (!Positive(value.ScaleX) || !Positive(value.ScaleY) || !Positive(value.ScaleZ) ||
                Math.Abs(value.ScaleY / value.ScaleX - 1) > .001 ||
                Math.Abs(value.ScaleZ / value.ScaleX - 1) > .001)
                return UuvrRigStatus.InvalidRigScale;
            if (value.SourceOverrideActive) return UuvrRigStatus.SourceOverridden;
            return UuvrRigStatus.ObservedAwaitingHandoff;
        }

        private static bool Positive(double v) => v > .000001 && !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
