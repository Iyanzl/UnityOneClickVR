using System;
using UnityEngine;
using UnityEngine.Rendering;
using Uuvr.OpenVR;
using HybridVR.Shared;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Pair the eye colour target with the renderer-owned depth attachment. A fullscreen
// copy is not a substitute for a matching attachment and its view/projection.
internal sealed class SceneDepthTransfer : IDisposable
{
    private float _nextDiscovery;
    private bool _universal;
    private string _pipeline = "unknown";
    private string _lastStatus = "";
    private bool _needsSceneDraw;
    private float _nextLog;
    private readonly UrpEyeDepthScope _eye = new();

    private void Discover()
    {
        if (Time.unscaledTime < _nextDiscovery) return;
        _nextDiscovery = Time.unscaledTime + 5;
        string pipeline = RenderPipelineIdentity.Read();
        if (pipeline != _pipeline) _needsSceneDraw = false;
        _pipeline = pipeline;
        _universal = _pipeline.IndexOf("UniversalRenderPipelineAsset", StringComparison.Ordinal) >= 0;
    }

    internal bool PreferSceneDraw
    {
        get
        {
            Discover();
            // Keep the validated HDRP/URP attachment path when it works. Built-in
            // post effects can clear target depth; draw inside its scene instead.
            return _needsSceneDraw || _pipeline.IndexOf("Built", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal IDisposable? BeginEye(Camera camera, RenderTexture target)
    {
        try
        {
            Discover();
            if (!_universal || camera.orthographic) return null;
            if (_eye.Begin(camera, target)) return _eye;
            Status(_eye.Status);
        }
        catch (Exception e) { Status("eye depth request unavailable: " + e.Message); }
        return null;
    }

    internal void CompleteRender(Camera camera, RenderTexture target) => _eye.CompleteRender(camera, target);

    internal bool BindWorldDepth(CommandBuffer commands, RenderTexture target, Matrix4x4 view, Matrix4x4 projection)
    {
        try
        {
            Discover();
            if (!_universal) { Status("eye target depth; pipeline=" + _pipeline); return true; }
            if (_eye.Target != target || _eye.Frame != Time.frameCount)
            { _needsSceneDraw = true; Status("no current eye depth scope; " + _eye.Status); return false; }
            if (!_eye.MatchesWorldMatrices(view, projection, out var mapping))
            { Status("world depth rejected: " + mapping); return false; }
            var source = _eye.ReadResolvedDepth();
            if (source == null || !ToolDepthPolicy.CanBindEyeDepth(_eye.Frame, Time.frameCount,
                _eye.Target == target, source == target, source.IsCreated(), source.dimension == TextureDimension.Tex2D,
                source.antiAliasing, target.antiAliasing, source.depth, source.width, source.height, target.width, target.height))
            {
                _needsSceneDraw = true;
                Status($"URP depth attachment unavailable: source={_eye.DepthSource}, depth={source?.name ?? "none"}, bits={source?.depth}, samples={source?.antiAliasing}, size={source?.width}x{source?.height}, target={target.width}x{target.height}; world tools foreground fallback");
                return false;
            }
            // No sampling, UV remapping, depth reconstruction or colour-key mask.
            // Tool shaders do not write depth. Caller MUST restore the eye target
            // before clearing overlay depth or drawing depth-writing hands.
            commands.SetRenderTarget(new RenderTargetIdentifier(target), new RenderTargetIdentifier(source));
            commands.SetViewport(new Rect(0, 0, target.width, target.height));
            Status($"URP direct depth attachment queued: source={source.name}, {source.width}x{source.height}, bits={source.depth}, eye={target.name}, {mapping}; no copy; visual occlusion requires verification");
            return true;
        }
        catch (Exception error)
        {
            // This optional capability must not take down panel/input rendering.
            commands.SetRenderTarget(target);
            Status("depth attachment unavailable: " + error.Message);
            return false;
        }
    }
    private void Status(string value)
    {
        if (_lastStatus == value || Time.unscaledTime < _nextLog) return;
        _lastStatus = value;
        _nextLog = Time.unscaledTime + 5;
        OpenVrDiagnostics.Write("tool scene depth: " + value);
    }
    public void Dispose() => _eye.Dispose();
}
