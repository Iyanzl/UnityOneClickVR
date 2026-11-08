namespace HybridVR.Shared;

internal enum PanelCaptureMode { FullFrame, UiOnly }

internal static class UiCapturePolicy
{
    internal static bool CanReplayWithoutStencil(int comparison, int channels) =>
        (comparison == 0 || comparison == 8) && (channels & 1) != 0 && (channels & 14) != 0;

    internal static bool Supported(string shader, int materials, bool mask, bool clipped,
        bool pop, bool worldCanvas, bool textureTarget, bool alphaTexture)
    {
        // Mask geometry, pop materials and rect clipping are validated separately.
        if (materials != 1 || worldCanvas || textureTarget || alphaTexture) return false;
        return shader == "UI/Default" || shader == "UI/Default Font" ||
            shader == "UI/AddBlend" || shader == "UI/Mulatiply" || shader == "Shader Graphs/Overray" ||
            shader == "TextMeshPro/Distance Field" || shader == "TextMeshPro/Mobile/Distance Field" ||
            shader == "TextMeshPro/Distance Field Overlay" || shader == "TextMeshPro/Mobile/Distance Field Overlay";
    }
}
