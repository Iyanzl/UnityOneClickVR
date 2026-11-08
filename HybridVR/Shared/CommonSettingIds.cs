namespace HybridVR.Shared
{
    // Width in metres and a host's panel scale multiplier are intentionally distinct.
    public static class CommonSettingIds
    {
        public const string PanelVisible = "panel.visible", PanelWidth = "panel.width-metres", PanelScale = "panel.scale-factor";
        public const string PanelPitch = "panel.pitch", PanelYaw = "panel.yaw", PanelRoll = "panel.roll";
        public const string PanelForward = "panel.forward-metres", PanelUp = "panel.up-metres";
        public const string WorldScale = "world.scale", ScaleMaximum = "world.scale-maximum";
        public const string WalkSpeed = "movement.walk-speed", GrabSpeed = "movement.grab-speed", PitchSpeed = "movement.pitch-speed", Snap = "movement.snap-degrees";
        public const string FollowStory = "camera.follow-story", Language = "settings.language", Shortcut = "settings.shortcut";
        public const string MouseA = "input.a", MouseB = "input.b", MouseR3 = "input.r3", Xbox = "input.xbox-chord";
        public const string DesktopMirror = "rendering.desktop-mirror", OriginalCameraPass = "rendering.original-camera-pass";
        public const string SkinMatrices = "rendering.skin-matrices", SingleFrameQueue = "rendering.single-frame-queue";
        public const string ShowHands = "rendering.show-hands", DisableDepthOfField = "rendering.disable-depth-of-field";
        public const string DisableMotionBlur = "rendering.disable-motion-blur";
        public const string RenderInUpdate = "rendering.render-in-update";
        public const string VirtualKeyboard = "input.virtual-keyboard";
    }
}
