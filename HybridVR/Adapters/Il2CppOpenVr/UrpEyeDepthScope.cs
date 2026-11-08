using System;
using System.Reflection;
using UnityEngine;
using HybridVR.Shared;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Optional URP capability, resolved at runtime so built-in games need no URP DLL.
// The enum itself is restored: restoring only requiresDepthTexture would destroy
// the camera's UsePipelineSettings/Off distinction.
internal sealed class UrpEyeDepthScope : IDisposable
{
    private Camera? _boundCamera;
    private object? _data, _on, _before;
    private PropertyInfo? _option;
    private PropertyInfo? _rendererProperty, _depthProperty, _textureProperty;
    private FieldInfo? _depthField;
    private Type? _rendererType;
    private object? _renderer;
#if !HYBRID_MONO
    private IntPtr _rendererPointer;
#endif
    private Texture? _previousTexture;
    private bool _active;
    private bool _rendered;
    private Matrix4x4 _beginView, _beginProjection, _endView, _endProjection;
    private float _retryAfter;
    internal RenderTexture? Target { get; private set; }
    internal int Frame { get; private set; }
    internal string Status { get; private set; } = "not requested";
    internal string DepthSource { get; private set; } = "none";

    internal bool Begin(Camera camera, RenderTexture target)
    {
        if (_active) { Status = "nested eye scope rejected"; return false; }
        try
        {
            if (_boundCamera == camera && _data == null && Time.unscaledTime < _retryAfter) return false;
            if (_boundCamera != camera || _data == null)
            {
                _retryAfter = Time.unscaledTime + 5;
                _data = null; _option = null; _boundCamera = camera;
                _renderer = null;
#if !HYBRID_MONO
                _rendererPointer = IntPtr.Zero;
#endif
                Type? type = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", false);
                    if (type != null) break;
                }
                if (type == null) { Status = "URP camera type unavailable"; return false; }
                _option = type.GetProperty("requiresDepthOption");
                if (_option == null || !_option.CanRead || !_option.CanWrite || !_option.PropertyType.IsEnum)
                { Status = "URP depth option unavailable"; return false; }
                // Generic GetComponent returns the typed IL2CPP wrapper as well as
                // Mono's actual component. Do not add components to game cameras.
                foreach (var method in typeof(GameObject).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    if (method.Name == "GetComponent" && method.IsGenericMethodDefinition &&
                        method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0)
                    { _data = method.MakeGenericMethod(type).Invoke(camera.gameObject, null); break; }
                if (_data == null) { Status = "existing URP camera data unavailable"; return false; }
                _on = Enum.Parse(_option.PropertyType, "On");
                _rendererProperty = type.GetProperty("scriptableRenderer");
                _rendererType = type.Assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderer", false);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                // IL2CPP exposes native fields as properties; Mono retains fields.
                _depthProperty = _rendererType?.GetProperty("m_DepthTexture", flags);
                _depthField = _rendererType?.GetField("m_DepthTexture", flags);
                var handleType = _depthProperty?.PropertyType ?? _depthField?.FieldType;
                _textureProperty = handleType?.GetProperty("rt", flags);
            }
            _before = _option!.GetValue(_data);
            _previousTexture = Shader.GetGlobalTexture("_CameraDepthTexture");
            _active = true; _rendered = false; DepthSource = "none";
            _option.SetValue(_data, _on);
            // An eye render must publish its own texture, not reuse a desktop or
            // previous-eye global which happens to have a compatible resolution.
            Shader.SetGlobalTexture("_CameraDepthTexture", (Texture?)null);
            Target = target; Frame = Time.frameCount;
            _beginView = camera.worldToCameraMatrix; _beginProjection = camera.projectionMatrix;
            Status = "requested for " + camera.name;
            return true;
        }
        catch (Exception e)
        {
            Status = "URP depth request unavailable: " + e.GetBaseException().Message;
            Dispose(); _data = null; _retryAfter = Time.unscaledTime + 5;
            return false;
        }
    }

    internal void CompleteRender(Camera camera, RenderTexture target)
    {
        _rendered = ToolDepthPolicy.IsCurrentEyeRender(_active, true, Frame, Time.frameCount,
            _boundCamera == camera, Target == target, camera.targetTexture == target);
        if (_rendered) { _endView = camera.worldToCameraMatrix; _endProjection = camera.projectionMatrix; }
    }

    internal bool MatchesWorldMatrices(Matrix4x4 view, Matrix4x4 projection, out string status)
    {
        float viewDelta = Mathf.Max(MatrixDelta(view, _beginView), MatrixDelta(view, _endView));
        float projectionDelta = Mathf.Max(MatrixDelta(projection, _beginProjection), MatrixDelta(projection, _endProjection));
        status = $"viewDelta={viewDelta:0.000000}, projectionDelta={projectionDelta:0.000000}";
        return _active && _rendered && ToolDepthPolicy.MatricesMatch(viewDelta, projectionDelta);
    }

    private static float MatrixDelta(Matrix4x4 a, Matrix4x4 b)
    {
        float result = 0;
        for (int i = 0; i < 16; i++)
        {
            float d = Mathf.Abs(a[i] - b[i]);
            if (float.IsNaN(d) || float.IsInfinity(d)) return float.PositiveInfinity;
            result = Mathf.Max(result, d);
        }
        return result;
    }

    internal RenderTexture? ReadResolvedDepth()
    {
        if (!ToolDepthPolicy.IsCurrentEyeRender(_active, _rendered, Frame, Time.frameCount,
            _boundCamera != null, Target != null, _boundCamera != null && _boundCamera.targetTexture == Target))
        { DepthSource = "no completed current eye"; return null; }
        try
        {
            var renderer = _rendererProperty?.GetValue(_data);
            if (renderer == null || _rendererType == null || _textureProperty == null)
            { DepthSource = "resolved depth capability unavailable"; return null; }
#if HYBRID_MONO
            if (!_rendererType.IsInstanceOfType(renderer))
            { DepthSource = "renderer is not UniversalRenderer"; return null; }
            _renderer = renderer;
#else
            // The declared ScriptableRenderer wrapper cannot access derived native
            // fields. Check the native type before constructing the typed wrapper.
            if (renderer is not Il2CppSystem.Object native)
            { DepthSource = "renderer has no native wrapper"; return null; }
            if (_renderer == null || _rendererPointer != native.Pointer)
            {
                if (native.GetIl2CppType().FullName != _rendererType.FullName)
                { DepthSource = "renderer is not UniversalRenderer"; return null; }
                _renderer = Activator.CreateInstance(_rendererType, new object[] { native.Pointer });
                _rendererPointer = native.Pointer;
            }
#endif
            var handle = _depthProperty != null ? _depthProperty.GetValue(_renderer) : _depthField?.GetValue(_renderer);
            var texture = handle == null ? null : _textureProperty.GetValue(handle) as RenderTexture;
            DepthSource = texture == null ? "renderer resolved RTHandle empty" : "renderer.m_DepthTexture.rt";
            return texture;
        }
        catch (Exception e)
        { DepthSource = "resolved depth unavailable: " + e.GetBaseException().Message; return null; }
    }

    public void Dispose()
    {
        if (!_active) return;
        _active = false; _rendered = false; Target = null;
        try { _option!.SetValue(_data, _before); }
        catch (Exception e) { Status = "URP depth option restore failed: " + e.GetBaseException().Message; }
        finally
        {
            try { Shader.SetGlobalTexture("_CameraDepthTexture", _previousTexture); }
            catch (Exception e) { Status = "URP depth global restore failed: " + e.GetBaseException().Message; }
            finally { _previousTexture = null; _before = null; }
        }
    }
}
