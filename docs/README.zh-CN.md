# UnityOneClickVR

[English](../README.md) | [日本語](README.ja.md)

面向 Windows Unity Mono 与 IL2CPP 游戏的一键 VR 化项目，目标是统一操作、设置与部署体验。

**当前公开的是源码预览，不是可直接拖入 EXE 的安装包。** 本地测试包含有待核清的原生依赖和 Unity 缓存，暂不公开分发。请勿从不明镜像下载安装器。

支持方向包括双眼渲染、手柄与手部、左手面板、虚拟键盘、世界移动和缩放、第一／第三人称以及实时设置。OnlyUI 受游戏 UI 实现限制，不能保证所有游戏自动透明化，完整画面捕获是保守回退方案。

当前主要支持 Windows x64 / D3D11，通过 SteamVR / OpenVR 工作；已测试 VD 与 Quest Link，但不代表原生 OpenXR。新配置默认英语，可切换简体中文、日语。内部 HybridVR / Uuvr 名称为兼容旧配置而保留。

构建请参阅 [BUILDING.md](BUILDING.md)。请只修改有权使用的游戏，备份存档与旧插件，不要用于带反作弊的多人游戏。

代码遵循 GPL-3.0-only，第三方组件保留原许可。详见 [致谢](../ACKNOWLEDGMENTS.md) 与 [许可说明](../THIRD_PARTY_NOTICES.md)。本项目基于 UUVR、UnityVRMod、SiH 等项目改进，不是从零独立开发。

[Ko-fi 自愿支持](https://ko-fi.com/iyanzl)。赞助不解锁额外功能，也不构成对单款游戏兼容性的保证。
