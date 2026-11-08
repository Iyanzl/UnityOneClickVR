using System;

namespace HybridVR.Adapters.Il2CppOpenVr;

// No Unity state is changed. Only an original true IsActive result inside our
// synchronous eye render can be suppressed; desktop and unrelated threads pass through.
internal sealed class EyeMotionBlurGate
{
    private readonly int _thread;
    private readonly bool _suppress;
    private int _depth;
    private bool _disposed;
    internal long EyeQueries, EyeActive, EyeSuppressed, OutsideQueries, OutsideActive;

    internal EyeMotionBlurGate(int thread, bool suppress) { _thread = thread; _suppress = suppress; }
    internal int Enter(int thread)
    {
        if (_disposed || thread != _thread) return -1;
        return _depth++;
    }
    internal void Exit(int previousDepth)
    {
        if (previousDepth >= 0) _depth = previousDepth;
    }
    internal void Filter(int thread, ref bool result)
    {
        if (_disposed || thread != _thread) return;
        if (_depth == 0)
        {
            OutsideQueries++;
            if (result) OutsideActive++;
            return;
        }
        EyeQueries++;
        if (!result) return;
        EyeActive++;
        if (_suppress) { EyeSuppressed++; result = false; }
    }
    internal void Dispose() { _disposed = true; _depth = 0; }
}
