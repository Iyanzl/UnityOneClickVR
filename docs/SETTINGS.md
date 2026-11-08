# Settings Guide

[简体中文](SETTINGS.zh-CN.md) | [日本語](SETTINGS.ja.md) | [Controller guide](CONTROLS.md) | [Home](../README.md)

For the shared Mono/IL2CPP menu in R20. Defaults below apply to **new configurations**; upgrading preserves saved preferences. Older game-specific plugins may use different names.

## Open, Navigate and Save

- Press **L3+R3** together, or **F10** on the PC keyboard, to toggle settings. L3/R3 mean pressing the sticks. The keyboard shortcut is configurable.
- There are five tabs: **Main, View, UI, Keys, More**. Use the bottom `<` / `>` buttons for additional pages, especially in a small game window.
- These menu settings apply live and autosave. Scheduling changes take effect at the next frame boundary; target discovery and compatibility scans may take a few seconds. Close settings before comparing movement, which pauses while the menu is open.
- Per-game configuration: `BepInEx/config/codex.hybrid-mono.cfg` or `codex.hybrid-il2cpp.cfg`. Close the game and back up before editing files directly. Do not copy another game's entire configuration blindly.

## Animation Tearing: Two Different Workarounds

Both switches are under **More** and default to **Off**. Skinning means calculating bone-driven mesh deformation, **not masking, cutting out pixels or hiding the model**.

### Render in Update (Off: LateUpdate)

Off runs the VR loop in `LateUpdate`; On moves the complete pose/input/stereo-render loop to the earlier `Update` phase. This changes **when drawing happens relative to the game's animation and camera work**. It does not replace eye cameras, recalculate skin matrices or lower resolution.

Try it alone when large movements cause head/body separation, displaced hair/clothes or unnatural animation. Earlier drawing helps some games; others need later bone/camera updates, so it can instead produce stale animation or camera movement. `LateUpdate` runs after that frame's `Update`, but does not guarantee that every custom game update has completed. Later is not automatically better.

### Per-render skin matrices (experimental)

Configuration key: `[Rendering] Recalculate skin matrices per render`. Where Unity exposes the required API, this requests **skin-matrix recalculation for each render**, rather than reusing an unsuitable result across camera/eye draws. Test it when skinned hair/body/clothing disagree in VR but look relatively correct on the desktop.

It does not rewrite bones, cloth or physics, and does not guarantee identical animation in every view. Extra calculations can noticeably reduce FPS; cost depends on the game and models. Disabling it or leaving VR attempts to restore values changed by the plugin. A missing Unity API cannot be enabled by configuration; check `skinned matrices` support messages in the log.

### Test One Variable at a Time

Use the same scene and animation. Close settings and observe for several seconds after each change.

| Test | Render in Update | Per-render skin matrices | Interpretation |
| --- | --- | --- | --- |
| Baseline | Off | Off | Keep both off if there is no problem. |
| Scheduling only | On | Off | Keep this alone if it fixes animation without regressions. |
| Skin matrices only | Off | On | If this works, compare same-scene FPS before accepting the cost. |
| Combined | On | On | Try only if a single workaround is insufficient; not necessarily better. |

In past local tests, Update scheduling fixed one SummerInHeat build, whereas a similar-looking issue in Mayonaka still needed skin-matrix compatibility. Those are version/scene-specific observations, not automatic rules for those game names.

**Neither is a universal ghosting fix.** Geometry displacement differs from whole-frame stutter, display persistence, ASW/SSW artifacts and motion blur. Do not enable every workaround just because motion looks less smooth.

## Main

<!-- menu-rows: View, Panel, VirtualKeyboard, Width, Scale, WalkSpeed, ShowHands, DisableDepthOfField, DisableMotionBlur, DesktopMirror, Language -->

| Setting | New default | Purpose and caution |
| --- | --- | --- |
| Camera mode | GameCamera | Free/game-camera view, first person or third-person follow. Character views require a valid target. This is not a VR rendering-backend selector. |
| 2D panel / X | On | Show/hide the game panel, like X in normal VR mode. Does not exit VR or hide settings itself. |
| Show virtual keyboard | Off | Display the 104-key keyboard, below the panel while L3-locked and to its right otherwise. See [keyboard controls](CONTROLS.md#virtual-keyboard). |
| Panel width (m) | 0.8 | Panel presentation size, not capture resolution. Range 0.2–2 m; L3 source-view alignment also constrains locked placement. |
| World scale | 1 | Perceived world size, not camera focal zoom or resolution. Menu minimum 0.1; maximum is set by Maximum scale. Extreme scale can magnify game-effect/projection issues. |
| Walk speed | 1.5 | Base free-camera stick speed, adjusted with world scale; not the game's native character speed. Suspended during L3 lock. |
| Show hands | On | Controller-tracked glove meshes with approximate trigger/grip finger curl. Not optical hand tracking or physical hand collision. |
| Disable depth of field | On | **On disables** recognised game depth of field, reducing blur away from the original focus distance. Covers supported PPv2/URP/HDRP paths, not every custom blur. |
| Disable motion blur | On | **On disables** recognised motion blur for captured frames and both eyes, avoiding false multi-view camera-history motion. Does not change resolution or skinning. |
| Desktop mirror | On | Show the desktop game view, including VD desktop mode. Off stays black during VR; leaving VR restores it. Panel capture can still require desktop rendering, so Off does not guarantee removal of a complete render pass. |
| Language | English | Cycle English, Simplified Chinese and Japanese. Changes the plugin interface, not game text. Existing preferences are preserved. |

Depth-of-field/motion-blur suppression is temporary during VR. Turning it off or leaving VR restores normal effect evaluation; game post-processing assets are not permanently edited.

## View

<!-- menu-rows: FollowStory, Target, TargetActions, FirstForward, FirstUp, FirstSide, FirstFollowHeading, CharacterStick, VerticalLag, HorizontalLag, YawLag, ThirdDistance, ThirdHeight, ThirdShoulder, FollowHeading, ThirdLag -->

Character offsets and follow distances use **game-world units**, which need not equal real metres in every game. Follow lag smooths the moving character anchor, not physical headset tracking.

| Setting/action | New default | Purpose and caution |
| --- | --- | --- |
| Free view follows game camera | On | Respond to original game-camera changes in free view; Off makes free view more independent. Separate from character-heading follow. Scene transitions can still require a valid camera to be found again. |
| Follow target | Not selected | Cycle discovered targets; judge the actual eye position, not only Head/Hips names. The character must exist in the scene. |
| Refresh targets; Recenter follow | Actions | Rescan when a new character appears. Recenter rebuilds the follow offset around the selected target; it does not reset SteamVR room calibration or select a different target. |
| First eye forward | 0.2 | Move the eye position in front of the face to reduce self-occlusion; range -2–2. |
| First eye up | 0.05 | Height relative to the head; range -2–2. |
| First eye side | 0 | Lateral character-relative offset; range -2–2. |
| First follow character yaw | On | Follow character-root horizontal heading, not animated head pitch/roll. Physical head turns remain immediate. |
| Follow: left stick to character (Xbox) | On | Route the left stick to native gamepad movement during an active character view instead of moving the free camera. Requires a compatible virtual-controller driver and a game with gamepad movement. Cannot make an arbitrary NPC controllable. |
| Character vertical lag (s) | 0.06 | Smooth first-person target height changes. Zero follows immediately; higher values smooth more but lag more, up to 0.5 s. |
| Character horizontal lag (s) | 0.06 | Same for horizontal motion; not a stick dead zone or physical-head tracking filter. |
| Character yaw lag (s) | 0.1 | Smooth target heading changes, 0–0.5 s. |
| Third distance | 2 | Third-person distance behind the target's head, 0.1–15. |
| Third height above head | 0.2 | Height relative to the head, not the ground, -3–8. |
| Third shoulder | 0.4 | Lateral third-person offset, -3–3. |
| Third follow character yaw | Off | On moves the over-shoulder bearing with character heading; Off keeps the heading established on entering follow. |
| Third follow lag (s) | 0.1 | Smooth third-person position and heading, 0–0.5 s. Too much can feel like the camera trails behind. |

If a target or heading is wrong, or the desired character is not found, return to GameCamera before trying extreme offsets.

## UI

<!-- menu-rows: CaptureMode, CaptureStatus, Forward, Up, Pitch, Yaw, Roll, PointerPitch -->

| Setting/status | New default | Purpose and caution |
| --- | --- | --- |
| 2D capture | FullFrame | FullFrame keeps the complete game image. UiOnly attempts supported uGUI replay and preserves UI images. It is not black-pixel removal or a universal cutout-depth setting. Establish working FullFrame interaction first. |
| Capture status | FullFrame selected | UiOnly active means transparent replay is running. Full-frame fallback means the complete frame is retained for UI safety; see the log for the reason. Selecting UiOnly alone does not establish support. |
| Grip forward (m) | 0.04 | Hand-held panel offset along the grip's forward axis, -0.3–0.7 m. This is a controller-local axis, not world-horizontal forward. |
| Grip up (m) | 0.08 | Grip-local upward offset, -0.3–0.7 m. The axis rotates with the controller. |
| Panel pitch | 90° | Hand-held panel up/down tilt relative to the controller, -180–180°. Does not rotate the 3D world. |
| Panel yaw | 0° | Hand-held panel left/right heading, -180–180°. |
| Panel roll | 0° | Hand-held panel side tilt, -180–180°. |
| Pointer pitch | 0° | Up/down correction relative to the right controller, -90–90°. Not a fix for an incorrect overall head/hand coordinate frame. |

These offsets mainly affect hand-held placement; world pinning and L3 source-view lock have their own placement logic. Hand-held/L3-locked tools remain in front, while world placement uses scene depth. Transparency and spatial occlusion are separate features. See [OnlyUI limitations](ONLYUI.md).

## Keys

<!-- menu-rows: A, B, Xbox, Snap, GrabSpeed, PitchSpeed, Shortcut -->

| Setting | New default | Purpose and caution |
| --- | --- | --- |
| A; B | Left; Right | Cycle None/Left/Right/Middle mouse bindings. Changes these two buttons, not every combination gesture; right trigger retains its pointer-left-click use. |
| Xbox chord enabled | On | Hold both grips and both triggers for at least 0.5 s, then release all; repeat to return. Requires a compatible driver and game support. Not the world-scale gesture. |
| Snap turn degrees | 30° | Angle per right-stick snap turn, 10–90°; not smooth-turn speed. |
| Grab speed | 1 | World-drag displacement factor for one-hand grip+trigger, 0.1–10. Larger values move the world farther for the same hand movement; applied with world scaling. |
| Pitch / roll speed | 45°/s | Virtual-angle adjustment speed with right grip+right stick, 5–120°/s. Not right trigger or headset sensitivity. Right grip+R3 levels roll without undoing intentional pitch. |
| Desktop shortcut | F10 | Toggle settings; menu cycles F6/F7/F8/F9/F10/F12. Change if it conflicts with the game. Does not alter L3+R3. |

See the [controller guide](CONTROLS.md) for panel pinning, scale/reset, Xbox mapping and keyboard input.

## More

<!-- menu-rows: SingleFrameQueue, CaptureRate, PreserveOriginalPass, RenderInUpdate, SkinnedMatrices, ScaleMaximum, FollowStatus, XboxStatus -->

| Setting/status | New default | Purpose and caution |
| --- | --- | --- |
| Single-frame queue | Off | Limit the D3D11/D3D12 graphics-driver queue to one frame. Can reduce input-to-display latency, but can lower FPS or expose frame-time variation. Off uses the existing `-1` policy. Not an FPS cap; does not change headset Hz, ASW/SSW or resolution. Compare in the same scene. |
| Panel capture rate | 30 Hz | Menu cycles 15/30/60 Hz. Only limits game-image sampling, not headset FPS or hand-pose updates. Higher values update content sooner but can add capture work. Config accepts whole-number 10–90 Hz. |
| Preserve original camera pass | Off | Keep the source game camera drawing even with the desktop hidden and no capture due, preserving callbacks some games use for gaze, animation or effects. May cost another pass and lower FPS; not a guaranteed tearing fix. Test only when source callbacks may be needed. Independent of Desktop mirror. |
| Render in Update | Off (LateUpdate) | Move the complete VR loop's timing; see the detailed explanation and comparison above. Does not also enable skin-matrix compatibility. |
| Per-render skin matrices | Off | Recalculate supported skinned-mesh matrices per render; potentially significant FPS cost and requires the Unity API. See above. |
| Maximum scale | 8 | Upper bound for world-scale adjustment, 1–100. Not the current scale or a resolution/quality multiplier; current scale is on Main. |
| Follow state | Runtime status | L3 locked, active character follow or game camera. Read-only; helps explain why movement/offset changes may not apply. |
| Xbox status | Actual connection | Connected, not connected or error. Read-only. Enabling the chord does not install a driver or guarantee a connected virtual controller. |

### Desktop Visibility Is Not Camera Callback Preservation

| Desktop mirror | Preserve original camera pass | Result |
| --- | --- | --- |
| On | Off (normal default) | Desktop visible; no additional requirement to draw hidden/no-capture frames. |
| Off | Off | Desktop black; source rendering may be skipped when no capture is needed, not necessarily all work. |
| Off | On | Desktop still black, but source render callbacks are kept; useful for diagnosis and potentially more expensive. |

Preserving the source pass replaces neither Update scheduling nor skin-matrix compatibility. It is not recommended On for every game.

## Reporting a Problem

Record the game/Unity version, Mono or IL2CPP, scene, both compatibility-switch states, whether the issue occurs only during animation, same-scene FPS and what happens after restoring defaults. Remove personal paths/accounts before sharing logs. Do not simultaneously change reprojection, capture, camera and skinning settings and then attribute the result to one switch.

Based on the current [settings definitions](../HybridVR/Adapters/Il2CppOpenVr/InteractionSettings.cs), [menu catalog](../HybridVR/Adapters/Il2CppOpenVr/MenuCatalog.cs) and [menu actions](../HybridVR/Adapters/Il2CppOpenVr/InteractionMenu.cs). This guide adds no runtime switch or default change.
