using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HybridVR.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Reuse URP's native RenderObjects implementation, including its RenderGraph
// depth attachment lifetime. No managed subclass injection or extra Camera.Render.
internal sealed class UrpWorldToolPass : IDisposable
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private Object? _feature;
    private object? _featureList, _scenePass, _beforeFilter;
    private Member? _sceneFilter;
    private MethodInfo? _remove;
    private Type? _dataType, _rendererType, _featureType;
    private float _retry;
    private string _lastStatus = "";
    private bool _inserted;
    private int _scanFrame = -1, _occupied;
    private Camera? _scopedCamera;
    private int _cameraMask;
    internal int Layer { get; private set; } = -1;
    internal bool Active => _inserted;

    internal bool Begin(Camera camera, GameObject? ownedRoot)
    {
        if (_inserted) throw new InvalidOperationException("Nested URP world pass");
        if (Time.unscaledTime < _retry) return false;
        try
        {
            if (!RenderPipelineIdentity.Read().Contains("UniversalRenderPipelineAsset")) return false;
            Discover();
            var data = GetComponent(camera.gameObject, _dataType!);
            if (data == null) return Unavailable("no existing URP camera data");
            // A stacked camera may replace the depth before our pass. Do not
            // silently convert stacks or render scaling to make an adapter fit.
            if (Member.Require(_dataType!, "renderType").Get(data)!.ToString() != "Base")
                return Unavailable("overlay camera; R11 depth path retained");
            var stack = Member.Require(_dataType!, "cameraStack").Get(data);
            if (stack != null && (int)Member.Require(stack.GetType(), "Count").Get(stack)! != 0)
                return Unavailable("camera stack; R11 depth path retained");
            if (camera.allowDynamicResolution || !WorldToolPassPolicy.SupportsScale(PipelineScale()))
                return Unavailable("scaled depth; R11 depth path retained");

            var renderer = Typed(Member.Require(_dataType!, "scriptableRenderer").Get(data), _rendererType!);
            if (renderer == null) return Unavailable("not UniversalRenderer");
            _scenePass = Member.Require(_rendererType!, "m_RenderTransparentForwardPass").Get(renderer);
            if (_scenePass == null) return Unavailable("no transparent scene pass");
            _sceneFilter = Member.Require(_scenePass.GetType(), "m_FilteringSettings");
            // Scan once per frame, not once per eye. Do not reserve a game layer
            // across frames: spawned objects or scene changes can claim it.
            if (_scanFrame != Time.frameCount)
            {
                _occupied = 0;
                foreach (var r in Object.FindObjectsOfType<Renderer>())
                    if (r != null && (ownedRoot == null || !r.transform.IsChildOf(ownedRoot.transform)))
                        _occupied |= 1 << r.gameObject.layer;
                foreach (var canvas in Object.FindObjectsOfType<Canvas>())
                    if (canvas != null) _occupied |= 1 << canvas.gameObject.layer;
                _occupied |= TerrainLayers();
                _scanFrame = Time.frameCount;
            }
            Layer = WorldToolPassPolicy.ChooseLayer(camera.cullingMask, _occupied);
            if (Layer < 0) return Unavailable("no unused user layer");

            _featureList = Member.Require(_rendererType!, "rendererFeatures").Get(renderer);
            if (_featureList == null) return Unavailable("renderer feature list unavailable");
            var add = _featureList.GetType().GetMethod("Add", Flags) ?? throw new MissingMethodException("feature Add");
            _remove = _featureList.GetType().GetMethod("Remove", Flags) ?? throw new MissingMethodException("feature Remove");
            EnsureFeature();
            ConfigureFeature(Layer);
            _featureType!.GetMethod("SetActive", Flags)!.Invoke(_feature, new object[] { true });

            _beforeFilter = _sceneFilter.Get(_scenePass) ?? throw new InvalidOperationException("Empty scene filter");
            object filter = RuntimeHelpers.GetObjectValue(_beforeFilter);
            var mask = Member.Require(filter.GetType(), "layerMask");
            mask.Set(filter, (int)mask.Get(filter)! & ~(1 << Layer));
            _sceneFilter.Set(_scenePass, filter);
            _scopedCamera = camera;
            _cameraMask = camera.cullingMask;
            camera.cullingMask = _cameraMask | (1 << Layer);
            // Register removal before invoking Add so partial failures unwind.
            _inserted = true;
            add.Invoke(_featureList, new object[] { _feature! });
            Status("active: native after-post RenderObjects; same-eye scene depth; no exposure/bloom reprocessing; layer=" + Layer + "; scopedCameraMask=" + (_cameraMask != camera.cullingMask));
            return true;
        }
        catch (Exception e)
        {
            Dispose();
            if (_feature != null) Object.Destroy(_feature);
            _feature = null;
            return Unavailable(e.GetBaseException().Message);
        }
    }

    private void Discover()
    {
        if (_dataType != null) return;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var data = assembly.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", false);
            if (data == null) continue;
            _rendererType = assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderer", false);
            _featureType = assembly.GetType("UnityEngine.Rendering.Universal.RenderObjects", false) ??
                assembly.GetType("UnityEngine.Experimental.Rendering.Universal.RenderObjects", false);
            if (_rendererType == null || _featureType == null) break;
            _dataType = data;
            return;
        }
        throw new NotSupportedException("native URP RenderObjects capability unavailable");
    }

    private static int TerrainLayers()
    {
        // TerrainModule is stripped in some games; do not create a hard API dependency.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("UnityEngine.Terrain", false);
            if (type == null) continue;
            var property = type.GetProperty("activeTerrains", BindingFlags.Public | BindingFlags.Static);
            var terrains = property?.GetValue(null) as IEnumerable;
            if (terrains == null) throw new NotSupportedException("terrain layer coverage unavailable");
            int mask = 0;
            foreach (var terrain in terrains)
                if (terrain is Component component && component != null) mask |= 1 << component.gameObject.layer;
            return mask;
        }
        return 0;
    }

    private void EnsureFeature()
    {
        if (_feature != null) return;
        foreach (var method in typeof(ScriptableObject).GetMethods(BindingFlags.Public | BindingFlags.Static))
            if (method.Name == "CreateInstance" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
            {
                _feature = method.MakeGenericMethod(_featureType!).Invoke(null, null) as Object;
                break;
            }
        if (_feature == null) throw new NotSupportedException("native RenderObjects creation unavailable");
        _feature.hideFlags = HideFlags.HideAndDontSave;
        _feature.name = "HybridVR world tools after post";
        var settings = Member.Require(_featureType!, "settings").Get(_feature)!;
        Member.Require(settings.GetType(), "passTag").Set(settings, "HybridVR world tools after post");
        var evt = Member.Require(settings.GetType(), "Event");
        evt.Set(settings, Enum.Parse(evt.Type, "AfterRenderingPostProcessing"));
        var filter = Member.Require(settings.GetType(), "filterSettings").Get(settings)!;
        var queue = Member.Require(filter.GetType(), "RenderQueueType");
        queue.Set(filter, Enum.Parse(queue.Type, "Transparent"));
        var camera = Member.Require(settings.GetType(), "cameraSettings").Get(settings)!;
        Member.Require(camera.GetType(), "overrideCamera").Set(camera, false);
        Member.Require(settings.GetType(), "overrideDepthState").Set(settings, true);
        Member.Require(settings.GetType(), "enableWrite").Set(settings, false);
        Member.Require(settings.GetType(), "depthCompareFunction").Set(settings, CompareFunction.LessEqual);
        _featureType!.GetMethod("Create", Flags)!.Invoke(_feature, null);
    }

    private void ConfigureFeature(int layer)
    {
        var pass = Member.Require(_featureType!, "renderObjectsPass").Get(_feature!) ??
            throw new InvalidOperationException("native RenderObjects pass not created");
        var member = Member.Require(pass.GetType(), "m_FilteringSettings");
        var filter = member.Get(pass)!;
        Member.Require(filter.GetType(), "layerMask").Set(filter, 1 << layer);
        member.Set(pass, filter);
    }

    private static float PipelineScale()
    {
        var property = typeof(GraphicsSettings).GetProperty("currentRenderPipeline") ??
            typeof(GraphicsSettings).GetProperty("renderPipelineAsset");
        var pipeline = property?.GetValue(null);
        if (pipeline == null) throw new NotSupportedException("pipeline scale unavailable");
#if !HYBRID_MONO
        var native = (Il2CppSystem.Object)pipeline;
        Type? type = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if ((type = a.GetType(native.GetIl2CppType().FullName, false)) != null) break;
        pipeline = type == null ? null : Typed(pipeline, type);
        if (pipeline == null) throw new NotSupportedException("native pipeline scale unavailable");
#endif
        return (float)Member.Require(pipeline.GetType(), "renderScale").Get(pipeline)!;
    }

    private static object? Typed(object? value, Type type)
    {
        if (value == null) return null;
#if HYBRID_MONO
        return type.IsInstanceOfType(value) ? value : null;
#else
        if (value is not Il2CppSystem.Object native || native.GetIl2CppType().FullName != type.FullName) return null;
        return Activator.CreateInstance(type, new object[] { native.Pointer });
#endif
    }

    private static object? GetComponent(GameObject go, Type type)
    {
        foreach (var method in typeof(GameObject).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            if (method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                return method.MakeGenericMethod(type).Invoke(go, null);
        return null;
    }

    private bool Unavailable(string reason)
    {
        _retry = Time.unscaledTime + 5;
        Status("R11 fallback: " + reason);
        return false;
    }
    private void Status(string text)
    {
        if (text == _lastStatus) return;
        _lastStatus = text;
        Uuvr.OpenVR.OpenVrDiagnostics.Write("world tools post isolation: " + text);
    }

    public void Dispose()
    {
        try
        {
            if (_inserted) _remove!.Invoke(_featureList, new object[] { _feature! });
        }
        catch (Exception e) { Status("feature removal failed: " + e.GetBaseException().Message); }
        finally
        {
            try { if (_feature != null) _featureType!.GetMethod("SetActive", Flags)!.Invoke(_feature, new object[] { false }); }
            catch (Exception e) { Status("feature disable failed: " + e.GetBaseException().Message); }
            _inserted = false;
            try { if (_beforeFilter != null) _sceneFilter!.Set(_scenePass!, _beforeFilter); }
            catch (Exception e) { Status("filter restoration failed: " + e.GetBaseException().Message); }
            _beforeFilter = null; _scenePass = null; _featureList = null;
            try { if (_scopedCamera != null) _scopedCamera.cullingMask = _cameraMask; }
            catch (Exception e) { Status("camera mask restoration failed: " + e.Message); }
            _scopedCamera = null;
        }
    }
    internal void Release()
    {
        Dispose();
        if (_feature != null) Object.Destroy(_feature);
        _feature = null;
    }

    // Native IL2CPP fields are properties on their managed wrappers.
    private sealed class Member
    {
        private static readonly Dictionary<Tuple<Type, string>, Member> Cache = new();
        private readonly FieldInfo? _field;
        private readonly PropertyInfo? _property;
        internal Type Type => _field?.FieldType ?? _property!.PropertyType;
        private Member(FieldInfo? field, PropertyInfo? property) { _field = field; _property = property; }
        internal static Member Require(Type type, string name)
        {
            var key = Tuple.Create(type, name);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
                var p = t.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
                if (f != null || p != null)
                {
                    var member = new Member(f, p);
                    Cache.Add(key, member);
                    return member;
                }
            }
            throw new MissingMemberException(type.FullName, name);
        }
        internal object? Get(object target) => _field != null ? _field.GetValue(target) : _property!.GetValue(target);
        internal void Set(object target, object value)
        {
            if (_field != null) _field.SetValue(target, value); else _property!.SetValue(target, value);
        }
    }
}
