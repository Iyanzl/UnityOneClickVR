# OnlyUI Compatibility and Local AI Assistance

[English](#english) | [简体中文](#简体中文) | [日本語](#日本語) | [Home](../README.md)

## English

### What OnlyUI Is

**FullFrame** keeps the complete captured game image. It is the default and the safe fallback.

**UiOnly** attempts to replay supported uGUI elements onto a transparent panel, without rendering a second copy of the 3D scene. It preserves UI pictures as well as text/buttons; a 2D background that is itself a UI image is not automatically unwanted scenery. This is **not black-colour keying**. Black text and dark interface regions should not simply become holes.

OnlyUI does not control whether a panel is attached to the hand or placed in the world. Y placement, L3 view lock and real 3D depth occlusion are separate features.

### How to Test

1. Use FullFrame first. Confirm that menus, text and clicks work before changing capture mode.
2. Open settings with L3+R3 or F10. Select UiOnly for panel capture. Direct configuration uses `[Panel] Capture mode = UiOnly` in the active `codex.hybrid-mono.cfg` or `codex.hybrid-il2cpp.cfg`.
3. Check a menu, a 3D scene, dialogue/subtitles and a scene transition. Look for missing text/buttons, masks, wrong colours, upside-down output and pointer alignment. Confirm actual clicks, not only hover highlights.
4. If the UI is incomplete or the scene remains captured, return to FullFrame. The runtime can deliberately fall back to a full image when it detects an unsupported path.

Change live settings through the in-game menu. **Close the game before directly editing a configuration file**, otherwise runtime autosave can overwrite the edit. Keep a backup.

### What Can Prevent Transparency

| Situation | Boundary |
| --- | --- |
| Supported uGUI geometry and standard materials | May work automatically; test each scene, not only the title screen. |
| Masking, custom UI shaders, unusual ordering or render-texture UI | Requires game-specific diagnosis. Existing configuration may not expose a solution. |
| UI and 3D are already composited into one texture | The plugin cannot reliably separate them by UI layer alone. Removing that texture may also remove portraits or useful UI. |
| UI Toolkit, IMGUI or an unrecognised UI system | Current conservative replay can be unsupported; FullFrame may be retained. |
| Stripped/unavailable `CanvasRenderer.GetMesh` or another required runtime API | Not a parameter problem. A runtime compatibility change/new build may be required. |

There is no universal "UI layer depth 1-5" or guaranteed AI auto-fix in this release. Missing letters or unclickable buttons are a failed result even if the panel looks transparent.

### Where a Local AI Assistant Helps

Some games work without AI. Ordinary VR, full-frame panels, movement and camera tools do **not** require an AI model. The installer neither bundles nor runs a local model, and does not promise that an OnlyUI skill is installed.

A local assistant can inspect logs, identify the active runtime/UI capability, check documented configuration and produce a reproducible compatibility report. It cannot create a missing runtime API by inventing configuration keys. Code/DLL changes require a separate, explicitly authorised development task and regression testing; they are not routine parameter deployment.

Suggested bounded instruction for a local assistant:

> Diagnose OnlyUI for this one game. Start read-only, identify the loaded Mono/IL2CPP runtime and actual config, then compare FullFrame with UiOnly. Back up before any change. Only edit documented parameters belonging to this game, and only while the game is closed. Do not modify source, DLLs, shaders, cameras, other games or global VR/driver settings; do not download an alternative mod. Preserve complete, correctly coloured and clickable UI. If a required API/UI path is unsupported, stop repeating settings and restore FullFrame. Report the log evidence, precise change and remaining capability gap. Ask me to visually confirm the result in the headset. Do not upload files automatically.

Provide the game/Unity version, Mono or IL2CPP, active capture mode, a small relevant log excerpt and a screenshot if useful. Remove private paths/identifiers before posting public reports. Do not publish game DLLs, assets or saves.

## 简体中文

### 不是把黑色抠透明

**FullFrame** 保留完整游戏画面，是默认和安全回退方式。**UiOnly** 尝试把支持的 uGUI 元素重新绘制到透明面板上，而不重复渲染 3D 场景。文字、按钮、UI 图片都应尽量保留；本来就是 UI 图片的 2D 背景，不一定应该被删除。

这不是按黑色抠图。黑字、深色按钮不应因此变成洞。Y 控制手边／世界摆放、L3 控制原视角锁定、模型真实遮挡面板，是另外三件事，不能和 OnlyUI 混为一谈。

### 测试顺序

1. 先用 FullFrame，确认完整画面和点击都正常。
2. L3+R3／F10 打开设置，把面板捕获切到 UiOnly。配置对应活动游戏的 `codex.hybrid-mono.cfg` 或 `codex.hybrid-il2cpp.cfg` 中 `[Panel] Capture mode = UiOnly`。
3. 检查选单、3D 场景、对话字幕和一次场景切换；观察漏字、漏按钮、蒙版、颜色、上下翻转、指针对齐。必须确认实际点击，不只看悬停变色。
4. UI 有遗漏或仍显示完整场景，先回退 FullFrame。运行库检测到不支持的路径时，也可能有意保留完整画面。

游戏内菜单用于实时调整。**直接修改配置文件前要关闭游戏并备份**，否则运行时自动保存可能覆盖修改。

### 适用边界

- 标准 uGUI 与常见材质可能直接有效，但应跨场景确认，不只测开场。
- 自定义 UI 材质、裁剪蒙版、特殊排序、RenderTexture 界面需要逐游戏排查，未必有现成参数可改。
- 如果 3D 和 UI 已合成在同一纹理里，不能仅靠 UI 层级可靠分离；删掉整张纹理可能连头像和有用图片一起丢失。
- UI Toolkit／IMGUI 或未识别系统，当前保守捕获可能不支持，应保留 FullFrame。
- `CanvasRenderer.GetMesh` 等必需接口被 IL2CPP 剥离或不可用，是运行库能力问题，不是调几个参数就能补上，可能需要专门兼容代码和新版本。

当前没有通吃所有游戏的“UI 深度 1–5”参数，也没有保证成功的 AI 自动修复。只要漏字或不能点，即使透明了也不算成功。

### 本地 AI 能做什么

有些游戏不用 AI 就能 OnlyUI；普通 VR、完整面板、移动和视角功能也不依赖 AI。安装器不包含、不运行 AI 模型，也不表示已经安装了某个 OnlyUI skill。

本地 AI 可帮助读日志、确认实际加载的运行库和 UI 能力、核对已有参数、整理复现报告。不要让它虚构配置项或无限循环试错。修改源码、DLL、材质／相机属于另外一项开发任务，需要明确授权并做回归，不应混在普通参数部署里。

可给本地 AI 以下受限指令：

> 仅诊断这一个游戏的 OnlyUI。先只读确认 Mono／IL2CPP、实际配置和日志，对比 FullFrame 与 UiOnly；任何改动先备份，只在关闭游戏后修改该游戏公开支持的参数。禁止改源码、DLL、Shader、相机、其他游戏、全局 VR 或驱动设置，禁止另下替代 mod。以文字／按钮完整、颜色正确且实际可点击为通过标准。遇到缺 API／不支持的 UI 路径就停止重复试参数，恢复 FullFrame，记录证据与能力缺口，并请我在头显内确认。不要自动上传任何文件。

提供游戏／Unity 版本、运行时类型、当前捕获方式及少量相关日志即可，必要时提供截图。公开反馈先隐藏私人路径和账号信息，不上传游戏 DLL、素材、存档。

## 日本語

FullFrame はゲーム画面全体を維持する標準・安全な方式です。UiOnly は対応する uGUI を透明なパネルへ再描画し、3D シーンの二重表示を避ける試みです。文字・ボタンだけでなく UI の画像も保持します。UI 画像として作られた 2D 背景は、必ずしも削除対象ではありません。黒色を抜く方式ではありません。

まず FullFrame で表示とクリックを確認し、設定から UiOnly を試してください。メニュー、3D シーン、字幕、シーン切り替え、色、マスク、実際のクリックを確認します。問題があれば FullFrame に戻してください。設定ファイルを直接編集する場合はゲームを終了し、バックアップしてください。

標準 uGUI はそのまま動作する場合があります。一方、独自マテリアル、マスク、UI Toolkit / IMGUI、3D と UI を合成済みのテクスチャ、削除された CanvasRenderer.GetMesh などは、既存の設定だけでは解決できない場合があります。すべてに有効な UI 深度パラメーターや AI 自動修復はありません。

ローカル AI は任意の補助です。通常の VR 利用にモデルは不要で、インストーラーにも含まれません。ログの確認、対応能力の判定、公開された設定の確認、再現報告を補助できますが、存在しない API を設定値だけで復元することはできません。

AI には「対象ゲームだけをまず読み取り専用で診断し、バックアップ後、終了中に公開設定だけを変更する。DLL・ソース・シェーダー・カメラ・他のゲーム・システム VR 設定を変更しない。未対応の能力が原因なら試行を止めて FullFrame に戻し、証拠を報告して、ヘッドセットでの確認を依頼する。ファイルを自動送信しない」と指示してください。コードの変更には別途の明確な許可と回帰試験が必要です。

文字やボタンが欠ける状態は、透明に見えても成功ではありません。公開ログは個人情報を除き、ゲームのアセンブリやアセット、セーブデータを投稿しないでください。
