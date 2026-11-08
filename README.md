# UnityOneClickVR

[简体中文](docs/README.zh-CN.md) | [日本語](docs/README.ja.md)

An experimental, one-click VR setup project for Windows Unity **Mono and IL2CPP** games.
The goal is a shared control scheme and settings experience across games, rather than a separate mod for every title.

**Download the complete installer from [Releases](https://github.com/Iyanzl/UnityOneClickVR/releases).** Choose the asset named `UnityOneClickVR-M033-R20-full-win-x64.zip`, not GitHub's automatic Source code archive. This is an experimental compatibility release, not a guarantee that every Unity game works. This repository is not an official Unity, Valve, Meta, or Virtual Desktop product.

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

1. Extract the **full installer ZIP** into a writable folder. Do not run it from inside the ZIP or copy its payload folders over a game manually.
2. Close the game. Drag its Unity game EXE onto `Install-HybridVR.bat` (or double-click it and select the EXE).
3. Read the detected runtime and planned changes. Confirm with lowercase `y`. An existing VR mod is backed up before recognized files are replaced; unrelated mods are preserved where possible.
4. On first IL2CPP installation, approve the official Unity base-library download if prompted. It is cached for reuse. `Prepare-Offline-Dependencies.cmd` can prepare that cache on an internet-connected PC in advance. Missing/unknown Unity versions or loader conflicts can still block installation safely.
5. Connect your headset through SteamVR and launch `Start-VR.bat` from the game directory. Press both stick buttons (L3+R3) for settings, or use F10 (configurable). New configurations default to English; Chinese and Japanese are selectable.

The complete installer includes the tested BepInEx loader payloads and required mod dependencies. SteamVR, headset streaming software and game assemblies are not included. Existing supported loaders/configuration are retained where possible. Camera/rendering compatibility and OnlyUI may still need settings changes.

Xbox-controller emulation additionally requires a compatible virtual-controller driver already installed on the PC; the installer does not install drivers or change the system XR runtime. Keep the generated `_HybridVR` backup directory until after uninstall/restore. On first IL2CPP startup, generating this game's own interop can take longer than later starts.

For developers, see [BUILDING.md](docs/BUILDING.md). Source code alone is **not** the drag-and-drop installer.

The installer creates these entry points:

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
