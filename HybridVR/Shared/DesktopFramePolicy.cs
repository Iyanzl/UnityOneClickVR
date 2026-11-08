namespace HybridVR.Shared
{
    internal static class DesktopFramePolicy
    {
        internal static bool Hide(bool mirrorEnabled, bool vrActive)
            => !mirrorEnabled && vrActive;
        // Hiding the output does not imply permission to skip game camera callbacks.
        internal static bool SkipSourcePass(bool hide, bool captureDue, bool preserveOriginalPass)
            => hide && !captureDue && !preserveOriginalPass;
    }
}
