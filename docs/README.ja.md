# UnityOneClickVR

[English](../README.md) | [简体中文](README.zh-CN.md)

Windows の Unity Mono / IL2CPP ゲームを VR 化し、操作と設定を共通化することを目指す実験的プロジェクトです。

**完全版インストーラーは [Releases](https://github.com/Iyanzl/UnityOneClickVR/releases) から取得できます。** `UnityOneClickVR-M033-R20-full-win-x64.zip` を選んでください。GitHub の Source code アーカイブはインストーラーではありません。

1. ZIP を書き込み可能なフォルダーに展開し、ゲームを終了します。
2. ゲームの EXE を `Install-HybridVR.bat` にドラッグします。ダブルクリックして EXE を選択することもできます。
3. 検出結果を確認し、`y` で承認します。既存の認識済み VR ファイルは置換前にバックアップされます。
4. IL2CPP の初回セットアップでは、BepInEx 公式の Unity 基本ライブラリをダウンロードする確認が表示される場合があります。キャッシュ後は再利用できます。
5. SteamVR にヘッドセットを接続し、ゲームフォルダーの `Start-VR.bat` で起動します。L3+R3 または F10 で設定を開き、日本語を選択できます。

デスクトップ用は `Start-Desktop.bat`、削除と復元は `Uninstall-HybridVR.cmd` です。アンインストールにも `y` の確認が必要です。既存の MOD やセーブデータを必ずバックアップしてください。

ステレオ描画、コントローラーと手の表示、左手の 2D パネル、仮想キーボード、ワールド移動・拡大縮小、一人称・三人称カメラ、リアルタイム設定に対応することを目指しています。OnlyUI の対応範囲はゲームの UI 実装によって異なります。すべてのゲームで動作する保証はありません。

主な対象は Windows x64 / D3D11 です。現在のバックエンドは SteamVR / OpenVR を使用します。VD / Quest Link でテストしていますが、ネイティブ OpenXR 対応を意味しません。新規設定の言語は英語で、中国語（簡体字）と日本語を選択できます。

ビルド手順は [BUILDING.md](BUILDING.md) を参照してください。ゲーム本体や生成された相互運用アセンブリは同梱されません。既存の MOD とセーブデータをバックアップしてください。

本プロジェクトは GPL-3.0-only です。第三者のコード・リソースには元のライセンスが適用されます。[謝辞](../ACKNOWLEDGMENTS.md) と [ライセンス情報](../THIRD_PARTY_NOTICES.md) を参照してください。

[Ko-fi で任意の支援](https://ko-fi.com/iyanzl)。支援の有無による機能制限はなく、個別ゲームへの対応を保証するものではありません。
