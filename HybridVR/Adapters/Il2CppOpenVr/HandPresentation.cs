using System;
using UnityEngine;
using UnityEngine.Rendering;
using Uuvr.OpenVR;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// The SiH glove is baked offline, avoiding Unity-version-specific prefab/shader
// bundles. These meshes never enter the scene, capture camera or physics world.
internal sealed class HandPresentation : IDisposable
{
    private HandMeshData[]? _data;
    private readonly Mesh?[] _meshes = new Mesh?[2];
    private readonly Vector3[][] _vertices = new Vector3[2][];
    private readonly bool[] _visible = new bool[2];
    private readonly Matrix4x4[] _matrices = new Matrix4x4[2];
    private readonly float[] _grips = { -1, -1 }, _triggers = { -1, -1 };
    private Material? _material;
    private bool _failed;
    internal string Status => _failed ? "unavailable" : $"left={_visible[0]},right={_visible[1]},ready={_material != null}";
    internal void Hide() { _visible[0] = _visible[1] = false; }
    internal void Update(bool enabled, Transform rig, HandInput left, HandInput right)
    {
        Hide();
        if (!enabled || _failed) return;
        try
        {
            Ensure();
            UpdateHand(0, rig, left); UpdateHand(1, rig, right);
        }
        catch (Exception error)
        {
            Dispose(); _failed = true;
            OpenVrDiagnostics.Write("hand models unavailable; panel/input retained: " + error);
        }
    }
    private void Ensure()
    {
        if (_material != null && _meshes[0] != null && _meshes[1] != null) return;
        Dispose();
        _data ??= HandMeshData.Read(typeof(HandPresentation).Assembly.GetManifestResourceStream("HybridVR.SihHands.bin")
            ?? throw new InvalidOperationException("Embedded hand meshes missing"));
        var shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null || !shader.isSupported) throw new NotSupportedException("Hand shader unavailable");
        _material = OwnedUnityAsset.Retain(new Material(shader) { color = Color.white });
        // UI/Default hard-codes ZWrite Off: changing its material property cannot
        // stop rear glove triangles from painting over the front of the hand.
        _material.SetInt("_SrcBlend", (int)BlendMode.One); _material.SetInt("_DstBlend", (int)BlendMode.Zero);
        _material.SetInt("_ZWrite", 1); _material.SetInt("_Cull", (int)CullMode.Off);
        _material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        for (int h = 0; h < 2; h++)
        {
            var data = _data[h];
            _vertices[h] = new Vector3[data.VertexCount];
            var mesh = _meshes[h] = OwnedUnityAsset.Retain(new Mesh { name = h == 0 ? "HybridVR left glove" : "HybridVR right glove" });
            Fill(h, 0, 0);
            mesh.vertices = _vertices[h]; mesh.uv = new Vector2[data.VertexCount]; mesh.triangles = data.Triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var normals = mesh.normals;
            var colors = new Color[data.VertexCount];
            for (int i = 0; i < colors.Length; i++)
            {
                float shade = .45f + .5f * Mathf.Abs(normals[i].y);
                colors[i] = new Color(shade * .88f, shade * .94f, shade, 1);
            }
            mesh.colors = colors;
            _grips[h] = _triggers[h] = -1;
        }
        OpenVrDiagnostics.Write("hand meshes ready: SiH glove, eye-only, grip-local, opaque self-depth");
    }
    private void Fill(int h, float grip, float trigger)
    {
        var data = _data![h];
        for (int i = 0; i < data.VertexCount; i++)
            _vertices[h][i] = new Vector3(data.Coordinate(i * 3, grip, trigger), data.Coordinate(i * 3 + 1, grip, trigger), data.Coordinate(i * 3 + 2, grip, trigger));
    }
    private void UpdateHand(int h, Transform rig, HandInput hand)
    {
        if (hand.Valid == 0) return;
        var pose = new Utils.RigidTransform(hand.GripPose);
        _matrices[h] = Matrix4x4.TRS(rig.TransformPoint(pose.pos), rig.rotation * pose.rot, rig.localScale);
        _visible[h] = true;
        float grip = Mathf.Clamp01(hand.Grip), trigger = Mathf.Clamp01(hand.Trigger);
        if (Mathf.Abs(grip - _grips[h]) < .025f && Mathf.Abs(trigger - _triggers[h]) < .025f) return;
        Fill(h, grip, trigger); _meshes[h]!.vertices = _vertices[h];
        _meshes[h]!.RecalculateBounds();
        _grips[h] = grip; _triggers[h] = trigger;
    }
    internal void Append(CommandBuffer commands)
    {
        if (_material == null) return;
        try
        {
            for (int h = 0; h < 2; h++) if (_visible[h] && _meshes[h] != null) commands.DrawMesh(_meshes[h], _matrices[h], _material, 0, 0);
        }
        catch (Exception error)
        {
            Dispose(); _failed = true;
            OpenVrDiagnostics.Write("hand draw disabled; panel retained: " + error.Message);
        }
    }
    public void Dispose()
    {
        Hide();
        for (int h = 0; h < 2; h++) { Object.Destroy(_meshes[h]); _meshes[h] = null; }
        Object.Destroy(_material); _material = null;
    }
}
