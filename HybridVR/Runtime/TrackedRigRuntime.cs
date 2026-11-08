#if OPENXR_BUILD
using System;
using HybridVR.Shared;
using UnityEngine;

namespace HybridVR.Runtime
{
    internal struct RigInteractionSettings
    {
        internal bool FollowStoryCamera;
        internal bool LockHeadTranslation;
        internal float WalkSpeed, PitchSpeed, SnapDegrees, GrabSensitivity, ScaleMaximum;
    }

    // Requires an exclusively owned tracking rig and an unmodified game-camera source.
    // Hosts adapt their camera ownership separately; this never writes the source Camera.
    internal sealed class TrackedRigRuntime
    {
        internal Action<float> ScaleCommitted;
        internal Action<bool> LockChanged;
        private readonly WorldScaleGesture _scaleGesture = new WorldScaleGesture();
        private readonly HorizontalLocomotion _walk = new HorizontalLocomotion();
        private Camera _source;
        private Vector3 _sourcePosition, _savedRelativePosition, _lockedHeadPosition;
        private Quaternion _sourceRotation, _savedRelativeRotation, _lockedHeadRotation;
        private Transform _rig;
        private bool _sourceKnown, _turnReady = true, _lastValid;
        private int _generation;
        private int _grabHand;
        private Vector3 _grabPosition;
        internal bool DefaultViewLocked { get; private set; }
        internal bool HasLockedSource => DefaultViewLocked && _source != null && _sourceKnown;

        internal void Reset()
        {
            _sourceKnown = _lastValid = false;
            _source = null; _rig = null; _grabHand = 0; _turnReady = false;
            DefaultViewLocked = false; _generation++;
            _walk.Suspend();
        }

        internal void Update(Transform rig, Camera source, bool valid, Vector3 headLocal, Quaternion headRotation,
            bool leftValid, Vector3 leftLocal, bool rightValid, Vector3 rightLocal,
            ControllerSample sample, InteractionActions actions, bool menuOpen, RigInteractionSettings settings)
        {
            if (rig == null) return;
            if (_rig != rig) { _rig = rig; _sourceKnown = false; _generation++; _grabHand = 0; _walk.Suspend(); }
            if (source != _source)
            {
                _source = source; _sourceKnown = false; _generation++;
                _grabHand = 0; _walk.Suspend();
            }
            if (valid != _lastValid) { _generation++; _lastValid = valid; _grabHand = 0; _walk.Suspend(); }
            if (!valid) return;

            if (_source != null)
            {
                Vector3 position = _source.transform.position;
                Quaternion rotation = _source.transform.rotation;
                if (_sourceKnown && !DefaultViewLocked && settings.FollowStoryCamera)
                {
                    Quaternion delta = rotation * Quaternion.Inverse(_sourceRotation);
                    rig.SetPositionAndRotation(position + delta * (rig.position - _sourcePosition), delta * rig.rotation);
                }
                _sourcePosition = position; _sourceRotation = rotation; _sourceKnown = true;
            }
            if (actions.ToggleDefaultView && (_sourceKnown || DefaultViewLocked))
            {
                if (!DefaultViewLocked)
                {
                    _savedRelativePosition = Quaternion.Inverse(_sourceRotation) * (rig.position - _sourcePosition);
                    _savedRelativeRotation = Quaternion.Inverse(_sourceRotation) * rig.rotation;
                    _lockedHeadPosition = headLocal; _lockedHeadRotation = headRotation;
                    DefaultViewLocked = true;
                }
                else
                {
                    if (_sourceKnown) rig.SetPositionAndRotation(_sourcePosition + _sourceRotation * _savedRelativePosition,
                        _sourceRotation * _savedRelativeRotation);
                    DefaultViewLocked = false;
                }
                _grabHand = 0; _walk.Suspend();
                LockChanged?.Invoke(DefaultViewLocked);
            }
            if (HasLockedSource)
            {
                Quaternion rotation = _sourceRotation * Quaternion.Inverse(_lockedHeadRotation);
                // Opt-in positional lock: cancel room-scale translation, not physical head rotation.
                var lockedPosition = settings.LockHeadTranslation ? headLocal : _lockedHeadPosition;
                rig.SetPositionAndRotation(_sourcePosition - rotation * (lockedPosition * rig.localScale.x), rotation);
            }

            var scaled = _scaleGesture.Update(_generation, leftValid && rightValid && !menuOpen,
                actions.ScaleHeld, actions.ResetScale, Vector3.Distance(leftLocal, rightLocal),
                1f / Mathf.Max(.0001f, rig.localScale.x), settings.ScaleMaximum);
            if (scaled.Changed)
            {
                SetWorldScale(rig, headLocal, (float)scaled.Value);
            }
            if (scaled.Commit)
            {
                ScaleCommitted?.Invoke((float)scaled.Value);
            }

            if (DefaultViewLocked || menuOpen || !actions.AllowMovement)
            {
                _walk.Suspend(); _grabHand = 0; _turnReady = false;
                return;
            }
            Vector3 headWorld = rig.TransformPoint(headLocal);
            Vector3 headForward = rig.rotation * headRotation * Vector3.forward;
            var motion = _walk.Update(new MoveSample
            {
                FrameId = sample.Frame, TrackingGeneration = _generation, Allowed = true,
                HasHeadOrientation = true, HeadForwardX = headForward.x, HeadForwardY = headForward.y, HeadForwardZ = headForward.z,
                StickX = (float)sample.LeftStickX, StickY = (float)sample.LeftStickY,
                Speed = settings.WalkSpeed,
                DeltaSeconds = Time.unscaledDeltaTime, Deadzone = .18f
            });
            rig.position += new Vector3(motion.WorldX, 0, motion.WorldZ);
            headWorld = rig.TransformPoint(headLocal);
            float x = (float)sample.RightStickX, y = (float)sample.RightStickY;
            if (actions.Pitch)
            {
                if (actions.ResetHorizon) LevelHorizon(rig, headLocal);
                if (!(actions.Roll && sample.R3))
                {
                    // Project the current right axis, not forward: forward loses
                    // its heading at vertical and flips when pitching past it.
                    Vector3 axis = Vector3.ProjectOnPlane(rig.rotation * headRotation * new Vector3(1, 0, 0), Vector3.up);
                    if (axis.sqrMagnitude < .001f)
                        axis = Vector3.Cross(Vector3.up, headForward);
                    float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
                    if (axis.sqrMagnitude > .001f && Mathf.Abs(y) > .18f)
                        rig.RotateAround(headWorld, axis.normalized, -y * settings.PitchSpeed * dt);
                    if (actions.Roll && Mathf.Abs(x) > .18f)
                        rig.RotateAround(headWorld, rig.rotation * headRotation * Vector3.forward, -x * settings.PitchSpeed * dt);
                }
                _turnReady = false;
            }
            else if (Mathf.Abs(x) < .25f) _turnReady = true;
            else if (_turnReady && Mathf.Abs(x) > .65f && !sample.R3)
            {
                rig.RotateAround(headWorld, Vector3.up, Mathf.Sign(x) * settings.SnapDegrees);
                _turnReady = false;
            }
            int hand = actions.GrabLeft && leftValid ? 1 : actions.GrabRight && rightValid ? 2 : 0;
            Vector3 grip = hand == 1 ? leftLocal : rightLocal;
            // Virtual traversal uses game units, unlike real head/hand tracking which uses rig scale.
            if (hand != 0 && hand == _grabHand) rig.position += rig.rotation * (_grabPosition - grip) * settings.GrabSensitivity;
            _grabPosition = grip; _grabHand = hand;
        }

        internal static void LevelHorizon(Transform rig, Vector3 headLocal)
        {
            Vector3 forward = rig.rotation * Vector3.forward;
            Vector3 up = rig.rotation * Vector3.up;
            Vector3 targetUp = Vector3.ProjectOnPlane(Vector3.up, forward);
            // Roll is undefined when looking vertically; do not introduce a yaw
            // jump there. Physical HMD orientation is never overwritten.
            if (targetUp.sqrMagnitude < .0001f) return;
            float angle = Vector3.SignedAngle(up, targetUp, forward);
            rig.RotateAround(rig.TransformPoint(headLocal), forward, angle);
        }

        internal static void SetScale(Transform rig, Vector3 headLocal, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            Vector3 headWorld = rig.TransformPoint(headLocal);
            rig.localScale = Vector3.one / Mathf.Clamp(value, .1f, 100f);
            rig.position += headWorld - rig.TransformPoint(headLocal);
        }

        internal void SetWorldScale(Transform rig, Vector3 headLocal, float value)
        {
            float oldUnits = rig.localScale.x;
            SetScale(rig, headLocal, value);
            if (!HasLockedSource) return;
            _lockedHeadPosition = Quaternion.Inverse(rig.rotation) * (_sourcePosition - rig.position) / rig.localScale.x;
            _savedRelativePosition += _savedRelativeRotation * (headLocal * (oldUnits-rig.localScale.x));
        }
    }
}
#endif
