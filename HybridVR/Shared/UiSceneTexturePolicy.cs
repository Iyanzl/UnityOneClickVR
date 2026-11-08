namespace HybridVR.Shared;

internal static class UiSceneTexturePolicy
{
    // Narrow proof for a common composition pattern, not a layer/name heuristic.
    internal static bool CanExclude(bool rawImage, bool fullScreen, bool ownsMask,
        bool mainCameraTarget, bool visibleCharacter, bool uiInTarget, bool otherCameraWriter) =>
        rawImage && fullScreen && !ownsMask && mainCameraTarget && visibleCharacter && !uiInTarget && !otherCameraWriter;
}
