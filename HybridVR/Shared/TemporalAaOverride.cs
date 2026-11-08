using System;
using System.Reflection;

namespace HybridVR.Shared;

// Own only the enum value we changed. Do not undo a game's later non-temporal choice.
internal sealed class TemporalAaOverride : IDisposable
{
    private readonly Func<object?> _read;
    private readonly Action<object> _write;
    private readonly Action<string> _log;
    private readonly object _temporal, _replacement;
    private bool _owned;

    private TemporalAaOverride(Func<object?> read, Action<object> write, Type type, Action<string> log)
    {
        _read = read; _write = write; _log = log;
        _temporal = Enum.Parse(type, "TemporalAntialiasing");
        _replacement = Enum.Parse(type, "SubpixelMorphologicalAntiAliasing");
    }

    internal static TemporalAaOverride? TryCreate(object target, Action<string> log)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var field = target.GetType().GetField("antialiasing", flags);
        var property = target.GetType().GetProperty("antialiasing", flags);
        Type? type = field?.FieldType ?? property?.PropertyType;
        if (type == null || !type.IsEnum || !Enum.IsDefined(type, "TemporalAntialiasing") ||
            !Enum.IsDefined(type, "SubpixelMorphologicalAntiAliasing")) return null;
        if (field != null)
            return field.IsInitOnly ? null : new TemporalAaOverride(() => field.GetValue(target), value => field.SetValue(target, value), type, log);
        if (property == null || property.GetIndexParameters().Length != 0 || property.GetGetMethod() == null || property.GetSetMethod() == null) return null;
        return new TemporalAaOverride(() => property.GetValue(target), value => property.SetValue(target, value), type, log);
    }

    internal void Update(bool enabled)
    {
        if (!enabled) { Dispose(); return; }
        object? current = _read();
        if (Equals(current, _temporal))
        {
            bool firstChange = !_owned;
            _write(_replacement);
            _owned = true;
            if (firstChange) _log("shared camera TAA: TemporalAntialiasing -> SubpixelMorphologicalAntiAliasing (VR session only)");
        }
        else if (_owned && !Equals(current, _replacement))
        {
            _owned = false;
            _log("shared camera TAA: external non-temporal choice retained");
        }
    }

    public void Dispose()
    {
        if (!_owned) return;
        try
        {
            if (Equals(_read(), _replacement))
            {
                _write(_temporal);
                _log("shared camera TAA: original mode restored");
            }
        }
        finally { _owned = false; }
    }
}
