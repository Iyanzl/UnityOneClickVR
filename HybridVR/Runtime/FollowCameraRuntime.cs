using System;
using HybridVR.Shared;
using UnityEngine;

namespace HybridVR.Runtime
{
    internal struct FollowAnchor
    {
        internal bool Valid;
        internal int Id;
        internal Vector3 Head;
        internal Quaternion Rotation;
    }

    internal struct FollowCameraSettings
    {
        internal Vector3 FirstOffset;
        internal float Distance, Height, Shoulder, VerticalLag, HorizontalLag, YawLag, ThirdLag;
        internal bool FollowHeading, FirstFollowHeading;
    }

    // Moves the owned rig, never the game camera or the tracked HMD. Manual
    // locomotion stays an offset within the moving follow frame.
    internal sealed class FollowCameraRuntime
    {
        private FollowViewMode _mode;
        private int _target;
        private bool _locked;
        private Vector3 _basePosition, _savedPosition, _freePosition;
        private Quaternion _baseRotation, _savedRotation, _freeRotation, _entryBody, _headYaw;
        internal bool Active { get; private set; }

        internal void Update(Transform rig, Camera source, Vector3 head, Quaternion headRotation,
            FollowViewMode mode, FollowAnchor anchor, FollowCameraSettings settings, bool locked, bool paused, bool recenter)
        {
            if (mode == FollowViewMode.GameCamera || !anchor.Valid)
            {
                if (Active && locked) return;
                if (Active && !locked)
                    rig.SetPositionAndRotation(source.transform.position + source.transform.rotation * _savedPosition,
                        source.transform.rotation * _savedRotation);
                Active = false;
                return;
            }
            if (locked) { if (Active) _locked = true; return; }
            if (paused) return;
            Quaternion body = Horizontal(anchor.Rotation);
            bool enter = !Active || _mode != mode || _target != anchor.Id || recenter;
            if (!Active)
            {
                _savedPosition = Quaternion.Inverse(source.transform.rotation) * (rig.position - source.transform.position);
                _savedRotation = Quaternion.Inverse(source.transform.rotation) * rig.rotation;
            }
            if (enter)
            {
                _entryBody = body;
                _headYaw = Horizontal(headRotation);
            }
            bool followHeading = mode == FollowViewMode.FirstPerson ? settings.FirstFollowHeading : settings.FollowHeading;
            Quaternion bearing = followHeading ? body : _entryBody;
            Vector3 goal = anchor.Head + (mode == FollowViewMode.FirstPerson
                ? body * settings.FirstOffset : bearing * new Vector3(settings.Shoulder, settings.Height, -settings.Distance));
            Quaternion rotation = bearing * Quaternion.Inverse(_headYaw);
            if (enter)
            {
                _basePosition = goal; _baseRotation = rotation;
                rig.SetPositionAndRotation(goal - rotation * (head * rig.localScale.x), rotation);
            }
            else
            {
                if (_locked) rig.SetPositionAndRotation(_freePosition, _freeRotation);
                bool first = mode == FollowViewMode.FirstPerson;
                float horizontal = Blend(first ? settings.HorizontalLag : settings.ThirdLag);
                float vertical = Blend(first ? settings.VerticalLag : settings.ThirdLag);
                Vector3 position = _basePosition + (goal - _basePosition) * horizontal;
                position.y = _basePosition.y + (goal.y - _basePosition.y) * vertical;
                // Only virtual target yaw is eased; physical head tracking is untouched.
                float turn = Vector3.SignedAngle(_baseRotation * Vector3.forward, rotation * Vector3.forward, Vector3.up);
                rotation = Quaternion.AngleAxis(turn * Blend(first ? settings.YawLag : settings.ThirdLag), Vector3.up) * _baseRotation;
                Quaternion delta = rotation * Quaternion.Inverse(_baseRotation);
                rig.SetPositionAndRotation(position + delta * (rig.position - _basePosition), delta * rig.rotation);
                _basePosition = position; _baseRotation = rotation;
            }
            Active = true; _mode = mode; _target = anchor.Id; _locked = false;
            _freePosition = rig.position; _freeRotation = rig.rotation;
        }

        private static float Blend(float lag) => lag <= 0 ? 1 : 1 - (float)Math.Exp(-Mathf.Min(Time.unscaledDeltaTime, .05f) / lag);

        private static Quaternion Horizontal(Quaternion rotation)
        {
            Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f) return Quaternion.identity;
            return Quaternion.AngleAxis(Vector3.SignedAngle(Vector3.forward, forward, Vector3.up), Vector3.up);
        }
    }
}
