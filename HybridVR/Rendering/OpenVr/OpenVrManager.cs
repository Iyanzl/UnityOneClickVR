using System;
using UnityEngine;
using System.IO;
using UnityEngine.Rendering;
#if !HYBRID_MONO
using BepInEx.Unity.IL2CPP.Utils;
using Il2CppInterop.Runtime.Attributes;
#endif
using HybridVR.Adapters.Il2CppOpenVr;
using HybridVR.Shared;

namespace Uuvr.OpenVR;

public class OpenVrManager : MonoBehaviour {
    public Camera vrCamera;
    public bool renderHmdToScreen;
    public float eyeDistanceMultiplier = 1.0f;
    private OpenVrInteractionHost? _interaction;
    private HeadMotionDiagnostics? _headMotionDiagnostics;
    private FrameQueuePolicy? _frameQueuePolicy;
    private readonly RenderLoopPolicy _renderLoopPolicy = new();
    private bool? _lastLoopInUpdate;
#if HYBRID_MONO
    private HybridVR.MonoRuntime.MonoTemporalAaCompatibility? _temporalAa;
    private HybridVR.MonoRuntime.MonoIsolatedEyeCameras? _isolatedEyes;
    private HybridVR.MonoRuntime.MonoEyeRenderPhase? _eyeRenderPhase;
    private Camera? _preparedCamera;
    private int _preparedFrame;
    private HmdMatrix34_t _preparedPose;
    private Utils.RigidTransform _preparedHmd;
    private Utils.RigidTransform[]? _preparedEyes;
    private bool _preparedTrace;
    private long _preparedStart, _preparedPoseEnd;
#endif

#if !HYBRID_MONO
    [HideFromIl2Cpp]
#endif
    public void EnableInteraction(string configDirectory)
    {
        if (_interaction != null || !InitializationSucceeded) return;
        try
        {
            _interaction = new OpenVrInteractionHost(configDirectory);
            this.StartCoroutine(_interaction.CaptureFrames());
        }
        catch (Exception e) { DisableInteraction(e); }
    }

#if !HYBRID_MONO
    [HideFromIl2Cpp]
#endif
    private void DisableInteraction(Exception? error = null)
    {
        _headMotionDiagnostics?.Dispose();
        _headMotionDiagnostics = null;
        if (error != null) OpenVrDiagnostics.Write("interaction disabled; F display retained: " + error);
        try { _interaction?.Dispose(); }
        catch (Exception e) { OpenVrDiagnostics.Write("interaction cleanup: " + e.Message); }
        _interaction = null;
    }

    private void OnGUI()
    {
        _interaction?.ObserveInputGui();
        try { _interaction?.OnGUI(); }
        catch (Exception e) { _interaction?.MenuFailed(e); }
    }

    private readonly TrackedDevicePose_t[] _devicePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private bool _waitingForTracking;
    private float _renderResolutionScale = 1f;
    private float _headsetFrequency;
    private long _lastTimedFrame;
    private readonly RenderCostWindow _renderCosts = new();

    private RenderTexture _leftEyeRenderTexture;
    private RenderTexture _rightEyeRenderTexture;
    private CommandBuffer _leftSubmitCommand;
    private CommandBuffer _rightSubmitCommand;
    private CommandBuffer _beginFrameCommand;
    private IntPtr _submitEventFunction;
    private IntPtr _stereoSubmitToken;
    private NativeSubmitDiagnostics? _submitDiagnostics;
    private static bool _sessionReserved;
    private bool _ownsSession;
    private bool _cleanupPending;
    private IntPtr _leftNativeTexture;
    private IntPtr _rightNativeTexture;
    private float _aspect;
    private float _fieldOfView;
    private int _lastRenderedFrame = -1;
    private bool _renderingActive;
    private bool _firstFrameLogged;
    private bool _firstLateUpdateLogged;
    private bool _firstPoseLogged;
    private bool _firstLeftEyeLogged;
    private bool _firstRightEyeLogged;
    private bool _waitingForSceneCameraLogged;
    private VRTextureBounds_t _submitBounds;
    private EVRSubmitFlags _submitFlags;
    private EColorSpace _submitColorSpace;
    private bool _nativeSubmitLogged;
    private bool _traceNextReboundFrame;
    private int _stereoFramesSubmitted;
    private int _lastHeartbeatFrame;
    private int _cameraRebindCount;
    private Camera? _configuredCamera;
    private bool _configuredCameraWasEnabled;
    private float _configuredCameraFieldOfView;
    private float _configuredCameraAspect;
    private RenderTexture? _configuredCameraTargetTexture;
    private Matrix4x4 _configuredCameraProjectionMatrix;

    public bool InitializationSucceeded { get; private set; }
    public string InitializationFailure { get; private set; } = string.Empty;

    public static OpenVrManager Create() => Create(1f);

    public static OpenVrManager Create(float renderResolutionScale)
    {
        OpenVrDiagnostics.Write("OpenVrManager.Create entered");
        var camera = FindBestCamera(out var cameraSource);
        if (camera == null)
        {
            OpenVrDiagnostics.Write("OpenVrManager.Create failed: no suitable camera");
            Debug.LogWarning("Failed to find suitable camera for OpenVR, can't initialize.");
            return null;
        }
        OpenVrDiagnostics.Write(
            $"camera selected: source={cameraSource}, name={camera.name}, active={camera.isActiveAndEnabled}, " +
            $"depth={camera.depth}, target={(camera.targetTexture == null ? "screen" : camera.targetTexture.name)}");

        GameObject? instanceObject = null;
        try
        {
            OpenVrDiagnostics.Write("manager GameObject create begin");
            instanceObject = new GameObject(nameof(OpenVrManager));
            DontDestroyOnLoad(instanceObject);
            OpenVrDiagnostics.Write("manager marked DontDestroyOnLoad");
            instanceObject.SetActive(false);
            OpenVrDiagnostics.Write("manager AddComponent begin");
            var instance = instanceObject.AddComponent<OpenVrManager>();
            OpenVrDiagnostics.Write("manager AddComponent complete");
            instance.vrCamera = camera;
            instance._renderResolutionScale = EyeRenderSizing.Normalize(renderResolutionScale);
            OpenVrDiagnostics.Write("manager SetActive(true) begin");
            instanceObject.SetActive(true);
            OpenVrDiagnostics.Write(
                $"manager SetActive(true) complete: initialized={instance.InitializationSucceeded}, " +
                $"failure={instance.InitializationFailure}");

            if (instance.InitializationSucceeded) return instance;

            Destroy(instanceObject);
            return null;
        }
        catch (Exception exception)
        {
            OpenVrDiagnostics.Write("OpenVrManager.Create exception: " + exception);
            Debug.LogError("UUVR OpenVR manager creation failed: " + exception);
            if (instanceObject != null) Destroy(instanceObject);
            return null;
        }
    }
    
    private void OnEnable()
    {
        OpenVrDiagnostics.Write("OpenVrManager.OnEnable entered");
        InitializationSucceeded = false;
        InitializationFailure = string.Empty;
        _renderingActive = false;

        try
        {
            if (_sessionReserved)
                throw new InvalidOperationException("A previous OpenVR session still owns the submission bridge; restart the game if its driver call is blocked.");
            if (OpenVrNativeSubmitBridge.GetAbiVersion() != 4)
                throw new InvalidOperationException("Native submit ABI mismatch: deploy the paired IL01-F DLLs.");
            if (OpenVrNativeSubmitBridge.TryStop() == 0)
                throw new InvalidOperationException("Previous native render callback has not drained; refusing to initialize another session.");
            _sessionReserved = _ownsSession = true;
            _submitDiagnostics = new NativeSubmitDiagnostics(Path.Combine(
                Path.GetDirectoryName(OpenVrDiagnostics.LogPath)!, "UUVR.Il2Cpp.submit.latest.log"));
            OpenVrDiagnostics.Write("InitializeOpenVR begin");
            InitializeOpenVR();
            OpenVrDiagnostics.Write("InitializeOpenVR complete");
            _headMotionDiagnostics = HeadMotionDiagnostics.TryCreate();
            InitializeFrameQueueExperiment();
            OpenVrDiagnostics.Write("SetUpCamera begin");
            SetUpCamera();
            OpenVrDiagnostics.Write("SetUpCamera complete");
            InitializationSucceeded = true;
            _renderingActive = true;
#if HYBRID_MONO
            _eyeRenderPhase = HybridVR.MonoRuntime.MonoEyeRenderPhase.Create(DrawPreparedStereoFrame);
            if (_eyeRenderPhase != null)
                OpenVrDiagnostics.Write("explicit after-pre-late diagnostic overrides live render-loop phase for this session");
#endif
            Debug.Log($"UUVR OpenVR render loop ready: texture={_leftEyeRenderTexture.width}x{_leftEyeRenderTexture.height}, camera={vrCamera.name}.");
            OpenVrDiagnostics.Write(
                $"render loop ready: texture={_leftEyeRenderTexture.width}x{_leftEyeRenderTexture.height}, camera={vrCamera.name}");
        }
        catch (Exception exception)
        {
            InitializationFailure = exception.ToString();
#if HYBRID_MONO
            _eyeRenderPhase?.Dispose(); _eyeRenderPhase = null;
#endif
            OpenVrDiagnostics.Write("OpenVrManager.OnEnable failed: " + exception);
            Debug.LogError("UUVR OpenVR initialization failed: " + exception);
            try
            {
                RestoreFrameQueueExperiment();
                RestoreConfiguredCamera();
                ReleaseSession();
            }
            catch (Exception shutdownException)
            {
                OpenVrDiagnostics.Write("OpenVR.Shutdown after initialization failure failed: " + shutdownException);
            }
        }
    }

    private void OnDisable()
    {
#if HYBRID_MONO
        _eyeRenderPhase?.Dispose(); _eyeRenderPhase = null;
#endif
        RestoreFrameQueueExperiment();
        DisableInteraction();
        _renderingActive = false;
        OpenVrDiagnostics.Write("OpenVrManager.OnDisable");
        RestoreConfiguredCamera();
        ReleaseSession();
    }

    private void OnDestroy()
    {
#if HYBRID_MONO
        _eyeRenderPhase?.Dispose(); _eyeRenderPhase = null;
#endif
        RestoreFrameQueueExperiment();
        DisableInteraction();
        OpenVrDiagnostics.Write("OpenVrManager.OnDestroy");
        RestoreConfiguredCamera();
        ReleaseSession();
    }

    private void Update()
    {
        _interaction?.ObserveInputUpdate();
        if (_cleanupPending) ReleaseSession();
        // Application.targetFrameRate = -1; // disabling for now since caused log spam in sons of the forest.
        Application.runInBackground = true; // don't require companion window focus.
        if (_renderingActive && _frameQueuePolicy != null)
        {
            var wasEnabled = _frameQueuePolicy.Enabled;
            var current = QualitySettings.maxQueuedFrames;
            var requested = _frameQueuePolicy.Apply(current, _interaction?.SingleFrameQueueEnabled);
            if (current != requested) QualitySettings.maxQueuedFrames = requested;
            if (wasEnabled != _frameQueuePolicy.Enabled)
                OpenVrDiagnostics.Write($"frame queue setting applied: enabled={_frameQueuePolicy.Enabled}, requested={requested}, readback={QualitySettings.maxQueuedFrames}");
        }
        QualitySettings.vSyncCount = 0; // this applies to the companion window.
        TryRenderLoop(true);
    }

    private void FixedUpdate()
    {
    }

    private void InitializeFrameQueueExperiment()
    {
        _frameQueuePolicy = new FrameQueuePolicy(Environment.GetCommandLineArgs());
        OpenVrDiagnostics.Write($"frame queue experiment: enabled={_frameQueuePolicy.Enabled}, requested={(_frameQueuePolicy.Enabled ? 1 : -1)}, before={QualitySettings.maxQueuedFrames}; native submit and pose timing unchanged");
    }

    private void RestoreFrameQueueExperiment()
    {
        var policy = _frameQueuePolicy;
        _frameQueuePolicy = null;
        if (policy?.OwnsSetting != true) return;
        QualitySettings.maxQueuedFrames = policy.Restore(QualitySettings.maxQueuedFrames);
        OpenVrDiagnostics.Write($"frame queue experiment restored: readback={QualitySettings.maxQueuedFrames}");
    }

    private void LogFrameQueueExperiment()
    {
        if (_frameQueuePolicy?.Enabled == true)
            OpenVrDiagnostics.Write($"frame queue experiment: requested=1, readback={QualitySettings.maxQueuedFrames}");
    }

    private bool UpdatePoses()
    {
        // Only the guarded render callback begins the compositor frame. Never flush
        // Unity's graphics queue to fetch poses from this main-thread callback.
        var system = OpenVR.System;
        float sinceVsync = 0f;
        ulong frameCounter = 0;
        var timingAvailable = system.GetTimeSinceLastVsync(ref sinceVsync, ref frameCounter);
        var frequencyError = ETrackedPropertyError.TrackedProp_Success;
        var photonsError = ETrackedPropertyError.TrackedProp_Success;
        var frequency = system.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd,
            ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref frequencyError);
        _headsetFrequency = frequencyError == ETrackedPropertyError.TrackedProp_Success ? frequency : 0f;
        var photons = system.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd,
            ETrackedDeviceProperty.Prop_SecondsFromVsyncToPhotons_Float, ref photonsError);
        var prediction = PosePrediction.SecondsToPhotons(timingAvailable &&
            frequencyError == ETrackedPropertyError.TrackedProp_Success &&
            photonsError == ETrackedPropertyError.TrackedProp_Success, frequency, sinceVsync, photons);
        var origin = OpenVR.Compositor.GetTrackingSpace();
        system.GetDeviceToAbsoluteTrackingPose(origin, prediction, _devicePoses);
        var hmd = _devicePoses[OpenVR.k_unTrackedDeviceIndex_Hmd];
        var valid = hmd.bDeviceIsConnected && hmd.bPoseIsValid;
        if (!_firstPoseLogged || valid == _waitingForTracking)
        {
            OpenVrDiagnostics.Write($"system pose sample: origin={origin}, predictionMs={prediction * 1000f:0.00}, valid={valid}; compositor begin is render-thread only");
            LogCurrentHmdPose("system pose");
        }
        _firstPoseLogged = true;
        _waitingForTracking = !valid;
        return valid;
    }

    private void LateUpdate()
    {
        TryRenderLoop(false);
    }

#if !HYBRID_MONO
    [HideFromIl2Cpp]
#endif
    private void TryRenderLoop(bool updateCallback)
    {
        if (!_renderingActive) return;
        bool requested = _interaction?.RenderInUpdateEnabled == true;
#if HYBRID_MONO
        if (_eyeRenderPhase != null) requested = false;
#endif
        if (!_renderLoopPolicy.TryEnter(Time.frameCount, updateCallback, requested)) return;
        bool changed = _lastLoopInUpdate != _renderLoopPolicy.InUpdate;
        _lastLoopInUpdate = _renderLoopPolicy.InUpdate;
        int before = _stereoFramesSubmitted;
        RunRenderLoop();
        if (_stereoFramesSubmitted > before && (changed || _stereoFramesSubmitted == 1 || Time.frameCount % 600 == 0))
            OpenVrDiagnostics.Write($"render loop verified: callback={(updateCallback ? "Update" : "LateUpdate")}, frame={Time.frameCount}, queued={_stereoFramesSubmitted}");
    }

    private void RunRenderLoop()
    {
        if (!_renderingActive || _lastRenderedFrame == Time.frameCount) return;
        _lastRenderedFrame = Time.frameCount;

        try
        {
            if (!EnsureSceneCamera()) { _interaction?.Suspend(); return; }
#if HYBRID_MONO
            _temporalAa?.Update();
#endif
            var traceThisFrame = _traceNextReboundFrame;

            if (!_firstLateUpdateLogged)
            {
                _firstLateUpdateLogged = true;
                OpenVrDiagnostics.Write($"first render loop entered: phase={(_renderLoopPolicy.InUpdate ? "Update" : "LateUpdate")}, frame={Time.frameCount}");
            }

            if (OpenVrNativeSubmitBridge.IsActive() == 0)
                throw new InvalidOperationException("Native submission stopped: " + OpenVrNativeSubmitBridge.ReadDiagnostics());
            // Recreated Unity textures need fresh references and a new event generation.
            if (!_leftEyeRenderTexture.IsCreated() || !_rightEyeRenderTexture.IsCreated())
            {
                if (!_leftEyeRenderTexture.Create() || !_rightEyeRenderTexture.Create())
                    throw new InvalidOperationException("Eye render texture recreation failed");
                ConfigureNativeSubmitBridge();
            }
            var sampleStart = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!UpdatePoses()) { _interaction?.Suspend(); _lastTimedFrame = 0; _renderCosts.Reset(); return; }
            var poseEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            if (traceThisFrame) OpenVrDiagnostics.Write($"rebound frame begin: frame={Time.frameCount}");
            var vrLeftEyeTransform = OpenVR.System.GetEyeToHeadTransform(EVREye.Eye_Left);
            var vrRightEyeTransform = OpenVR.System.GetEyeToHeadTransform(EVREye.Eye_Right);
            if (!_firstFrameLogged) OpenVrDiagnostics.Write("first frame: eye transforms acquired");
            if (traceThisFrame) OpenVrDiagnostics.Write("rebound frame: eye transforms acquired");
            if (traceThisFrame) OpenVrDiagnostics.Write("rebound frame: poses acquired");

            var renderPose = _devicePoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking;
            var hmdTransform = new Utils.RigidTransform(renderPose);
            _headMotionDiagnostics?.ObserveFrame(hmdTransform, vrCamera);
            try { _interaction?.Update(vrCamera, hmdTransform, (int)OpenVR.Compositor.GetTrackingSpace()); }
            catch (Exception e) { DisableInteraction(e); }
            var hmdEyeTransform = new Utils.RigidTransform[2];
            hmdEyeTransform[0] = new Utils.RigidTransform(vrLeftEyeTransform);
            hmdEyeTransform[1] = new Utils.RigidTransform(vrRightEyeTransform);

#if HYBRID_MONO
            if (_eyeRenderPhase != null)
            {
                _preparedCamera = vrCamera; _preparedFrame = Time.frameCount;
                _preparedPose = renderPose; _preparedHmd = hmdTransform; _preparedEyes = hmdEyeTransform;
                _preparedTrace = traceThisFrame; _preparedStart = sampleStart; _preparedPoseEnd = poseEnd;
                if (_eyeRenderPhase.TryQueue(_preparedFrame)) return;
            }
#endif
            RenderStereoFrame(renderPose, hmdTransform, hmdEyeTransform, traceThisFrame, sampleStart, poseEnd);
        }
        catch (Exception e) { FailRendering(e); }
    }

#if HYBRID_MONO
    private bool DrawPreparedStereoFrame()
    {
        // Scene unloads between sampling and drawing must not submit a stale camera/pose.
        if (!_renderingActive || _preparedFrame != Time.frameCount || _preparedEyes == null || vrCamera == null || vrCamera != _preparedCamera) return false;
        var before = _stereoFramesSubmitted;
        RenderStereoFrame(_preparedPose, _preparedHmd, _preparedEyes, _preparedTrace, _preparedStart, _preparedPoseEnd);
        return _stereoFramesSubmitted != before;
    }
#endif

#if !HYBRID_MONO
    [HideFromIl2Cpp]
#endif
    private void RenderStereoFrame(HmdMatrix34_t renderPose, Utils.RigidTransform hmdTransform,
        Utils.RigidTransform[] hmdEyeTransform, bool traceThisFrame, long sampleStart, long poseEnd)
    {
        try
        {
            var frameToken = OpenVrNativeSubmitBridge.QueueRenderPose(_stereoSubmitToken, ref renderPose);
            if (frameToken == IntPtr.Zero)
            {
                // A slow/blocked graphics callback must not grow an unbounded pose queue.
                _lastTimedFrame = 0;
                return;
            }
            QueueFrameCommands(frameToken);
            Graphics.ExecuteCommandBuffer(_beginFrameCommand);
            var eyeStart = System.Diagnostics.Stopwatch.GetTimestamp();
            RenderEye(EVREye.Eye_Left, hmdTransform, hmdEyeTransform[0], traceThisFrame);
            var leftEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            RenderEye(EVREye.Eye_Right, hmdTransform, hmdEyeTransform[1], traceThisFrame);
            var rightEnd = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastTimedFrame != 0)
            {
                var msPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _renderCosts.Add((poseEnd - sampleStart) * msPerTick,
                    (leftEnd - eyeStart) * msPerTick, (rightEnd - leftEnd) * msPerTick,
                    (sampleStart - _lastTimedFrame) * msPerTick);
                if (_renderCosts.Count >= 180)
                {
                    OpenVrDiagnostics.Write(_renderCosts.Format());
                    OpenVrDiagnostics.Write($"frame budget: headsetHz={_headsetFrequency:0.##}, targetFrameRate={Application.targetFrameRate}, vSync={QualitySettings.vSyncCount}, desktop={Screen.width}x{Screen.height}, eye={_leftEyeRenderTexture.width}x{_leftEyeRenderTexture.height}, desktopCameraEnabled={vrCamera.enabled}");
                    LogFrameQueueExperiment();
                    _renderCosts.Reset();
                }
            }
            _lastTimedFrame = sampleStart;

            if (traceThisFrame) OpenVrDiagnostics.Write("rebound frame: both eyes rendered and native submit events queued");
            if (renderHmdToScreen) Graphics.Blit(_leftEyeRenderTexture, null as RenderTexture);
            Graphics.SetRenderTarget(null);
            _stereoFramesSubmitted++;
            try { _interaction?.PrepareDesktop(vrCamera); }
            catch (Exception e) { DisableInteraction(e); }
            if (traceThisFrame)
            {
                _traceNextReboundFrame = false;
                OpenVrDiagnostics.Write($"rebound stereo frame submitted: total={_stereoFramesSubmitted}");
            }
            if (Time.frameCount - _lastHeartbeatFrame >= 600)
            {
                _lastHeartbeatFrame = Time.frameCount;
                var leftCount = OpenVrNativeSubmitBridge.GetSubmitCount((int)EVREye.Eye_Left);
                var rightCount = OpenVrNativeSubmitBridge.GetSubmitCount((int)EVREye.Eye_Right);
                var leftError = (EVRCompositorError)OpenVrNativeSubmitBridge.GetLastError((int)EVREye.Eye_Left);
                var rightError = (EVRCompositorError)OpenVrNativeSubmitBridge.GetLastError((int)EVREye.Eye_Right);
                OpenVrDiagnostics.Write(
                    $"render heartbeat: frame={Time.frameCount}, queued={_stereoFramesSubmitted}, " +
                    $"nativeLeft={leftCount}/{leftError}, nativeRight={rightCount}/{rightError}, " +
                    $"camera={(vrCamera == null ? "null" : vrCamera.name)}, rebinds={_cameraRebindCount}");
            }
            if (!_firstFrameLogged)
            {
                _firstFrameLogged = true;
                Debug.Log("UUVR OpenVR first stereo frame queued to native submit bridge.");
                OpenVrDiagnostics.Write("first stereo frame queued to native submit bridge");
            }
        }
        catch (Exception e) { FailRendering(e); }
    }

#if !HYBRID_MONO
    [HideFromIl2Cpp]
#endif
    private void FailRendering(Exception e)
    {
#if HYBRID_MONO
            _eyeRenderPhase?.Dispose(); _eyeRenderPhase = null;
#endif
            _renderingActive = false;
            Graphics.SetRenderTarget(null);
            OpenVrDiagnostics.Write("OpenVR render failed: " + e);
            Debug.LogError("UUVR OpenVR render failed: " + e);
            RestoreFrameQueueExperiment();
            DisableInteraction();
            RestoreConfiguredCamera();
            ReleaseSession();
    }

    private void SetUpCamera()
    {
        if (vrCamera == null) throw new InvalidOperationException("VR camera became null before setup");

        if (_configuredCamera != vrCamera)
        {
            RestoreConfiguredCamera();
            _configuredCamera = vrCamera;
            _configuredCameraWasEnabled = vrCamera.enabled;
            _configuredCameraFieldOfView = vrCamera.fieldOfView;
            _configuredCameraAspect = vrCamera.aspect;
            _configuredCameraTargetTexture = vrCamera.targetTexture;
            _configuredCameraProjectionMatrix = vrCamera.projectionMatrix;
#if HYBRID_MONO
            _temporalAa = new HybridVR.MonoRuntime.MonoTemporalAaCompatibility(vrCamera);
            _isolatedEyes = HybridVR.MonoRuntime.MonoIsolatedEyeCameras.Create(vrCamera);
#endif
        }

        OpenVrDiagnostics.Write(
            $"camera setup state: name={vrCamera.name}, enabled={vrCamera.enabled}, " +
            $"fov={vrCamera.fieldOfView:0.###}, aspect={vrCamera.aspect:0.###}, " +
            $"near={vrCamera.nearClipPlane:0.###}, far={vrCamera.farClipPlane:0.###}");
        OpenVrDiagnostics.Write("camera ownership: original camera remains enabled for desktop rendering");
        var policy = new EyeCameraPolicy(vrCamera.orthographic, vrCamera.nearClipPlane, vrCamera.farClipPlane);
        OpenVrDiagnostics.Write($"eye camera policy: source3D={policy.RenderSource}, orthographic={vrCamera.orthographic}, clear={vrCamera.clearFlags}, near={policy.Near}, far={policy.Far}; eye color cleared every frame");
#if HYBRID_MONO
        HybridVR.MonoRuntime.MonoCameraDiagnostics.Write(vrCamera);
#endif
    }

    private void RestoreConfiguredCamera()
    {
#if HYBRID_MONO
        _isolatedEyes?.Dispose();
        _isolatedEyes = null;
        _temporalAa?.Dispose();
        _temporalAa = null;
#endif
        if (_configuredCamera == null) return;

        try
        {
            _configuredCamera.targetTexture = _configuredCameraTargetTexture;
            _configuredCamera.fieldOfView = _configuredCameraFieldOfView;
            _configuredCamera.aspect = _configuredCameraAspect;
            _configuredCamera.projectionMatrix = _configuredCameraProjectionMatrix;
            _configuredCamera.enabled = _configuredCameraWasEnabled;
            OpenVrDiagnostics.Write($"camera restored after OpenVR release: name={_configuredCamera.name}");
        }
        catch (Exception exception)
        {
            OpenVrDiagnostics.Write("camera restore failed: " + exception);
        }
        finally
        {
            _configuredCamera = null;
            _configuredCameraTargetTexture = null;
        }
    }

    private void LogCurrentHmdPose(string prefix)
    {
        var hmdPose = _devicePoses[OpenVR.k_unTrackedDeviceIndex_Hmd];
        OpenVrDiagnostics.Write(
            $"{prefix}: connected={hmdPose.bDeviceIsConnected}, valid={hmdPose.bPoseIsValid}, " +
            $"tracking={hmdPose.eTrackingResult}");
    }

    private bool EnsureSceneCamera()
    {
        Camera? replacement = null;
        var replacementSource = string.Empty;
        var mainCamera = Camera.main;

        if (vrCamera == null || !vrCamera.gameObject.activeInHierarchy)
        {
            replacement = FindBestCamera(out replacementSource);
        }
        else if (mainCamera != null && mainCamera != vrCamera && mainCamera.isActiveAndEnabled)
        {
            replacement = mainCamera;
            replacementSource = "Camera.main-changed";
        }

        if (replacement == null)
        {
            if (vrCamera != null && vrCamera.gameObject.activeInHierarchy)
            {
                _waitingForSceneCameraLogged = false;
                return true;
            }

            if (!_waitingForSceneCameraLogged)
            {
                _waitingForSceneCameraLogged = true;
                OpenVrDiagnostics.Write("scene camera unavailable; retaining OpenVR session and waiting");
            }
            return false;
        }

        if (replacement == vrCamera)
        {
            _waitingForSceneCameraLogged = false;
            return true;
        }

        var previousName = vrCamera == null ? "null" : vrCamera.name;
        vrCamera = replacement;
        SetUpCamera();
        _cameraRebindCount++;
        _traceNextReboundFrame = true;
        _waitingForSceneCameraLogged = false;
        OpenVrDiagnostics.Write(
            $"scene camera rebound #{_cameraRebindCount}: previous={previousName}, source={replacementSource}, " +
            $"name={vrCamera.name}, depth={vrCamera.depth}, active={vrCamera.gameObject.activeInHierarchy}");
        return true;
    }

    private static Camera? FindBestCamera(out string source)
    {
        var camera = Camera.main;
        source = "Camera.main";
        if (camera != null && camera.isActiveAndEnabled) return camera;

        camera = Camera.current;
        source = "Camera.current";
        if (camera != null && camera.isActiveAndEnabled) return camera;

        camera = null;
        source = "Camera.allCameras";
        var cameras = Camera.allCameras;
        OpenVrDiagnostics.Write($"camera fallback candidates={cameras.Length}");
        for (var i = 0; i < cameras.Length; i++)
        {
            var candidate = cameras[i];
            if (candidate == null || !candidate.isActiveAndEnabled) continue;
            if (camera == null || candidate.depth > camera.depth)
            {
                camera = candidate;
            }
        }

        return camera;
    }

    private static Matrix4x4 Matrix4x4_OpenVr2UnityFormat(ref HmdMatrix44_t mat44Openvr)
    {
        var mat44Unity = Matrix4x4.identity;
        mat44Unity.m00 = mat44Openvr.m0;
        mat44Unity.m01 = mat44Openvr.m1;
        mat44Unity.m02 = mat44Openvr.m2;
        mat44Unity.m03 = mat44Openvr.m3;
        mat44Unity.m10 = mat44Openvr.m4;
        mat44Unity.m11 = mat44Openvr.m5;
        mat44Unity.m12 = mat44Openvr.m6;
        mat44Unity.m13 = mat44Openvr.m7;
        mat44Unity.m20 = mat44Openvr.m8;
        mat44Unity.m21 = mat44Openvr.m9;
        mat44Unity.m22 = mat44Openvr.m10;
        mat44Unity.m23 = mat44Openvr.m11;
        mat44Unity.m30 = mat44Openvr.m12;
        mat44Unity.m31 = mat44Openvr.m13;
        mat44Unity.m32 = mat44Openvr.m14;
        mat44Unity.m33 = mat44Openvr.m15;
        return mat44Unity;
    }

    private void RenderEye(
        EVREye eye,
        Utils.RigidTransform hmdTransform,
        Utils.RigidTransform hmdEyeTransform,
        bool traceThisFrame)
    {
        if (vrCamera == null) return;

#if HYBRID_MONO
        if (_isolatedEyes != null)
        {
            RenderIsolatedEye(eye, hmdTransform, hmdEyeTransform, traceThisFrame);
            return;
        }
#endif
        var logThisEye = traceThisFrame || (eye == EVREye.Eye_Left ? !_firstLeftEyeLogged : !_firstRightEyeLogged);
        if (logThisEye) OpenVrDiagnostics.Write($"first {eye} render begin");
        
        var cameraTransform = vrCamera.transform;
        var prevCameraRotation = cameraTransform.localRotation;
        var prevCameraPosition = cameraTransform.localPosition;
        var prevProjectionMatrix = vrCamera.projectionMatrix;
        var prevTargetTexture = vrCamera.targetTexture;
        var prevFieldOfView = vrCamera.fieldOfView;
        var prevAspect = vrCamera.aspect;
        var prevNear = vrCamera.nearClipPlane;
        var prevFar = vrCamera.farClipPlane;
        var eyePolicy = new EyeCameraPolicy(vrCamera.orthographic, prevNear, prevFar, _interaction?.WorldUnits ?? 1);

        try
        {
            var trackedEyePosition = hmdTransform.pos + hmdTransform.rot * hmdEyeTransform.pos * eyeDistanceMultiplier;
            cameraTransform.localPosition = prevCameraPosition + prevCameraRotation * trackedEyePosition;
            cameraTransform.localRotation = prevCameraRotation * hmdTransform.rot * hmdEyeTransform.rot;
            if (_interaction?.Active == true)
            {
                _interaction.Eye(hmdTransform, hmdEyeTransform, out var eyePosition, out var eyeRotation);
                cameraTransform.SetPositionAndRotation(eyePosition, eyeRotation);
            }

            var projectionMatrix =
                OpenVR.System.GetProjectionMatrix(eye, eyePolicy.Near, eyePolicy.Far,
                    EGraphicsAPIConvention.API_DirectX);
            // Match culling and shader clip parameters to the temporary eye projection.
            vrCamera.nearClipPlane = eyePolicy.Near;
            vrCamera.farClipPlane = eyePolicy.Far;
            vrCamera.projectionMatrix = Matrix4x4_OpenVr2UnityFormat(ref projectionMatrix);
            vrCamera.fieldOfView = _fieldOfView;
            vrCamera.aspect = _aspect;
            var eyeTexture = eye == EVREye.Eye_Left ? _leftEyeRenderTexture : _rightEyeRenderTexture;
            var submitCommand = eye == EVREye.Eye_Left ? _leftSubmitCommand : _rightSubmitCommand;
            vrCamera.targetTexture = eyeTexture;
            var overlayPosition = cameraTransform.position;
            var overlayRotation = cameraTransform.rotation;
            var overlayProjection = vrCamera.projectionMatrix;
            using var depthScope = _interaction?.BeginSceneDepth(vrCamera, eyeTexture);

            // Eye targets persist across frames; overlay/Depth/Nothing cameras do
            // not clear their color. Clear explicitly before both scene and tools.
            var previousActive = RenderTexture.active;
            try { Graphics.SetRenderTarget(eyeTexture); GL.Clear(true, true, Color.black); }
            finally { RenderTexture.active = previousActive; }

            if (logThisEye) OpenVrDiagnostics.Write($"first {eye}: sourceRender={eyePolicy.RenderSource}, toolsNear={eyePolicy.Near}, toolsFar={eyePolicy.Far}");
            var observedView = _headMotionDiagnostics != null ? vrCamera.worldToCameraMatrix : default;
            int eyeScope = _headMotionDiagnostics?.BeginEye() ?? -1;
            try
            {
                if (eyePolicy.RenderSource)
                {
                    vrCamera.Render();
                    _interaction?.CompleteSceneDepth(vrCamera, eyeTexture);
                }
            }
            finally { _headMotionDiagnostics?.EndEye(eyeScope); }
            _headMotionDiagnostics?.ObserveEye(vrCamera, overlayPosition, overlayRotation, observedView, overlayProjection);
            try { _interaction?.Render(overlayPosition, overlayRotation, overlayProjection, eyeTexture); }
            catch (Exception e) { DisableInteraction(e); }
            if (logThisEye) OpenVrDiagnostics.Write($"first {eye}: Camera.Render complete");

            Graphics.ExecuteCommandBuffer(submitCommand);
            if (logThisEye || !_nativeSubmitLogged)
            {
                var nativeTexture = eye == EVREye.Eye_Left ? _leftNativeTexture : _rightNativeTexture;
                OpenVrDiagnostics.Write(
                    $"native bridge {eye} event queued: texture=0x{nativeTexture.ToInt64():X}");
            }

            if (eye == EVREye.Eye_Left)
                _firstLeftEyeLogged = true;
            else
                _firstRightEyeLogged = true;
            if (eye == EVREye.Eye_Right) _nativeSubmitLogged = true;
        }
        finally
        {
            vrCamera.targetTexture = prevTargetTexture;
            vrCamera.nearClipPlane = prevNear;
            vrCamera.farClipPlane = prevFar;
            vrCamera.projectionMatrix = prevProjectionMatrix;
            vrCamera.fieldOfView = prevFieldOfView;
            vrCamera.aspect = prevAspect;
            cameraTransform.localRotation = prevCameraRotation;
            cameraTransform.localPosition = prevCameraPosition;
        }
    }

#if HYBRID_MONO
    private void RenderIsolatedEye(EVREye eye, Utils.RigidTransform head,
        Utils.RigidTransform eyeOffset, bool trace)
    {
        var right = eye == EVREye.Eye_Right;
        var camera = _isolatedEyes!.Prepare(right);
        var policy = new EyeCameraPolicy(vrCamera.orthographic, vrCamera.nearClipPlane, vrCamera.farClipPlane, _interaction?.WorldUnits ?? 1);
        var trackedEye = head.pos + head.rot * eyeOffset.pos * eyeDistanceMultiplier;
        var source = vrCamera.transform;
        var localPosition = source.localPosition + source.localRotation * trackedEye;
        var localRotation = source.localRotation * head.rot * eyeOffset.rot;
        var parent = source.parent;
        var position = parent == null ? localPosition : parent.TransformPoint(localPosition);
        var rotation = parent == null ? localRotation : parent.rotation * localRotation;
        if (_interaction?.Active == true) _interaction.Eye(head, eyeOffset, out position, out rotation);
        camera.transform.SetPositionAndRotation(position, rotation);
        var projection = OpenVR.System.GetProjectionMatrix(eye, policy.Near, policy.Far, EGraphicsAPIConvention.API_DirectX);
        camera.nearClipPlane = policy.Near;
        camera.farClipPlane = policy.Far;
        camera.projectionMatrix = Matrix4x4_OpenVr2UnityFormat(ref projection);
        camera.fieldOfView = _fieldOfView;
        camera.aspect = _aspect;
        var texture = right ? _rightEyeRenderTexture : _leftEyeRenderTexture;
        var submit = right ? _rightSubmitCommand : _leftSubmitCommand;
        camera.targetTexture = texture;
        var overlayProjection = camera.projectionMatrix;
        bool log = trace || (right ? !_firstRightEyeLogged : !_firstLeftEyeLogged);
        try
        {
            using var depthScope = _interaction?.BeginSceneDepth(camera, texture);
            var previousActive = RenderTexture.active;
            try { Graphics.SetRenderTarget(texture); GL.Clear(true, true, Color.black); }
            finally { RenderTexture.active = previousActive; }
            var view = _headMotionDiagnostics != null ? camera.worldToCameraMatrix : default;
            int scope = _headMotionDiagnostics?.BeginEye() ?? -1;
            try
            {
                if (policy.RenderSource)
                {
                    camera.Render();
                    _interaction?.CompleteSceneDepth(camera, texture);
                }
            }
            finally { _headMotionDiagnostics?.EndEye(scope); }
            _headMotionDiagnostics?.ObserveEye(camera, position, rotation, view, overlayProjection);
            try { _interaction?.Render(position, rotation, overlayProjection, texture); }
            catch (Exception e) { DisableInteraction(e); }
            Graphics.ExecuteCommandBuffer(submit);
            if (log) OpenVrDiagnostics.Write($"isolated {eye} submitted: eyeCamera={camera.name}/{camera.GetInstanceID()}, source={vrCamera.name}/{vrCamera.GetInstanceID()}, size={texture.width}x{texture.height}, source3D={policy.RenderSource}, phase=LateUpdate");
            if (right) _firstRightEyeLogged = true; else _firstLeftEyeLogged = true;
            _nativeSubmitLogged = true;
        }
        finally { camera.targetTexture = null; }
    }
#endif

    private void InitializeOpenVR()
    {
        // check if HMD is connected on the system
        var hmdPresent = OpenVR.IsHmdPresent();
        OpenVrDiagnostics.Write($"OpenVR.IsHmdPresent={hmdPresent}");
        if (!hmdPresent) throw new InvalidOperationException("HMD not found on this system");

        // check if SteamVR runtime is installed
        var runtimeInstalled = OpenVR.IsRuntimeInstalled();
        OpenVrDiagnostics.Write($"OpenVR.IsRuntimeInstalled={runtimeInstalled}");
        if (!runtimeInstalled)
            throw new InvalidOperationException("SteamVR runtime not found on this system");

        // initialize HMD
        var hmdInitErrorCode = EVRInitError.None;
        OpenVrDiagnostics.Write("OpenVR.Init begin");
        OpenVR.Init(ref hmdInitErrorCode);
        OpenVrDiagnostics.Write($"OpenVR.Init complete: error={hmdInitErrorCode}");
        if (hmdInitErrorCode != EVRInitError.None)
            throw new Exception("OpenVR error: " + OpenVR.GetStringForHmdError(hmdInitErrorCode));

        OpenVrDiagnostics.Write(
            $"OpenVR interfaces: System={(OpenVR.System == null ? "null" : "ready")}, " +
            $"Compositor={(OpenVR.Compositor == null ? "null" : "ready")}");
        if (OpenVR.System == null) throw new InvalidOperationException("OpenVR.System is null after initialization");
        if (OpenVR.Compositor == null) throw new InvalidOperationException("OpenVR.Compositor is null after initialization");

        SetUp();
    }

    private void SetUp()
    {
        uint w = 0, h = 0;
        OpenVR.System.GetRecommendedRenderTargetSize(ref w, ref h);
        OpenVrDiagnostics.Write($"recommended render target={w}x{h}");
        if (w == 0 || h == 0) throw new InvalidOperationException($"Invalid OpenVR render target size {w}x{h}");
        EyeRenderSizing.Apply(ref w, ref h, _renderResolutionScale);
        OpenVrDiagnostics.Write($"eye resolution scale={_renderResolutionScale:0.###}, actual={w}x{h}; desktop resolution and projection unchanged; restart required for scale changes");

        float lLeft = 0.0f, lRight = 0.0f, lTop = 0.0f, lBottom = 0.0f;
        OpenVR.System.GetProjectionRaw(EVREye.Eye_Left, ref lLeft, ref lRight, ref lTop, ref lBottom);

        float rLeft = 0.0f, rRight = 0.0f, rTop = 0.0f, rBottom = 0.0f;
        OpenVR.System.GetProjectionRaw(EVREye.Eye_Right, ref rLeft, ref rRight, ref rTop, ref rBottom);

        var tanHalfFov = new Vector2(
            Mathf.Max(-lLeft, lRight, -rLeft, rRight),
            Mathf.Max(-lTop, lBottom, -rTop, rBottom));

        var hmdTextureBounds = new VRTextureBounds_t
        {
            uMin = 0.0f,
            uMax = 1.0f,
            vMin = 1.0f, // flip the vertical coordinate (should only be needed in DirectX)
            vMax = 0.0f
        };

        _submitBounds = hmdTextureBounds;
        _submitFlags = EVRSubmitFlags.Submit_Default;

        const bool hdr = false;
        const int aa = 1;
        const RenderTextureFormat format = RenderTextureFormat.ARGB32;
        _aspect = tanHalfFov.x / tanHalfFov.y;
        _fieldOfView = 2.0f * Mathf.Atan(tanHalfFov.y) * Mathf.Rad2Deg;
        OpenVrDiagnostics.Write(
            $"render setup: hdr={hdr}, aa={aa}, format={format}, aspect={_aspect:0.###}, fov={_fieldOfView:0.###}");

        _leftEyeRenderTexture = CreateEyeRenderTexture((int)w, (int)h, format, aa, "UUVR OpenVR Left Eye");
        _rightEyeRenderTexture = CreateEyeRenderTexture((int)w, (int)h, format, aa, "UUVR OpenVR Right Eye");
        OpenVrDiagnostics.Write(
            $"eye RenderTextures constructed: width={_leftEyeRenderTexture.width}, height={_leftEyeRenderTexture.height}, " +
            $"aa={_leftEyeRenderTexture.antiAliasing}, format={_leftEyeRenderTexture.format}");

        var colorSpace = hdr && QualitySettings.activeColorSpace == ColorSpace.Gamma
            ? EColorSpace.Gamma
            : EColorSpace.Auto;
        _submitColorSpace = colorSpace;
        ConfigureNativeSubmitBridge();
        OpenVrDiagnostics.Write($"native submit bridge ready: colorSpace={colorSpace}");
    }

    private static RenderTexture CreateEyeRenderTexture(
        int width,
        int height,
        RenderTextureFormat format,
        int antiAliasing,
        string name)
    {
        var texture = new RenderTexture(width, height, 24, format)
        {
            antiAliasing = antiAliasing,
            name = name,
            hideFlags = HideFlags.DontSave
        };
        if (!texture.Create())
            throw new InvalidOperationException($"Failed to create {name} render texture");
        return texture;
    }

    private void ConfigureNativeSubmitBridge()
    {
        if (OpenVrNativeSubmitBridge.TryStop() == 0)
            throw new InvalidOperationException("Cannot replace eye textures while a native render callback is active");
        _submitEventFunction = OpenVrNativeSubmitBridge.GetRenderEventFunc();
        if (_submitEventFunction == IntPtr.Zero)
            throw new InvalidOperationException("Native OpenVR submit bridge returned a null render event function");

        var leftTexture = _leftEyeRenderTexture.GetNativeTexturePtr();
        var rightTexture = _rightEyeRenderTexture.GetNativeTexturePtr();
        if (leftTexture == IntPtr.Zero || rightTexture == IntPtr.Zero)
            throw new InvalidOperationException("Unity returned a null eye render texture pointer");
        if (leftTexture == rightTexture)
            throw new InvalidOperationException("Unity returned the same native texture for both eyes");
        _leftNativeTexture = leftTexture;
        _rightNativeTexture = rightTexture;

        _stereoSubmitToken = OpenVrNativeSubmitBridge.ConfigureStereo(
            leftTexture,
            rightTexture,
            _submitBounds.uMin,
            _submitBounds.vMin,
            _submitBounds.uMax,
            _submitBounds.vMax,
            (int)_submitColorSpace,
            (uint)_submitFlags);
        if (_stereoSubmitToken == IntPtr.Zero)
            throw new InvalidOperationException("Native stereo configuration rejected: " + OpenVrNativeSubmitBridge.ReadDiagnostics());

        _leftSubmitCommand?.Release();
        _rightSubmitCommand?.Release();
        _beginFrameCommand?.Release();
        _beginFrameCommand = new CommandBuffer { name = "UUVR OpenVR Begin Frame" };
        _leftSubmitCommand = new CommandBuffer { name = "UUVR OpenVR Submit Left" };
        _rightSubmitCommand = new CommandBuffer { name = "UUVR OpenVR Submit Right" };

        OpenVrDiagnostics.Write(
            $"native bridge ABI=4 configured: generation={_stereoSubmitToken.ToInt64()}, left=0x{leftTexture.ToInt64():X}, right=0x{rightTexture.ToInt64():X}, poseWait=render-thread, submitPose=immutable-render-frame");
    }

    private void QueueFrameCommands(IntPtr frameToken)
    {
        // ExecuteCommandBuffer queues a copy; later frames cannot rewrite its token.
        _beginFrameCommand.Clear();
        _beginFrameCommand.IssuePluginEventAndData(_submitEventFunction, 3, frameToken);
        _leftSubmitCommand.Clear();
        _leftSubmitCommand.IssuePluginEventAndData(_submitEventFunction, 1, frameToken);
        _rightSubmitCommand.Clear();
        _rightSubmitCommand.IssuePluginEventAndData(_submitEventFunction, 2, frameToken);
    }

    private void ReleaseSession()
    {
        _renderingActive = false;
        if (!_ownsSession) return;
        try
        {
            if (OpenVrNativeSubmitBridge.TryStop() == 0)
            {
                if (!_cleanupPending)
                    OpenVrDiagnostics.Write("native callback still in flight; retaining textures and OpenVR session, no forced Shutdown");
                _cleanupPending = true;
                return;
            }
            _leftSubmitCommand?.Release();
            _rightSubmitCommand?.Release();
            _beginFrameCommand?.Release();
            _beginFrameCommand = null;
            _leftSubmitCommand = null;
            _rightSubmitCommand = null;
            _leftNativeTexture = IntPtr.Zero;
            _rightNativeTexture = IntPtr.Zero;
        }
        catch (Exception exception)
        {
            OpenVrDiagnostics.Write("native bridge stop failed; session retained: " + exception);
            return;
        }

        if (_leftEyeRenderTexture != null)
        {
            _leftEyeRenderTexture.Release();
            Destroy(_leftEyeRenderTexture);
            _leftEyeRenderTexture = null;
        }
        if (_rightEyeRenderTexture != null)
        {
            _rightEyeRenderTexture.Release();
            Destroy(_rightEyeRenderTexture);
            _rightEyeRenderTexture = null;
        }
        OpenVR.Shutdown();
        _submitDiagnostics?.Dispose();
        _submitDiagnostics = null;
        _cleanupPending = _ownsSession = _sessionReserved = false;
        OpenVrDiagnostics.Write("native bridge drained, eye textures released, OpenVR.Shutdown complete");
    }
}
