# Controller Guide

[简体中文](CONTROLS.zh-CN.md) | [日本語](CONTROLS.ja.md) | [Home](../README.md)

These are the defaults in the shared Mono/IL2CPP runtime, using Quest / Touch names. L3 and R3 mean pressing the left/right stick. Other controllers depend on their SteamVR bindings. User configuration can override A/B and the settings shortcut.

## First Session

1. Connect the headset through SteamVR and use the game's `Start-VR.bat`. Allow first-time IL2CPP interop generation to finish.
2. Let go of all controller buttons and centre both sticks once so input can arm safely.
3. Look at the left-hand panel and point at it with the right-hand ray. A or the right trigger is left click; B is right click. Holding a button holds the corresponding mouse button.
4. Press L3+R3 together to open settings. Release both before using them again. F10 is the default keyboard alternative; change **Settings key** if the game already uses it.
5. Keep FullFrame capture until ordinary UI and clicks work. OnlyUI is an optional later compatibility step, not a startup requirement.

## Three Different Panel States

| State | How to enter | Behaviour |
| --- | --- | --- |
| Left hand | Default, or Y again after world placement | Follows left-hand position and tilt. Front-layer rendering keeps it usable close to the hand. Size and hand-relative offsets/angles are configurable. |
| Placed in the world | Y while the L3 view is unlocked | Stays where it was placed. Scene depth allows intervening 3D objects to occlude it. Y returns it to the left hand. |
| Original-game interaction view | Click and release L3 | Locks virtual movement to the game's source view and places the panel in that view. This is intended for UI that must line up with the original scene. Physical head rotation remains available; virtual movement/grabbing stops. L3 again restores the previous VR viewing offset. |

**Y placement and L3 view locking are not the same feature.** Unlock L3 before trying to move a panel between the hand and the world. A game may have orthographic cameras, unusual projections or scene-specific camera changes; matching that original view is not guaranteed in every game.

X shows/hides the panel. The settings menu can still be opened if the panel was hidden. In full Xbox mode X/Y are gamepad buttons rather than panel commands.

### Move and Resize

- Point at the **empty outer margin**, not at the game picture. Hold A or right trigger and move the controller to drag a world-placed panel.
- Point at a **corner of the outer margin**, hold A or right trigger, then move the hand toward/away from the panel to resize. Release to save the size. Corner resizing also works while hand-held.
- L3-locked panels do not allow edge dragging/resizing; their geometry is fitted to the source view. Unlock first.
- When a panel is world-placed, ordinary scene occlusion applies. Its visibility rules deliberately differ from the hand-held/locked front-layer panel.

## Movement, Angles and World Scale

Close settings and unlock L3 before free movement or angle adjustments.

| Action | Input |
| --- | --- |
| Walk in free/game-camera view | Left stick; movement follows current head facing, projected onto the horizontal plane |
| Snap turn | Right stick left/right, then centre it before the next turn; default step 30 degrees |
| Grab the world | Hold grip + trigger on one controller; move the hand, then release |
| Look up/down virtually | Right **grip** + right stick up/down (not the right trigger) |
| Adjust roll | Right grip + right stick left/right |
| Level the horizon | Right grip + press R3 |
| Scale the world | Hold both triggers, then press and hold A; change the distance between hands; release all to finish |
| Reset scale to 1 | While holding both triggers, double-tap A (within about 0.4 seconds) |

Both grips can be used instead of both triggers for the scale chord. **Do not hold all four grip/trigger buttons for scaling**: that is the Xbox-mode chord. Point away from clickable UI before beginning a chord, and release all buttons between operations to avoid unintended clicks or chord reuse.

World scale changes perceived world size, not the desktop resolution or a normal camera zoom. The default maximum is 8; **Scale maximum** can be configured up to 100. The available range is 0.1 to the chosen maximum. Large values can make navigation or game-specific effects impractical.

## Free, First-Person and Third-Person Views

Open the **View** settings and select the viewing mode. Refresh targets after new characters appear, then cycle/select the intended character.

- **GameCamera / free view**: follows the game's camera according to the follow-game-camera setting; virtual locomotion can move away from it. L3 returns to its interaction view.
- **First person**: places the VR origin near the selected head. Adjust forward/up/side offsets to keep the face out of the view. Heading follow uses character-root yaw rather than animated head pitch/roll; physical headset look remains tracked. Horizontal/vertical/yaw lag settings buffer character motion, not the headset's own tracking.
- **Third person**: adjust distance, height, shoulder offset, follow lag and whether to follow the character's yaw.

Automatic target detection is not guaranteed. A name such as Hips can still resolve to a head anchor on some skeletons; inspect the actual result. The camera can follow an NPC, but that does not give control of that NPC.

**Left stick controls character** routes that stick to virtual gamepad movement in an active character-follow view instead of moving the free camera. It requires a compatible virtual-controller driver and native gamepad movement support in the game. Turn it off to use free-camera movement again.

## Xbox-Controller Mode

Hold **both grips and both triggers** for at least 0.5 seconds, then release all buttons. Repeat the same chord to return to VR tools. The **Xbox chord enabled** setting must be on, and a compatible virtual-controller driver must already be installed. The installer does not install drivers.

| VR input | Xbox input |
| --- | --- |
| A / B / X / Y | A / B / X / Y |
| Left / right grip | LB / RB |
| Left / right trigger | LT / RT, analogue |
| Left / right stick | Left / right stick axes |
| L3 / R3 | Left / right stick press; L3+R3 is reserved for the plugin settings chord |

The VR pointer and free-world controls are disabled while full Xbox mode owns the input. The panel can remain visible. L3+R3 still opens settings. If switching fails, inspect the Xbox status/log rather than repeatedly holding the chord; missing drivers or a game that does not support gamepads cannot be fixed by that gesture.

## Virtual Keyboard

Enable **Show virtual keyboard** in settings. It contains 104 keys including F1-F12, navigation keys, modifiers and a numeric keypad.

- In L3-locked mode it sits below the panel, tilted toward the player, with fixed keyboard presentation.
- Otherwise it sits to the right of the panel, outside its white margin, with its lower edge aligned to the game image. Drag the keyboard's own edge with A/right trigger to resize; no extra white resize frame is needed.
- Point at a key and select using A/right trigger. Ctrl/Alt/Shift/Win are one-shot modifiers for a following key. Ordinary keys can repeat while held. Hiding the keyboard or losing focus releases injected keys.

## Troubleshooting Shortcuts

- Cannot move? Close settings, unlock L3 and check whether Xbox mode or character-stick routing owns the input.
- Wrong character or heading? Refresh/select a target and adjust the view offsets/heading follow. Return to GameCamera if there is no valid character target.
- Game UI fails only in the VD desktop view? Check VD's **Use touch input** option; disabling it resolved that input mismatch in local testing. This is a separate issue from the VR panel's pointer.
- Blurred or torn animation? Test the exposed motion-blur/depth-of-field, Update scheduling and skin-matrix compatibility options one at a time. No combination is a universal fix, and per-render skin matrices can cost performance.
- OnlyUI loses text, colours or buttons? Return to FullFrame and use the [OnlyUI guide](ONLYUI.md). Do not solve it by making every black pixel transparent.
