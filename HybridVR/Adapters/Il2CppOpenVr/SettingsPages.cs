using System;

namespace HybridVR.Adapters.Il2CppOpenVr;

// No scroll-view wrappers: several IL2CPP games strip their implementation.
internal readonly struct SettingsPages
{
    private readonly int _rows, _perPage, _rowHeight;
    private readonly float _top;
    internal int Count => (_rows + _perPage - 1) / _perPage;
    internal SettingsPages(float height, bool compact, int rows, float top = 48, float footer = 64, int compactRowHeight = 78)
    {
        _rows = Math.Max(1, rows);
        _top = top;
        _rowHeight = compact ? compactRowHeight : 44;
        _perPage = Math.Max(1, (int)(height - top - footer) / _rowHeight);
    }
#if HYBRID_MONO
    internal int ClampPage(int page) => HybridVR.Adapters.Mono.FrameworkCompatibility.Clamp(page, 0, Count - 1);
#else
    internal int ClampPage(int page) => Math.Clamp(page, 0, Count - 1);
#endif
    internal bool TryRow(int page, int row, out float y)
    {
        int local = row - ClampPage(page) * _perPage;
        y = _top + local * _rowHeight;
        return row >= 0 && row < _rows && local >= 0 && local < _perPage;
    }
}
