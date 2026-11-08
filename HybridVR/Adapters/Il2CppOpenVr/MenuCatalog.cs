using System;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal enum MenuRow
{
    Language, Panel, DisableDepthOfField, Width, Forward, Up, Pitch, Yaw, Roll, PointerPitch,
    WalkSpeed, GrabSpeed, PitchSpeed, Snap, Scale, ScaleMaximum, FollowStory, Xbox, A, B,
    CaptureRate, Shortcut, View, Target, TargetActions, FirstUp, FirstForward, FirstSide,
    VerticalLag, HorizontalLag, YawLag, FirstFollowHeading, CharacterStick, XboxStatus,
    ThirdDistance, ThirdHeight, ThirdShoulder, ThirdLag, FollowHeading, FollowStatus, DesktopMirror, ShowHands, SingleFrameQueue, SkinnedMatrices, PreserveOriginalPass, DisableMotionBlur, RenderInUpdate, VirtualKeyboard, CaptureMode, CaptureStatus
}

internal static class MenuCatalog
{
    // Exactly one presentation location per existing row; no profile migration.
    private static readonly MenuRow[][] Sections = {
        new[] { MenuRow.View, MenuRow.Panel, MenuRow.VirtualKeyboard, MenuRow.Width, MenuRow.Scale, MenuRow.WalkSpeed,
            MenuRow.ShowHands, MenuRow.DisableDepthOfField, MenuRow.DisableMotionBlur, MenuRow.DesktopMirror, MenuRow.Language },
        new[] { MenuRow.FollowStory, MenuRow.Target, MenuRow.TargetActions, MenuRow.FirstForward,
            MenuRow.FirstUp, MenuRow.FirstSide, MenuRow.FirstFollowHeading, MenuRow.CharacterStick,
            MenuRow.VerticalLag, MenuRow.HorizontalLag, MenuRow.YawLag, MenuRow.ThirdDistance,
            MenuRow.ThirdHeight, MenuRow.ThirdShoulder, MenuRow.FollowHeading, MenuRow.ThirdLag },
        new[] { MenuRow.CaptureMode, MenuRow.CaptureStatus, MenuRow.Forward, MenuRow.Up, MenuRow.Pitch, MenuRow.Yaw, MenuRow.Roll, MenuRow.PointerPitch },
        new[] { MenuRow.A, MenuRow.B, MenuRow.Xbox, MenuRow.Snap, MenuRow.GrabSpeed,
            MenuRow.PitchSpeed, MenuRow.Shortcut },
        new[] { MenuRow.SingleFrameQueue, MenuRow.CaptureRate, MenuRow.PreserveOriginalPass, MenuRow.RenderInUpdate, MenuRow.SkinnedMatrices, MenuRow.ScaleMaximum, MenuRow.FollowStatus, MenuRow.XboxStatus }
    };

    internal static int RowCount(int section) => Sections[section].Length;
    internal static int IndexOf(int section, MenuRow row) => Array.IndexOf(Sections[section], row);
}
