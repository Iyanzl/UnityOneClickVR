# UnityOneClickVR

[简体中文](docs/README.zh-CN.md) | [日本語](docs/README.ja.md)

An experimental, one-click VR setup project for Windows Unity **Mono and IL2CPP** games.
The goal is a shared control scheme and settings experience across games, rather than a separate mod for every title.

**Initial public source preview. There is not yet a public, audited installer release.**
The existing local test package is not uploaded: it contains Unity reference caches and native dependencies that still need a redistribution audit. Do not download installers from unrelated mirrors. This repository is not an official Unity, Valve, Meta, or Virtual Desktop product.

## Features

- Stereo rendering through OpenVR / SteamVR, with game-dependent camera compatibility.
- Tracked controllers, hand models, left-hand 2D panel, pointer and virtual keyboard.
- World placement, depth occlusion, panel resizing, world movement and scale controls.
- Free, first-person and third-person camera tools; target detection varies by game.
- Shared real-time settings in English, Simplified Chinese and Japanese. Fresh installations default to English.
- Optional UI-only capture. It does not reliably extract every game's UI; full-frame capture remains the fallback.
- Separate desktop and VR launch paths, installation backups and an uninstall entry point.

## Compatibility

The current target is **Windows x64, D3D11**. Mono and IL2CPP have different loader and Unity-reference requirements. Runtime detection selects a candidate, not a guarantee that every camera or UI will work.

Local compatibility tests have covered multiple games, including BUNNY GARDEN 2, Orc Massage, SummerInHeat and several IL2CPP titles. Results depend on the game version, rendering pipeline and configuration. This is not a claim of universal compatibility.

Virtual Desktop and Quest Link / Air Link have been used during testing. The active backend uses SteamVR/OpenVR; support for a headset transport is not a claim of native OpenXR support.

Use only with games you are entitled to modify. Do not use in anti-cheat-protected multiplayer games. Back up saves and existing mods before testing. Stop using VR if visual artifacts or discomfort occur.

## Getting Started

For developers, see [BUILDING.md](docs/BUILDING.md). Unity/game assemblies, SteamVR and loader distributions are not included. Source code alone is **not** the drag-and-drop installer.

The eventual installer preserves these entry points:

- `Start-VR.bat`: launch with the VR mod.
- `Start-Desktop.bat`: launch without this mod's VR runtime.
- `Uninstall-HybridVR.cmd`: confirm and restore files owned by the installation.

Internal `HybridVR` / `Uuvr` names are retained for configuration and compatibility. This project is a modified derivative, not an upstream release.

## Reports and Contributions

Please use the compatibility issue template. Include Unity version, Mono/IL2CPP, headset/runtime, reproduction steps and the relevant error excerpt. Remove personal paths, account information and game content from logs before sharing. Reports are not uploaded automatically.

## License and Thanks

The combined project is released under **GPL-3.0-only**, with upstream rights and component-specific licenses retained. See [LICENSE](LICENSE), [ACKNOWLEDGMENTS.md](ACKNOWLEDGMENTS.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

This work builds on UUVR, UnityVRMod and SummerInHeat SiH VR. Please support and credit the original authors as well.

## Optional Support

[Support development on Ko-fi](https://ko-fi.com/iyanzl). Support is entirely optional: it does not unlock features, buy a game compatibility guarantee, or replace the upstream licenses.
