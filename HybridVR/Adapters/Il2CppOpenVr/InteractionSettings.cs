using System;
using BepInEx.Configuration;
using HybridVR.Shared;
using UnityEngine;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed class InteractionSettings
{
    internal readonly ConfigFile File;
    internal readonly ConfigEntry<bool> Enabled, Panel, FollowStory, Xbox, DisableDepthOfField, DisableMotionBlur, DesktopMirror, ShowHands;
    internal readonly ConfigEntry<bool> SingleFrameQueue;
    internal readonly ConfigEntry<bool> SkinnedMatrices;
    internal readonly ConfigEntry<bool> RenderInUpdate;
    internal readonly ConfigEntry<bool> VirtualKeyboard;
    internal readonly ConfigEntry<float> KeyboardScale;
    internal readonly ConfigEntry<PanelCaptureMode> CaptureMode;
    internal readonly ConfigEntry<bool> PreserveOriginalCameraPass;
    private bool _launchSingleFrameQueue;
    internal bool SingleFrameQueueEnabled => _launchSingleFrameQueue || SingleFrameQueue.Value;
    internal void SetSingleFrameQueue(bool enabled)
    {
        _launchSingleFrameQueue = false;
        SingleFrameQueue.Value = enabled;
    }
    internal readonly ConfigEntry<float> Width, Forward, Up, Pitch, Yaw, Roll, PointerPitch, WalkSpeed, GrabSpeed, PitchSpeed, Snap, Scale, ScaleMaximum;
    internal readonly ConfigEntry<int> CaptureHz;
    internal readonly ConfigEntry<MouseBinding> A, B;
    internal readonly ConfigEntry<KeyCode> Shortcut;
    internal readonly ConfigEntry<string> Language;
    internal readonly ConfigEntry<FollowViewMode> View;
    internal readonly ConfigEntry<string> TargetPath;
    internal readonly ConfigEntry<bool> FollowHeading, FirstFollowHeading, CharacterStick;
    internal readonly ConfigEntry<float> FirstUp, FirstForward, FirstSide, VerticalLag, HorizontalLag, YawLag, ThirdDistance, ThirdHeight, ThirdShoulder, ThirdLag;
    internal InteractionSettings(string path, string[]? launchArguments = null)
    {
        File = new ConfigFile(path, true);
        Enabled = File.Bind("General", "Interaction enabled", true);
        Shortcut = File.Bind("General", "Settings key", KeyCode.F10);
        Language = File.Bind("General", "Language", "en", "Interface language: en (English), zh-CN (Chinese), ja (Japanese). Existing preferences are preserved.");
        VirtualKeyboard = File.Bind("Input", "Show virtual keyboard", false,
            "Eye-only 104-key keyboard. L3 locked: below the panel, tilted toward the player. Otherwise: lower right of the panel. A or right trigger selects keys. Ctrl/Alt/Shift/Win are one-shot modifiers; long press repeats ordinary keys. No mouse click is sent through the keyboard. Hiding it or losing focus releases injected keys.");
        SingleFrameQueue = File.Bind("Rendering", "Single frame queue", false,
            "Limit the graphics driver queue to one frame during VR (D3D11/D3D12). Live and autosaved. May reduce input latency but may lower frame rate. Off keeps the existing -1 queue behavior. Does not change resolution, headset refresh rate or reprojection. The legacy launch flag is session-only until this setting is changed.");
        _launchSingleFrameQueue = Array.Exists(launchArguments ?? Environment.GetCommandLineArgs(), value => value == FrameQueuePolicy.Argument);
        KeyboardScale = Number("Input", "Keyboard scale", 1, .5f, 2.5f);
        ShowHands = File.Bind("Rendering", "Show hands", true, "Draw grip-tracked SiH glove meshes in VR only. Finger curl approximates grip and trigger values. No physics or optical hand tracking.");
        DesktopMirror = File.Bind("Rendering", "Desktop mirror", true,
            "Show the desktop game view, including Virtual Desktop desktop mode. Off stays black while VR is active, regardless of mouse activity or focus. Live panel capture still requires desktop rendering. Leaving VR restores the desktop.");
        PreserveOriginalCameraPass = File.Bind("Rendering", "Preserve original camera pass", false,
            "Keep the original game camera enabled on every frame even when the desktop is hidden and no panel capture is due. Preserves normal render callbacks used by some games for gaze, animation or effects. May cost an extra rendering pass; does not guarantee identical animation between VR and desktop. Off permits the previous source-pass skipping optimization only when the desktop is hidden and no capture is due. Live; independent of Desktop mirror.");
        DisableDepthOfField = File.Bind("Rendering", "Disable depth of field", true,
            "Bypass standard PPv2/URP/HDRP depth of field while this VR interaction session is active. Applies to captured desktop and eyes; other post effects and game profiles remain unchanged.");
        DisableMotionBlur = File.Bind("Rendering", "Disable motion blur", true,
            "Bypass standard PPv2/URP/HDRP motion blur during the manual VR session, including the captured desktop and both eyes. Avoids false motion from reusing one camera for multiple viewpoints. Live; turning this off or leaving VR restores normal effect evaluation. Does not change game Volume assets, other post effects, resolution or skin matrices.");
        RenderInUpdate = File.Bind("Rendering", "Render in Update", false,
            "Experimental scheduling compatibility. Off keeps LateUpdate. On moves the complete pose/input/stereo-render loop into Update, without changing eye cameras or skin matrices. Live changes take effect at the next frame boundary and are autosaved. Earlier drawing helps some animation systems but can miss later game-camera/bone changes; not a universal tearing fix. An explicit after-pre-late diagnostic launch overrides this setting for that session.");
        SkinnedMatrices = File.Bind("Rendering", "Recalculate skin matrices per render", false,
            "Experimental compatibility for skinned hair/body displacement between eyes or cameras. Forces per-render skin matrices where the Unity API exists; may cost GPU time. Does not change bones, cloth or physics. Off or leaving VR restores values changed by this plugin. Not a guaranteed fix.");
        Panel = File.Bind("Panel", "Visible", true);
        CaptureMode = File.Bind("Panel", "Capture mode", PanelCaptureMode.FullFrame,
            "FullFrame preserves the existing desktop framebuffer capture. UiOnly is conservative transparent uGUI capture without scene rendering or black colour keying. Preserves UI images, including 2D backgrounds. Unsupported masks, custom shaders or missing uGUI retain the complete frame. Plugin settings always use FullFrame. No game camera, Canvas or renderer is modified; desktop mirror is unchanged.");
        Width = Number("Panel", "Width metres", .8f, .2f, 2);
        Forward = Number("Panel", "Forward metres", .04f, -.3f, .7f);
        Up = Number("Panel", "Up metres", .08f, -.3f, .7f);
        Pitch = Number("Panel", "Pitch degrees", 90, -180, 180);
        Yaw = Number("Panel", "Yaw degrees", 0, -180, 180);
        Roll = Number("Panel", "Roll degrees", 0, -180, 180);
        PointerPitch = Number("Pointer", "Pitch degrees", 0, -90, 90);
        CaptureHz = File.Bind("Panel", "Capture Hz", 30, new ConfigDescription("Game framebuffer only. Hand poses update every render frame.", new AcceptableValueRange<int>(10, 90)));
        WalkSpeed = Number("Movement", "Walk speed", 1.5f, .1f, 10);
        GrabSpeed = Number("Movement", "Grab speed", 1, .1f, 10);
        PitchSpeed = Number("Movement", "Pitch speed", 45, 5, 120);
        Snap = Number("Movement", "Snap degrees", 30, 10, 90);
        Scale = Number("Movement", "World scale", 1, .1f, 100);
        ScaleMaximum = Number("Movement", "Scale maximum", 8, 1, 100);
        FollowStory = File.Bind("Movement", "Follow game camera", true);
        Xbox = File.Bind("Input", "Xbox chord enabled", true);
        A = File.Bind("Input", "Right A", MouseBinding.Left);
        B = File.Bind("Input", "Right B", MouseBinding.Right);
        View = File.Bind("Follow camera", "Mode", FollowViewMode.GameCamera);
        TargetPath = File.Bind("Follow camera", "Target path", "");
        FirstUp = Number("Follow camera", "First eye up", .05f, -2, 2);
        FirstForward = Number("Follow camera", "First eye forward", .2f, -2, 2);
        FirstSide = Number("Follow camera", "First eye side", 0, -2, 2);
        VerticalLag = Number("Follow camera", "First vertical lag seconds", .06f, 0, .5f);
        HorizontalLag = Number("Follow camera", "First horizontal lag seconds", .06f, 0, .5f);
        YawLag = Number("Follow camera", "First yaw lag seconds", .1f, 0, .5f);
        FirstFollowHeading = File.Bind("Follow camera", "First follow character yaw", true,
            "Follow character root yaw, not animated head pitch/roll. Physical HMD tracking remains immediate.");
        CharacterStick = File.Bind("Follow camera", "Left stick controls character", true,
            "In an active character view send only the left stick to native Xbox input instead of moving the VR camera. Requires ViGEm and a game with native gamepad movement. Does not give control of an NPC. Off restores free camera walking.");
        ThirdDistance = Number("Follow camera", "Third distance", 2, .1f, 15);
        ThirdHeight = Number("Follow camera", "Third height above head", .2f, -3, 8);
        ThirdShoulder = Number("Follow camera", "Third shoulder", .4f, -3, 3);
        ThirdLag = Number("Follow camera", "Third follow lag seconds", .1f, 0, .5f);
        FollowHeading = File.Bind("Follow camera", "Third follow character yaw", false);
    }
    private ConfigEntry<float> Number(string section, string key, float value, float min, float max) =>
        File.Bind(section, key, value, new ConfigDescription(key, new AcceptableValueRange<float>(min, max)));
}
