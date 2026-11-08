using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Uuvr.OpenVR;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Explicit diagnostic launch only. Never change a canvas, material or camera.
internal static class UiCaptureDiagnostics
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("HYBRIDVR_UI_DIAGNOSTICS") == "1";
    private static readonly MethodInfo? GetMesh = typeof(CanvasRenderer).GetMethod("GetMesh", Type.EmptyTypes);
    private static readonly string Session = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
    private static bool _pending = true;
    private static float _next;
    private static int _count;
    internal static void Arm() { if (Enabled) _pending = true; }

    internal static void Capture(RenderTexture texture, string status)
    {
        if (!Enabled || !_pending || _count >= 8 || Time.unscaledTime < _next) return;
        _pending = false; _next = Time.unscaledTime + 2; _count++;
        try
        {
            OpenVrDiagnostics.Write($"UI probe {_count}: {status}; target={texture.width}x{texture.height}; screen={Screen.width}x{Screen.height}; pipeline={RenderPipelineIdentity.Read()}");
            try { SaveSample(texture); }
            catch (Exception e) { OpenVrDiagnostics.Write("UI probe pixels unavailable: " + e.GetBaseException().Message); }
            int graphics = 0;
            foreach (var root in Object.FindObjectsOfType<Canvas>())
            {
                if (!root.isRootCanvas || !root.isActiveAndEnabled) continue;
                var camera = root.worldCamera;
                OpenVrDiagnostics.Write($"UI probe canvas: path={PathOf(root.transform)}, mode={root.renderMode}, targetDisplay={root.targetDisplay}, sort={root.sortingOrder}, camera={camera?.name ?? "none"}, target={camera?.targetTexture?.name ?? "screen"}, position={root.transform.position}, scale={root.transform.lossyScale}");
                foreach (var renderer in root.GetComponentsInChildren<CanvasRenderer>())
                {
                    if (++graphics > 96) break;
                    var graphic = renderer.GetComponent<Graphic>();
                    var material = renderer.materialCount > 0 ? renderer.GetMaterial(0) : null;
                    var mesh = GetMesh?.Invoke(renderer, null) as Mesh;
                    var atlas = graphic?.mainTexture;
                    var materialAtlas = material?.mainTexture;
                    var vertexColors = mesh?.colors32;
                    string keywords = "";
                    if (material != null) foreach (var keyword in material.shaderKeywords) keywords += keyword + ",";
                    OpenVrDiagnostics.Write($"UI probe bindings: object={renderer.name}, materialAtlas={materialAtlas?.name ?? "none"}:{materialAtlas?.width}x{materialAtlas?.height}, keywords={keywords}, vertexColors={vertexColors?.Length ?? 0}, firstAlpha={(vertexColors != null && vertexColors.Length > 0 ? vertexColors[0].a : -1)}");
                    var point = renderer.transform.position;
                    var projected = camera != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? camera.WorldToScreenPoint(point) : point;
                    OpenVrDiagnostics.Write($"UI probe graphic: path={PathOf(renderer.transform)}, type={graphic?.GetType().FullName ?? "unclassified"}, active={renderer.gameObject.activeInHierarchy}, cull={renderer.cull}, depth={renderer.absoluteDepth}, mesh={mesh?.vertexCount ?? 0}, bounds={mesh?.bounds}, position={point}, projected={projected}, atlas={atlas?.name ?? "none"}:{atlas?.width}x{atlas?.height}, tint={renderer.GetColor()}, inheritedAlpha={renderer.GetInheritedAlpha()}, shader={material?.shader?.name ?? "none"}, materials={renderer.materialCount}, pop={renderer.hasPopInstruction}, rectClip={renderer.hasRectClipping}, stencil={Property(material,"_Stencil")}, stencilComp={Property(material,"_StencilComp")}, colorMask={Property(material,"_ColorMask")}, color={ColorProperty(material,"_Color")}, faceColor={ColorProperty(material,"_FaceColor")}");
                }
                if (graphics > 96) break;
            }
            OpenVrDiagnostics.Write($"UI probe complete: enumerated={Math.Min(graphics,96)}, capped={graphics>96}; diagnostic only, no scene edits");
        }
        catch (Exception e) { OpenVrDiagnostics.Write("UI probe unavailable: " + e.GetBaseException().Message); }
    }

    private static string Property(Material? material, string name) => material != null && material.HasProperty(name) ? material.GetFloat(name).ToString() : "absent";
    private static string ColorProperty(Material? material, string name) => material != null && material.HasProperty(name) ? material.GetColor(name).ToString() : "absent";
    private static string PathOf(Transform transform)
    {
        string path = transform.name;
        for (int i=0; transform.parent != null && i<10; i++) { transform=transform.parent; path=transform.name+"/"+path; }
        return path;
    }
    private static void SaveSample(RenderTexture source)
    {
        // One tiny synchronous read on an explicitly armed diagnostic, never a
        // normal capture-frame loop. PNG failure must not affect presentation.
        var previous = RenderTexture.active;
        RenderTexture? small = null;
        Texture2D? pixels = null;
        try
        {
            int width=256, height=Math.Max(1,(int)Math.Round(256d*source.height/source.width));
            height=Math.Min(height,256);
            small=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);
            Graphics.Blit(source,small);
            RenderTexture.active=small;
            pixels=new Texture2D(width,height,TextureFormat.RGBA32,false);
            pixels.ReadPixels(new Rect(0,0,width,height),0,0,false);
            pixels.Apply(false,false);
            var colors=pixels.GetPixels32();
            int alpha=0, colored=0;
            for(int i=0;i<colors.Length;i++) { var c=colors[i]; if(c.a>8)alpha++; if(c.a>8 && (c.r>8||c.g>8||c.b>8))colored++; }
            OpenVrDiagnostics.Write($"UI probe pixels: size={width}x{height}, alphaNonzero={alpha}/{colors.Length}, colorNonzero={colored}/{colors.Length}; sampling does not prove UI completeness");
            var folder=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OpenVrDiagnostics.LogPath)!,"HybridVR-UiProbe",Session);
            Directory.CreateDirectory(folder);
#if HYBRID_MONO
            File.WriteAllBytes(System.IO.Path.Combine(folder,"capture-"+_count+".png"),ImageConversion.EncodeToPNG(pixels));
#else
            // Some IL2CPP ImageConversion wrappers fail constructing the native
            // byte array. Uncompressed TGA needs no Unity encoder or extra DLL.
            var bytes=new byte[18+colors.Length*4];
            bytes[2]=2; bytes[12]=(byte)width; bytes[13]=(byte)(width>>8);
            bytes[14]=(byte)height; bytes[15]=(byte)(height>>8); bytes[16]=32; bytes[17]=8;
            for(int i=0;i<colors.Length;i++)
            {
                var c=colors[i]; int p=18+i*4;
                bytes[p]=c.b; bytes[p+1]=c.g; bytes[p+2]=c.r; bytes[p+3]=c.a;
            }
            File.WriteAllBytes(System.IO.Path.Combine(folder,"capture-"+_count+".tga"),bytes);
#endif
            OpenVrDiagnostics.Write("UI probe image folder: " + folder);
        }
        finally
        {
            RenderTexture.active=previous;
            if(small!=null)RenderTexture.ReleaseTemporary(small);
            if(pixels!=null)Object.Destroy(pixels);
        }
    }
}
