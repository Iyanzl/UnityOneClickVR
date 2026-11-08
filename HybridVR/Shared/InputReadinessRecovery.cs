namespace HybridVR.Shared;

// Rebind only before the first usable action sample, never on ordinary tracking loss.
internal sealed class InputReadinessRecovery
{
    private double _since = -1;
    private int _attempts;
    private bool _everActive;
    internal bool ShouldRetry(double seconds, int result, uint active)
    {
        if (result == 0 && active != 0) { _everActive = true; _since = -1; }
        if (_everActive || _attempts >= 3) return false;
        bool pending = result == 0 || result == 3 || result == 7 || result == 8 || result == 13;
        if (!pending) { _since = -1; return false; }
        if (_since < 0) _since = seconds;
        if (seconds - _since < 5) return false;
        _attempts++;
        _since = seconds;
        return true;
    }
}
