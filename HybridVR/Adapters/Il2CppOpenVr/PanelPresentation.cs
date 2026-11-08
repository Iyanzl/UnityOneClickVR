using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Uuvr.OpenVR;
using HybridVR.Shared;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Foreground tools are drawn after the scene; world tools use eye-scoped renderers.
// No extra camera, canvas redirection or game-camera layer edits.
internal sealed class PanelPresentation : IDisposable
{
    private Mesh? _quad, _imageQuad;
    private Material? _image, _white, _beam, _cursor;
    private Texture2D? _cursorTexture;
    private float _retryAfter;
    private int _generation;
    private bool _disposed;
    private readonly SceneDepthTransfer _sceneDepth = new();
    private readonly WorldToolSceneScope _worldTools = new();
    private bool _buildingWorldTools, _worldToolsRendered;
    internal bool ResourcesReady => !_disposed && _quad != null && _imageQuad != null && _image != null && _white != null && _beam != null && _cursor != null;
    private readonly CommandBuffer _commands = new() { name = "HybridVR eye-only interaction" };
    internal Vector3 Position, RayStart, RayEnd, CursorPoint;
    internal Quaternion Rotation = Quaternion.identity;
    internal float Width = .8f, Height = .45f, Units = 1;
    internal bool Visible, HasRay, HasCursor, UseSceneDepth;
    internal Texture? Image;
    internal HandPresentation? Hands;
    internal KeyboardPresentation? Keyboard;
    internal Quaternion? CursorRotation;

    internal IDisposable? BeginSceneDepth(Camera camera, RenderTexture target)
    {
        _worldToolsRendered = false;
        if (!UseSceneDepth || !EnsureResources()) return null;
        try
        {
            if (_sceneDepth.PreferSceneDraw && _worldTools.Begin(camera))
            {
                _buildingWorldTools = true;
                AppendSurfaceAndPointer(camera.transform.position);
                _worldTools.Commit();
                return _worldTools;
            }
        }
        catch (Exception error)
        {
            _worldTools.Dispose();
            OpenVrDiagnostics.Write("world tool scene path unavailable: " + error.Message);
        }
        finally { _buildingWorldTools = false; }
        return _sceneDepth.BeginEye(camera, target);
    }

    internal void CompleteSceneDepth(Camera camera, RenderTexture target)
    {
        _worldToolsRendered = _worldTools.Active;
        _sceneDepth.CompleteRender(camera, target);
    }

    internal PanelPresentation()
    {
        EnsureResources();
    }
    private void BuildResources()
    {
        _quad = BuildQuad(false);
        // Captured D3D framebuffer has a top-left origin; PNG cursor UVs do not.
        _imageQuad = BuildQuad(SystemInfo.graphicsUVStartsAtTop);
        OpenVrDiagnostics.Write("panel capture UV flipY=" + SystemInfo.graphicsUVStartsAtTop);
        var shader = Shader.Find("UI/Default");
        if (shader == null || !shader.isSupported) throw new NotSupportedException("UI/Default shader unavailable");
        _image = Material(shader, Color.white);
        _white = Material(shader, Color.white);
        _beam = Material(shader, new Color(.1f,.85f,1,1));
        _cursor = Material(shader, Color.white);
        try { BuildCursor(); }
        catch (Exception error)
        {
            Object.Destroy(_cursorTexture);
            _cursorTexture = null;
            OpenVrDiagnostics.Write("cursor texture unavailable; using crosshair, panel and hands retained: " + error);
        }
    }
    private void BuildCursor()
    {
        using var stream = typeof(PanelPresentation).Assembly.GetManifestResourceStream("HybridVR.Cursor.rgba")
            ?? throw new FileNotFoundException("Embedded cursor missing");
        var asset = RawCursorAsset.Read(stream);
        _cursorTexture = OwnedUnityAsset.Retain(new Texture2D(asset.Width, asset.Height, TextureFormat.RGBA32, false));
        var pinned = GCHandle.Alloc(asset.Pixels, GCHandleType.Pinned);
        try
        {
            // Use the pointer overload. Unity 6's byte-array overload also routes through Span.
            _cursorTexture.LoadRawTextureData(pinned.AddrOfPinnedObject(), asset.Pixels.Length);
            _cursorTexture.Apply(false, true);
        }
        finally { pinned.Free(); }
        _cursor!.mainTexture = _cursorTexture;
        _cursorTexture.wrapMode = TextureWrapMode.Clamp;
        OpenVrDiagnostics.Write("cursor texture ready: raw RGBA " + asset.Width + "x" + asset.Height);
    }
    private static Mesh BuildQuad(bool flipY)
    {
        var mesh = OwnedUnityAsset.Retain(new Mesh { name = flipY ? "HybridVR capture quad" : "HybridVR tool quad" });
        try
        {
            mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0), new Vector3(.5f,-.5f,0) };
            float bottom = flipY ? 1 : 0, top = flipY ? 0 : 1;
            mesh.uv = new[] { new Vector2(0,bottom), new Vector2(0,top), new Vector2(1,top), new Vector2(1,bottom) };
            mesh.triangles = new[] { 0,1,2, 0,2,3 };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.RecalculateBounds();
            return mesh;
        }
        catch { Object.Destroy(mesh); throw; }
    }
    private bool EnsureResources()
    {
        if (ResourcesReady) return true;
        if (_disposed || Time.unscaledTime < _retryAfter) return false;
        ReleaseAssets();
        try
        {
            BuildResources();
            OpenVrDiagnostics.Write("panel resources ready: generation=" + ++_generation);
            return true;
        }
        catch (Exception error) { Invalidate(error); return false; }
    }
    internal void Invalidate(Exception error)
    {
        _commands.Clear();
        ReleaseAssets();
        _retryAfter = Time.unscaledTime + 2;
        OpenVrDiagnostics.Write("panel visual retry; controls retained: " + error);
    }
    private static Material Material(Shader shader, Color color)
    {
        var m = OwnedUnityAsset.Retain(new Material(shader) { color = color, mainTexture = Texture2D.whiteTexture });
        m.SetInt("_ZWrite", 0); m.SetInt("_Cull", (int)CullMode.Off);
        m.SetInt("_ZTest", (int)CompareFunction.Always);
        m.SetInt("_StencilComp", (int)CompareFunction.Always);
        return m;
    }
    internal bool Hit(Vector3 origin, Vector3 direction, bool drag, out Vector2 uv)
    {
        uv = default;
        Vector3 normal = Rotation * Vector3.forward;
        float denominator = Vector3.Dot(normal, direction);
        if (!Visible || !ResourcesReady || Width <= 0 || Height <= 0 || Mathf.Abs(denominator) < .0001f) return false;
        float distance = Vector3.Dot(Position - origin, normal) / denominator;
        if (distance <= 0 || distance > 30 * Units) return false;
        CursorPoint = origin + direction * distance;
        Vector3 local = Quaternion.Inverse(Rotation) * (CursorPoint - Position);
        uv = new Vector2(local.x / Width + .5f, local.y / Height + .5f);
        return drag || (uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1);
    }
    internal void Render(Vector3 eyePosition, Quaternion eyeRotation, Matrix4x4 projection, RenderTexture target)
    {
        if (!EnsureResources()) return;
        _commands.Clear();
        _commands.SetRenderTarget(target);
        // Use the same immutable physical eye pose as the frame, not game camera callbacks' mutable matrices.
        var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(eyePosition, eyeRotation, Vector3.one).inverse;
        if (UseSceneDepth && !_worldToolsRendered)
        {
            bool depthAvailable = _sceneDepth.BindWorldDepth(_commands, target, view, projection);
            // Preserve the eye's scene depth AND projection for world-placed tools.
            // No extra scene render or CPU readback. Transparent objects that do not
            // write depth follow the game's normal transparent rendering limitations.
            _commands.SetViewProjectionMatrices(view, projection);
            _commands.SetGlobalFloat("unity_GUIZTestMode", (float)(depthAvailable ? CompareFunction.LessEqual : CompareFunction.Always));
            AppendSurfaceAndPointer(eyePosition);
        }
        // Hand-held, L3-locked and settings tools must remain legible in front of
        // scenery. Their depth range must not inherit a source camera's near=1m.
        // Clear only depth, after world tools, to retain glove self-occlusion.
        _commands.SetRenderTarget(target);
        _commands.ClearRenderTarget(true, false, Color.clear);
        ToolDepthPolicy.OverlayDepth(Units, out projection.m22, out projection.m23);
        _commands.SetViewProjectionMatrices(view, projection);
        _commands.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
        Hands?.Append(_commands);
        if (!UseSceneDepth) AppendSurfaceAndPointer(eyePosition);
        _commands.SetGlobalFloat("unity_GUIZTestMode", Shader.GetGlobalFloat("unity_GUIZTestMode"));
        Graphics.ExecuteCommandBuffer(_commands);
    }
    private void AppendSurfaceAndPointer(Vector3 eyePosition)
    {
        if (Visible && Image != null)
        {
            _image!.mainTexture = Image;
            Draw(Position, Rotation, Width, Height, _image, _imageQuad);
            // Short corner marks identify the surface without framing the whole view.
            foreach (float x in new[] { -.5f, .5f }) foreach (float y in new[] { -.5f, .5f })
            {
                float margin = PanelHitRegion.MarginMetres * Units;
                float outerX = x * Width + Math.Sign(x) * margin;
                float outerY = y * Height + Math.Sign(y) * margin;
                Draw(Position + Rotation * new Vector3(outerX - Math.Sign(x) * .02f * Units, outerY, -.0005f * Units), Rotation, .035f * Units, .002f * Units, _white);
                Draw(Position + Rotation * new Vector3(outerX, outerY - Math.Sign(y) * .02f * Units, -.0005f * Units), Rotation, .002f * Units, .035f * Units, _white);
            }
        }
        if (_buildingWorldTools) Keyboard?.AppendWorld(_worldTools, _quad!);
        else Keyboard?.Append(_commands, _quad!);
        if (HasRay)
        {
            Vector3 direction = RayEnd - RayStart;
            Vector3 normal = Vector3.Cross(direction, eyePosition - RayStart);
            if (direction.sqrMagnitude > .00001f && normal.sqrMagnitude > .00001f)
                Draw((RayStart + RayEnd) * .5f, Quaternion.LookRotation(Vector3.Cross(normal, direction), direction),
                    .0015f * Units, direction.magnitude, _beam);
        }
        if (Visible && HasCursor)
        {
            // PNG's tip is its upper-left corner. Keep visual and input at the same intersection.
            float size = .018f * Units;
            var cursorRotation = CursorRotation ?? Rotation;
            var centre = CursorPoint + cursorRotation * new Vector3(size * .5f, -size * .5f, -.001f * Units);
            if (_cursorTexture != null) Draw(centre, cursorRotation, size, size, _cursor);
            else
            {
                var tip = CursorPoint + cursorRotation * new Vector3(0, 0, -.001f * Units);
                Draw(tip, cursorRotation, .008f * Units, .001f * Units, _white);
                Draw(tip, cursorRotation, .001f * Units, .008f * Units, _white);
            }
        }
    }
    private void Draw(Vector3 position, Quaternion rotation, float width, float height, Material? material, Mesh? mesh = null)
    {
        var matrix = Matrix4x4.TRS(position, rotation, new Vector3(width, height, 1));
        if (_buildingWorldTools) _worldTools.Add((mesh ?? _quad)!, matrix, material!);
        else _commands.DrawMesh(mesh ?? _quad, matrix, material, 0, 0);
    }
    private void ReleaseAssets()
    {
        _worldTools.Release();
        foreach (var m in new[] { _image, _white, _beam, _cursor }) Object.Destroy(m);
        Object.Destroy(_quad); Object.Destroy(_imageQuad); Object.Destroy(_cursorTexture);
        _image = _white = _beam = _cursor = null;
        _quad = _imageQuad = null; _cursorTexture = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _commands.Release();
        _sceneDepth.Dispose();
        ReleaseAssets();
    }
}
