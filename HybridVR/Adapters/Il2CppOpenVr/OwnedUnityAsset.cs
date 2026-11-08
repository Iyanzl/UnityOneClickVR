using UnityEngine;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal static class OwnedUnityAsset
{
    // IL2CPP's scene asset sweep does not see the managed host's private references.
    // Only apply this to assets we create and explicitly destroy, never game assets.
    internal static T Retain<T>(T asset) where T : Object
    {
        asset.hideFlags = HideFlags.HideAndDontSave;
        return asset;
    }
}
