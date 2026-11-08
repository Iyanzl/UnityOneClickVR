using UnityEngine;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Observation only: no camera writes, object discovery or configuration changes.
internal sealed class RigTransitionProbe
{
    private bool _known;
    private int _sourceId;
    private Vector3 _sourcePosition;
    private Quaternion _sourceRotation;
    private float _nextLog;
    private int _coalesced;

    internal void Observe(Camera source, Transform rig, Vector3 head, Vector3 beforeEye,
        Vector3 afterSourceEye, Vector3 afterMotionEye, InteractionSettings settings, bool locked, bool following,
        bool inputMovement, int origin)
    {
        var position = source.transform.position;
        var rotation = source.transform.rotation;
        int id = source.GetInstanceID();
        float translation = _known ? Vector3.Distance(position, _sourcePosition) : 0;
        float rotationChange = _known ? Quaternion.Angle(rotation, _sourceRotation) : 0;
        bool sourceChanged = _known && id != _sourceId;
        bool jump = _known && (translation > .5f || rotationChange > 10);
        if (sourceChanged || jump)
        {
            if (Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + .5f;
                OpenVrDiagnostics.Write($"rig transition: source={_sourceId}->{id}, origin={origin}, mode={settings.View.Value}, followStory={settings.FollowStory.Value}, locked={locked}, following={following}, inputMovement={inputMovement}, units={rig.localScale.x:0.####}, sourceFrom={_sourcePosition}, sourceTo={position}, rotationFrom={_sourceRotation.eulerAngles}, rotationTo={rotation.eulerAngles}, sourceDistance={translation:0.###}, sourceAngle={rotationChange:0.###}, eyeBefore={beforeEye}, eyeAfterSource={afterSourceEye}, eyeAfterMotion={afterMotionEye}, eyeAfterFollow={rig.TransformPoint(head)}, coalesced={_coalesced}");
                _coalesced = 0;
            }
            else _coalesced++;
        }
        _sourceId = id; _sourcePosition = position; _sourceRotation = rotation; _known = true;
    }
}
