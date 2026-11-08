using System;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Driver queue ownership only; no compositor or pose changes.
internal sealed class FrameQueuePolicy
{
    internal const string Argument = "--hybrid-max-queued-frames=1";
    internal bool Enabled { get; private set; }
    internal bool OwnsSetting { get; private set; }
    private int _previous, _lastApplied;

    internal FrameQueuePolicy(string[] arguments)
    {
        Enabled = Array.Exists(arguments, value => value == Argument);
    }

    internal int Apply(int current, bool? configured = null)
    {
        if (configured.HasValue) Enabled = configured.Value;
        if (Enabled && !OwnsSetting) { _previous = current; OwnsSetting = true; }
        _lastApplied = Enabled ? 1 : -1;
        return _lastApplied;
    }

    internal int Restore(int current)
    {
        bool restore = OwnsSetting && current == _lastApplied;
        OwnsSetting = false;
        return restore ? _previous : current;
    }
}
