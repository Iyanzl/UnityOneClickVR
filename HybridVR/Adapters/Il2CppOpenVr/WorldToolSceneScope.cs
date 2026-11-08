using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// World tools participate in the same eye render as scenery, before URP releases
// its depth attachment. Never enabled during desktop or screenshot capture.
internal sealed class WorldToolSceneScope : IDisposable
{
    private GameObject? _root;
    private Shader? _shader;
    private readonly List<MeshRenderer> _renderers = new();
    private readonly List<MeshFilter> _filters = new();
    private readonly Dictionary<int, Material> _materials = new();
    private int _count, _layer;
    private bool _logged;
    private bool _missingShaderLogged;
    private readonly UrpWorldToolPass _afterPost = new();
    internal bool Active { get; private set; }

    internal bool Begin(Camera camera)
    {
        if (Active) throw new InvalidOperationException("Nested world tool render");
        _layer = -1;
        for (int i = 0; i < 32; i++) if ((camera.cullingMask & (1 << i)) != 0) { _layer = i; break; }
        if (_layer < 0) return false;
        if (_shader == null) _shader = Shader.Find("Sprites/Default");
        if (_shader == null || !_shader.isSupported)
        {
            if (!_missingShaderLogged)
                Uuvr.OpenVR.OpenVrDiagnostics.Write("world tool scene path unavailable: Sprites/Default missing or unsupported; depth attachment fallback retained");
            _missingShaderLogged = true;
            return false;
        }
        if (_root == null)
        {
            _root = new GameObject("HybridVR eye-scoped world tools") { hideFlags = HideFlags.HideAndDontSave };
            _root.SetActive(false);
            Object.DontDestroyOnLoad(_root);
        }
        _count = 0; Active = true;
        if (_afterPost.Begin(camera, _root)) _layer = _afterPost.Layer;
        return true;
    }

    internal void Add(Mesh mesh, Matrix4x4 matrix, Material source)
    {
        if (!Active) throw new InvalidOperationException("No world tool render");
        if (_count == _renderers.Count)
        {
            var go = new GameObject("HybridVR world surface") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(_root!.transform, false);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _filters.Add(go.AddComponent<MeshFilter>()); _renderers.Add(renderer);
        }
        int id = source.GetInstanceID();
        if (!_materials.TryGetValue(id, out var material) || material == null)
        {
            material = OwnedUnityAsset.Retain(new Material(_shader!) { name = "HybridVR world depth material" });
            _materials[id] = material;
        }
        material.mainTexture = source.mainTexture; material.color = source.color;
        var item = _renderers[_count];
        // Match the foreground command-buffer order in both eyes. Distance
        // sorting by each quad's centre can put the cursor behind its panel in
        // only one eye. This changes draw order, not the scene depth test.
        item.sortingOrder = _count;
        _filters[_count].sharedMesh = mesh;
        item.gameObject.layer = _layer;
        var t = item.transform;
        t.position = matrix.GetColumn(3); t.rotation = matrix.rotation; t.localScale = matrix.lossyScale;
        item.sharedMaterial = material; item.enabled = true;
        _count++;
    }

    internal void Commit()
    {
        if (!Active) return;
        for (int i = _count; i < _renderers.Count; i++) _renderers[i].enabled = false;
        _root!.SetActive(true);
        if (!_logged)
        {
            _logged = true;
            Uuvr.OpenVR.OpenVrDiagnostics.Write("world tool occlusion: eye-scoped scene geometry; native scene depth; desktop excluded; drawOrder=submission; shader=" + _shader!.name);
        }
    }

    public void Dispose()
    {
        if (_root != null) _root.SetActive(false);
        _afterPost.Dispose();
        Active = false;
    }

    internal void Release()
    {
        Dispose();
        _afterPost.Release();
        if (_root != null) Object.Destroy(_root);
        foreach (var material in _materials.Values) if (material != null) Object.Destroy(material);
        _root = null; _renderers.Clear(); _filters.Clear(); _materials.Clear();
    }
}
