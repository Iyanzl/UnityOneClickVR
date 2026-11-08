using System;
using System.Collections.Generic;
using System.Reflection;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Beautify computes its own shader keywords instead of using the standard DoF
// eligibility callbacks. Override only the live pass parameter during that work.
internal sealed class BeautifyDepthOfFieldGate
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Dictionary<Type, Access?> _access = new();
    private readonly List<Scope> _active = new();
    private bool _enabled, _reportedError, _reportedParameter;
    private long _calls, _suppressed, _missing;
    internal string Status => $"beautifyCalls={_calls}, beautifySuppressed={_suppressed}, beautifyMissing={_missing}";

    internal void SetEnabled(bool value)
    {
        _enabled = value;
        if (!value)
            foreach (var scope in _active.ToArray()) scope.Dispose();
    }

    internal Scope? Enter(object pass)
    {
        if (!_enabled) return null;
        _calls++;
        Scope? scope = null;
        try
        {
            Type type = pass.GetType();
            if (!_access.TryGetValue(type, out var access))
            {
                access = Access.Create(type);
                _access.Add(type, access);
            }
            object? effect = access?.Effect.GetValue(pass);
            object? parameter = effect == null ? null : access!.Parameter.GetValue(effect);
            if (access == null || parameter == null) { _missing++; return null; }
            bool enabled = (bool)access.Value.GetValue(parameter)!;
            if (!_reportedParameter)
            {
                _reportedParameter = true;
                OpenVrDiagnostics.Write($"Beautify live depthOfField found: enabled={enabled}; render-scoped override only");
            }
            if (!enabled) return null;
            scope = new Scope(this, parameter, access.Value);
            _active.Add(scope);
            access.Value.SetValue(parameter, false);
            _suppressed++;
            return scope;
        }
        catch (Exception error)
        {
            scope?.Dispose();
            Report(error);
            return null;
        }
    }

    private void Report(Exception error)
    {
        if (_reportedError) return;
        _reportedError = true;
        OpenVrDiagnostics.Write("Beautify DoF scope unavailable: " + error.Message);
    }

    private sealed class Access
    {
        internal PropertyInfo Effect = null!, Parameter = null!, Value = null!;
        internal static Access? Create(Type type)
        {
            var effect = type.GetProperty("beautify", Fields);
            var parameter = effect?.PropertyType.GetProperty("depthOfField", Fields);
            var value = parameter?.PropertyType.GetProperty("value", Fields);
            return effect?.CanRead == true && parameter?.CanRead == true &&
                value?.CanRead == true && value.CanWrite && value.PropertyType == typeof(bool)
                ? new Access { Effect = effect, Parameter = parameter, Value = value } : null;
        }
    }

    internal sealed class Scope : IDisposable
    {
        private BeautifyDepthOfFieldGate? _owner;
        private readonly object _parameter;
        private readonly PropertyInfo _value;
        internal Scope(BeautifyDepthOfFieldGate owner, object parameter, PropertyInfo value)
        { _owner = owner; _parameter = parameter; _value = value; }

        public void Dispose()
        {
            var owner = _owner;
            if (owner == null) return;
            _owner = null;
            owner._active.Remove(this);
            try { _value.SetValue(_parameter, true); }
            catch (Exception error) { owner.Report(error); }
        }
    }
}
