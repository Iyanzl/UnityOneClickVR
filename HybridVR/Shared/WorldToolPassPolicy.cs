namespace HybridVR.Shared;

internal static class WorldToolPassPolicy
{
    // Prefer a visible spare layer. Otherwise temporarily admit a completely
    // unoccupied user layer for this eye; the caller must restore the mask.
    internal static int ChooseLayer(int visible, int occupied)
    {
        uint available = (uint)(visible & ~occupied) & 0xffffff00u;
        for (int i = 31; i >= 8; i--)
            if ((available & (1u << i)) != 0) return i;
        available = (uint)~occupied & 0xffffff00u;
        for (int i = 31; i >= 8; i--)
            if ((available & (1u << i)) != 0) return i;
        return -1;
    }

    internal static bool SupportsScale(float scale) => scale >= .9999f && scale <= 1.0001f;
}
