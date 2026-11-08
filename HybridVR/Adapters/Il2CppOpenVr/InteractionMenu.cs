using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HybridVR.Shared;
using UnityEngine;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed partial class OpenVrInteractionHost
{
    private int _menuPage, _menuSection;
    private GUIStyle? _labelStyle, _buttonStyle, _backgroundStyle;
    private Font? _font;
    private string? _fontLanguage;
    private bool _fontFallback;
    private MenuBitmapText? _bitmapFont;
    private bool _bitmapTried;
    private Rect _menuRect;
    private bool MenuButton(Rect rect, string label, GUIStyle? style)
    {
        bool clicked = _menuInput.Button(rect.x+_menuRect.x, rect.y+_menuRect.y, rect.width, rect.height, out bool hover);
        var color = GUI.color;
        if (hover) GUI.color = new Color(.5f,.95f,.8f,1);
        if (_fontFallback && _bitmapFont != null)
        {
            GUI.Box(rect, GUIContent.none, style);
            _bitmapFont.Draw(rect, label, true);
        }
        else GUI.Box(rect,label,style);
        GUI.color=color;
        if (clicked) Uuvr.OpenVR.OpenVrDiagnostics.Write("settings action: " + label);
        return clicked;
    }
    private string DisplayLanguage => !_fontFallback || _bitmapFont != null ? MenuLanguage.Normalize(_settings.Language.Value) : MenuLanguage.Default;
    private void MenuLabel(Rect rect, string text, GUIStyle? style)
    {
        if (_fontFallback && _bitmapFont != null) _bitmapFont.Draw(rect, text, false);
        else GUI.Label(rect, text, style);
    }
    private string Text(string zh, string en) => MenuLanguage.Text(DisplayLanguage, zh, en);
    internal void MenuFailed(Exception error)
    {
        // A GUI failure must never release an unrelated game click on every repaint.
        if (!_menu) return;
        _menu = false;
        _menuInput.Reset();
        _mouse.Release();
        Uuvr.OpenVR.OpenVrDiagnostics.Write("settings unavailable; controls retained: " + error);
    }
    private Font? CreateMenuFont()
    {
        try
        {
            var font = (Font?)MenuFontFactory.Create(typeof(Font),
                true,
                value =>
                {
                    var candidate = (Font)value;
                    foreach (char c in MenuLanguage.GlyphProbe(_settings.Language.Value))
                        if (!candidate.HasCharacter(c)) return false;
                    return true;
                }, value => UnityEngine.Object.Destroy((Font)value), Uuvr.OpenVR.OpenVrDiagnostics.Write, _settings.Language.Value);
            if (font != null) OwnedUnityAsset.Retain(font);
            return font;
        }
        catch (Exception error)
        {
            Uuvr.OpenVR.OpenVrDiagnostics.Write("settings font fallback: game skin / English; " + error);
            return null;
        }
    }
    internal void OnGUI()
    {
        if (!_menu || _disposed) return;
        // Keep optional GUI API resolution behind the open-menu guard, including JIT errors.
        try { DrawMenu(); }
        catch (Exception error) { MenuFailed(error); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawMenu()
    {
        if (Event.current.type != EventType.Repaint) return;
        string requestedLanguage = MenuLanguage.Normalize(_settings.Language.Value);
        if (_fontLanguage != requestedLanguage)
        {
            if (_font != null) UnityEngine.Object.Destroy(_font);
            _font = null; _labelStyle = null; _fontLanguage = requestedLanguage;
        }
        if (_labelStyle == null || _buttonStyle == null || _backgroundStyle == null || (!_fontFallback && _font == null))
        {
            if (_font == null) _font = CreateMenuFont();
            _fontFallback = _font == null;
            if (_fontFallback && !_bitmapTried)
            {
                _bitmapTried = true;
                try { _bitmapFont = new MenuBitmapText(); }
                catch (Exception error) { Uuvr.OpenVR.OpenVrDiagnostics.Write("settings bitmap fallback unavailable: " + error); }
            }
            _labelStyle = (GUIStyle)MenuStyleFactory.Create(typeof(GUIStyle), GUI.skin.label, Uuvr.OpenVR.OpenVrDiagnostics.Write);
            _labelStyle.fontSize = 16; _labelStyle.wordWrap = true;
            _buttonStyle = (GUIStyle)MenuStyleFactory.Create(typeof(GUIStyle), GUI.skin.button, Uuvr.OpenVR.OpenVrDiagnostics.Write);
            _buttonStyle.fontSize = 16;
            _buttonStyle.wordWrap = true;
            _backgroundStyle = (GUIStyle)MenuStyleFactory.Create(typeof(GUIStyle), GUI.skin.box, Uuvr.OpenVR.OpenVrDiagnostics.Write);
            _backgroundStyle.normal.background = Texture2D.whiteTexture;
            if (!_fontFallback) { _labelStyle.font = _font; _buttonStyle.font = _font; }
        }
        float width = Mathf.Min(820, Screen.width - 24), height = Mathf.Min(850, Screen.height - 24);
        if (width < 240 || height < 208) return;
        var oldColor = GUI.color;
        var oldBackground = GUI.backgroundColor;
        var oldContent = GUI.contentColor;
        var oldMatrix = GUI.matrix;
        bool oldEnabled = GUI.enabled;
        int depth = GUI.depth;
        bool grouped = false;
        GUI.depth = int.MinValue;
        GUI.enabled = true; GUI.matrix = Matrix4x4.identity;
        GUI.backgroundColor = GUI.contentColor = Color.white;
        _menuInput.BeginLayout($"{Screen.width}:{Screen.height}:{_menuSection}:{_menuPage}:{DisplayLanguage}");
        try
        {
            var rect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
            _menuRect = rect;
            GUI.color = new Color(.08f,.09f,.11f,.98f);
            // DrawTexture and BeginScrollView can hide throwing IL2CPP stubs in
            // nested overloads. Native-backed Box and paged rows avoid both paths.
            GUI.Box(rect, GUIContent.none, _backgroundStyle);
            GUI.color = Color.white;
            GUI.BeginGroup(rect);
            grouped = true;
            MenuLabel(new Rect(14, 8, width - 125, 30), "HybridVR", _labelStyle);
            if (MenuButton(new Rect(width - 95, 8, 80, 30), Text("关闭", "Close"), _buttonStyle)) ToggleMenu();
            float y = 0, w = width - 28;
            bool compact = w < 500;
            for (int i = 0; i < SettingsSections.Keys.Length; i++)
            {
                GUI.color = i == _menuSection ? new Color(.5f, .9f, .8f) : Color.white;
                if (MenuButton(new Rect(14+i*w/5, 44, w/5-3, 30),
                    Text(SettingsSections.Chinese[i], SettingsSections.English[i]), _buttonStyle))
                { _menuSection = i; _menuPage = 0; _targetScanPending = true; }
            }
            GUI.color = Color.white;
            var pages = new SettingsPages(height, compact, MenuCatalog.RowCount(_menuSection), 82, 52, 74);
            _menuPage = pages.ClampPage(_menuPage);
            bool Row(MenuRow row)
            {
                int index = MenuCatalog.IndexOf(_menuSection, row);
                if (!pages.TryRow(_menuPage, index, out y)) return false;
                return true;
            }
            void Caption(string label, float controlsWidth)
            {
                MenuLabel(new Rect(14,y,compact ? w : w-controlsWidth,compact ? 38 : 40), label, _labelStyle);
                if (compact) y += 40;
            }
            void Toggle(MenuRow row, string label, ConfigEntry<bool> entry)
            {
                if (!Row(row)) return;
                Caption(label, 135);
                if (MenuButton(new Rect(w - 111,y,120,30), entry.Value ? Text("开启", "On") : Text("关闭", "Off"), _buttonStyle)) SettingCommands.Toggle(entry);
            }
            void Number(MenuRow row, string label, ConfigEntry<float> entry, float step, float min, float max)
            {
                if (!Row(row)) return;
                Caption(label, 190);
                if (MenuButton(new Rect(w-166,y,36,30), "-", _buttonStyle)) SettingCommands.Number(entry, -1, step, min, max);
                MenuLabel(new Rect(w-123,y,86,30), entry.Value.ToString("0.##"), _labelStyle);
                if (MenuButton(new Rect(w-28,y,36,30), "+", _buttonStyle)) SettingCommands.Number(entry, 1, step, min, max);
            }
            void Binding(MenuRow row, string name, ConfigEntry<MouseBinding> entry)
            {
                if (!Row(row)) return;
                Caption(name, 150);
                if (MenuButton(new Rect(w-131,y,140,30), MenuLanguage.Mouse(entry.Value, DisplayLanguage), _buttonStyle)) SettingCommands.Cycle(entry);
            }
            if (Row(MenuRow.Language))
            {
                string languageLabel = MenuLanguage.ChoiceLabel(_settings.Language.Value, DisplayLanguage);
                if (MenuButton(new Rect(14,y,w,30), languageLabel, _buttonStyle))
                {
                    SettingCommands.Language(_settings.Language, MenuLanguage.Next(_settings.Language.Value));
                    // Retry only on an explicit request, never on every GUI repaint.
                    _labelStyle = null;
                }
            }
            Toggle(MenuRow.Panel, Text("2D 面板 / X", "2D panel / X"), _settings.Panel);
            if (Row(MenuRow.CaptureMode))
            {
                Caption(Text("2D 捕获方式", "2D capture"), 220);
                if (MenuButton(new Rect(w-211,y,220,30), _settings.CaptureMode.Value == PanelCaptureMode.UiOnly
                    ? Text("仅 UI（保守）", "UiOnly (conservative)") : Text("完整画面", "FullFrame"), _buttonStyle))
                    SettingCommands.Cycle(_settings.CaptureMode);
            }
            if (Row(MenuRow.CaptureStatus)) MenuLabel(new Rect(14,y,w,compact ? 70 : 40),
                _settings.CaptureMode.Value != PanelCaptureMode.UiOnly ? Text("当前使用完整画面", "FullFrame selected") :
                _capture.UiOnlyActive ? Text("仅 UI 已生效", "UiOnly active") :
                Text("仅 UI：保留完整画面，原因见日志", "UiOnly: full-frame fallback; see log"), _labelStyle);
            Toggle(MenuRow.VirtualKeyboard, Text("显示虚拟键盘（全键盘）", "Show virtual keyboard (104 keys)"), _settings.VirtualKeyboard);
            Toggle(MenuRow.DesktopMirror, Text("显示桌面画面", "Desktop mirror"), _settings.DesktopMirror);
            Toggle(MenuRow.PreserveOriginalPass, Text("保留原镜头渲染回调（可能降帧）", "Preserve original camera pass (may reduce FPS)"), _settings.PreserveOriginalCameraPass);
            if (Row(MenuRow.SingleFrameQueue))
            {
                Caption(Text("单帧排队（低延迟，可能降帧）", "Single-frame queue (may reduce FPS)"), 135);
                if (MenuButton(new Rect(w-111,y,120,30), _settings.SingleFrameQueueEnabled ? Text("开启", "On") : Text("关闭", "Off"), _buttonStyle))
                    SettingCommands.FrameQueue(_settings);
            }
            Toggle(MenuRow.ShowHands, Text("显示手部模型", "Show hands"), _settings.ShowHands);
            Toggle(MenuRow.DisableDepthOfField, Text("禁用景深（VR 会话期间）", "Disable depth of field (VR session)"), _settings.DisableDepthOfField);
            Toggle(MenuRow.DisableMotionBlur, Text("禁用运动模糊（VR 会话期间）", "Disable motion blur (VR session)"), _settings.DisableMotionBlur);
            Toggle(MenuRow.RenderInUpdate, Text("Update 阶段渲染（实验；关闭为 LateUpdate）", "Render in Update (Off: LateUpdate)"), _settings.RenderInUpdate);
            Toggle(MenuRow.SkinnedMatrices, Text("逐次重算蒙皮矩阵（实验，可能降帧）", "Per-render skin matrices (experimental)"), _settings.SkinnedMatrices);
            Number(MenuRow.Width, Text("面板宽度（米）", "Panel width (m)"), _settings.Width, .05f, .2f, 2);
            Number(MenuRow.Forward, Text("手柄前方距离（米）", "Grip forward (m)"), _settings.Forward, .01f, -.3f, .7f);
            Number(MenuRow.Up, Text("手柄上方距离（米）", "Grip up (m)"), _settings.Up, .01f, -.3f, .7f);
            Number(MenuRow.Pitch, Text("面板俯仰角", "Panel pitch"), _settings.Pitch, 5, -180, 180);
            Number(MenuRow.Yaw, Text("面板水平角", "Panel yaw"), _settings.Yaw, 5, -180, 180);
            Number(MenuRow.Roll, Text("面板侧倾角", "Panel roll"), _settings.Roll, 5, -180, 180);
            Number(MenuRow.PointerPitch, Text("右手射线俯仰角", "Pointer pitch"), _settings.PointerPitch, 5, -90, 90);
            Number(MenuRow.WalkSpeed, Text("行走速度", "Walk speed"), _settings.WalkSpeed, .25f, .1f, 10);
            Number(MenuRow.GrabSpeed, Text("抓取速度", "Grab speed"), _settings.GrabSpeed, .1f, .1f, 10);
            Number(MenuRow.PitchSpeed, Text("俯仰 / 侧倾速度", "Pitch / roll speed"), _settings.PitchSpeed, 5, 5, 120);
            Number(MenuRow.Snap, Text("转向角度", "Snap turn degrees"), _settings.Snap, 5, 10, 90);
            Number(MenuRow.Scale, Text("世界放大倍率", "World scale"), _settings.Scale, .25f, .1f, _settings.ScaleMaximum.Value);
            Number(MenuRow.ScaleMaximum, Text("最大放大倍率", "Maximum scale"), _settings.ScaleMaximum, 1, 1, 100);
            Toggle(MenuRow.FollowStory, Text("自由视角跟随剧情镜头", "Free view follows game camera"), _settings.FollowStory);
            Toggle(MenuRow.Xbox, Text("双握持加双扳机切换 Xbox", "Xbox chord enabled"), _settings.Xbox);
            Binding(MenuRow.A, "A", _settings.A); Binding(MenuRow.B, "B", _settings.B);
            if (Row(MenuRow.CaptureRate))
            {
                Caption(Text("面板刷新率", "Panel capture rate"), 135);
                if (MenuButton(new Rect(w-111,y,120,30), $"{_settings.CaptureHz.Value} Hz", _buttonStyle))
                    SettingCommands.CaptureRate(_settings.CaptureHz);
            }
            if (Row(MenuRow.Shortcut))
            {
                Caption(Text("桌面快捷键", "Desktop shortcut"), 135);
                if (MenuButton(new Rect(w-111,y,120,30), _settings.Shortcut.Value.ToString(), _buttonStyle))
                    SettingCommands.Shortcut(_settings.Shortcut);
            }
            if (Row(MenuRow.View))
            {
                Caption(Text("镜头模式", "Camera mode"), 220);
                if (MenuButton(new Rect(w-211,y,220,30), MenuLanguage.View(_settings.View.Value, DisplayLanguage), _buttonStyle))
                    SettingCommands.Cycle(_settings.View);
            }
            if (Row(MenuRow.Target))
            {
                Caption(Text("跟随目标", "Follow target") + $" ({_targets.Count})", 230);
                if (MenuButton(new Rect(w-221,y,36,30), "<", _buttonStyle)) SelectFollowTarget(-1);
                string label = _targets.Status == "No target" ? Text("未找到人物", "No target") : _targets.Status;
                MenuLabel(new Rect(w-181,y,150,40), label, _labelStyle);
                if (MenuButton(new Rect(w-28,y,36,30), ">", _buttonStyle)) SelectFollowTarget(1);
            }
            if (Row(MenuRow.TargetActions))
            {
                if (MenuButton(new Rect(14,y,w/2-8,30), Text("刷新人物列表", compact ? "Rescan" : "Refresh targets"), _buttonStyle)) RefreshTargets();
                if (MenuButton(new Rect(22+w/2,y,w/2-8,30), Text("重置跟随偏移", compact ? "Recenter" : "Recenter follow"), _buttonStyle)) _recenterFollow = true;
            }
            Number(MenuRow.FirstUp, Text("第一人称眼部上下", "First eye up"), _settings.FirstUp, .02f, -2, 2);
            Number(MenuRow.FirstForward, Text("第一人称眼部前后", "First eye forward"), _settings.FirstForward, .02f, -2, 2);
            Number(MenuRow.FirstSide, Text("第一人称眼部左右", "First eye side"), _settings.FirstSide, .02f, -2, 2);
            Number(MenuRow.VerticalLag, Text("人物纵向缓冲（秒）", "Character vertical lag (s)"), _settings.VerticalLag, .02f, 0, .5f);
            Number(MenuRow.HorizontalLag, Text("人物横向缓冲（秒）", "Character horizontal lag (s)"), _settings.HorizontalLag, .02f, 0, .5f);
            Number(MenuRow.YawLag, Text("人物转向缓冲（秒）", "Character yaw lag (s)"), _settings.YawLag, .02f, 0, .5f);
            Toggle(MenuRow.FirstFollowHeading, Text("第一人称跟随人物转向", "First follow character yaw"), _settings.FirstFollowHeading);
            Toggle(MenuRow.CharacterStick, Text("跟随时左摇杆控制人物（Xbox）", "Follow: left stick to character (Xbox)"), _settings.CharacterStick);
            if (Row(MenuRow.XboxStatus)) MenuLabel(new Rect(14,y,w,40),
                _xbox.Status == "Xbox: connected" ? Text("Xbox：已连接", _xbox.Status) :
                _xbox.Status == "Xbox: not connected" ? Text("Xbox：未连接", _xbox.Status) : _xbox.Status, _labelStyle);
            Number(MenuRow.ThirdDistance, Text("过肩后方距离", "Third distance"), _settings.ThirdDistance, .1f, .1f, 15);
            Number(MenuRow.ThirdHeight, Text("过肩高度（相对头部）", "Third height above head"), _settings.ThirdHeight, .1f, -3, 8);
            Number(MenuRow.ThirdShoulder, Text("过肩左右偏移", "Third shoulder"), _settings.ThirdShoulder, .1f, -3, 3);
            Number(MenuRow.ThirdLag, Text("过肩跟随缓冲（秒）", "Third follow lag (s)"), _settings.ThirdLag, .02f, 0, .5f);
            Toggle(MenuRow.FollowHeading, Text("过肩跟随人物转向", "Third follow character yaw"), _settings.FollowHeading);
            if (Row(MenuRow.FollowStatus)) MenuLabel(new Rect(14,y,w,40), Text("跟随状态", "Follow state") + ": " +
                (_motion.DefaultViewLocked ? Text("L3 已锁定", "L3 locked") : _followCamera.Active ? Text("跟随中", "Active") : Text("游戏镜头", "Game camera")), _labelStyle);
            if (MenuButton(new Rect(14,height-50,40,26), "<", _buttonStyle)) _menuPage = pages.ClampPage(_menuPage-1);
            MenuLabel(new Rect(60,height-50,100,26), $"{_menuPage+1} / {pages.Count}", _labelStyle);
            if (MenuButton(new Rect(width-54,height-50,40,26), ">", _buttonStyle)) _menuPage = pages.ClampPage(_menuPage+1);
            MenuLabel(new Rect(14,height-25,width-28,24), Text("自动保存", "Autosaved"), _labelStyle);
            if (_menuInput.Valid && rect.Contains(new Vector2(_menuInput.X,_menuInput.Y)))
            {
                var point = new Rect(_menuInput.X-rect.x-4, _menuInput.Y-rect.y-4, 9, 9);
                GUI.color = Color.black; GUI.Box(point, GUIContent.none, _backgroundStyle);
                GUI.color = new Color(.2f,1,.7f,1);
                GUI.Box(new Rect(point.x+2,point.y+2,5,5), GUIContent.none, _backgroundStyle);
            }
            GUI.EndGroup();
            grouped = false;
        }
        finally
        {
            if (grouped) GUI.EndGroup();
            GUI.color = oldColor; GUI.depth = depth;
            GUI.backgroundColor = oldBackground; GUI.contentColor = oldContent;
            GUI.enabled = oldEnabled; GUI.matrix = oldMatrix;
            _menuInput.EndLayout();
        }
    }
}
