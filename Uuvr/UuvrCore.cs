using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using Uuvr.VrTogglers;

#if CPP && MODERN
using Uuvr.OpenVR;
#endif

#if MONO || LEGACY
using Uuvr.VrCamera;
using Uuvr.VrUi;
#endif

namespace Uuvr;

public class UuvrCore: MonoBehaviour
{
#if CPP && MODERN
    private static readonly ManualLogSource Il2CppLog = BepInEx.Logging.Logger.CreateLogSource("UUVR IL2CPP");
#endif
#if CPP
    public UuvrCore(IntPtr pointer) : base(pointer)
    {
    }
#endif

    private readonly KeyboardKey _toggleVrKey = new (KeyboardKey.KeyCode.F3);
    private float _originalFixedDeltaTime;
    private VrTogglerManager? _vrTogglerManager;
    private PropertyInfo? _refreshRateProperty;
#if CPP && MODERN
    private int _automaticVrFramesRemaining;
    private bool _automaticVrAttempted;
    private bool _firstUpdateLogged;
#endif

#if MONO || LEGACY
    private VrUiManager? _vrUi;
#endif

    public static void Create()
    {
#if CPP && MODERN
        OpenVrDiagnostics.Write("UuvrCore.Create entered");
#endif
        new GameObject(nameof(UuvrCore)).AddComponent<UuvrCore>();
#if CPP && MODERN
        OpenVrDiagnostics.Write("UuvrCore.Create returned");
#endif
    }

    private void Awake()
    {
#if CPP && MODERN
        OpenVrDiagnostics.Write("UuvrCore.Awake entered");
#endif
        DontDestroyOnLoad(gameObject);

#if MONO || LEGACY
        gameObject.AddComponent<VrCameraManager>();
        // TODO: Emulate input.   
        // UuvrBehaviour.Create<UuvrInput>(transform);
#endif
    }

    private void Start()
    {
#if CPP && MODERN
        OpenVrDiagnostics.Write("UuvrCore.Start entered");
#endif
#if MONO || LEGACY
        var xrDeviceType = Type.GetType("UnityEngine.XR.XRDevice, UnityEngine.XRModule") ??
                           Type.GetType("UnityEngine.XR.XRDevice, UnityEngine.VRModule") ??
                           Type.GetType("UnityEngine.VR.VRDevice, UnityEngine.VRModule") ??
                           Type.GetType("UnityEngine.VR.VRDevice, UnityEngine");

        _refreshRateProperty = xrDeviceType?.GetProperty("refreshRate");

        _vrUi = UuvrBehaviour.Create<VrUiManager>(transform);

        SetPositionTrackingEnabled(false);
#endif
        _vrTogglerManager = new VrTogglerManager();
#if CPP && MODERN
        if (ModConfiguration.Instance.StartVrAutomatically.Value)
        {
            _automaticVrFramesRemaining = 30;
            Il2CppLog.LogInfo("Automatic OpenVR start armed; waiting for a game camera.");
            OpenVrDiagnostics.Write("automatic OpenVR start armed");
        }
#endif
    }

    private void OnDestroy()
    {
        Debug.Log("UUVR has been destroyed. This shouldn't have happened. Recreating...");
        Create();
    }
    
    private void Update()
    {
#if CPP && MODERN
        if (!_firstUpdateLogged)
        {
            _firstUpdateLogged = true;
            OpenVrDiagnostics.Write($"UuvrCore first Update: frame={Time.frameCount}");
        }
        TryStartVrAutomatically();
#endif
        if (_toggleVrKey.UpdateIsDown())
        {
            _vrTogglerManager?.ToggleVr();
        }
        UpdatePhysicsRate();
    }

#if CPP && MODERN
    private void TryStartVrAutomatically()
    {
        if (_automaticVrAttempted || _automaticVrFramesRemaining <= 0) return;

        if (Camera.main == null && Camera.current == null && Camera.allCameras.Length == 0)
        {
            _automaticVrFramesRemaining = 30;
            return;
        }

        _automaticVrFramesRemaining--;
        if (_automaticVrFramesRemaining > 0) return;

        _automaticVrAttempted = true;
        try
        {
            Il2CppLog.LogInfo("Automatic OpenVR start requested.");
            OpenVrDiagnostics.Write("automatic OpenVR start requested");
            _vrTogglerManager?.EnableVr();
            Il2CppLog.LogInfo("Automatic OpenVR start result: " +
                              (_vrTogglerManager?.IsVrEnabled == true ? "enabled" : "not-enabled"));
            OpenVrDiagnostics.Write("automatic OpenVR start result: " +
                                    (_vrTogglerManager?.IsVrEnabled == true ? "enabled" : "not-enabled"));
        }
        catch (Exception exception)
        {
            Il2CppLog.LogError("Automatic OpenVR start failed: " + exception);
            OpenVrDiagnostics.Write("automatic OpenVR start exception: " + exception);
        }
    }
#endif
    
    private void UpdatePhysicsRate()
    {
        if (_originalFixedDeltaTime == 0)
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
        }

        if (_refreshRateProperty == null) return;

        var headsetRefreshRate = (float)_refreshRateProperty.GetValue(null, null);
        if (headsetRefreshRate <= 0) return;

        if (ModConfiguration.Instance.PhysicsMatchHeadsetRefreshRate.Value)
        {
            Time.fixedDeltaTime = 1f / (float) _refreshRateProperty.GetValue(null, null);
        }
        else
        {
            Time.fixedDeltaTime = _originalFixedDeltaTime;
        }
    }

#if MONO || LEGACY
    private static void SetPositionTrackingEnabled(bool positionTrackingEnabled)
    {
        try
        {
            var inputTrackingType =
                Type.GetType("UnityEngine.XR.InputTracking, UnityEngine.XRModule") ??
                Type.GetType("UnityEngine.XR.InputTracking, UnityEngine.VRModule") ??
                Type.GetType("UnityEngine.VR.InputTracking, UnityEngine.VRModule") ??
                Type.GetType("UnityEngine.VR.InputTracking, UnityEngine");

            if (inputTrackingType != null)
            {
                var disablePositionalTrackingProperty = inputTrackingType.GetProperty("disablePositionalTracking");
                if (disablePositionalTrackingProperty != null)
                {
                    disablePositionalTrackingProperty.SetValue(null, !positionTrackingEnabled, null);
                }
                else
                {
                    Debug.LogWarning("Failed to get property disablePositionalTracking");
                }
            }
            else
            {
                Debug.LogWarning("Failed to get type UnityEngine.XR.InputTracking");
            }
        } catch (Exception e)
        {
            Debug.LogWarning($"Failed to change position tacking mode via native Unity settings: {e}");
        }
    }
#endif
}
