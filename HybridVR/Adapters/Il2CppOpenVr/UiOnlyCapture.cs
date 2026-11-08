using System;
using System.Collections.Generic;
using System.Reflection;
using HybridVR.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Uuvr.OpenVR;
using Object = UnityEngine.Object;
#if HYBRID_MONO
using VertexList = System.Collections.Generic.List<UnityEngine.Vector3>;
using UvList = System.Collections.Generic.List<UnityEngine.Vector4>;
using ColorList = System.Collections.Generic.List<UnityEngine.Color32>;
using IndexList = System.Collections.Generic.List<int>;
#else
using VertexList = Il2CppSystem.Collections.Generic.List<UnityEngine.Vector3>;
using UvList = Il2CppSystem.Collections.Generic.List<UnityEngine.Vector4>;
using ColorList = Il2CppSystem.Collections.Generic.List<UnityEngine.Color32>;
using IndexList = Il2CppSystem.Collections.Generic.List<int>;
#endif

namespace HybridVR.Adapters.Il2CppOpenVr;

// Replay existing uGUI geometry. Never render the scene, edit a game material,
// reparent canvases, or infer transparency from pixel colours.
internal sealed class UiOnlyCapture : IDisposable
{
    private static readonly MethodInfo? GetMesh = typeof(CanvasRenderer).GetMethod("GetMesh", Type.EmptyTypes);
    private static readonly PropertyInfo? ClipSoftness = typeof(CanvasRenderer).GetProperty("clippingSoftness");
    private static readonly PropertyInfo? MaskSoftness = typeof(RectMask2D).GetProperty("softness");
    private static readonly PropertyInfo? MaskPadding = typeof(RectMask2D).GetProperty("padding");
    private readonly CommandBuffer _commands = new() { name = "HybridVR UI replay" };
    private readonly MaterialPropertyBlock _properties = new();
    private readonly List<Item> _items = new();
    private readonly List<Item> _stack = new();
    private readonly List<Canvas> _canvases = new();
    private readonly Dictionary<int, Material> _materials = new();
    private readonly Dictionary<int, Material> _softMaterials = new();
    private readonly HashSet<int> _usedSoftMaterials = new();
    private readonly Dictionary<int, Mesh> _meshes = new();
    private readonly HashSet<int> _usedMeshes = new();
    private readonly VertexList _vertices = new();
    private readonly UvList _uvs = new();
    private readonly ColorList _colors = new();
    private readonly IndexList _indices = new();
    private readonly HashSet<int> _used = new();
    private readonly List<int> _expired = new();
    private readonly UiSystemCoverage _coverage = new();
    private Camera? _sceneCamera;
    private RenderTexture? _sceneTexture;
    private RenderTexture? _surface;
    private float _nextScan, _nextLog;
    private string _lastStatus = "";
    internal string Status { get; private set; } = "not requested";

    private struct Item
    {
        internal Graphic Graphic;
        internal Canvas Canvas;
        internal Mesh Mesh;
        internal Material Material;
        internal Material? Pop;
        internal Texture Atlas;
        internal int Depth;
        internal float CameraDepth;
        internal bool Clipped;
        internal Rect Clip;
        internal Rect CanvasClip;
        internal Vector2 Softness;
        internal bool SoftClipped;
    }

    internal bool TryCapture(RenderTexture target)
    {
        _items.Clear(); _stack.Clear(); _used.Clear(); _usedSoftMaterials.Clear(); _usedMeshes.Clear(); _commands.Clear();
        var previous = RenderTexture.active;
        // Built-in UI shaders still read these globals in SRP. Setting only the
        // command-buffer camera state is not sufficient across SRP versions.
        var oldView = Shader.GetGlobalMatrix("unity_MatrixV");
        var oldProjection = Shader.GetGlobalMatrix("glstate_matrix_projection");
        var oldVp = Shader.GetGlobalMatrix("unity_MatrixVP");
        var oldScreen = Shader.GetGlobalVector("_ScreenParams");
        float oldDepth = Shader.GetGlobalFloat("unity_GUIZTestMode");
        try
        {
            if (GetMesh == null) return Fallback("CanvasRenderer.GetMesh unavailable");
            var incomplete = _coverage.UnsupportedActiveSystem();
            if (incomplete != null) return Fallback(incomplete);
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + .5f;
                _canvases.Clear();
                foreach (var canvas in Object.FindObjectsOfType<Canvas>())
                    if (canvas != null && canvas.isRootCanvas) _canvases.Add(canvas);
                RefreshSceneTexture();
            }
            int sceneImages = 0;
            foreach (var root in _canvases)
            {
                if (root == null || !root.isActiveAndEnabled) continue;
                foreach (var renderer in root.GetComponentsInChildren<CanvasRenderer>())
                {
                    if (!renderer.gameObject.activeInHierarchy || renderer.cull) continue;
                    var mesh = GetMesh.Invoke(renderer, null) as Mesh;
                    if (mesh == null || mesh.vertexCount == 0) continue;
                    var graphic = renderer.GetComponent<Graphic>();
                    if (graphic == null || !graphic.isActiveAndEnabled) return Fallback("unclassified CanvasRenderer");
                    var canvas = graphic.canvas;
                    if (canvas == null) return Fallback("canvas unavailable");
                    var mode = canvas.rootCanvas.renderMode;
                    var camera = canvas.rootCanvas.worldCamera;
                    if (mode != RenderMode.ScreenSpaceOverlay && (camera == null || !camera.isActiveAndEnabled))
                        return Fallback("UI camera unavailable");
                    var material = renderer.materialCount == 1 ? renderer.GetMaterial(0) : null;
                    string shader = material?.shader?.name ?? "";
                    if (material == null || !material.shader.isSupported || !UiCapturePolicy.Supported(shader,
                        renderer.materialCount, false, renderer.hasRectClipping, renderer.hasPopInstruction,
                        mode == RenderMode.WorldSpace, camera != null && camera.targetTexture != null, ExternalAlpha(material)))
                        return Fallback($"unsupported UI: shader={shader}, object={graphic.name}, mode={mode}");
                    // Inspected single-texture, premultiplied uGUI ShaderGraph.
                    // Do not generalize this to arbitrary ShaderGraph materials.
                    if (shader == "Shader Graphs/Overray" && (material.passCount != 1 || !material.HasProperty("_Texture2D") ||
                        !material.HasProperty("_MainTex") || !material.HasProperty("_StencilComp") || !material.HasProperty("_ColorMask")))
                        return Fallback("unrecognized Overray material layout");

                    Material? pop = null;
                    if (renderer.hasPopInstruction)
                    {
                        var mask = renderer.GetComponent<Mask>();
                        if (mask == null || !mask.isActiveAndEnabled || renderer.popMaterialCount != 1)
                            return Fallback("unclassified stencil pop: " + graphic.name);
                        pop = renderer.GetPopMaterial(0);
                        if (pop == null || pop.shader != material.shader) return Fallback("stencil pop shader mismatch");
                    }
                    var atlas = shader.StartsWith("TextMeshPro/", StringComparison.Ordinal)
                        ? material.mainTexture : graphic.mainTexture;
                    if (atlas == null) return Fallback("UI atlas unavailable: " + graphic.name);
                    if (CanExcludeSceneImage(graphic, atlas, renderer.hasPopInstruction)) { sceneImages++; continue; }
                    var item = new Item { Graphic = graphic, Canvas = canvas, Mesh = mesh, Material = material,
                        Atlas = atlas, Pop = pop, Depth = renderer.absoluteDepth,
                        CameraDepth = mode == RenderMode.ScreenSpaceOverlay ? float.MaxValue : camera!.depth };
                    if (renderer.hasRectClipping)
                    {
                        if (!TryClip(graphic, target, out item.Clip, out item.CanvasClip, out item.Softness)) return Fallback("unsupported rect clip: " + graphic.name);
                        item.SoftClipped = item.Softness.sqrMagnitude > .001f;
                        if (item.SoftClipped && shader != "UI/Default" && shader != "UI/Default Font")
                            return Fallback("unsupported soft clip shader: " + shader);
                        item.Clipped = true;
                    }
                    if (_items.Count >= 2048) return Fallback("UI draw limit");
                    _items.Add(item);
                }
            }
            if (_items.Count == 0) return Fallback("no supported uGUI; full frame retained");
            _items.Sort((a,b) =>
            {
                int order = a.CameraDepth.CompareTo(b.CameraDepth);
                if (order == 0) order = SortingLayer.GetLayerValueFromID(a.Canvas.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(b.Canvas.sortingLayerID));
                if (order == 0) order = a.Canvas.sortingOrder.CompareTo(b.Canvas.sortingOrder);
                if (order == 0) order = a.Canvas.rootCanvas.GetInstanceID().CompareTo(b.Canvas.rootCanvas.GetInstanceID());
                return order != 0 ? order : a.Depth.CompareTo(b.Depth);
            });
            // Reject inherited stencil state whose writer was not captured. Do
            // not silently erase those children or relax their comparison.
            foreach (var item in _items)
            {
                while (_stack.Count > 0 && !item.Graphic.transform.IsChildOf(_stack[_stack.Count-1].Graphic.transform)) _stack.RemoveAt(_stack.Count-1);
                int compare = item.Material.HasProperty("_StencilComp") ? item.Material.GetInt("_StencilComp") : 8;
                if (compare != 0 && compare != 8 && (compare != 3 || _stack.Count == 0)) return Fallback("stencil writer unavailable: " + item.Graphic.name);
                if (item.Pop != null) _stack.Add(item);
            }
            _stack.Clear();
            EnsureSurface(target);
            _commands.SetRenderTarget(_surface);
            _commands.ClearRenderTarget(true, true, Color.clear);
            _commands.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            _commands.SetGlobalVector("_ScreenParams", new Vector4(target.width,target.height,1f+1f/target.width,1f+1f/target.height));
            int masks = 0, clips = 0;
            foreach (var item in _items)
            {
                while (_stack.Count > 0 && !item.Graphic.transform.IsChildOf(_stack[_stack.Count-1].Graphic.transform)) Pop(target);
                var drawable = item;
                var renderer = item.Graphic.canvasRenderer;
                var vertexTint = Color.white;
                if (!item.Material.HasProperty("_Color") && !item.Material.HasProperty("_FaceColor"))
                { vertexTint=renderer.GetColor(); vertexTint.a=renderer.GetInheritedAlpha(); }
                drawable.Mesh = CopyMesh(renderer.GetInstanceID(), item.Mesh,
                    item.SoftClipped ? item.Canvas.rootCanvas.transform.worldToLocalMatrix * item.Graphic.transform.localToWorldMatrix : Matrix4x4.identity,
                    item.SoftClipped, vertexTint);
                Draw(drawable, item.Material, target);
                if (item.Clipped) clips++;
                if (item.Pop != null) { _stack.Add(drawable); masks++; }
            }
            while (_stack.Count > 0) Pop(target);
            _commands.DisableScissorRect();
            _commands.SetGlobalMatrix("unity_MatrixV", oldView);
            _commands.SetGlobalMatrix("glstate_matrix_projection", oldProjection);
            _commands.SetGlobalMatrix("unity_MatrixVP", oldVp);
            _commands.SetGlobalVector("_ScreenParams", oldScreen);
            _commands.SetGlobalFloat("unity_GUIZTestMode", oldDepth);
            Graphics.ExecuteCommandBuffer(_commands);
            // PanelPresentation expects framebuffer-oriented pixels. UI replay
            // uses normal RT orientation; normalize here, not in hit testing.
            if (SystemInfo.graphicsUVStartsAtTop)
                Graphics.Blit(_surface, target, new Vector2(1, -1), new Vector2(0, 1));
            else Graphics.Blit(_surface, target);
            SetStatus($"UiOnly replay: {_items.Count} graphics, {masks} masks, {clips} rect clips, {sceneImages} verified scene images excluded; scene not rendered");
            return true;
        }
        catch (Exception e) { return Fallback(e.GetBaseException().GetType().Name + ": " + e.GetBaseException().Message); }
        finally
        {
            Shader.SetGlobalMatrix("unity_MatrixV", oldView);
            Shader.SetGlobalMatrix("glstate_matrix_projection", oldProjection);
            Shader.SetGlobalMatrix("unity_MatrixVP", oldVp);
            Shader.SetGlobalVector("_ScreenParams", oldScreen);
            Shader.SetGlobalFloat("unity_GUIZTestMode", oldDepth);
            RenderTexture.active = previous;
            _commands.Clear(); _items.Clear(); _stack.Clear();
            _expired.Clear();
            foreach (var pair in _materials) if (!_used.Contains(pair.Key)) _expired.Add(pair.Key);
            foreach (int id in _expired) { Object.Destroy(_materials[id]); _materials.Remove(id); }
            _expired.Clear();
            foreach (var pair in _softMaterials) if (!_usedSoftMaterials.Contains(pair.Key)) _expired.Add(pair.Key);
            foreach (int id in _expired) { Object.Destroy(_softMaterials[id]); _softMaterials.Remove(id); }
            _expired.Clear();
            foreach (var pair in _meshes) if (!_usedMeshes.Contains(pair.Key)) _expired.Add(pair.Key);
            foreach (int id in _expired) { Object.Destroy(_meshes[id]); _meshes.Remove(id); }
        }
    }

    private Mesh CopyMesh(int id, Mesh source, Matrix4x4 rebase, bool canvasSpace, Color tint)
    {
        // CanvasRenderer meshes can expose valid CPU data but no standalone GPU
        // draw (verified in HDRP). Keep our own reusable buffers, never mutate
        // the source. Copy every capture, including animated TMP UV channels.
        if (source.subMeshCount != 1) throw new NotSupportedException("UI mesh submesh count");
        if (!_meshes.TryGetValue(id, out var mesh))
        {
            mesh = OwnedUnityAsset.Retain(new Mesh { name = "HybridVR UI drawable mesh" });
            mesh.MarkDynamic();
            _meshes.Add(id, mesh);
        }
        _usedMeshes.Add(id);
        mesh.Clear();
        mesh.indexFormat = source.indexFormat;
        source.GetVertices(_vertices);
        if (canvasSpace) for(int i=0;i<_vertices.Count;i++) _vertices[i]=rebase.MultiplyPoint3x4(_vertices[i]);
        mesh.SetVertices(_vertices);
        source.GetColors(_colors);
        if (tint != Color.white)
        {
            if (_colors.Count == 0) for(int i=0;i<source.vertexCount;i++) _colors.Add((Color32)tint);
            else for(int i=0;i<_colors.Count;i++) _colors[i]=(Color32)((Color)_colors[i]*tint);
        }
        if (_colors.Count != 0) mesh.SetColors(_colors);
        source.GetNormals(_vertices); if (_vertices.Count != 0) mesh.SetNormals(_vertices);
        source.GetTangents(_uvs); if (_uvs.Count != 0) mesh.SetTangents(_uvs);
        for (int channel=0; channel<8; channel++)
        {
            source.GetUVs(channel,_uvs);
            if (_uvs.Count != 0) mesh.SetUVs(channel,_uvs);
        }
#if HYBRID_MONO
        source.GetTriangles(_indices,0);
        mesh.SetTriangles(_indices,0,false);
#else
        // The list overload is a throwing unstrip stub in some Unity 6 games.
        mesh.triangles = source.triangles;
#endif
        if (canvasSpace) mesh.RecalculateBounds(); else mesh.bounds = source.bounds;
        return mesh;
    }

    private void Draw(Item item, Material source, RenderTexture target)
    {
        var root = item.Canvas.rootCanvas;
        Matrix4x4 view, projection;
        if (root.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            _commands.SetViewport(new Rect(0,0,target.width,target.height));
            view = Matrix4x4.identity;
            projection = Matrix4x4.Ortho(0,Screen.width,0,Screen.height,-10000,10000);
        }
        else
        {
            var camera = root.worldCamera;
            var rect = camera.rect;
            _commands.SetViewport(new Rect(rect.x*target.width,rect.y*target.height,rect.width*target.width,rect.height*target.height));
            view = camera.worldToCameraMatrix; projection = camera.projectionMatrix;
        }
        _commands.SetViewProjectionMatrices(view, projection);
        var gpu = GL.GetGPUProjectionMatrix(projection, true);
        _commands.SetGlobalMatrix("unity_MatrixV", view);
        _commands.SetGlobalMatrix("glstate_matrix_projection", gpu);
        _commands.SetGlobalMatrix("unity_MatrixVP", gpu * view);
        if (item.Clipped)
        {
            if (item.Clip.width <= 0 || item.Clip.height <= 0) return;
            _commands.EnableScissorRect(item.Clip);
        }
        else _commands.DisableScissorRect();
        int id = source.GetInstanceID();
        var materials = item.SoftClipped ? _softMaterials : _materials;
        var used = item.SoftClipped ? _usedSoftMaterials : _used;
        if (!materials.TryGetValue(id, out var material))
        {
            material = OwnedUnityAsset.Retain(new Material(source) { name = "HybridVR UI replay material" });
            materials.Add(id, material);
        }
        if (used.Add(id))
        {
            material.CopyPropertiesFromMaterial(source);
            // CanvasRenderer supplies clip keywords separately. This draw uses a
            // verified screen scissor instead; stale material clip rects are unsafe.
            var keywords = new List<string>();
            foreach (var keyword in source.shaderKeywords)
                if (keyword != "UNITY_UI_CLIP_RECT") keywords.Add(keyword);
            if (item.SoftClipped) keywords.Add("UNITY_UI_CLIP_RECT");
            material.shaderKeywords = keywords.ToArray();
            // This inspected shader multiplies destination alpha by its texture
            // alpha, which is not UI coverage. Preserve coverage already drawn;
            // destination-only decoration cannot create pixels on empty space.
            if (source.shader.name == "UI/Mulatiply" && source.HasProperty("_ColorMask"))
                material.SetInt("_ColorMask", source.GetInt("_ColorMask") & 7);
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            if (material.HasProperty("_ZTestMode")) material.SetInt("_ZTestMode", (int)CompareFunction.Always);
        }
        _properties.Clear();
        if (item.SoftClipped)
        {
            _properties.SetVector("_ClipRect",new Vector4(item.CanvasClip.xMin,item.CanvasClip.yMin,item.CanvasClip.xMax,item.CanvasClip.yMax));
            _properties.SetFloat("_UIMaskSoftnessX",item.Softness.x);
            _properties.SetFloat("_UIMaskSoftnessY",item.Softness.y);
        }
        _properties.SetTexture("_MainTex", item.Atlas);
#if HYBRID_MONO
        var texture = item.Atlas as Texture2D;
#else
        var texture = item.Atlas.TryCast<Texture2D>();
#endif
        bool alphaOnly = texture != null && texture.format == TextureFormat.Alpha8;
        _properties.SetVector("_TextureSampleAdd", alphaOnly ? new Vector4(1,1,1,0) : Vector4.zero);
        var renderer = item.Graphic.canvasRenderer;
        var tint = renderer.GetColor();
        tint.a = renderer.GetInheritedAlpha();
        string color = source.HasProperty("_FaceColor") ? "_FaceColor" : "_Color";
        if (source.HasProperty(color)) _properties.SetColor(color, source.GetColor(color) * tint);
        _commands.DrawMesh(item.Mesh, item.SoftClipped ? root.transform.localToWorldMatrix : item.Graphic.transform.localToWorldMatrix, material,0,0,_properties);
    }

    private void Pop(RenderTexture target)
    {
        int index = _stack.Count-1;
        var item = _stack[index]; _stack.RemoveAt(index);
        // Exactly the game's mask mesh/atlas and pop material, including alpha
        // cutouts. A rectangle in its place would corrupt nested/nonrect masks.
        Draw(item, item.Pop!, target);
    }

    private static bool TryClip(Graphic graphic, RenderTexture target, out Rect result, out Rect canvasClip, out Vector2 softness)
    {
        result = new Rect(0,0,target.width,target.height);
        canvasClip = new Rect(-1e9f,-1e9f,2e9f,2e9f);
        softness = Vector2.zero;
        var root = graphic.canvas.rootCanvas;
        var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        bool found = false;
        for (var t = graphic.transform; t != null; t = t.parent)
        {
            var mask = t.GetComponent<RectMask2D>();
            if (mask != null && mask.isActiveAndEnabled)
            {
                if (!found && MaskSoftness?.GetValue(mask, null) is Vector2Int soft) softness = new Vector2(soft.x,soft.y);
                found = true;
                var rect = mask.canvasRect;
                // Padding was added in later uGUI; default is zero in older builds.
                var padding = MaskPadding?.GetValue(mask, null);
                if (padding is Vector4 p) { rect.xMin += p.x; rect.yMin += p.y; rect.xMax -= p.z; rect.yMax -= p.w; }
                float left=Mathf.Max(canvasClip.xMin,rect.xMin), bottom=Mathf.Max(canvasClip.yMin,rect.yMin);
                canvasClip = new Rect(left,bottom,Mathf.Max(0,Mathf.Min(canvasClip.xMax,rect.xMax)-left),Mathf.Max(0,Mathf.Min(canvasClip.yMax,rect.yMax)-bottom));
                var a = ScreenPoint(root, camera, rect.xMin,rect.yMin,target);
                var b = ScreenPoint(root, camera, rect.xMax,rect.yMin,target);
                var c = ScreenPoint(root, camera, rect.xMax,rect.yMax,target);
                var d = ScreenPoint(root, camera, rect.xMin,rect.yMax,target);
                if (Mathf.Abs(a.y-b.y) > .5f || Mathf.Abs(b.x-c.x) > .5f || Mathf.Abs(c.y-d.y) > .5f || Mathf.Abs(d.x-a.x) > .5f)
                    return false;
                float x = Mathf.Max(result.xMin,Mathf.Min(a.x,c.x)), y = Mathf.Max(result.yMin,Mathf.Min(a.y,c.y));
                result = new Rect(x,y,Mathf.Max(0,Mathf.Min(result.xMax,Mathf.Max(a.x,c.x))-x),Mathf.Max(0,Mathf.Min(result.yMax,Mathf.Max(a.y,c.y))-y));
            }
            if (t == root.transform) break;
        }
        if (ClipSoftness?.GetValue(graphic.canvasRenderer, null) is Vector2 nativeSoftness) softness = nativeSoftness;
        return found;
    }
    private static Vector2 ScreenPoint(Canvas root, Camera? camera, float x, float y, RenderTexture target)
    {
        var p = root.transform.TransformPoint(new Vector3(x,y,0));
        if (camera != null) p = camera.WorldToScreenPoint(p);
        return new Vector2(p.x * target.width / Screen.width,p.y * target.height / Screen.height);
    }
    private void RefreshSceneTexture()
    {
        _sceneCamera = null; _sceneTexture = null;
        var camera = Camera.main;
        if (camera == null || camera.targetTexture == null || !camera.gameObject.activeInHierarchy) return;
        var texture = camera.targetTexture;
        bool uiInTarget = false, otherWriter = false, character = false;
        foreach (var canvas in _canvases)
        {
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode == RenderMode.ScreenSpaceOverlay) continue;
            var writer = canvas.worldCamera;
            if (writer != null && writer.targetTexture == texture ||
                canvas.renderMode == RenderMode.WorldSpace && (camera.cullingMask & (1 << canvas.gameObject.layer)) != 0) uiInTarget = true;
        }
        foreach (var writer in Object.FindObjectsOfType<Camera>())
            if (writer != camera && writer.isActiveAndEnabled && writer.targetTexture == texture) otherWriter = true;
        if (uiInTarget || otherWriter) return;
        foreach (var renderer in Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
            var p = camera.WorldToViewportPoint(renderer.bounds.center);
            if (p.z > camera.nearClipPlane && p.z < camera.farClipPlane && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1) { character = true; break; }
        }
        if (!UiSceneTexturePolicy.CanExclude(true,true,false,true,character,uiInTarget,otherWriter)) return;
        _sceneCamera = camera; _sceneTexture = texture;
    }
    private bool CanExcludeSceneImage(Graphic graphic, Texture atlas, bool pop)
    {
        if (_sceneCamera == null || _sceneTexture == null || _sceneCamera.targetTexture != _sceneTexture || atlas != _sceneTexture) return false;
        if (graphic.GetComponent<RawImage>() == null || graphic.canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay || pop || graphic.GetComponent<Mask>() != null) return false;
#if HYBRID_MONO
        var corners = new Vector3[4];
#else
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
#endif
        graphic.rectTransform.GetWorldCorners(corners);
        // A cropped minimap or in-world monitor must never be classified as the
        // main scene image. Require the four viewport corners, not just area.
        return Mathf.Abs(corners[0].x) < 2 && Mathf.Abs(corners[0].y) < 2 &&
            Mathf.Abs(corners[1].x) < 2 && Mathf.Abs(corners[1].y-Screen.height) < 2 &&
            Mathf.Abs(corners[2].x-Screen.width) < 2 && Mathf.Abs(corners[2].y-Screen.height) < 2 &&
            Mathf.Abs(corners[3].x-Screen.width) < 2 && Mathf.Abs(corners[3].y) < 2;
    }
    private void EnsureSurface(RenderTexture target)
    {
        if (_surface != null && _surface.width == target.width && _surface.height == target.height && _surface.IsCreated()) return;
        if (_surface != null) { _surface.Release(); Object.Destroy(_surface); }
        var descriptor = target.descriptor;
        descriptor.depthBufferBits = 24; descriptor.msaaSamples = 1;
        _surface = OwnedUnityAsset.Retain(new RenderTexture(descriptor) { name = "HybridVR UI stencil surface" });
        if (!_surface.Create()) throw new InvalidOperationException("UI stencil surface unavailable");
    }
    private static bool ExternalAlpha(Material material)
    {
        var keywords = material.shaderKeywords;
        if (keywords != null) for (int i=0;i<keywords.Length;i++) if (keywords[i] == "ETC1_EXTERNAL_ALPHA") return true;
        return false;
    }
    private bool Fallback(string reason) { SetStatus("UiOnly fallback to FullFrame: " + reason); return false; }
    private void SetStatus(string text)
    {
        Status = text;
        if (_lastStatus == text || Time.unscaledTime < _nextLog) return;
        _lastStatus = text; _nextLog = Time.unscaledTime + 5;
        OpenVrDiagnostics.Write("panel capture mode: " + text);
    }
    public void Dispose()
    {
        _commands.Release();
        foreach (var material in _materials.Values) Object.Destroy(material);
        foreach (var material in _softMaterials.Values) Object.Destroy(material);
        _softMaterials.Clear(); _usedSoftMaterials.Clear();
        foreach (var mesh in _meshes.Values) Object.Destroy(mesh);
        _meshes.Clear(); _usedMeshes.Clear();
        _materials.Clear(); _items.Clear(); _canvases.Clear(); _stack.Clear();
        if (_surface != null) { _surface.Release(); Object.Destroy(_surface); _surface = null; }
    }
}
