using System;
using System.IO;
using System.Runtime.InteropServices;
using HybridVR.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed class KeyboardPresentation : IDisposable
{
    private Material? _image, _highlight;
    private Texture2D? _texture;
    private float _retry;
    internal readonly VirtualKeyboardInput Input = new(new WindowsKeyboardOutput());
    internal bool Visible;
    internal int Hover = -1;
    internal Vector3 Position, HitPoint;
    internal Quaternion Rotation;
    internal float Width, Height, Units;
    private readonly KeyboardResizeGesture _resize = new();
    private Vector3 _anchor;
    private float _baseWidth, _hitX, _hitY, _configuredScale;
    private bool _locked, _edgeHit;
    internal bool Resizing => _resize.Active;
    internal bool Ready => _image != null && _highlight != null && _texture != null;
    internal void Place(PanelPresentation panel, bool locked, bool visible, float scale)
    {
        Visible = visible && panel.Visible;
        Units = panel.Units;
        _locked = locked; _configuredScale = KeyboardResizeGesture.Clamp(scale);
        if (locked || !Visible) _resize.Reset();
        _baseWidth = Mathf.Clamp(panel.Width / Units, .8f, 1.05f) * Units;
        Width = _baseWidth * (locked ? 1 : _resize.Active ? _resize.Scale : _configuredScale);
        Height = Width * VirtualKeyboardLayout.Height / VirtualKeyboardLayout.Width;
        Rotation = panel.Rotation * Quaternion.Euler(locked ? 25 : 15, locked ? 0 : 20, 0);
        _anchor = panel.Position + panel.Rotation * new Vector3(panel.Width * .5f + KeyboardResizeGesture.PanelEdgeClearance * Units, -panel.Height * .5f, -.04f * Units);
        Position = locked
            ? panel.Position + panel.Rotation * new Vector3(0, -panel.Height * .5f - .035f * Units, 0) - Rotation * Vector3.up * (Height * .5f)
            : AnchoredCentre();
    }
    internal bool Hit(Vector3 origin, Vector3 direction, out int key, out float distance)
    {
        key = -1; distance = 0; _edgeHit = false;
        if (!Visible || !Ready) return false;
        Vector3 normal = Rotation * Vector3.forward;
        float dot = Vector3.Dot(direction, normal);
        if (Mathf.Abs(dot) < .0001f) return false;
        distance = Vector3.Dot(Position - origin, normal) / dot;
        if (distance <= 0 || distance > 30 * Units) return false;
        HitPoint = origin + direction * distance;
        var p = Quaternion.Inverse(Rotation) * (HitPoint - Position);
        // The lower-left corner stays level with the image, outside its bezel.
        _hitX = (p.x + Width * .5f) / Units; _hitY = (p.y + Height * .5f) / Units;
        float u = p.x / Width + .5f, v = p.y / Height + .5f;
        _edgeHit = KeyboardResizeGesture.Edge(u, v);
        if (_resize.Active) return true;
        if (u < 0 || u > 1 || v < 0 || v > 1) return false;
        key = VirtualKeyboardLayout.Hit(u, v);
        return true;
    }
    internal bool Resize(PanelPresentation panel, bool allowed, bool held, bool hit, out bool committed)
    {
        bool owned = _resize.Update(allowed && !_locked, held, hit && _edgeHit, hit,
            _hitX, _hitY, _configuredScale, out committed);
        if (_resize.Active)
        {
            Width = _baseWidth * _resize.Scale;
            Height = Width * VirtualKeyboardLayout.Height / VirtualKeyboardLayout.Width;
            Position = AnchoredCentre();
        }
        return owned;
    }
    private Vector3 AnchoredCentre()
    {
        KeyboardResizeGesture.CentreFromBottomLeft(Width, Height, out float x, out float y);
        return _anchor + Rotation * new Vector3(x, y, 0);
    }
    internal float ResizeScale => _resize.Scale;
    internal void CancelResize() => _resize.Reset();
    internal void Append(CommandBuffer commands, Mesh quad)
        => AppendGeometry(quad, (mesh, matrix, material) => commands.DrawMesh(mesh, matrix, material, 0, 0));
    internal void AppendWorld(WorldToolSceneScope world, Mesh quad)
        => AppendGeometry(quad, world.Add);
    private void AppendGeometry(Mesh quad, Action<Mesh, Matrix4x4, Material> draw)
    {
        if (!Visible || !Ensure()) return;
        draw(quad, Matrix4x4.TRS(Position, Rotation, new Vector3(Width, Height, 1)), _image!);
        for (int i = 0; i < VirtualKeyboardLayout.Keys.Length; i++)
        {
            if (i != Hover && !Input.Selected(i)) continue;
            var key = VirtualKeyboardLayout.Keys[i];
            float w = (key.Width - VirtualKeyboardLayout.KeyGap * 2) * Width / VirtualKeyboardLayout.Width;
            float h = (key.Height - VirtualKeyboardLayout.KeyGap * 2) * Height / VirtualKeyboardLayout.Height;
            Vector3 centre = Position + Rotation * new Vector3(((key.X + key.Width/2) / VirtualKeyboardLayout.Width - .5f) * Width,
                (.5f - (key.Y + key.Height/2) / VirtualKeyboardLayout.Height) * Height, -.0007f * Units);
            float line = .0025f * Units;
            Draw(-w/2, 0, line, h); Draw(w/2, 0, line, h); Draw(0, -h/2, w, line); Draw(0, h/2, w, line);
            void Draw(float x, float y, float sx, float sy) => draw(quad,
                Matrix4x4.TRS(centre + Rotation * new Vector3(x,y,0), Rotation, new Vector3(sx,sy,1)), _highlight!);
        }
    }
    private bool Ensure()
    {
        if (Ready) return true;
        if (Time.unscaledTime < _retry) return false;
        ReleaseAssets();
        try
        {
            using var stream = typeof(KeyboardPresentation).Assembly.GetManifestResourceStream("HybridVR.Keyboard.rgba") ?? throw new FileNotFoundException("Keyboard atlas missing");
            using var reader = new BinaryReader(stream);
            int width = reader.ReadInt32(), height = reader.ReadInt32();
            if (width < 1 || height < 1 || width > 4096 || height > 2048) throw new InvalidDataException("Keyboard dimensions invalid");
            var pixels = reader.ReadBytes(checked(width * height * 4));
            if (pixels.Length != width * height * 4 || stream.ReadByte() != -1) throw new InvalidDataException("Keyboard atlas incomplete");
            _texture = OwnedUnityAsset.Retain(new Texture2D(width, height, TextureFormat.RGBA32, false));
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { _texture.LoadRawTextureData(pinned.AddrOfPinnedObject(), pixels.Length); _texture.Apply(false, true); }
            finally { pinned.Free(); }
            _texture.filterMode = FilterMode.Bilinear; _texture.wrapMode = TextureWrapMode.Clamp;
            var shader = Shader.Find("UI/Default");
            if (shader == null || !shader.isSupported) throw new NotSupportedException("Keyboard shader unavailable");
            _image = Make(shader, Color.white, _texture);
            _highlight = Make(shader, new Color(.2f, .95f, .75f, 1), Texture2D.whiteTexture);
            Uuvr.OpenVR.OpenVrDiagnostics.Write("virtual keyboard ready: 104-key eye-only atlas; no camera or screen capture");
            return true;
        }
        catch (Exception error)
        {
            ReleaseAssets(); _retry = Time.unscaledTime + 5;
            Uuvr.OpenVR.OpenVrDiagnostics.Write("keyboard visual unavailable; panel retained: " + error.Message);
            return false;
        }
    }
    private static Material Make(Shader shader, Color color, Texture texture)
    {
        var result = OwnedUnityAsset.Retain(new Material(shader) { color = color, mainTexture = texture });
        result.SetInt("_ZWrite", 0); result.SetInt("_Cull", (int)CullMode.Off);
        result.SetInt("_ZTest", (int)CompareFunction.Always); result.SetInt("_StencilComp", (int)CompareFunction.Always);
        return result;
    }
    private void ReleaseAssets()
    { Object.Destroy(_image); Object.Destroy(_highlight); Object.Destroy(_texture); _image = _highlight = null; _texture = null; }
    public void Dispose() { _resize.Reset(); Input.Suspend(); ReleaseAssets(); }
}
