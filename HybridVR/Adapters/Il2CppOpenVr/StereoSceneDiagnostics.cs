using System;
using UnityEngine;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Bounded read-only snapshot on menu open after a source change, not a per-frame scan.
internal static class StereoSceneDiagnostics
{
    internal static void Write(Camera? source)
    {
        try
        {
            OpenVrDiagnostics.Write($"stereo scene: source={source?.name}; eyes=left/right unchanged; no scene correction applied");
            OpenVrDiagnostics.Write("scene pipeline: " + RenderPipelineIdentity.Read());
            var cameras=Camera.allCameras;
            for(int i=0;i<cameras.Length && i<16;i++)
            {
                var c=cameras[i];
                OpenVrDiagnostics.Write($"scene camera: name={c.name}, id={c.GetInstanceID()}, depth={c.depth}, ortho={c.orthographic}, fov={c.fieldOfView}, aspect={c.aspect}, near={c.nearClipPlane}, far={c.farClipPlane}, mask=0x{c.cullingMask:X}, clear={c.clearFlags}, target={c.targetTexture?.name ?? "screen"}, pos={c.transform.position}, rot={c.transform.eulerAngles}");
            }
            var canvases=UnityEngine.Object.FindObjectsOfType<Canvas>();
            for(int i=0;i<canvases.Length && i<40;i++)
            {
                var c=canvases[i];
                OpenVrDiagnostics.Write($"scene canvas: name={c.name}, mode={c.renderMode}, camera={c.worldCamera?.name ?? "none"}, distance={c.planeDistance}, sort={c.sortingOrder}, layer={c.gameObject.layer}, pos={c.transform.position}, rot={c.transform.eulerAngles}, scale={c.transform.lossyScale}");
            }
            WriteSurfaces(source);
        }
        catch(Exception e) { OpenVrDiagnostics.Write("scene snapshot unavailable: "+e.Message); }
    }
    private static void WriteSurfaces(Camera? source)
    {
        // sharedMaterial(s), never material(s): inspection must not instantiate or
        // replace a game's materials. This is evidence, not a background correction.
        try
        {
            var sky = RenderSettings.skybox;
            OpenVrDiagnostics.Write($"scene skybox: material={sky?.name ?? "none"}, shader={sky?.shader?.name ?? "none"}");
            if (sky != null)
            {
                // Known common property names only; do not enumerate or instantiate
                // materials, and do not reinterpret a flat backdrop as a stereo sky.
                foreach (var property in new[] { "_MainTex", "_Tex", "_Exposure", "_Rotation", "_Shrink", "_Scale" })
                {
                    if (!sky.HasProperty(property)) continue;
                    if (property == "_MainTex" || property == "_Tex")
                    {
                        var texture = sky.GetTexture(property);
                        OpenVrDiagnostics.Write($"skybox property: {property}={texture?.name}, size={texture?.width}x{texture?.height}");
                    }
                    else OpenVrDiagnostics.Write($"skybox property: {property}={sky.GetFloat(property)}");
                }
            }
            if (source == null) return;
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            int logged = 0;
            for (int i = 0; i < renderers.Length && logged < 64; i++)
            {
                var r = renderers[i];
                if (!r.enabled || !r.gameObject.activeInHierarchy || (source.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                try
                {
                    var t = r.transform;
                    bool cameraChild = false;
                    string path = t.name;
                    for (int depth = 0; t.parent != null && depth < 8; depth++)
                    {
                        t = t.parent; cameraChild |= t == source.transform;
                        path = t.name + "/" + path;
                    }
                    var bounds = r.bounds;
                    var materials = r.sharedMaterials;
                    string description = "";
                    for (int m = 0; m < materials.Length && m < 4; m++)
                    {
                        var material = materials[m];
                        description += $" [{material?.name ?? "none"}:{material?.shader?.name ?? "none"}]";
                    }
                    OpenVrDiagnostics.Write($"scene surface: path={path}, layer={r.gameObject.layer}, cameraChild={cameraChild}, pos={r.transform.position}, rot={r.transform.eulerAngles}, boundsCentre={bounds.center}, boundsSize={bounds.size}, materials={description}");
                    logged++;
                }
                catch (Exception e) { OpenVrDiagnostics.Write("scene surface unavailable: " + e.Message); logged++; }
            }
            OpenVrDiagnostics.Write($"scene surface snapshot: total={renderers.Length}, logged={logged}, limit=64; no scene edits");
        }
        catch (Exception e) { OpenVrDiagnostics.Write("scene surfaces unavailable: " + e.Message); }
    }
}
