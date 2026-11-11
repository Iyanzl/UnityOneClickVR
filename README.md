# UnityOneClickVR

[简体中文](docs/README.zh-CN.md) | [日本語](docs/README.ja.md)

[![Download the full installer](https://img.shields.io/badge/Download-full_installer-2ea44f?style=for-the-badge&logo=github)](https://github.com/Iyanzl/UnityOneClickVR/releases/latest)
[![Support development on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/iyanzl)

**Bring Unity games into VR, then keep their original interface within reach.**
One installer for Windows Unity **Mono and IL2CPP** games, with a shared controller layout, a movable 2D interface, free/character-follow views and in-headset settings. Compatibility still varies by game.

**[Install](#getting-started) · [Controller Guide](docs/CONTROLS.md) · [Settings Explained](docs/SETTINGS.md) · [OnlyUI / Local AI Boundaries](docs/ONLYUI.md) · [Release Notes](https://github.com/Iyanzl/UnityOneClickVR/releases)**

Download the full Windows x64 installer ZIP, not GitHub's automatic Source code archive. Support is optional and never unlocks features or guarantees compatibility. This is not an official Unity, Valve, Meta or Virtual Desktop product.

## What You Can Do

| Feature | In practice |
| --- | --- |
| Keep the game's interface in VR | Put menus, dialogue, inventory and game buttons on a left-hand 2D panel; point and click with the right controller. |
| Switch between hand-held and world placement | Y pins the panel where you place it; Y again brings it back to the left hand. World-placed panels use scene depth; hand-held and L3-locked panels remain front-layer tools. |
| Move and resize the panel | Drag its outer margin in world mode, or resize from the corner margin. Resize also works while hand-held; moving it does not. |
| Explore at a different scale | Walk using the stick, grab and move the world, adjust pitch/roll, or change world scale with a two-hand gesture. |
| Choose a viewing style | Free/game-camera view, first person and third-person/over-shoulder follow, with target selection and adjustable offsets. Finding the correct character depends on the game's models. |
| Switch to Xbox-style controls | Toggle controller emulation for games with native gamepad support; switch back to the VR interaction tools with the same chord. Requires a compatible virtual-controller driver. |
| Type without leaving VR | A 104-key virtual keyboard includes function keys, modifiers, arrows and a numeric keypad. |
| Adjust common settings in-headset | Shared settings for both runtimes, with English, Simplified Chinese and Japanese. Common panel/input/view settings apply live; some startup/rendering changes require restarting VR. |
| Try transparent UI-only panels | OnlyUI attempts to keep the game's UI without a second copy of the 3D scene. It is game-dependent, not universal black-pixel transparency. See the limitations below. |
| Keep a desktop launch path | Separate desktop/VR launchers, backups before replacement, and confirmed uninstall/restore. |

## Default Controls

Quest / Touch button names are used here. **L3/R3 mean pressing the sticks**, not moving them. Bindings changed in settings override these defaults.

| Action | Default input |
| --- | --- |
| Open / close settings | L3 + R3 together, or F10 on the PC keyboard |
| Show / hide the 2D panel | X |
| Pin panel in the world / return to left hand | Y, with L3 view lock off |
| Lock to the original game's interaction view / restore your VR viewing offset | Click and release L3 |
| Left / right / middle mouse button | A or right trigger / B / R3 |
| Free-view movement | Left stick, relative to current head facing |
| Snap turn | Right stick left / right; 30 degrees by default |
| Grab and move the world | Hold grip + trigger on **one** controller and move that hand |
| Pitch / roll / level the horizon | Right grip + right stick up/down / left/right / R3 |
| Scale the world | Hold both triggers, then hold A and move the hands apart/together; release to finish |
| Reset world scale | Hold both triggers and double-tap A |
| Toggle VR tools / Xbox-controller mode | Hold **both grips and both triggers** for at least 0.5 seconds, then release all; repeat to return |
| Show the virtual keyboard | Enable **Show virtual keyboard** in settings; point and select with A or right trigger |

Movement, world grabbing and angle adjustment are suspended while the L3 view is locked or settings are open. Unlock first. First-/third-person character views can route the left stick to a gamepad-controlled character instead of free-camera movement. Full Xbox mode repurposes buttons for the game and disables the VR pointer; the settings chord remains available.

**[Full operating guide: panel edges, locking, camera targets, keyboard and Xbox mapping](docs/CONTROLS.md)**

**[Settings guide: every option, defaults, performance costs and animation-tearing tests](docs/SETTINGS.md)**
The **More** tab's **Render in Update** changes drawing order; **Per-render skin matrices** addresses per-render skinned-mesh calculation. Both default Off, solve different problems and should be tested separately. Neither is a universal ghosting fix.

## OnlyUI: Important Boundaries

OnlyUI is optional. Start with **FullFrame**, confirm that the full menu and pointer work, then try **UiOnly** under panel capture settings.

It works automatically in some games; others need game-specific investigation, potentially with a local AI assistant. **AI is not required for ordinary VR use, is not bundled, and cannot guarantee a fix.** Missing/stripped UI APIs, custom shaders, UI Toolkit/IMGUI and pre-composited 3D/UI textures can require runtime changes rather than configuration. Do not repeatedly guess settings or erase black pixels from the interface.

If text/buttons disappear, masks break, colours change or the scene is still included, switch back to FullFrame and read **[OnlyUI compatibility and bounded local-AI assistance](docs/ONLYUI.md)**. Never trade away readable, clickable UI just to obtain transparency.

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

The complete installer includes the tested BepInEx loader payloads and required mod dependencies. SteamVR, headset streaming software and game assemblies are not included. An existing BepInEx loader is reused without reinstalling it. An unlisted IL2CPP build can be reused after confirmation if the backend matches, the bootstrap is enabled and complete, and game interop has already been generated. This does not guarantee VR compatibility. Leftover or incomplete bootstrap files still block installation. Camera/rendering compatibility and OnlyUI may still need settings changes.

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

[![Support development on Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/iyanzl)

Support is entirely optional: it does not unlock features, buy a game compatibility guarantee, or replace the upstream licenses.
