using System.IO;
using System.Reflection;
using BepInEx;

#if CPP
using Uuvr.OpenVR;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
#if LEGACY
using HarmonyLib;
using Uuvr.VrCamera;
using Uuvr.VrUi;
using Uuvr.VrUi.PatchModes;
#endif
#endif

namespace Uuvr;

[BepInPlugin(
#if LEGACY
    "raicuparta.uuvr-legacy",
#elif MODERN
    "raicuparta.uuvr-modern",
#endif
    "UUVR",
    "0.3.1")]
public class UuvrPlugin
#if CPP
: BasePlugin
#elif MONO
: BaseUnityPlugin
#endif
{
    private static UuvrPlugin _instance;
    public static string ModFolderPath { get; private set; }
    
#if CPP
    public override void Load()
#elif MONO
    private void Awake()
#endif
    {
#if HYBRID_UI_GPU_PROBE
        if (System.Environment.GetEnvironmentVariable("HYBRIDVR_UI_REPLAY_GPU_PROBE") == "1")
        {
            HybridVR.Diagnostics.UiSceneProbe.Schedule();
            return;
        }
#endif
        _instance = this;

#if CPP && MODERN
        OpenVrDiagnostics.StartSession("M03.3-r19-ui-orientation");
        OpenVrDiagnostics.Write("plugin Load entered");
#endif
        
        ModFolderPath = Path.GetDirectoryName(Assembly.GetAssembly(typeof(UuvrPlugin)).Location);
        
        new ModConfiguration(Config);
        
#if CPP
#if LEGACY
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());

        ClassInjector.RegisterTypeInIl2Cpp<VrCamera.VrCamera>();
        ClassInjector.RegisterTypeInIl2Cpp<VrCameraOffset>();
        ClassInjector.RegisterTypeInIl2Cpp<CanvasRedirect>();
        ClassInjector.RegisterTypeInIl2Cpp<UiOverlayRenderMode>();
        ClassInjector.RegisterTypeInIl2Cpp<VrUiCursor>();
        ClassInjector.RegisterTypeInIl2Cpp<VrUiManager>();
        ClassInjector.RegisterTypeInIl2Cpp<FollowTarget>();
        // ClassInjector.RegisterTypeInIl2Cpp<UuvrInput>();
        ClassInjector.RegisterTypeInIl2Cpp<UuvrPoseDriver>();
        ClassInjector.RegisterTypeInIl2Cpp<UuvrBehaviour>();
        // ClassInjector.RegisterTypeInIl2Cpp<AdditionalCameraData>();
       ClassInjector.RegisterTypeInIl2Cpp<VrCameraManager>();
       ClassInjector.RegisterTypeInIl2Cpp<CanvasRedirectPatchMode>();
       ClassInjector.RegisterTypeInIl2Cpp<ScreenMirrorPatchMode>();
#else
        OpenVrDiagnostics.Write("register OpenVrManager begin");
        ClassInjector.RegisterTypeInIl2Cpp<OpenVrManager>();
        OpenVrDiagnostics.Write("register OpenVrManager complete");
#endif
        OpenVrDiagnostics.Write("register UuvrCore begin");
        ClassInjector.RegisterTypeInIl2Cpp<UuvrCore>();
        OpenVrDiagnostics.Write("register UuvrCore complete");
#endif

        OpenVrDiagnostics.Write("UuvrCore.Create begin");
        UuvrCore.Create();
        OpenVrDiagnostics.Write("UuvrCore.Create complete");
    }
}
