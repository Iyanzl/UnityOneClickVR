namespace HybridVR.Shared
{
    internal static class PanelInteractionPolicy
    {
        internal static bool CanManipulate(bool pinned, bool locked, bool menu, bool blocked, bool pressed,
            PanelRegion region, bool mouseHolding, bool alreadyManipulating)
            => (region == PanelRegion.Resize || (pinned && region == PanelRegion.Move)) &&
               !locked && !menu && !blocked && pressed && !mouseHolding && !alreadyManipulating;
    }
}
