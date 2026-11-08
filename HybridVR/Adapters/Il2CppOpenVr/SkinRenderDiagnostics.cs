using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Read-only, explicitly requested CPU bone probe. It cannot validate GPU-deformed vertices.
internal sealed class SkinRenderDiagnostics
{
    private readonly List<Transform> _bones = new();
    private readonly Vector3[] _before = new Vector3[96], _frameStart = new Vector3[96];
    private readonly Quaternion[] _rotations = new Quaternion[96];
    private readonly Action<string> _write;
    private bool _stopped, _sample;
    private int _frame = -1, _eyeIndex, _samples, _changes;
    private string _scene = "";
    private float _maxPosition, _maxAngle, _maxBetweenEyes;
    private double _nextInventory, _nextReport = 5;
    private string _changedBone = "none", _error = "";

    internal SkinRenderDiagnostics(Action<string> write) { _write = write; }

    internal void Frame(Camera source, double seconds)
    {
        if (_error.Length > 0) { _write("skin probe stopped: " + _error); _error = ""; }
        if (_stopped) return;
        try
        {
            if (seconds >= 120) { Report(); _write("skin probe complete (120s); no bone/cloth settings changed"); _bones.Clear(); _stopped = true; return; }
            string scene = source.gameObject.scene.path;
            if (seconds >= _nextInventory && (scene != _scene || _bones.Count == 0))
            {
                _scene = scene; _nextInventory = seconds + 5;
                Inventory();
            }
            if (seconds >= _nextReport) { Report(); _nextReport = seconds + 5; }
        }
        catch (Exception error) { Stop(error); }
    }

    private void Inventory()
    {
        _bones.Clear();
        var seen = new HashSet<int>();
        int skins = 0;
        foreach (var skin in Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (skin == null || !skin.gameObject.activeInHierarchy || !skin.enabled) continue;
            if (++skins > 32) break;
            var root = skin.rootBone;
            _write($"skin inventory: name={skin.name}, root={(root != null ? root.name : "none")}, offscreen={skin.updateWhenOffscreen}, visible={skin.isVisible}, bounds={skin.bounds}, transform={skin.transform.position}");
            Add(skin.transform, seen); Add(root, seen);
            var bones = skin.bones;
            int picked = 0;
            for (int i = 0; i < bones.Length && i < 2048 && picked < 4; i++)
            {
                var bone = bones[i];
                if (bone == null) continue;
                string name = bone.name.ToLowerInvariant();
                if (name.Contains("hair") || name.Contains("head") || name.Contains("neck"))
                {
                    if (Add(bone, seen)) picked++;
                }
            }
        }
        _write($"skin inventory complete: renderers={Math.Min(skins, 32)}, transforms={_bones.Count}; CPU observations only, no BakeMesh or physics changes");
    }

    private bool Add(Transform? bone, HashSet<int> seen)
    {
        if (bone == null || _bones.Count >= _before.Length || !seen.Add(bone.GetInstanceID())) return false;
        _bones.Add(bone);
        _write($"skin probe bone[{_bones.Count - 1}]={bone.name}, parent={(bone.parent != null ? bone.parent.name : "none")}");
        return true;
    }

    internal void BeginEye()
    {
        if (_stopped) return;
        try
        {
            int frame = Time.frameCount;
            if (_frame != frame) { _frame = frame; _eyeIndex = 0; }
            _sample = frame % 15 == 0;
            if (!_sample) return;
            for (int i = 0; i < _bones.Count; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;
                _before[i] = bone.position; _rotations[i] = bone.rotation;
                if (_eyeIndex == 0) _frameStart[i] = _before[i];
                else _maxBetweenEyes = Mathf.Max(_maxBetweenEyes, Vector3.Distance(_before[i], _frameStart[i]));
            }
        }
        catch (Exception error) { Stop(error); }
    }

    internal void EndEye()
    {
        if (_stopped || !_sample) return;
        try
        {
            _samples++;
            for (int i = 0; i < _bones.Count; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;
                float p = Vector3.Distance(bone.position, _before[i]);
                float r = Quaternion.Angle(bone.rotation, _rotations[i]);
                if (p > .0001f || r > .02f) { _changes++; _changedBone = bone.name; }
                _maxPosition = Mathf.Max(_maxPosition, p); _maxAngle = Mathf.Max(_maxAngle, r);
            }
            _eyeIndex++;
        }
        catch (Exception error) { Stop(error); }
    }

    private void Report()
    {
        _write($"skin render window: eyeSamples={_samples}, changes={_changes}, maxBoneDeltaUnits={_maxPosition:0.000000}, maxBoneAngle={_maxAngle:0.0000}, betweenEyesMaxUnits={_maxBetweenEyes:0.000000}, lastChanged={_changedBone}");
        _samples = _changes = 0; _maxPosition = _maxAngle = _maxBetweenEyes = 0; _changedBone = "none";
    }
    private void Stop(Exception error) { _stopped = true; _sample = false; _error = error.Message; }
}
