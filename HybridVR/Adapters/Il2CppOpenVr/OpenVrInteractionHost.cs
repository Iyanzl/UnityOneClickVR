using System;
using System.Collections;
using System.IO;
using HybridVR.Shared;
using HybridVR.Runtime;
using UnityEngine;
using Uuvr.OpenVR;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed partial class OpenVrInteractionHost : IDisposable
{
    private readonly InteractionSettings _settings;
    private readonly OpenVrActions _input;
    private readonly InteractionSession _session = new();
    private readonly TrackedRigRuntime _motion = new();
    private readonly FollowCameraRuntime _followCamera = new();
    private readonly RigTransitionProbe _transitionProbe = new();
    private readonly FollowStickRouting _followStick = new();
    private float _nextCharacterXboxAttempt;
    private readonly FollowTargets _targets = new();
    private FollowViewMode _requestedView;
    private bool _targetScanPending = true, _recenterFollow;
    private float _nextTargetScan;
    private readonly VirtualXboxOutput _xbox = new();
    private readonly GameMouse _mouse = new();
    private readonly KeyboardPresentation _keyboard = new();
    private readonly MenuPointerInput _menuInput = new();
    private int _sceneSnapshotCameraId;
    private bool _sceneSnapshotWritten;
    private bool _keyboardPressOwned;
    private readonly InputClickDiagnostics? _clickTrace;
    private readonly HandPresentation _hands = new();
    private readonly DepthOfFieldSuppression _depthOfField = new();
    private readonly MotionBlurSuppression _motionBlur = new();
    private readonly SkinnedMatrixCompatibility _skinMatrices = new();
    private readonly GameFrameCapture _capture;
    private readonly PanelPresentation _panel;
    private readonly Transform _rig;
    private bool _started, _pinned, _menu, _disposed;
    private Vector3 _anchorPosition, _menuPosition, _head;
    private Quaternion _anchorRotation, _menuRotation, _headRotation;
    private float _scale;
    private double _nextLog;
    private int _lastButtons = -1;
    private bool _lastInputValid;
    private bool _vrInputAvailable = true, _focusWarning, _lastPanelClick;
    private Camera? _source;
    private int _sourceId;
    private Vector3 _sourcePosition;
    private Quaternion _sourceRotation;
    private bool _resizing, _draggingPanel;
    private float _resizeDistance, _resizeWidth, _resizeCurrentWidth;
    private Vector3 _panelGrabOffset;
    private Quaternion _panelGrabRotation;
    internal bool Active => _started && _settings.Enabled.Value && !_disposed;
    internal float WorldUnits => Active ? _rig.localScale.x : 1;
    internal bool SingleFrameQueueEnabled => _settings.SingleFrameQueueEnabled;
    internal bool RenderInUpdateEnabled => _settings.RenderInUpdate.Value;

    internal OpenVrInteractionHost(string directory)
    {
        try
        {
#if HYBRID_MONO
        _settings = new InteractionSettings(Path.Combine(directory, "codex.hybrid-mono.cfg"));
#else
        _settings = new InteractionSettings(Path.Combine(directory, "codex.hybrid-il2cpp.cfg"));
#endif
        _clickTrace = InputClickDiagnostics.Create(_mouse);
        _input = new OpenVrActions(directory);
        _capture = new GameFrameCapture(_settings);
        _panel = new PanelPresentation { Hands = _hands, Keyboard = _keyboard };
        var go = new GameObject("HybridVR owned interaction origin");
        Object.DontDestroyOnLoad(go); _rig = go.transform;
        _motion.ScaleCommitted = value => { _settings.Scale.Value = value; _scale = value; };
        _motion.LockChanged = locked => { _mouse.Release(); _pinned = false; OpenVrDiagnostics.Write("default view locked=" + locked); };
        OpenVrDiagnostics.Write("IL01-G shared interaction created; capture=game-frame, no extra Camera.Render");
        }
        catch { Dispose(); throw; }
    }
    internal IEnumerator CaptureFrames() => _capture.Frames();
    internal void ObserveInputUpdate() => _clickTrace?.Sample(false, _menu);
    internal void ObserveInputGui() => _clickTrace?.Gui(_menu);
    internal void Suspend()
    {
        _capture.ResumeDesktop();
        _keyboard.Visible = false; _keyboard.CancelResize(); _keyboard.Input.Suspend(); _keyboardPressOwned = false;
        _menuInput.Reset();
        _resizing = _draggingPanel = _lastPanelClick = false;
        _depthOfField.Update(false, Time.unscaledTime);
        _motionBlur.Update(false, Time.unscaledTime);
        _skinMatrices.Update(false, Time.unscaledTime);
        _session.Suspend(); _followStick.Reset(); _mouse.Release(); _xbox.Neutral();
        _panel.HasRay = _panel.HasCursor = false;
        _hands.Hide();
    }
    internal void Update(Camera source, Utils.RigidTransform head, int origin)
    {
        if (_disposed) return;
        _clickTrace?.Sample(true, _menu);
        _head = head.pos; _headRotation = head.rot;
        if (!_settings.Enabled.Value) { Suspend(); _capture.Requested = false; return; }
        _depthOfField.Update(_settings.DisableDepthOfField.Value, Time.unscaledTime);
        _motionBlur.Update(_settings.DisableMotionBlur.Value, Time.unscaledTime);
        _skinMatrices.Update(_settings.SkinnedMatrices.Value, Time.unscaledTime);
        if (!_started)
        {
            _rig.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            _scale = _settings.Scale.Value;
            _rig.localScale = Vector3.one / _scale;
            _started = true;
        }
        int sourceId = source.GetInstanceID();
        Vector3 beforeSourceEye = _rig.TransformPoint(_head);
        if (sourceId != _sourceId) _targetScanPending = true;
        if (_targetScanPending && _settings.View.Value != FollowViewMode.GameCamera && Time.unscaledTime >= _nextTargetScan)
            RefreshTargets();
        var followAnchor = _targets.Read();
        bool followRequested = _settings.View.Value != FollowViewMode.GameCamera && followAnchor.Valid;
        if (_sourceId != 0 && sourceId != _sourceId)
        {
            // Carry the user's relative offset across camera ownership changes, not the old room's coordinates.
            if (_settings.FollowStory.Value && !_motion.DefaultViewLocked && !followRequested && !_followCamera.Active)
            {
                var delta = source.transform.rotation * Quaternion.Inverse(_sourceRotation);
                _rig.SetPositionAndRotation(source.transform.position + delta * (_rig.position - _sourcePosition), delta * _rig.rotation);
            }
            _pinned = _resizing = _draggingPanel = false;
            _keyboard.Input.Suspend(); _keyboardPressOwned = false;
            _menuInput.Reset();
            _mouse.Release(); _session.Suspend();
            if (_menu) PlaceMenu();
            OpenVrDiagnostics.Write($"interaction source changed: {_sourceId}->{sourceId}, panel returned to hand");
        }
        _source = source; _sourceId = sourceId;
        _sourcePosition = source.transform.position; _sourceRotation = source.transform.rotation;
        Vector3 afterSourceEye = _rig.TransformPoint(_head);
        if (_requestedView != _settings.View.Value)
        {
            _requestedView = _settings.View.Value;
            _motion.Reset(); _session.Suspend(); _mouse.Release();
            _pinned = _resizing = _draggingPanel = false;
            OpenVrDiagnostics.Write("follow view requested: " + _requestedView + ", target=" + _targets.Status);
        }
        int key = (int)_settings.Shortcut.Value;
        int virtualKey = key >= (int)KeyCode.F1 && key <= (int)KeyCode.F15 ? key - (int)KeyCode.F1 + 0x70 : char.ToUpperInvariant((char)key);
        if (_mouse.Shortcut(virtualKey)) ToggleMenu();
        if (_scale != _settings.Scale.Value)
        {
            _motion.SetWorldScale(_rig, _head, _settings.Scale.Value);
            _scale = _settings.Scale.Value;
        }
        var sample = _input.Read(Time.frameCount, Time.unscaledTime, origin);
        _mouse.ObserveDesktop(Time.unscaledTime);
        _vrInputAvailable = VrInputAvailable();
        bool focus = _mouse.Foreground;
        sample.Valid &= focus && _vrInputAvailable && !_mouse.DesktopInUse;
        _session.Bindings.A = _settings.A.Value; _session.Bindings.B = _settings.B.Value;
        _session.Bindings.XboxEnabled = _settings.Xbox.Value;
        _session.Bindings.HorizonControlsEnabled = true;
        var actions = _session.Update(sample, _menu);
        int buttons = (int)(_input.Frame.Left.Buttons | (_input.Frame.Right.Buttons << 3)) |
            (sample.LeftTrigger ? 64 : 0) | (sample.RightTrigger ? 128 : 0) |
            (sample.LeftGrip ? 256 : 0) | (sample.RightGrip ? 512 : 0);
        if (buttons != _lastButtons || sample.Valid != _lastInputValid)
        {
            OpenVrDiagnostics.Write($"input edge: buttons=0x{buttons:X}, valid={sample.Valid}, foreground={focus}, mouse={actions.MouseLeft}/{actions.MouseRight}/{actions.MouseMiddle}, blocked={actions.Blocked}");
            _lastButtons = buttons; _lastInputValid = sample.Valid;
        }
        if (actions.ToggleMenu) ToggleMenu();
        if (actions.ToggleXbox)
        {
            if (_session.XboxActive) _session.SetXboxActive(false);
            else if (_xbox.Connect()) _session.SetXboxActive(true);
            _mouse.Release();
        }
        if (actions.TogglePanel) _settings.Panel.Value = !_settings.Panel.Value;
        if (actions.ToggleAnchor)
        {
            _resizing = _draggingPanel = false;
            _pinned = !_pinned;
            _anchorPosition = _panel.Position; _anchorRotation = _panel.Rotation;
        }
        var left = _input.Frame.Left.Valid != 0 ? new Utils.RigidTransform(_input.Frame.Left.GripPose) : new Utils.RigidTransform(Vector3.zero, Quaternion.identity);
        var right = _input.Frame.Right.Valid != 0 ? new Utils.RigidTransform(_input.Frame.Right.GripPose) : new Utils.RigidTransform(Vector3.zero, Quaternion.identity);
        _motion.Update(_rig, source, true, _head, _headRotation,
            _input.Frame.Left.Valid != 0, left.pos, _input.Frame.Right.Valid != 0, right.pos,
            FollowStickRouting.CameraSample(sample, _settings.CharacterStick.Value && (followRequested || _followCamera.Active)), actions, _menu, new RigInteractionSettings
            {
                FollowStoryCamera = _settings.FollowStory.Value && !followRequested && !_followCamera.Active,
                LockHeadTranslation = true, WalkSpeed = _settings.WalkSpeed.Value,
                GrabSensitivity = _settings.GrabSpeed.Value, SnapDegrees = _settings.Snap.Value,
                PitchSpeed = _settings.PitchSpeed.Value, ScaleMaximum = _settings.ScaleMaximum.Value
            });
        Vector3 beforeFollowPosition = _rig.position;
        Vector3 afterMotionEye = _rig.TransformPoint(_head);
        Quaternion beforeFollowRotation = _rig.rotation;
        bool followWasActive = _followCamera.Active;
        _followCamera.Update(_rig, source, _head, _headRotation, _settings.View.Value, followAnchor,
            new FollowCameraSettings {
                FirstOffset = new Vector3(_settings.FirstSide.Value, _settings.FirstUp.Value, _settings.FirstForward.Value),
                VerticalLag = _settings.VerticalLag.Value, Distance = _settings.ThirdDistance.Value,
                HorizontalLag = _settings.HorizontalLag.Value, YawLag = _settings.YawLag.Value,
                FirstFollowHeading = _settings.FirstFollowHeading.Value,
                Height = _settings.ThirdHeight.Value, Shoulder = _settings.ThirdShoulder.Value,
                ThirdLag = _settings.ThirdLag.Value, FollowHeading = _settings.FollowHeading.Value
            }, _motion.DefaultViewLocked, false, _recenterFollow);
        _transitionProbe.Observe(source, _rig, _head, beforeSourceEye, afterSourceEye, afterMotionEye,
            _settings, _motion.DefaultViewLocked, _followCamera.Active,
            actions.GrabLeft || actions.GrabRight || actions.Pitch || actions.ScaleHeld ||
            sample.LeftStickX != 0 || sample.LeftStickY != 0 || sample.RightStickX != 0,
            origin);
        if (_menu && (followRequested || followWasActive))
        {
            Quaternion delta = _rig.rotation * Quaternion.Inverse(beforeFollowRotation);
            _menuPosition = _rig.position + delta * (_menuPosition - beforeFollowPosition);
            _menuRotation = delta * _menuRotation;
        }
        if (followWasActive != _followCamera.Active)
        {
            _mouse.Release(); _session.Suspend(); _menuInput.Reset();
            _pinned = _resizing = _draggingPanel = false;
            actions = new InteractionActions { Blocked = true };
        }
        if (!_motion.DefaultViewLocked) _recenterFollow = false;
        bool characterInput = FollowStickRouting.CanControl(_settings.CharacterStick.Value, followRequested && _followCamera.Active,
            _motion.DefaultViewLocked, _menu, _session.XboxActive, sample, actions);
        var character = _followStick.Update(sample, _settings.View.Value, followAnchor.Id, characterInput);
        if (character.Valid && !_xbox.Connected)
        {
            if (Time.unscaledTime >= _nextCharacterXboxAttempt)
            {
                _nextCharacterXboxAttempt = Time.unscaledTime + 15;
                _xbox.Connect();
            }
            // A newly attached device must not start with an already held stick.
            _followStick.Reset(); character = default;
        }
        bool fullXbox = actions.SubmitXbox && sample.Valid && !_menu;
        _xbox.Update(fullXbox || character.Valid, fullXbox ? sample : character,
            fullXbox ? _input.Frame.Left.Trigger : 0, fullXbox ? _input.Frame.Right.Trigger : 0);
        _panel.Units = _rig.localScale.x;
        _panel.Image = _capture.Texture;
        _panel.Visible = (_settings.Panel.Value || _menu) && _capture.HasFrame;
        _panel.Width = (_resizing ? _resizeCurrentWidth : _settings.Width.Value) * _panel.Units;
        float aspect = _capture.Height > 0 ? (float)_capture.Width / _capture.Height : 16f / 9f;
        _panel.Height = _panel.Width / aspect;
        if (_menu)
        {
            _panel.Position = _menuPosition; _panel.Rotation = _menuRotation;
            _panel.Width = 1.25f * _panel.Units; _panel.Height = _panel.Width / aspect;
        }
        else if (_motion.DefaultViewLocked)
        {
            float distance = .8f * _panel.Units;
            _panel.Position = source.transform.position + source.transform.forward * distance;
            _panel.Rotation = source.transform.rotation;
            _panel.Height = 2 * distance * Mathf.Tan(source.fieldOfView * Mathf.Deg2Rad * .5f);
            _panel.Width = _panel.Height * aspect;
            var projection = source.projectionMatrix;
            var viewport = source.rect;
            // Orthographic sources cannot match a perspective VR world at every depth.
            // Keep their prior presentation instead of inventing a focus distance.
            if (!source.orthographic && Mathf.Abs(projection.m01) < .0001f && Mathf.Abs(projection.m10) < .0001f &&
                LockedPanelProjection.TryFit(distance, projection.m00, projection.m11, projection.m02, projection.m12,
                    viewport.x, viewport.y, viewport.width, viewport.height, out var fit))
            {
                _panel.Position += source.transform.rotation * new Vector3((float)fit.X, (float)fit.Y, 0);
                _panel.Width = (float)fit.Width; _panel.Height = (float)fit.Height;
            }
        }
        else if (_pinned) { _panel.Position = _anchorPosition; _panel.Rotation = _anchorRotation; }
        else if (_input.Frame.Left.Valid != 0)
        {
            _panel.Position = _rig.TransformPoint(left.pos + left.rot * new Vector3(0, _settings.Up.Value, _settings.Forward.Value));
            _panel.Rotation = _rig.rotation * left.rot * Quaternion.Euler(_settings.Pitch.Value, _settings.Yaw.Value, _settings.Roll.Value);
        }
        else _panel.Visible = false;
        _panel.UseSceneDepth = _panel.Visible && ToolDepthPolicy.UsesSceneDepth(_pinned, _motion.DefaultViewLocked, _menu);
        _capture.Requested = _settings.Panel.Value; _capture.MenuOpen = _menu;
        _keyboard.Place(_panel, _motion.DefaultViewLocked,
            _settings.VirtualKeyboard.Value && !_menu && !_session.XboxActive, _settings.KeyboardScale.Value);
        _hands.Update(_settings.ShowHands.Value && _vrInputAvailable && !_mouse.DesktopInUse, _rig, _input.Frame.Left, _input.Frame.Right);
        UpdatePointer(sample, actions, right);
        if (Time.unscaledTime >= _nextLog)
        {
            _nextLog = Time.unscaledTime + 5;
            OpenVrDiagnostics.Write($"interaction: {_input.Status}; foreground={focus}, vrInput={_vrInputAvailable}, desktopInput={_mouse.DesktopInUse}, panel={_panel.Visible}, pinned={_pinned}, lock={_motion.DefaultViewLocked}, menu={_menu}, xbox={_session.XboxActive}, scale={_scale:0.###}; {_capture.Status}; {_capture.DesktopStatus}; {_mouse.Status}; {_mouse.OwnershipStatus}; keyboard={_keyboard.Visible}, keyboardReady={_keyboard.Ready}");
            if (_motion.DefaultViewLocked)
                OpenVrDiagnostics.Write($"locked projection: ortho={source.orthographic}, fov={source.fieldOfView:0.###}, aspect={source.aspect:0.###}, viewport={source.rect}, matrix={source.projectionMatrix}, panel={_panel.Width:0.###}x{_panel.Height:0.###}, capture={_capture.Width}x{_capture.Height}");
            var inverseHead = Quaternion.Inverse(_headRotation);
            var aim = _input.Frame.Right.AimValid != 0 ? new Utils.RigidTransform(_input.Frame.Right.AimPose) : right;
            OpenVrDiagnostics.Write($"tool pose: origin={origin}, leftFromHead={inverseHead * (left.pos - _head)}, rightFromHead={inverseHead * (aim.pos - _head)}, gripForward={inverseHead * right.rot * Vector3.forward}, aimForward={inverseHead * aim.rot * Vector3.forward}, panelPitch={_settings.Pitch.Value}, pointerPitch={_settings.PointerPitch.Value}, resources={_panel.ResourcesReady}");
            OpenVrDiagnostics.Write(_depthOfField.Status);
            OpenVrDiagnostics.Write(_motionBlur.Status);
            OpenVrDiagnostics.Write("hand visuals: " + _hands.Status);
            OpenVrDiagnostics.Write($"tool depth: sceneDepth={_panel.UseSceneDepth}, sourceNear={source.nearClipPlane}, sourceFar={source.farClipPlane}, overlayNear={.005f * _panel.Units}, units={_panel.Units}");
            if (_settings.View.Value != FollowViewMode.GameCamera)
                OpenVrDiagnostics.Write($"follow camera: requested={_settings.View.Value}, active={_followCamera.Active}, target={_targets.Status}, valid={followAnchor.Valid}, id={followAnchor.Id}, locked={_motion.DefaultViewLocked}, firstYaw={_settings.FirstFollowHeading.Value}, characterStick={character.Valid}, {_xbox.Status}");
            OpenVrDiagnostics.Write($"rig frame: source={_sourceId}, sourcePosition={source.transform.position}, sourceEuler={source.transform.eulerAngles}, rigPosition={_rig.position}, rigEuler={_rig.eulerAngles}, units={_rig.localScale.x:0.####}, hmdRaw={_head}, leftValid={_input.Frame.Left.Valid}, rightValid={_input.Frame.Right.Valid}, aimValid={_input.Frame.Right.AimValid}, gripToAim={Vector3.Distance(right.pos, aim.pos):0.000}, panelWorld={_panel.Position}, rayWorld={_panel.RayStart}");
        }
    }
    private bool VrInputAvailable()
    {
        try { return OpenVR.System != null && !OpenVR.System.IsInputFocusCapturedByAnotherProcess() && !(OpenVR.Overlay?.IsDashboardVisible() ?? false); }
        catch (Exception error)
        {
            if (!_focusWarning) { _focusWarning = true; OpenVrDiagnostics.Write("VR focus query unavailable; desktop cursor arbitration retained: " + error.Message); }
            return true;
        }
    }
    internal void PrepareDesktop(Camera source) => _capture.PrepareDesktop(source,
        DesktopFramePolicy.Hide(_settings.DesktopMirror.Value, Active));
    private void UpdatePointer(ControllerSample sample, InteractionActions actions, Utils.RigidTransform grip)
    {
        var hand = _input.Frame.Right;
        bool valid = hand.AimValid != 0 || hand.Valid != 0;
        var aim = hand.AimValid != 0 ? new Utils.RigidTransform(hand.AimPose) : grip;
        Vector3 start = _rig.TransformPoint(aim.pos);
        Vector3 direction = _rig.rotation * aim.rot * Quaternion.Euler(_settings.PointerPitch.Value, 0, 0) * Vector3.forward;
        _panel.HasRay = valid && !_session.XboxActive;
        _panel.RayStart = start; _panel.RayEnd = start + direction * 1.5f * _panel.Units;
        Vector2 uv = default;
        bool planeHit = valid && !_session.XboxActive && _panel.Hit(start, direction, true, out uv);
        var region = planeHit ? PanelHitRegion.Classify(uv.x, uv.y, _panel.Width, _panel.Height, _panel.Units) : PanelRegion.None;
        _panel.CursorRotation = null;
        bool canType = sample.Valid && !actions.Blocked && !_menu && !_session.XboxActive && _keyboard.Visible;
        int key = -1; float keyboardDistance = 0;
        bool keyboardHit = canType && !_mouse.Holding && !_draggingPanel && !_resizing &&
            _keyboard.Hit(start, direction, out key, out keyboardDistance) &&
            (_keyboard.Resizing || region == PanelRegion.None || keyboardDistance < Vector3.Distance(start, _panel.CursorPoint));
        bool selectKey = sample.A || sample.RightTrigger;
        if (_keyboard.Resize(_panel, canType, selectKey, keyboardHit, out bool resized))
        {
            if (resized) _settings.KeyboardScale.Value = _keyboard.ResizeScale;
            _keyboard.Input.Suspend(); _keyboard.Hover = -1;
            if (!selectKey) _keyboard.Input.Update(-1, false, canType, Time.unscaledTime);
            _keyboardPressOwned = selectKey; _lastPanelClick = selectKey;
            _mouse.Release(); _panel.HasCursor = keyboardHit;
            if (keyboardHit)
            {
                _panel.RayEnd = _panel.CursorPoint = _keyboard.HitPoint;
                _panel.CursorRotation = _keyboard.Rotation;
            }
            return;
        }
        if (!canType || !selectKey) _keyboardPressOwned = false;
        else if (keyboardHit) _keyboardPressOwned = true;
        _keyboard.Hover = keyboardHit ? key : -1;
        _keyboard.Input.Update(keyboardHit ? key : -1, selectKey, canType, Time.unscaledTime);
        if (keyboardHit || _keyboardPressOwned)
        {
            _mouse.Release(); _lastPanelClick = selectKey;
            _panel.HasCursor = keyboardHit;
            if (keyboardHit)
            {
                _panel.RayEnd = _panel.CursorPoint = _keyboard.HitPoint;
                _panel.CursorRotation = _keyboard.Rotation;
            }
            return;
        }
        bool hit = planeHit && (region == PanelRegion.Content || _mouse.Holding);
        _panel.HasCursor = planeHit && (region != PanelRegion.None || _mouse.Holding);
        if (_panel.HasCursor) _panel.RayEnd = _panel.CursorPoint;
        if (_menu)
        {
            // A private modal pointer avoids consumed IMGUI events, game mouse effects,
            // and clicks leaking through to game controls behind the settings window.
            _mouse.Release();
            bool desktop = _mouse.DesktopInUse || !_vrInputAvailable;
            if (desktop)
            {
                bool available = _mouse.MenuPointer(Screen.width, Screen.height, out float x, out float y, out bool held);
                _menuInput.Update(2, available, x, y, held);
            }
            else _menuInput.Update(1, hit && sample.Valid, uv.x*Screen.width, (1-uv.y)*Screen.height,
                sample.A || sample.RightTrigger);
            _lastPanelClick=false;
            return;
        }
        bool click = actions.MouseLeft;
        bool pressed = click && !_lastPanelClick;
        _lastPanelClick = click;
        if (PanelInteractionPolicy.CanManipulate(_pinned, _motion.DefaultViewLocked, _menu,
            actions.Blocked || !sample.Valid, pressed, region, _mouse.Holding, _resizing || _draggingPanel))
        {
            _resizing = region == PanelRegion.Resize;
            _draggingPanel = !_resizing;
            _resizeDistance = Mathf.Max(.02f, Vector3.Distance(grip.pos, _rig.InverseTransformPoint(_panel.Position)));
            _resizeWidth = _settings.Width.Value;
            _resizeCurrentWidth = _resizeWidth;
            _anchorPosition = _panel.Position; _anchorRotation = _panel.Rotation;
            _panelGrabOffset = Quaternion.Inverse(grip.rot) * (_rig.InverseTransformPoint(_panel.Position) - grip.pos);
            _panelGrabRotation = Quaternion.Inverse(_rig.rotation * grip.rot) * _panel.Rotation;
        }
        if (_resizing || _draggingPanel)
        {
            if (click && sample.Valid && (_resizing || _pinned) && !_motion.DefaultViewLocked && !_menu)
            {
                if (_resizing)
                    _resizeCurrentWidth = Mathf.Clamp(_resizeWidth * Vector3.Distance(grip.pos, _rig.InverseTransformPoint(_panel.Position)) / _resizeDistance, .2f, 2f);
                else
                {
                    _anchorPosition = _rig.TransformPoint(grip.pos + grip.rot * _panelGrabOffset);
                    _anchorRotation = _rig.rotation * grip.rot * _panelGrabRotation;
                    _panel.Position = _anchorPosition; _panel.Rotation = _anchorRotation;
                }
                if (_resizing) { _panel.Height *= _resizeCurrentWidth * _panel.Units / _panel.Width; _panel.Width = _resizeCurrentWidth * _panel.Units; }
            }
            else { if (_resizing) _settings.Width.Value = _resizeCurrentWidth; _resizing = _draggingPanel = false; }
            _mouse.Release(); return;
        }
        var owner = new PointerOwner { SourceId = 1, RigId = _rig.GetInstanceID(), CameraId = _source!.GetInstanceID(),
            SurfaceId = _menu ? 2 : 1, LayoutGeneration = _capture.Generation, HandId = 2 };
        _mouse.Update(hit && sample.Valid && !_session.XboxActive, uv.x * _capture.Width, uv.y * _capture.Height,
            _capture.Width, _capture.Height, click, !_menu && actions.MouseRight, !_menu && actions.MouseMiddle, owner);
    }
    private void ToggleMenu()
    {
        _keyboard.CancelResize(); _keyboard.Input.Suspend(); _keyboardPressOwned = false;
        _menuInput.Reset();
        _menu = !_menu;
        if (!_menu) UiCaptureDiagnostics.Arm();
        if (_menu) _targetScanPending = true;
        if (_menu && _source != null && (!_sceneSnapshotWritten || _sceneSnapshotCameraId != _source.GetInstanceID()))
        {
            _sceneSnapshotWritten = true;
            _sceneSnapshotCameraId = _source.GetInstanceID();
            StereoSceneDiagnostics.Write(_source);
        }
        _mouse.Release();
        PlaceMenu();
        OpenVrDiagnostics.Write($"settings toggled: open={_menu}, position={_menuPosition}, captureRequested={_settings.Panel.Value || _menu}");
    }
    private void RefreshTargets()
    {
        _targetScanPending = false; _nextTargetScan = Time.unscaledTime + 2;
        try
        {
            _targets.Refresh(_settings.TargetPath.Value);
            if (_targets.Read().Valid && _targets.SelectedPath != _settings.TargetPath.Value)
                _settings.TargetPath.Value = _targets.SelectedPath;
        }
        catch (Exception error) { OpenVrDiagnostics.Write("follow target scan unavailable; original view retained: " + error.Message); }
    }
    private void SelectFollowTarget(int direction)
    {
        _targets.Select(direction);
        _settings.TargetPath.Value = _targets.SelectedPath;
        _mouse.Release(); _session.Suspend(); _menuInput.Reset();
        _pinned = _resizing = _draggingPanel = false;
        _recenterFollow = true;
    }
    private void PlaceMenu()
    {
        _menuRotation = _rig.rotation * _headRotation;
        _menuPosition = _rig.TransformPoint(_head) + _menuRotation * Vector3.forward * (1.1f * _rig.localScale.x);
    }
    internal void Eye(Utils.RigidTransform head, Utils.RigidTransform offset, out Vector3 position, out Quaternion rotation)
    {
        position = _rig.TransformPoint(head.pos + head.rot * offset.pos);
        rotation = _rig.rotation * head.rot * offset.rot;
    }
    internal void Render(Vector3 eyePosition, Quaternion eyeRotation, Matrix4x4 projection, RenderTexture target)
    {
        if (!Active) return;
        try { _panel.Render(eyePosition, eyeRotation, projection, target); }
        catch (Exception error) { _mouse.Release(); _panel.Invalidate(error); }
    }
    internal IDisposable? BeginSceneDepth(Camera camera, RenderTexture target) =>
        Active ? _panel.BeginSceneDepth(camera, target) : null;
    internal void CompleteSceneDepth(Camera camera, RenderTexture target)
    {
        if (Active) _panel.CompleteSceneDepth(camera, target);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clickTrace?.Dispose();
        _depthOfField.Dispose();
        _motionBlur.Dispose();
        _skinMatrices.Dispose();
        _keyboard.Dispose();
        _mouse.Dispose(); _hands.Dispose(); _xbox.Dispose(); _input?.Dispose(); _capture?.Dispose(); _panel?.Dispose();
        if (_rig != null) Object.Destroy(_rig.gameObject);
        if (_font != null) Object.Destroy(_font);
        _bitmapFont?.Dispose();
    }
}
