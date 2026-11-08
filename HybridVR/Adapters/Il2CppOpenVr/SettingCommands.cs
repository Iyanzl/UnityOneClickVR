using System;
using System.Globalization;
using BepInEx.Configuration;
using HybridVR.Shared;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal static class SettingCommands
{
    internal static void FrameQueue(InteractionSettings settings)
        => Report(LiveSetting.Toggle(Key(settings.SingleFrameQueue.Definition),
            () => settings.SingleFrameQueueEnabled, settings.SetSingleFrameQueue).Adjust(1));

    internal static void Number(ConfigEntry<float> entry, int direction, float step, float min, float max)
        => Report(LiveSetting.Number(Key(entry.Definition), Unit(entry.Definition), () => entry.Value, v => entry.Value = v, step, min, max).Adjust(direction));

    internal static void Toggle(ConfigEntry<bool> entry)
        => Report(LiveSetting.Toggle(Key(entry.Definition), () => entry.Value, v => entry.Value = v).Adjust(1));

    internal static void Cycle<T>(ConfigEntry<T> entry) where T : struct, Enum
        => Report(LiveSetting.Choice(Key(entry.Definition), () => entry.Value.ToString(),
            v => entry.Value = (T)Enum.Parse(typeof(T), v), Enum.GetNames(typeof(T))).Adjust(1));

    internal static void Language(ConfigEntry<string> entry, string value)
        => Report(LiveSetting.Choice(CommonSettingIds.Language, () => entry.Value,
            v => entry.Value = v, MenuLanguage.Options).Configure(value));

    internal static void Shortcut(ConfigEntry<UnityEngine.KeyCode> entry)
        => Report(LiveSetting.Choice(CommonSettingIds.Shortcut, () => entry.Value.ToString(),
            v => entry.Value = (UnityEngine.KeyCode)Enum.Parse(typeof(UnityEngine.KeyCode), v),
            new[] { "F6", "F7", "F8", "F9", "F10", "F12" }).Adjust(1));

    internal static void CaptureRate(ConfigEntry<int> entry)
    {
        int next = entry.Value >= 60 ? 15 : entry.Value >= 30 ? 60 : 30;
        Report(LiveSetting.Number("il2cpp.panel.capture-hz", "hertz", () => entry.Value,
            v => { if (v != Math.Floor(v)) throw new ArgumentException("Whole Hz required"); entry.Value = (int)v; },
            15, 10, 90).Configure(next.ToString(CultureInfo.InvariantCulture)));
    }

    private static void Report(SettingChangeResult result)
        => OpenVrDiagnostics.Write($"setting-config: {result.Id}={result.Readback}; {result.Kind}; {result.Error}");

    internal static string Key(ConfigDefinition definition)
    {
        switch (definition.Section + "/" + definition.Key)
        {
            case "General/Language": return CommonSettingIds.Language;
            case "General/Settings key": return CommonSettingIds.Shortcut;
            case "Panel/Visible": return CommonSettingIds.PanelVisible;
            case "Panel/Width metres": return CommonSettingIds.PanelWidth;
            case "Panel/Forward metres": return CommonSettingIds.PanelForward;
            case "Panel/Up metres": return CommonSettingIds.PanelUp;
            case "Panel/Pitch degrees": return CommonSettingIds.PanelPitch;
            case "Panel/Yaw degrees": return CommonSettingIds.PanelYaw;
            case "Panel/Roll degrees": return CommonSettingIds.PanelRoll;
            case "Movement/World scale": return CommonSettingIds.WorldScale;
            case "Movement/Scale maximum": return CommonSettingIds.ScaleMaximum;
            case "Movement/Walk speed": return CommonSettingIds.WalkSpeed;
            case "Movement/Grab speed": return CommonSettingIds.GrabSpeed;
            case "Movement/Pitch speed": return CommonSettingIds.PitchSpeed;
            case "Movement/Snap degrees": return CommonSettingIds.Snap;
            case "Movement/Follow game camera": return CommonSettingIds.FollowStory;
            case "Input/Right A": return CommonSettingIds.MouseA;
            case "Input/Right B": return CommonSettingIds.MouseB;
            case "Input/Xbox chord enabled": return CommonSettingIds.Xbox;
            case "Input/Show virtual keyboard": return CommonSettingIds.VirtualKeyboard;
            case "Rendering/Desktop mirror": return CommonSettingIds.DesktopMirror;
            case "Rendering/Preserve original camera pass": return CommonSettingIds.OriginalCameraPass;
            case "Rendering/Recalculate skin matrices per render": return CommonSettingIds.SkinMatrices;
            case "Rendering/Render in Update": return CommonSettingIds.RenderInUpdate;
            case "Rendering/Single frame queue": return CommonSettingIds.SingleFrameQueue;
            case "Rendering/Show hands": return CommonSettingIds.ShowHands;
            case "Rendering/Disable depth of field": return CommonSettingIds.DisableDepthOfField;
            case "Rendering/Disable motion blur": return CommonSettingIds.DisableMotionBlur;
            default: return "il2cpp." + LiveSettingKey.Slug(definition.Section) + "." + LiveSettingKey.Slug(definition.Key);
        }
    }

    private static string Unit(ConfigDefinition definition)
    {
        string id = Key(definition);
        if (definition.Section == "Follow camera") return definition.Key.Contains("seconds") ? "seconds" : "game-units";
        if (id == CommonSettingIds.PanelWidth || id == CommonSettingIds.PanelForward || id == CommonSettingIds.PanelUp) return "metres";
        if (id == CommonSettingIds.WorldScale || id == CommonSettingIds.ScaleMaximum || id == CommonSettingIds.GrabSpeed) return "factor";
        if (id == CommonSettingIds.WalkSpeed) return "game-units-per-second";
        if (id == CommonSettingIds.PitchSpeed) return "degrees-per-second";
        return definition.Key.Contains("degrees") ? "degrees" : "host-defined";
    }
}
