using System;
using UnityEngine;
using UnityEngine.Rendering;
using Uuvr.OpenVR;

namespace HybridVR.MonoRuntime;

internal sealed class MonoIsolatedEyeCameras : IDisposable
{
    private readonly Camera _source;
    private readonly EyeCameraExperimentMode _mode;
    private readonly Camera[] _eyes;
    private readonly HdrpEyeSettings[] _settings;
    private bool _disposed;

    internal static MonoIsolatedEyeCameras? Create(Camera source)
    {
        var mode = EyeCameraExperiment.Parse(Environment.GetCommandLineArgs());
        OpenVrDiagnostics.Write("eye camera experiment: mode=" + mode + "; phase selected by render-loop policy; capture/input/skin policy unchanged");
        return mode == EyeCameraExperimentMode.Source ? null : new MonoIsolatedEyeCameras(source, mode);
    }

    private MonoIsolatedEyeCameras(Camera source, EyeCameraExperimentMode mode)
    {
        _source = source;
        _mode = mode;
        _eyes = new Camera[mode == EyeCameraExperimentMode.Dual ? 2 : 1];
        _settings = new HdrpEyeSettings[_eyes.Length];
        try
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || pipeline.GetType().FullName != "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset")
                throw new NotSupportedException("This isolated-eye candidate is HDRP-only; use A for other pipelines");
            Component? data = null;
            foreach (var component in source.GetComponents<Component>())
                if (component != null && component.GetType().FullName == "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData")
                    data = component;
            if (data == null) throw new NotSupportedException("Source HDRP camera data unavailable");
            var custom = data.GetType().GetProperty("hasCustomRender");
            if (custom == null || (bool)custom.GetValue(data, null)!)
                throw new NotSupportedException("Custom HDRP render callbacks cannot be isolated by this candidate");
            for (int i = 0; i < _eyes.Length; i++)
            {
                var root = new GameObject("HybridVR.Test.Eye." + (mode == EyeCameraExperimentMode.Shared ? "Shared" : i == 0 ? "Left" : "Right"));
                root.SetActive(false);
                root.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(root);
                try
                {
                    var eye = root.AddComponent<Camera>();
                    _eyes[i] = eye;
                    eye.enabled = false;
                    var target = root.AddComponent(data.GetType());
                    _settings[i] = new HdrpEyeSettings(data, target);
                    root.SetActive(true);
                    Prepare(i);
                }
                catch { _eyes[i] = null!; root.SetActive(false); UnityEngine.Object.Destroy(root); throw; }
            }
            OpenVrDiagnostics.Write($"isolated eyes created: mode={mode}, source={source.name}/{source.GetInstanceID()}, left={_eyes[0].GetInstanceID()}, right={_eyes[EyeCameraExperiment.Slot(mode, true)].GetInstanceID()}, sourceCommandBuffers={source.commandBufferCount}; no game scripts or command buffers cloned");
            foreach (var component in source.GetComponents<Component>())
                if (component != null) OpenVrDiagnostics.Write("isolated source component: " + component.GetType().FullName);
        }
        catch { Dispose(); throw; }
    }

    internal Camera Prepare(bool right) => Prepare(EyeCameraExperiment.Slot(_mode, right));

    private Camera Prepare(int index)
    {
        if (_disposed || _source == null || _eyes[index] == null)
            throw new InvalidOperationException("Isolated eye/source camera lost");
        var eye = _eyes[index];
        eye.CopyFrom(_source);
        eye.enabled = false;
        eye.stereoTargetEye = StereoTargetEyeMask.None;
        eye.targetTexture = null;
        eye.rect = new Rect(0f, 0f, 1f, 1f);
        eye.transform.SetPositionAndRotation(_source.transform.position, _source.transform.rotation);
        // Copy settings, not a cached game-camera view/culling matrix.
        eye.ResetWorldToCameraMatrix();
        eye.ResetCullingMatrix();
        _settings[index].Copy();
        return eye;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var eye in _eyes)
        {
            if (eye == null) continue;
            eye.enabled = false;
            eye.targetTexture = null;
            eye.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(eye.gameObject);
        }
    }
}
