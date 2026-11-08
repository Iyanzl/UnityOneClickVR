namespace HybridVR.Shared
{
    // Shared navigation only. Backends retain their own units and capabilities.
    internal static class SettingsSections
    {
        internal static readonly string[] Keys = { "basic", "camera", "panel", "input", "advanced" };
        internal static readonly string[] Chinese = { "常用", "视角", "面板", "操作", "高级" };
        internal static readonly string[] English = { "Main", "View", "UI", "Keys", "More" };
    }
}
