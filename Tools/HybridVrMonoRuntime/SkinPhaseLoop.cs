using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace HybridVR.MonoRuntime;

// Edit the current tree, never replace it with Unity's default or an old snapshot.
internal static class SkinPhaseLoop
{
    internal sealed class BeforeUpdate { }
    internal sealed class AfterUpdate { }
    internal sealed class BeforeLate { }
    internal sealed class AfterLate { }
    internal static bool Owns(Type? type) => type == typeof(BeforeUpdate) || type == typeof(AfterUpdate) ||
        type == typeof(BeforeLate) || type == typeof(AfterLate);

    internal static PlayerLoopSystem Add(PlayerLoopSystem original, Action<string> observe)
    {
        int update = 0, late = 0;
        var result = Insert(Remove(original), observe, ref update, ref late);
        if (update != 1 || late != 1) throw new NotSupportedException("Ambiguous/missing script phases: " + update + "/" + late);
        return result;
    }
    private static PlayerLoopSystem Marker(Type type, string phase, Action<string> observe) =>
        new() { type = type, updateDelegate = () => observe(phase) };
    private static PlayerLoopSystem Insert(PlayerLoopSystem node, Action<string> observe, ref int update, ref int late)
    {
        if (node.subSystemList == null) return node;
        var children = new List<PlayerLoopSystem>();
        foreach (var child in node.subSystemList)
        {
            bool u = child.type == typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate);
            bool l = child.type == typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate);
            if (u) { update++; children.Add(Marker(typeof(BeforeUpdate), "before-script-update", observe)); }
            if (l) { late++; children.Add(Marker(typeof(BeforeLate), "before-script-late", observe)); }
            children.Add(Insert(child, observe, ref update, ref late));
            if (u) children.Add(Marker(typeof(AfterUpdate), "after-script-update", observe));
            if (l) children.Add(Marker(typeof(AfterLate), "after-script-late", observe));
        }
        node.subSystemList = children.ToArray();
        return node;
    }
    internal static PlayerLoopSystem Remove(PlayerLoopSystem node)
    {
        if (node.subSystemList == null) return node;
        var children = new List<PlayerLoopSystem>();
        foreach (var child in node.subSystemList) if (!Owns(child.type)) children.Add(Remove(child));
        node.subSystemList = children.ToArray();
        return node;
    }
    internal static int Count(PlayerLoopSystem node)
    {
        int count = Owns(node.type) ? 1 : 0;
        if (node.subSystemList != null) foreach (var child in node.subSystemList) count += Count(child);
        return count;
    }
}
