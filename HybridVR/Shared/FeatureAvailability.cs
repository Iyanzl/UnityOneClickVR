namespace HybridVR.Shared
{
    internal static class FeatureAvailability
    {
        internal const string BuildId = "HI09-uuvr-contract-preflight";

        // Remains unsupported until a reversible, character-only scene renderer exists.
        // Runtime passthrough support or a black background alone is not sufficient.
        internal static bool CharacterOnlyPassthrough => false;
        internal const string PassthroughUnavailableReason =
            "Character-only scene isolation is not implemented; passthrough and color-key fallback are disabled.";
    }
}
