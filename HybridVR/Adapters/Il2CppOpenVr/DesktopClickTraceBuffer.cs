using System.Collections.Concurrent;
using System.Threading;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal readonly struct DesktopClickTrace
{
    internal readonly int Message, X, Y;
    internal readonly uint Flags;
    internal readonly ulong Extra;
    internal readonly long Timestamp;
    internal DesktopClickTrace(int message, int x, int y, uint flags, ulong extra, long timestamp)
    { Message = message; X = x; Y = y; Flags = flags; Extra = extra; Timestamp = timestamp; }
    internal bool Own => Extra == DesktopMouseObserver.InjectionTag;
    internal string Source => Own ? "hybrid" : (Extra & 0xFFFFFF00UL) == 0xFF515700UL
        ? ((Extra & 0x80) != 0 ? "touch-signature" : "pen-signature")
        : (Flags & 1) != 0 ? "external-injected" : "physical-or-driver";
}

// Button edges only. Never run Unity, disk I/O or input injection on the hook thread.
internal sealed class DesktopClickTraceBuffer
{
    internal const int Capacity = 128;
    private readonly ConcurrentQueue<DesktopClickTrace> _events = new();
    private int _count, _dropped;
    internal int Dropped => Volatile.Read(ref _dropped);
    internal void Add(DesktopClickTrace value, bool foreground)
    {
        if (!foreground || value.Message is not (0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208)) return;
        if (Interlocked.Increment(ref _count) > Capacity)
        { Interlocked.Decrement(ref _count); Interlocked.Increment(ref _dropped); return; }
        _events.Enqueue(value);
    }
    internal bool TryRead(out DesktopClickTrace value)
    {
        if (!_events.TryDequeue(out value)) return false;
        Interlocked.Decrement(ref _count);
        return true;
    }
}
