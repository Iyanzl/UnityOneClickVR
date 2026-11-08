using System;
using System.Collections.Generic;
using HybridVR.Shared;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal static class MenuLanguage
{
    internal const string Default = "en";
    internal static readonly string[] Options = { "en", "zh-CN", "ja" };
    internal static string Normalize(string? language)
    {
        string value = (language ?? "").Trim();
        if (value.Equals("zh", StringComparison.OrdinalIgnoreCase) || value.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) || value.Equals("Chinese", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (value.Equals("ja", StringComparison.OrdinalIgnoreCase) || value.Equals("ja-JP", StringComparison.OrdinalIgnoreCase) || value.Equals("Japanese", StringComparison.OrdinalIgnoreCase)) return "ja";
        return Default;
    }
    internal static bool WantsChinese(string language) => Normalize(language) == "zh-CN";
    internal static bool WantsJapanese(string language) => Normalize(language) == "ja";
    internal static string Next(string language) => Options[(Array.IndexOf(Options, Normalize(language)) + 1) % Options.Length];
    internal static string Text(string language, string chinese, string english)
        => WantsChinese(language) ? chinese : WantsJapanese(language) && Japanese.TryGetValue(english, out var value) ? value : english;
    internal static string GlyphProbe(string language) => WantsJapanese(language) ? "A日本語設定ひらがなカタカナ" : WantsChinese(language) ? "A设置中文镜头蒙皮" : "A";
    internal static string ChoiceLabel(string requested, string displayed)
    {
        if (Normalize(requested) != Normalize(displayed)) return "Language: " + (WantsJapanese(requested) ? "Japanese" : "Chinese") + " requested (font unavailable)";
        return Text(displayed, "界面语言：中文", "Language: English");
    }
    internal static string Mouse(MouseBinding value, string language)
        => Text(language, Mouse(value, true), value.ToString());
    internal static string View(FollowViewMode value, string language)
        => Text(language, View(value, true), value.ToString());

    // Keys remain the existing English UI text; translations never change config IDs.
    internal static readonly Dictionary<string, string> Japanese = new(StringComparer.Ordinal)
    {
        ["Close"] = "閉じる", ["On"] = "オン", ["Off"] = "オフ",
        ["Main"] = "基本", ["View"] = "視点", ["UI"] = "パネル", ["Keys"] = "操作", ["More"] = "詳細",
        ["Language: English"] = "言語：日本語",
        ["None"] = "なし", ["Left"] = "マウス左ボタン", ["Right"] = "マウス右ボタン", ["Middle"] = "マウス中ボタン",
        ["GameCamera"] = "ゲームカメラ / 自由視点", ["FirstPerson"] = "一人称", ["ThirdPerson"] = "三人称 / 肩越し",
        ["2D panel / X"] = "2D パネル / X",
        ["2D capture"] = "2D キャプチャ方式",
        ["UiOnly (conservative)"] = "UI のみ（安全優先）",
        ["FullFrame"] = "画面全体",
        ["FullFrame selected"] = "画面全体を表示中",
        ["UiOnly active"] = "UI のみを表示中",
        ["UiOnly: full-frame fallback; see log"] = "UI 抽出に未対応：画面全体を表示。詳細はログを確認",
        ["Show virtual keyboard (104 keys)"] = "仮想キーボードを表示（104 キー）",
        ["Desktop mirror"] = "デスクトップに映像を表示",
        ["Preserve original camera pass (may reduce FPS)"] = "元のカメラの描画処理を維持（負荷増加）",
        ["Single-frame queue (may reduce FPS)"] = "描画キューを 1 フレームに制限（FPS 低下の場合あり）",
        ["Show hands"] = "手のモデルを表示",
        ["Disable depth of field (VR session)"] = "被写界深度を無効化（VR 中）",
        ["Disable motion blur (VR session)"] = "モーションブラーを無効化（VR 中）",
        ["Render in Update (Off: LateUpdate)"] = "Update で描画（オフ：LateUpdate）",
        ["Per-render skin matrices (experimental)"] = "描画ごとにスキニングを再計算（実験的）",
        ["Panel width (m)"] = "パネルの幅（m）",
        ["Grip forward (m)"] = "コントローラーからの前後距離（m）",
        ["Grip up (m)"] = "コントローラーからの上下距離（m）",
        ["Panel pitch"] = "パネルの上下角度",
        ["Panel yaw"] = "パネルの左右角度",
        ["Panel roll"] = "パネルの傾き",
        ["Pointer pitch"] = "右手ポインターの上下角度",
        ["Walk speed"] = "移動速度", ["Grab speed"] = "ワールドをつかむ移動速度",
        ["Pitch / roll speed"] = "上下角度 / 傾きの調整速度",
        ["Snap turn degrees"] = "スナップターンの角度",
        ["World scale"] = "ワールドの拡大倍率", ["Maximum scale"] = "最大拡大倍率",
        ["Free view follows game camera"] = "自由視点をゲームカメラに追従",
        ["Xbox chord enabled"] = "両手のグリップ＋トリガーで Xbox モード切替",
        ["Panel capture rate"] = "パネルの更新頻度", ["Desktop shortcut"] = "設定のショートカットキー",
        ["Camera mode"] = "カメラモード", ["Follow target"] = "追従対象", ["No target"] = "対象が見つかりません",
        ["Rescan"] = "再検索", ["Refresh targets"] = "追従対象を再検索", ["Recenter"] = "位置をリセット", ["Recenter follow"] = "追従位置をリセット",
        ["First eye up"] = "一人称の視点位置：上下", ["First eye forward"] = "一人称の視点位置：前後", ["First eye side"] = "一人称の視点位置：左右",
        ["Character vertical lag (s)"] = "人物の上下移動を平滑化（秒）",
        ["Character horizontal lag (s)"] = "人物の横移動を平滑化（秒）",
        ["Character yaw lag (s)"] = "人物の旋回を平滑化（秒）",
        ["First follow character yaw"] = "一人称で人物の向きに追従",
        ["Follow: left stick to character (Xbox)"] = "追従中は左スティックで人物を操作（Xbox）",
        ["Xbox: connected"] = "Xbox：接続済み", ["Xbox: not connected"] = "Xbox：未接続",
        ["Third distance"] = "肩越し視点の後方距離", ["Third height above head"] = "肩越し視点の高さ（頭から）", ["Third shoulder"] = "肩越し視点の左右位置",
        ["Third follow lag (s)"] = "肩越し視点の追従を平滑化（秒）", ["Third follow character yaw"] = "肩越し視点で人物の向きに追従",
        ["Follow state"] = "追従状態", ["L3 locked"] = "L3 ロック中", ["Active"] = "追従中", ["Game camera"] = "ゲームカメラ", ["Autosaved"] = "自動保存済み"
    };
    internal static string Mouse(MouseBinding value, bool chinese)
    {
        if (!chinese) return value.ToString();
        switch (value)
        {
            case MouseBinding.None: return "\u65e0";
            case MouseBinding.Left: return "\u9f20\u6807\u5de6\u952e";
            case MouseBinding.Right: return "\u9f20\u6807\u53f3\u952e";
            case MouseBinding.Middle: return "\u9f20\u6807\u4e2d\u952e";
            default: return value.ToString();
        }
    }
    internal static string View(FollowViewMode value, bool chinese)
    {
        if (!chinese) return value.ToString();
        switch (value)
        {
            case FollowViewMode.GameCamera: return "\u6e38\u620f\u955c\u5934 / \u81ea\u7531\u89c6\u89d2";
            case FollowViewMode.FirstPerson: return "\u7b2c\u4e00\u4eba\u79f0";
            case FollowViewMode.ThirdPerson: return "\u7b2c\u4e09\u4eba\u79f0 / \u8fc7\u80a9";
            default: return value.ToString();
        }
    }
}
