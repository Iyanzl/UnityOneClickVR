using System;
using System.Collections.Generic;
using HybridVR.Runtime;
using HybridVR.Shared;
#if !HYBRID_MONO
using Il2CppInterop.Runtime;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Explicit, bounded discovery. Never traverse scene objects in the pose loop.
internal sealed class FollowTargets
{
    internal sealed class Target
    {
        internal Transform Root = null!;
        internal Transform? Head;
        internal float Height;
        internal string Path = "", Label = "", AnchorKind = "estimated-height";
    }
    private readonly List<Target> _targets = new();
    private Target? _selected;
    internal int Count => _targets.Count;
    internal string Status => _selected != null && _selected.Root != null ? _selected.Label : "No target";
    internal string SelectedPath => _selected?.Path ?? "";

    internal void Refresh(string savedPath)
    {
        _targets.Clear();
        _selected = null;
        var seen = new Dictionary<int, Target>();
        int boneBudget = 16384;
        // AnimationModule is optional: resolve humanoid support without making
        // every IL2CPP game load a hard assembly dependency on it.
        try
        {
            var type = Type.GetType("UnityEngine.Animator, UnityEngine.AnimationModule", false);
            var boneType = Type.GetType("UnityEngine.HumanBodyBones, UnityEngine.AnimationModule", false);
            if (type != null && boneType != null)
            {
                var human = type.GetProperty("isHuman");
                var bone = type.GetMethod("GetBoneTransform", new[] { boneType });
                object headId = Enum.Parse(boneType, "Head");
#if HYBRID_MONO
                foreach (var native in Object.FindObjectsOfType(type))
#else
                foreach (var native in Object.FindObjectsOfType(Il2CppType.From(type)))
#endif
                {
#if HYBRID_MONO
                    var component = native as Component;
#else
                    var component = native.TryCast<Component>();
#endif
                    if (component == null || !component.gameObject.activeInHierarchy || _targets.Count >= 64) continue;
                    Transform? head = null;
                    try
                    {
#if HYBRID_MONO
                        var animator = native;
#else
                        var animator = Activator.CreateInstance(type, new object[] { native.Pointer });
#endif
                        if (human?.GetValue(animator) is true) head = bone?.Invoke(animator, new[] { headId }) as Transform;
                    }
                    catch (Exception) { /* Non-humanoid or stripped bone API: use an adjustable anchor. */ }
                    try
                    {
                        var skins = component.GetComponentsInChildren<SkinnedMeshRenderer>();
                        if (head == null && skins.Length == 0) continue;
                        string kind = head != null ? "humanoid-head" : "estimated-height";
                        if (head == null)
                        {
                            head = FindNamedHead(component.transform, skins, ref boneBudget);
                            if (head != null) kind = "named-head";
                        }
                        float height = 1.6f;
                        if (skins.Length > 0 && skins[0] != null) height = Mathf.Max(.1f, skins[0].bounds.max.y - component.transform.position.y);
                        Add(component.transform, head, height, kind, seen);
                    }
                    catch (Exception error) { Uuvr.OpenVR.OpenVrDiagnostics.Write("follow candidate skipped: " + component.name + ": " + error.Message); }
                }
            }
        }
        catch (Exception error) { Uuvr.OpenVR.OpenVrDiagnostics.Write("optional humanoid discovery: " + error.Message); }
        // Non-humanoid games may animate skinned meshes without an Animator.
        foreach (var renderer in Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (renderer == null || !renderer.enabled || _targets.Count >= 64) continue;
            var root = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
            if (!seen.TryGetValue(root.GetInstanceID(), out var known) || known.Head == null)
            {
                try
                {
                    var head = FindNamedHead(root, new[] { renderer }, ref boneBudget);
                    Add(root, head, Mathf.Max(.1f, renderer.bounds.max.y - root.position.y), head != null ? "named-head" : "estimated-height", seen);
                }
                catch (Exception error) { Uuvr.OpenVR.OpenVrDiagnostics.Write("follow skin skipped: " + renderer.name + ": " + error.Message); }
            }
        }
        var raw = _targets.ToArray();
        var descriptors = new List<FollowTargetSelection.Candidate>();
        foreach (var t in raw)
            descriptors.Add(new FollowTargetSelection.Candidate { Root = t.Root.GetInstanceID(),
                Head = t.Head != null ? t.Head.GetInstanceID() : 0, RootAncestors = Ancestors(t.Root),
                HeadAncestors = t.Head != null ? Ancestors(t.Head) : Array.Empty<int>(), Path = t.Path });
        _targets.Clear();
        foreach (int index in FollowTargetSelection.Select(descriptors)) _targets.Add(raw[index]);
        _selected = _targets.Find(t => t.Path == savedPath);
        // Keep an existing attachment selection on its canonical head after deduplication.
        if (_selected == null)
        {
            var previous = Array.Find(raw, t => t.Path == savedPath);
            if (previous != null)
                _selected = _targets.Find(t => t.Head != null && previous.Root.IsChildOf(t.Root) &&
                    (previous.Head == null || previous.Head.IsChildOf(t.Head)));
        }
        Uuvr.OpenVR.OpenVrDiagnostics.Write($"follow target scan: raw={raw.Length}, candidates={Count}, selected={Status}, policy=heads-first-attachment-dedup");
        foreach (var t in _targets)
            Uuvr.OpenVR.OpenVrDiagnostics.Write($"follow candidate: label={t.Label}, root={t.Path}, anchor={t.AnchorKind}, head={(t.Head != null ? PathOf(t.Head) : "none")}, fallbackHeight={t.Height:0.###}");
    }

    private static Transform? FindNamedHead(Transform root, IEnumerable<SkinnedMeshRenderer> skins, ref int budget)
    {
        var names = new List<string>();
        var bones = new List<Transform>();
        var seen = new HashSet<int>();
        int meshes = 0;
        foreach (var skin in skins)
        {
            if (skin == null || ++meshes > 16 || budget <= 0) break;
            var joints = skin.bones;
            for (int i = 0; i < joints.Length && i < 2048 && budget > 0; i++, budget--)
            {
                var bone = joints[i];
                if (bone == null || !bone.IsChildOf(root) || !seen.Add(bone.GetInstanceID()) || HeadBoneSelection.Score(bone.name) == 0) continue;
                names.Add(bone.name); bones.Add(bone);
            }
        }
        // A Head attachment nested below the actual Head is not a second person.
        for (int i = bones.Count - 1; i >= 0; i--)
        {
            bool nested = false;
            for (int j = 0; j < bones.Count; j++)
                if (i != j && bones[i].IsChildOf(bones[j])) { nested = true; break; }
            if (nested) { names.RemoveAt(i); bones.RemoveAt(i); }
        }
        int selected = HeadBoneSelection.Select(names);
        return selected >= 0 ? bones[selected] : null;
    }

    private static string PathOf(Transform root)
    {
        string path = "";
        for (var t = root; t != null; t = t.parent)
            path = "/" + t.name + "[" + t.GetSiblingIndex() + "]" + path;
        return root.gameObject.scene.path + "|" + path;
    }

    private static int[] Ancestors(Transform root)
    {
        var ids = new List<int>();
        for (var t = root.parent; t != null && ids.Count < 128; t = t.parent) ids.Add(t.GetInstanceID());
        return ids.ToArray();
    }

    private void Add(Transform root, Transform? head, float height, string kind, Dictionary<int, Target> seen)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return;
        if (seen.TryGetValue(root.GetInstanceID(), out var existing))
        {
            if (existing.Head != null || head == null) return;
            _targets.Remove(existing);
        }
        var lineage = new List<string>();
        for (var t = root; t != null && lineage.Count < 6; t = t.parent) lineage.Add(t.name);
        var target = new Target { Root = root, Head = head, Height = height, Path = PathOf(root),
            Label = FollowTargetSelection.Label(lineage, head != null), AnchorKind = kind };
        seen[root.GetInstanceID()] = target;
        _targets.Add(target);
    }

    internal void Select(int direction)
    {
        if (_targets.Count == 0) return;
        int index = _selected == null ? (direction > 0 ? -1 : 0) : _targets.IndexOf(_selected);
        _selected = _targets[(index + direction + _targets.Count) % _targets.Count];
        Uuvr.OpenVR.OpenVrDiagnostics.Write($"follow target selected: root={_selected.Path}, anchor={_selected.AnchorKind}, head={(_selected.Head != null ? PathOf(_selected.Head) : "none")}");
    }

    internal FollowAnchor Read()
    {
        var t = _selected;
        if (t == null || t.Root == null || !t.Root.gameObject.activeInHierarchy) return default;
        return new FollowAnchor { Valid = true, Id = t.Root.GetInstanceID(), Rotation = t.Root.rotation,
            Head = t.Head != null ? t.Head.position : t.Root.position + Vector3.up * t.Height };
    }
}
