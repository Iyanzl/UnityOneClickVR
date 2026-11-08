using System;
using System.Collections.Generic;

namespace HybridVR.MonoRuntime;

// Pure managed comparison: expected CPU palettes, never GPU skin-cache contents.
internal static class SkinPhaseData
{
    internal const int Stride = 42; // local TRS 10, world matrix 16, expected skin matrix 16
    internal sealed class Delta
    {
        public int compared, localChanged, worldChanged, paletteChanged;
        public float maxLocal, maxWorld, maxPalette;
        public readonly List<int> changedSlots = new();
    }
    internal static Delta Compare(float[]? before, float[] after)
    {
        var result = new Delta();
        if (before == null || before.Length != after.Length) return result;
        for (int start = 0; start + Stride <= after.Length; start += Stride)
        {
            if (!Finite(before, start) || !Finite(after, start)) continue;
            result.compared++;
            float local = Max(before, after, start, 10);
            float world = Max(before, after, start + 10, 16);
            float palette = Max(before, after, start + 26, 16);
            if (local > .0001f) result.localChanged++;
            if (world > .0001f) result.worldChanged++;
            if (palette > .0001f) result.paletteChanged++;
            result.maxLocal = Math.Max(result.maxLocal, local);
            result.maxWorld = Math.Max(result.maxWorld, world);
            result.maxPalette = Math.Max(result.maxPalette, palette);
            if ((local > .0001f || world > .0001f || palette > .0001f) && result.changedSlots.Count < 8)
                result.changedSlots.Add(start / Stride);
        }
        return result;
    }
    private static bool Finite(float[] values, int start)
    {
        for (int i = start; i < start + Stride; i++)
            if (float.IsNaN(values[i]) || float.IsInfinity(values[i])) return false;
        return true;
    }
    private static float Max(float[] a, float[] b, int start, int count)
    {
        float value = 0;
        for (int i = start; i < start + count; i++) value = Math.Max(value, Math.Abs(a[i] - b[i]));
        return value;
    }
}

internal sealed class SkinPhaseBudget
{
    internal int Frame { get; private set; } = -1;
    internal int Samples { get; private set; }
    internal int Dropped { get; private set; }
    private int _events;
    internal bool Expired(double seconds) => seconds >= 600 || Samples >= 600;
    internal bool Begin(int frame, double seconds)
    {
        if (frame == Frame || frame % 30 != 0 || Expired(seconds)) return false;
        Frame = frame; Samples++; _events = 0; return true;
    }
    internal bool Take(int frame)
    {
        if (frame != Frame) return false;
        if (_events++ < 64) return true;
        Dropped++; return false;
    }
}
