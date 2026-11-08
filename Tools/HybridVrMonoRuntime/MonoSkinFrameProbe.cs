using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Uuvr.OpenVR;
using Object = UnityEngine.Object;

namespace HybridVR.MonoRuntime;

// Opt-in observations only. No renders, BakeMesh, pose writes or game-method hooks.
public sealed class MonoSkinFrameProbe : MonoBehaviour
{
    private sealed class Binding
    {
        internal SkinnedMeshRenderer Skin = null!;
        internal Transform Bone = null!;
        internal Matrix4x4 Bind;
    }
    private static MonoSkinFrameProbe? _current;
    private OpenVrManager _owner = null!;
    private readonly List<Binding> _bindings = new();
    private readonly List<object> _pending = new();
    private readonly SkinPhaseBudget _budget = new();
    private readonly Stopwatch _clock = new();
    private readonly Harmony _hooks = new("codex.hybridvr.skin-frame-probe");
    private StreamWriter? _writer;
    private float[]? _previous, _preLeft;
    private string _previousPhase = "none", _eye = "outside-eye";
    private string? _failure;
    private bool _disposed, _loopInstalled;
    private int _scene = int.MinValue, _generation, _eventsDropped;
    private double _nextInventory, _nextFlush, _nextLoopCheck, _readMs, _flushMs;

    internal static bool Requested(string[] args) => Array.Exists(args,
        a => a == "--hybrid-skin-diagnostics" || a == "--hybrid-skin-frame-diagnostics");
    internal static void AttachIfRequested(OpenVrManager owner)
    {
        if (!Requested(Environment.GetCommandLineArgs())) return;
        MonoSkinFrameProbe? probe = null;
        try
        {
            if (_current != null) throw new InvalidOperationException("A skin phase probe is already active");
            probe = owner.gameObject.AddComponent<MonoSkinFrameProbe>();
            probe.Initialize(owner);
        }
        catch (Exception error)
        {
            if (probe != null) { probe.DisposeProbe(); Object.Destroy(probe); }
            OpenVrDiagnostics.Write("skin phase probe unavailable; renderer unchanged: " + error);
        }
    }
    private void Initialize(OpenVrManager owner)
    {
        _owner = owner;
        string path = Path.Combine(Path.GetDirectoryName(OpenVrDiagnostics.LogPath)!, "HybridVR.SkinPhases.latest.jsonl");
        _writer = new StreamWriter(path, false);
        _clock.Start(); _current = this;
        Queue(new { kind = "session", schema = 1, build = "M03.1", unity = Application.unityVersion,
            started = DateTime.Now.ToString("o"), sampleEvery = 30, maxFrames = 600, maxSeconds = 600,
            maxBones = 96, maxSkins = 32, maxEventsPerFrame = 64,
            meaning = "CPU local/world/expected skin matrices only; no GPU/cache readback; observer does not change drawing phase" });
        var loop = SkinPhaseLoop.Add(PlayerLoop.GetCurrentPlayerLoop(), ObserveLoop);
        // Set ownership first so partial initialization still has a cleanup path.
        _loopInstalled = true; PlayerLoop.SetPlayerLoop(loop);
        var method = AccessTools.Method(typeof(OpenVrManager), "RenderEye");
        _hooks.Patch(method, prefix: new HarmonyMethod(typeof(MonoSkinFrameProbe), nameof(EyePrefix)),
            finalizer: new HarmonyMethod(typeof(MonoSkinFrameProbe), nameof(EyeFinalizer)));
        Camera.onPreRender += CameraBefore;
        Camera.onPostRender += CameraAfter;
        RenderPipelineManager.beginCameraRendering += SrpBefore;
        RenderPipelineManager.endCameraRendering += SrpAfter;
        StartCoroutine(EndFrames());
        Flush();
        OpenVrDiagnostics.Write("skin phase probe active (read-only, bounded): " + path);
    }
    private void Update()
    {
        if (_disposed || _writer == null) return;
        if (_failure != null || (_budget.Expired(_clock.Elapsed.TotalSeconds) && Time.frameCount != _budget.Frame))
        { DisposeProbe(); return; }
        if (_clock.Elapsed.TotalSeconds < _nextLoopCheck) return;
        _nextLoopCheck = _clock.Elapsed.TotalSeconds + 5;
        try
        {
            if (SkinPhaseLoop.Count(PlayerLoop.GetCurrentPlayerLoop()) != 4)
                _failure = "PlayerLoop replaced by another owner; no automatic reinstall";
        }
        catch (Exception error) { _failure = error.Message; }
    }
    private void ObserveLoop(string phase)
    {
        if (_disposed || _failure != null) return;
        try
        {
            int frame = Time.frameCount;
            if (phase == "before-script-update" && _budget.Begin(frame, _clock.Elapsed.TotalSeconds))
            {
                _previous = _preLeft = null; _previousPhase = "none"; _eye = "outside-eye";
                int scene = SceneManager.GetActiveScene().handle;
                if (_clock.Elapsed.TotalSeconds >= _nextInventory && (scene != _scene || _bindings.Count == 0))
                {
                    _scene = scene; _nextInventory = _clock.Elapsed.TotalSeconds + 5;
                    Inventory();
                }
            }
            Observe(phase);
        }
        catch (Exception error) { _failure = error.ToString(); }
    }
    private static void EyePrefix(OpenVrManager __instance, EVREye eye)
    {
        var probe = _current;
        if (probe == null || !ReferenceEquals(probe._owner, __instance)) return;
        probe._eye = eye == EVREye.Eye_Left ? "left" : "right";
        probe.Observe("before-eye-" + probe._eye);
    }
    // Void finalizer preserves the original exception; diagnostics cannot consume it.
    private static void EyeFinalizer(OpenVrManager __instance, EVREye eye, Exception? __exception)
    {
        var probe = _current;
        if (probe == null || !ReferenceEquals(probe._owner, __instance)) return;
        probe.Observe("after-eye-" + (eye == EVREye.Eye_Left ? "left" : "right"));
        probe._eye = "outside-eye";
        if (__exception != null) probe._failure = "Eye rendering failed: " + __exception.Message;
    }
    private void CameraBefore(Camera camera) => ObserveCamera("builtin-before-camera", camera);
    private void CameraAfter(Camera camera) => ObserveCamera("builtin-after-camera", camera);
    private void SrpBefore(ScriptableRenderContext context, Camera camera) => ObserveCamera("srp-before-camera", camera);
    private void SrpAfter(ScriptableRenderContext context, Camera camera) => ObserveCamera("srp-after-camera", camera);
    private void ObserveCamera(string phase, Camera camera)
    {
        if (_disposed || _failure != null || _budget.Frame != Time.frameCount) return;
        try
        {
            if (_owner != null && camera != null && camera == _owner.vrCamera) Observe(phase, camera);
        }
        catch (Exception error) { _failure = error.ToString(); }
    }
    private IEnumerator EndFrames()
    {
        var end = new WaitForEndOfFrame();
        while (!_disposed)
        {
            yield return end;
            if (_disposed) yield break;
            Observe("end-of-frame");
            if (_clock.Elapsed.TotalSeconds >= _nextFlush)
            {
                try { Flush(); _nextFlush = _clock.Elapsed.TotalSeconds + 2; }
                catch (Exception error) { _failure = error.ToString(); }
            }
        }
    }
    private void Observe(string phase, Camera? eventCamera = null)
    {
        if (_disposed || _failure != null || !_budget.Take(Time.frameCount)) return;
        long start = Stopwatch.GetTimestamp();
        try
        {
            var now = Read();
            if (phase == "before-eye-left") _preLeft = now;
            var camera = eventCamera != null ? eventCamera : _owner.vrCamera;
            var skins = new List<object>();
            if (phase == "before-eye-left")
            {
                var seen = new HashSet<int>();
                var property = typeof(SkinnedMeshRenderer).GetProperty("forceMatrixRecalculationPerRender");
                foreach (var b in _bindings)
                    if (b.Skin != null && seen.Add(b.Skin.GetInstanceID()))
                        skins.Add(new { id = b.Skin.GetInstanceID(), b.Skin.enabled, b.Skin.isVisible,
                            b.Skin.updateWhenOffscreen, recalculate = property?.GetValue(b.Skin) });
            }
            Queue(new { kind = "phase", frame = Time.frameCount, seconds = _clock.Elapsed.TotalSeconds,
                generation = _generation, phase, eyeScope = _eye, previousPhase = _previousPhase,
                camera = camera == null ? 0 : camera.GetInstanceID(),
                cameraEnabled = camera != null && camera.enabled,
                target = camera == null || camera.targetTexture == null ? 0 : camera.targetTexture.GetInstanceID(),
                bones = _bindings.Count, delta = SkinPhaseData.Compare(_previous, now),
                sincePreLeft = SkinPhaseData.Compare(_preLeft, now), skins });
            _previous = now; _previousPhase = phase;
        }
        catch (Exception error) { _failure = error.ToString(); }
        finally { _readMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency; }
    }
    private float[] Read()
    {
        var values = new float[_bindings.Count * SkinPhaseData.Stride];
        for (int i = 0; i < _bindings.Count; i++)
        {
            int offset = i * SkinPhaseData.Stride;
            var b = _bindings[i];
            if (b.Bone == null || b.Skin == null) { values[offset] = float.NaN; continue; }
            var p = b.Bone.localPosition; var q = b.Bone.localRotation; var s = b.Bone.localScale;
            // q and -q describe the same rotation. Canonicalize to avoid sign-only changes.
            float sign = q.w < 0 ? -1 : 1;
            values[offset] = p.x; values[offset+1] = p.y; values[offset+2] = p.z;
            values[offset+3] = q.x*sign; values[offset+4] = q.y*sign; values[offset+5] = q.z*sign; values[offset+6] = q.w*sign;
            values[offset+7] = s.x; values[offset+8] = s.y; values[offset+9] = s.z;
            var world = b.Bone.localToWorldMatrix;
            var palette = b.Skin.transform.worldToLocalMatrix * world * b.Bind;
            for (int j = 0; j < 16; j++) { values[offset+10+j] = world[j]; values[offset+26+j] = palette[j]; }
        }
        return values;
    }
    private void Inventory()
    {
        _generation++; _bindings.Clear();
        int skins = 0;
        foreach (var skin in Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (skin == null || !skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
            if (++skins > 32 || _bindings.Count >= 96) break;
            var bones = skin.bones; var bind = skin.sharedMesh.bindposes;
            var selected = new HashSet<int>();
            void Add(int index)
            {
                if (index < 0 || index >= bones.Length || index >= bind.Length || bones[index] == null ||
                    selected.Count >= 6 || _bindings.Count >= 96 || !selected.Add(index)) return;
                var bone = bones[index];
                int slot = _bindings.Count;
                _bindings.Add(new Binding { Skin = skin, Bone = bone, Bind = bind[index] });
                Queue(new { kind = "bone", generation = _generation, slot, skinId = skin.GetInstanceID(),
                    skin = skin.name, boneId = bone.GetInstanceID(), bone = bone.name, index,
                    parent = bone.parent == null ? "none" : bone.parent.name });
            }
            for (int i = 0; i < bones.Length && i < 2048 && selected.Count < 4; i++)
            {
                if (bones[i] == null) continue;
                var name = bones[i].name.ToLowerInvariant();
                if (name.Contains("head") || name.Contains("hair") || name.Contains("neck") ||
                    name.Contains("breast") || name.Contains("chest") || name.Contains("hand")) Add(i);
            }
            Add(0); Add(bones.Length / 2); Add(bones.Length - 1);
        }
        var types = new HashSet<Type>();
        int activeLate = 0;
        foreach (var component in Object.FindObjectsOfType<MonoBehaviour>())
        {
            if (component == null || !component.isActiveAndEnabled) continue;
            var type = component.GetType();
            var late = type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (late == null) continue;
            activeLate++;
            if (types.Count >= 80 || !types.Add(type)) continue;
            var order = (DefaultExecutionOrder?)Attribute.GetCustomAttribute(type, typeof(DefaultExecutionOrder));
            Queue(new { kind = "late-component", generation = _generation, type = type.FullName,
                assembly = type.Assembly.GetName().Name, exampleObject = component.name,
                declaredOrder = order?.order, note = "Enabled instance; attribute is not proof of serialized execution order" });
        }
        Queue(new { kind = "inventory", generation = _generation, frame = Time.frameCount, scene = _scene,
            bones = _bindings.Count, skinsSeen = skins, activeLate, lateTypes = types.Count,
            note = "Bounded selection, not all character bones; discovery frame may have measurement overhead" });
    }
    private void Queue(object record)
    {
        if (_pending.Count < 512) _pending.Add(record); else _eventsDropped++;
    }
    private void Flush()
    {
        if (_writer == null) return;
        long start = Stopwatch.GetTimestamp();
        foreach (var row in _pending) _writer.WriteLine(JsonConvert.SerializeObject(row));
        _pending.Clear(); _writer.Flush();
        _flushMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    }
    private void OnDisable() => DisposeProbe();
    private void OnDestroy() => DisposeProbe();
    private void DisposeProbe()
    {
        if (_disposed) return;
        _disposed = true;
        if (ReferenceEquals(_current, this)) _current = null;
        Camera.onPreRender -= CameraBefore; Camera.onPostRender -= CameraAfter;
        RenderPipelineManager.beginCameraRendering -= SrpBefore; RenderPipelineManager.endCameraRendering -= SrpAfter;
        try { _hooks.UnpatchSelf(); } catch (Exception error) { OpenVrDiagnostics.Write("skin phase hook cleanup: " + error.Message); }
        if (_loopInstalled)
        {
            try { PlayerLoop.SetPlayerLoop(SkinPhaseLoop.Remove(PlayerLoop.GetCurrentPlayerLoop())); }
            catch (Exception error) { OpenVrDiagnostics.Write("skin phase loop cleanup: " + error.Message); }
        }
        try
        {
            Queue(new { kind = "complete", samples = _budget.Samples, seconds = _clock.Elapsed.TotalSeconds,
                dropped = _budget.Dropped + _eventsDropped, observationMs = _readMs, flushMs = _flushMs, failure = _failure });
            Flush();
        }
        catch (Exception error) { OpenVrDiagnostics.Write("skin phase log cleanup: " + error.Message); }
        finally
        {
            try { _writer?.Dispose(); } catch { /* Logging failure cannot stop the game. */ }
            _writer = null; _bindings.Clear();
        }
        OpenVrDiagnostics.Write("skin phase probe stopped; render timing unchanged; " + (_failure ?? "bounded recording completed or VR stopped"));
    }
}
