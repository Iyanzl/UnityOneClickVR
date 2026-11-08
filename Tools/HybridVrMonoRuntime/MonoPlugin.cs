using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Uuvr.OpenVR;

namespace HybridVR.MonoRuntime;

[BepInPlugin("codex.hybridvr.mono", "HybridVR Mono Unified", "0.3.3")]
[BepInIncompatibility("raicuparta.uuvr-modern")]
[BepInIncompatibility("raicuparta.uuvr-legacy")]
[BepInIncompatibility("com.newunitymodder.unityvrmod")]
[BepInIncompatibility("codex.vrgin-modern")]
public sealed class MonoPlugin : BaseUnityPlugin
{
    private void Awake()
    {
#if HYBRID_UI_GPU_PROBE
        if (Environment.GetEnvironmentVariable("HYBRIDVR_UI_REPLAY_GPU_PROBE") == "1")
        {
            UiReplayGpuProbe.Schedule();
            return;
        }
#endif
        OpenVrDiagnostics.StartSession("M03.3-r19-ui-orientation");
        MonoTemporalAaCompatibility.EnabledSetting = Config.Bind("Rendering", "Shared camera TAA compatibility", true,
            "During manual VR rendering only, replace HDRP camera TAA with SMAA to avoid sharing temporal history between desktop and both eyes. Restores the previous TAA on exit; does not change other camera effects. Disable for comparison, then restart VR.");
        var automatic = Config.Bind("Startup", "Start VR automatically", true);
        var toggle = Config.Bind("Startup", "Toggle VR key", KeyCode.F3);
        var resolution = Config.Bind("Rendering", "Eye resolution scale", 1f,
            new ConfigDescription("OpenVR recommended per-eye size multiplier. Applied on next VR start; independent of desktop resolution.",
                new AcceptableValueRange<float>(.5f, 1f)));
        OpenVrDiagnostics.Write($"Mono bootstrap: unity={Application.unityVersion}, graphics={SystemInfo.graphicsDeviceType}; shared operations and menu; no legacy host patches");
        MonoRuntimeBootstrap.Schedule(automatic, toggle, resolution, Logger);
    }
}

internal static class MonoRuntimeBootstrap
{
    private static MonoRuntimeHost? _host;
    private static ConfigEntry<bool>? _automatic;
    private static ConfigEntry<KeyCode> _toggle = null!;
    private static ConfigEntry<float> _resolution = null!;
    private static BepInEx.Logging.ManualLogSource _log = null!;
    private static bool _quitting;

    internal static void Schedule(ConfigEntry<bool> automatic, ConfigEntry<KeyCode> toggle,
        ConfigEntry<float> resolution, BepInEx.Logging.ManualLogSource log)
    {
        if (_automatic != null) return;
        _automatic = automatic; _toggle = toggle; _resolution = resolution; _log = log;
        // Loader objects can be destroyed before Start during the initial scene load.
        // Keep only managed configuration here; scene events own the persistent runner.
        SceneManager.sceneLoaded += SceneLoaded;
        Application.quitting += Quitting;
        OpenVrDiagnostics.Write("Mono runtime bootstrap subscribed to scene loading");
        if (SceneManager.GetActiveScene().isLoaded) EnsureHost();
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        OpenVrDiagnostics.Write($"Mono scene ready: name={scene.name}, mode={mode}");
        EnsureHost();
    }

    private static void EnsureHost()
    {
        if (_quitting || _host != null || _automatic == null) return;
        GameObject? root = null;
        try
        {
            root = new GameObject("HybridVR.Mono.Host");
            root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.HideAndDontSave;
            _host = root.AddComponent<MonoRuntimeHost>();
            _host.Configure(_automatic, _toggle, _resolution, _log);
            root.SetActive(true);
            OpenVrDiagnostics.Write("Mono independent runtime host created");
        }
        catch (Exception error)
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            _host = null;
            _log.LogError(error);
            OpenVrDiagnostics.Write("Mono host creation failed: " + error);
        }
    }

    private static void Quitting()
    {
        _quitting = true;
        SceneManager.sceneLoaded -= SceneLoaded;
        Application.quitting -= Quitting;
    }
}

public sealed class MonoRuntimeHost : MonoBehaviour
{
    private ConfigEntry<bool> _automatic = null!;
    private ConfigEntry<KeyCode> _toggle = null!;
    private ConfigEntry<float> _resolution = null!;
    private BepInEx.Logging.ManualLogSource _log = null!;
    private OpenVrManager? _manager;
    private bool _ready, _starting, _stopping, _keyWasDown, _wasBackground, _firstUpdateLogged;
    private int _wasVsync;

    internal void Configure(ConfigEntry<bool> automatic, ConfigEntry<KeyCode> toggle,
        ConfigEntry<float> resolution, BepInEx.Logging.ManualLogSource log)
    {
        _automatic = automatic; _toggle = toggle; _resolution = resolution; _log = log;
    }

    private IEnumerator Start()
    {
        OpenVrDiagnostics.Write("Mono owner Start entered");
        // Wait until BepInEx has finished loading plugins before checking ownership.
        yield return null;
        foreach (string id in new[] { "raicuparta.uuvr-modern", "raicuparta.uuvr-legacy", "com.newunitymodder.unityvrmod", "codex.vrgin-modern", "codex.unityvrmod.desktop-settings" })
            if (Chainloader.PluginInfos.ContainsKey(id))
            { Fail("Another VR owner is active: " + id + ". Use the reversible candidate installer."); yield break; }
        if (IntPtr.Size != 8 || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
        { Fail("This candidate requires Windows x64 Direct3D11. Use the VR launcher (-force-d3d11)."); yield break; }
        if (UnityXrOwnsHeadset())
        { Fail("Unity XR already owns the headset. Disable the old VR loader before selecting this backend."); yield break; }
        try { NativeDependencies.Load(Path.GetDirectoryName(typeof(MonoPlugin).Assembly.Location)!); }
        catch (Exception error) { Fail("Native dependency load failed: " + error.Message); yield break; }
        _ready = true;
        OpenVrDiagnostics.Write($"Mono native dependencies ready; automatic={_automatic.Value}");
        if (_automatic.Value) yield return StartVrWhenCameraReady();
    }

    private void Update()
    {
        if (!_firstUpdateLogged)
        {
            _firstUpdateLogged = true;
            OpenVrDiagnostics.Write($"Mono owner first Update: ready={_ready}, cameras={Camera.allCamerasCount}");
        }
        if (_toggle == null) return;
        bool down = IsDown(_toggle.Value);
        bool pressed = down && !_keyWasDown; _keyWasDown = down;
        if (!_ready || !pressed || !Application.isFocused) return;
        if (_manager != null || _starting) StopVr();
        else if (!_stopping) StartCoroutine(StartVrWhenCameraReady());
    }

    private IEnumerator StartVrWhenCameraReady()
    {
        if (_starting || _stopping || _manager != null) yield break;
        _starting = true;
        float earliest = Time.realtimeSinceStartup + .5f;
        float nextNotice = earliest + 2f;
        int notices = 0;
        OpenVrDiagnostics.Write("Mono waiting for an active game camera");
        while (_starting && (Time.realtimeSinceStartup < earliest || Camera.allCamerasCount == 0))
        {
            if (notices < 3 && Time.realtimeSinceStartup >= nextNotice)
            {
                notices++; nextNotice = Time.realtimeSinceStartup + 10f;
                OpenVrDiagnostics.Write($"Mono camera wait: cameras={Camera.allCamerasCount}, frame={Time.frameCount}");
            }
            yield return null;
        }
        if (!_starting) yield break;
        _starting = false;
        _wasVsync = QualitySettings.vSyncCount;
        _wasBackground = Application.runInBackground;
        try
        {
            MonoStartupDiagnostics.Write(Paths.ConfigPath);
            _manager = OpenVrManager.Create(_resolution.Value);
            if (_manager == null) { RestorePresentation(); Fail("OpenVR did not start. See diagnostics; F3 retries explicitly. No automatic restart loop."); yield break; }
            OpenVrDiagnostics.Write($"Mono OpenVR session module=0x{MonoOpenVrExports.Module.ToInt64():X}, initToken={OpenVRInterop.GetInitToken()}");
            _manager.EnableInteraction(Paths.ConfigPath);
            MonoSkinFrameProbe.AttachIfRequested(_manager);
            OpenVrDiagnostics.Write("Mono shared runtime active; settings=codex.hybrid-mono.cfg; F10 or L3+R3");
        }
        catch (Exception error) { StopVr(); Fail("VR startup failed: " + error); }
    }

    private void StopVr()
    {
        _starting = false;
        if (_manager == null) return;
        _stopping = true;
        var manager = _manager; _manager = null;
        manager.gameObject.SetActive(false);
        Destroy(manager.gameObject);
        RestorePresentation();
        _stopping = false;
    }

    private void RestorePresentation()
    {
        QualitySettings.vSyncCount = _wasVsync;
        Application.runInBackground = _wasBackground;
    }
    private void Fail(string message) { _log.LogError(message); OpenVrDiagnostics.Write(message); }
    private void OnEnable() { OpenVrDiagnostics.Write("Mono owner OnEnable"); }
    private void OnDisable() { OpenVrDiagnostics.Write("Mono owner OnDisable"); StopVr(); }
    private void OnDestroy() { OpenVrDiagnostics.Write("Mono owner OnDestroy"); StopVr(); }
    private void OnApplicationQuit() { StopVr(); }

    private static bool UnityXrOwnsHeadset()
    {
        try
        {
            var xr = Type.GetType("UnityEngine.XR.XRSettings, UnityEngine.XRModule", false);
            // enabled can default true without an initialized loader or a device.
            return xr?.GetProperty("enabled")?.GetValue(null) is true &&
                xr.GetProperty("isDeviceActive")?.GetValue(null) is true;
        }
        catch (Exception error)
        {
            OpenVrDiagnostics.Write("Optional Unity XR ownership query unavailable: " + error.Message);
            return false;
        }
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private static bool IsDown(KeyCode key)
    {
        int value = (int)key;
        if (value >= (int)KeyCode.F1 && value <= (int)KeyCode.F15) value = value - (int)KeyCode.F1 + 0x70;
        else if (value >= 'a' && value <= 'z') value -= 32;
        return value > 0 && value < 256 && (GetAsyncKeyState(value) & 0x8000) != 0;
    }
}

internal static class NativeDependencies
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    internal static void Load(string directory)
    {
        // Reuse a single already-loaded API, or preload the packaged one. Native imports
        // and managed exports must share its session; do not initialize two DLL copies.
        string apiPath = Path.Combine(directory, "openvr_api.dll");
        int apiCount = 0;
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
            foreach (System.Diagnostics.ProcessModule module in process.Modules)
                if (string.Equals(module.ModuleName, "openvr_api.dll", StringComparison.OrdinalIgnoreCase))
                { apiPath = module.FileName; apiCount++; }
        if (apiCount > 1) throw new InvalidOperationException("Multiple OpenVR API modules already loaded; refusing ambiguous session ownership.");
        if (!File.Exists(apiPath)) throw new FileNotFoundException("Missing OpenVR API", apiPath);
        var api = LoadLibraryEx(apiPath, IntPtr.Zero, 0x100 | 0x1000);
        if (api == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot load " + apiPath);
        MonoOpenVrExports.Bind(api);
        OpenVrDiagnostics.Write($"Mono OpenVR exports bound: path={apiPath}, module=0x{api.ToInt64():X}, reused={apiCount == 1}");
        // Retain modules until process exit: render callbacks may still drain during shutdown.
        foreach (string name in new[] { "Uuvr.OpenVR.NativeBridge.dll", "Uuvr.OpenVR.InputBridge.dll" })
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) throw new FileNotFoundException("Missing packaged native dependency", path);
            if (LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x1000) == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot load " + path);
        }
    }
}
