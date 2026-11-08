# UnityOneClickVR

[English](../README.md) | [日本語](README.ja.md)

面向 Windows Unity Mono 与 IL2CPP 游戏的一键 VR 化项目，目标是统一操作、设置与部署体验。

**完整安装包在 [Releases](https://github.com/Iyanzl/UnityOneClickVR/releases)。** 下载 `UnityOneClickVR-M033-R20-full-win-x64.zip`，不要下载 GitHub 自动生成的 Source code 当作安装包。

1. 将 ZIP 完整解压到可写文件夹，关闭游戏。
2. 把游戏 EXE 拖到 `Install-HybridVR.bat`，或双击后选择游戏 EXE。
3. 确认识别结果，输入小写 `y`。已有 VR 插件会先备份再替换，不能保证与所有第三方插件共存。
4. IL2CPP 首次安装如缺基础库，会提示从 BepInEx 官方地址下载并缓存。也可提前运行 `Prepare-Offline-Dependencies.cmd` 准备离线缓存。
5. 连接头显并启动 SteamVR，使用游戏目录的 `Start-VR.bat`。左右摇杆同时按下（L3+R3）或 F10 呼出设置，切换中文／日语。

`Start-Desktop.bat` 用于不加载本补丁 VR 运行时的桌面模式；`Uninstall-HybridVR.cmd` 经 `y` 确认后恢复安装时备份的文件。升级请先用旧版本卸载入口恢复，再安装新版，不要手动覆盖整个 BepInEx。

完整包包含 BepInEx 加载器和补丁依赖，不包含游戏程序集、SteamVR、串流软件或本机 Unity 缓存。当前属于兼容性测试版，不保证所有 Unity 游戏开箱即用。

支持方向包括双眼渲染、手柄与手部、左手面板、虚拟键盘、世界移动和缩放、第一／第三人称以及实时设置。OnlyUI 受游戏 UI 实现限制，不能保证所有游戏自动透明化，完整画面捕获是保守回退方案。

当前主要支持 Windows x64 / D3D11，通过 SteamVR / OpenVR 工作；已测试 VD 与 Quest Link，但不代表原生 OpenXR。新配置默认英语，可切换简体中文、日语。内部 HybridVR / Uuvr 名称为兼容旧配置而保留。

构建请参阅 [BUILDING.md](BUILDING.md)。请只修改有权使用的游戏，备份存档与旧插件，不要用于带反作弊的多人游戏。

代码遵循 GPL-3.0-only，第三方组件保留原许可。详见 [致谢](../ACKNOWLEDGMENTS.md) 与 [许可说明](../THIRD_PARTY_NOTICES.md)。本项目基于 UUVR、UnityVRMod、SiH 等项目改进，不是从零独立开发。

[Ko-fi 自愿支持](https://ko-fi.com/iyanzl)。赞助不解锁额外功能，也不构成对单款游戏兼容性的保证。
