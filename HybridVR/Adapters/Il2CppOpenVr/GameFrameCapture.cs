using System;
using System.Collections;
using HybridVR.Shared;
using UnityEngine;
using Uuvr.OpenVR;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed class GameFrameCapture : IDisposable
{
    private readonly InteractionSettings _settings;
    private RenderTexture? _full;
    private readonly UiOnlyCapture _uiOnly = new();
    private readonly FullFrameAlpha _opaqueFrame = new();
    internal RenderTexture? Texture { get; private set; }
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal int Generation { get; private set; }
    internal bool Requested, MenuOpen;
    internal bool HasFrame { get; private set; }
    internal bool UiOnlyActive { get; private set; }
    private bool _disposed;
    private double _next;
    private int _count;
    private Camera? _suppressedCamera;
    private bool _hideDesktop;
    private int _skippedSourcePasses;
    internal bool NeedsFrame => (Requested || MenuOpen) && Time.unscaledTime >= _next;
    internal string DesktopStatus => $"desktopHidden={_hideDesktop}, preserveOriginalPass={_settings.PreserveOriginalCameraPass.Value}, skippedSourcePasses={_skippedSourcePasses}";
    internal string Status { get; private set; } = "Capture waiting";
    internal GameFrameCapture(InteractionSettings settings) { _settings = settings; }
    internal IEnumerator Frames()
    {
        var end = new WaitForEndOfFrame();
        while (!_disposed)
        {
            yield return end;
            if (_disposed) yield break;
            try
            {
                if (NeedsFrame)
                {
#if HYBRID_MONO
                    _next = Time.unscaledTime + 1d / HybridVR.Adapters.Mono.FrameworkCompatibility.Clamp(_settings.CaptureHz.Value, 10, 90);
#else
                    _next = Time.unscaledTime + 1d / Math.Clamp(_settings.CaptureHz.Value, 10, 90);
#endif
                    Capture();
                }
                // Capture before hiding; never feed the hidden presentation into the panel.
                if (_hideDesktop)
                {
                    var previous = RenderTexture.active;
                    try { Graphics.SetRenderTarget(null as RenderTexture); GL.Clear(false, true, Color.black); }
                    finally { RenderTexture.active = previous; }
                }
            }
            catch (Exception e)
            {
                Status = "Capture failed: " + e.GetType().Name;
                OpenVrDiagnostics.Write(Status + ": " + e.Message);
                _next += 2;
            }
            finally { RestoreDesktopCamera(); }
        }
    }
    internal void PrepareDesktop(Camera source, bool hide)
    {
        RestoreDesktopCamera();
        _hideDesktop = hide;
        if (!DesktopFramePolicy.SkipSourcePass(hide, NeedsFrame, _settings.PreserveOriginalCameraPass.Value) || !source.enabled) return;
        _suppressedCamera = source;
        source.enabled = false;
        _skippedSourcePasses++;
    }
    internal void ResumeDesktop() { _hideDesktop = false; RestoreDesktopCamera(); }
    private void RestoreDesktopCamera()
    {
        if (_suppressedCamera != null) _suppressedCamera.enabled = true;
        _suppressedCamera = null;
    }
    private void Capture()
    {
        int width = Screen.width, height = Screen.height;
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 33554432) return;
        if (_full == null || Texture == null || !_full.IsCreated() || !Texture.IsCreated() || Width != width || Height != height)
        {
            Release();
            Width = width; Height = height;
            PanelCaptureLayout.TryBoundedTextureSize(width, height, 1920, out int tw, out int th);
            _full = Make(width, height, "HybridVR game framebuffer");
            Texture = Make(tw, th, "HybridVR panel image");
            Generation++;
            OpenVrDiagnostics.Write($"capture resources: game={width}x{height}, panel={tw}x{th}, generation={Generation}");
        }
        // The panel is drawn only into eye targets, never the desktop framebuffer.
        if (!MenuOpen) UiOnlyActive = _settings.CaptureMode.Value == PanelCaptureMode.UiOnly && _uiOnly.TryCapture(Texture!);
        if (!MenuOpen && UiOnlyActive)
        {
            HasFrame = true;
            Status = _uiOnly.Status;
            UiCaptureDiagnostics.Capture(Texture!, Status);
            return;
        }
        ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
        var previous = RenderTexture.active;
        try { Graphics.Blit(_full, Texture); _opaqueFrame.Apply(Texture!); }
        finally { RenderTexture.active = previous; }
        HasFrame = true;
        Status = $"Capture {_count++}: {Texture!.width}x{Texture.height}" +
            (!MenuOpen && _settings.CaptureMode.Value == PanelCaptureMode.UiOnly ? "; " + _uiOnly.Status : "");
        if (!MenuOpen) UiCaptureDiagnostics.Capture(Texture!, Status);
    }
    private static RenderTexture Make(int width, int height, string name)
    {
        var t = OwnedUnityAsset.Retain(new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        { name = name, antiAliasing = 1, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
        if (t.Create()) return t;
        Object.Destroy(t);
        throw new InvalidOperationException("Capture texture allocation failed");
    }
    private void Release()
    {
        foreach (var t in new[] { _full, Texture }) if (t != null) { t.Release(); Object.Destroy(t); }
        _full = Texture = null;
        HasFrame = false;
    }
    public void Dispose() { _disposed = true; ResumeDesktop(); Release(); _uiOnly.Dispose(); _opaqueFrame.Dispose(); }
}
