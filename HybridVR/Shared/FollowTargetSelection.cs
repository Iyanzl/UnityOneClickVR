using System;
using System.Collections.Generic;

namespace HybridVR.Shared;

// A scan snapshot, not live Unity objects. Ancestor IDs keep grouping independent
// of names, traversal order and scene-specific model conventions.
internal static class FollowTargetSelection
{
    internal sealed class Candidate
    {
        internal int Root, Head;
        internal int[] RootAncestors = Array.Empty<int>(), HeadAncestors = Array.Empty<int>();
        internal string Path = "";
    }

    internal static List<int> Select(IReadOnlyList<Candidate> candidates)
    {
        var order = new List<int>();
        bool hasHead = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            order.Add(i);
            hasHead |= candidates[i].Head != 0;
        }
        order.Sort((a, b) =>
        {
            int heads = candidates[a].HeadAncestors.Length.CompareTo(candidates[b].HeadAncestors.Length);
            int roots = candidates[a].RootAncestors.Length.CompareTo(candidates[b].RootAncestors.Length);
            return heads != 0 ? heads : roots != 0 ? roots : string.CompareOrdinal(candidates[a].Path, candidates[b].Path);
        });
        var selected = new List<int>();
        foreach (int i in order)
        {
            var current = candidates[i];
            if (hasHead && current.Head == 0) continue;
            bool duplicate = false;
            foreach (int previous in selected)
            {
                var parent = candidates[previous];
                duplicate = current.Root == parent.Root || (current.Head != 0
                    ? current.Head == parent.Head || Array.IndexOf(current.HeadAncestors, parent.Head) >= 0
                    : Array.IndexOf(current.RootAncestors, parent.Root) >= 0);
                if (duplicate) break;
            }
            if (!duplicate) selected.Add(i);
        }
        selected.Sort((a, b) => string.CompareOrdinal(candidates[a].Path, candidates[b].Path));
        return selected;
    }

    internal static string Label(IReadOnlyList<string> rootAndParents, bool hasHead)
    {
        string name = rootAndParents.Count == 0 ? "Target" : rootAndParents[0];
        foreach (string candidate in rootAndParents)
        {
            string lower = candidate.ToLowerInvariant();
            if (HeadBoneSelection.Score(candidate) > 0 || lower == "hips" || lower == "pelvis" ||
                lower == "armature" || lower == "skeleton" || lower == "rig" || lower == "root") continue;
            name = candidate;
            break;
        }
        if (name.Length > 16) name = name.Substring(0, 13) + "...";
        return name + (hasHead ? " / Head" : " / Estimated");
    }
}
