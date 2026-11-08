using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Desktop alpha is not a UI mask. Preserve its RGB and make complete frames opaque.
internal sealed class FullFrameAlpha : IDisposable
{
    private Material? _material;
    private Mesh? _quad;
    private readonly CommandBuffer _commands = new() { name = "HybridVR full-frame alpha" };

    internal void Apply(RenderTexture target)
    {
        if (_material == null)
        {
            var shader = Shader.Find("UI/Default");
            if (shader == null || !shader.isSupported) throw new NotSupportedException("UI/Default alpha writer unavailable");
            _material = OwnedUnityAsset.Retain(new Material(shader));
            if (!_material.HasProperty("_ColorMask"))
            {
                Object.Destroy(_material); _material = null;
                throw new NotSupportedException("UI shader has no channel write mask");
            }
            _material.mainTexture = Texture2D.whiteTexture;
            _material.color = Color.white;
            _material.SetInt("_ColorMask", (int)ColorWriteMask.Alpha);
            _material.SetInt("_StencilComp", (int)CompareFunction.Always);
            _quad = OwnedUnityAsset.Retain(new Mesh { name = "HybridVR alpha quad" });
            _quad.vertices = new[] { new Vector3(-1,-1,0), new Vector3(-1,1,0), new Vector3(1,1,0), new Vector3(1,-1,0) };
            _quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0,1,2,0,2,3 };
        }
        var previous = RenderTexture.active;
        float depth = Shader.GetGlobalFloat("unity_GUIZTestMode");
        var sampleAdd = Shader.GetGlobalVector("_TextureSampleAdd");
        try
        {
            _commands.Clear();
            _commands.SetRenderTarget(target);
            _commands.SetViewport(new Rect(0,0,target.width,target.height));
            _commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            _commands.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            _commands.SetGlobalVector("_TextureSampleAdd", Vector4.zero);
            _commands.DrawMesh(_quad, Matrix4x4.identity, _material, 0, 0);
            _commands.SetGlobalFloat("unity_GUIZTestMode", depth);
            _commands.SetGlobalVector("_TextureSampleAdd", sampleAdd);
            Graphics.ExecuteCommandBuffer(_commands);
        }
        finally
        {
            Shader.SetGlobalFloat("unity_GUIZTestMode", depth);
            Shader.SetGlobalVector("_TextureSampleAdd", sampleAdd);
            RenderTexture.active = previous; _commands.Clear();
        }
    }

    public void Dispose()
    {
        _commands.Release();
        if (_material != null) Object.Destroy(_material);
        if (_quad != null) Object.Destroy(_quad);
        _material = null; _quad = null;
    }
}
