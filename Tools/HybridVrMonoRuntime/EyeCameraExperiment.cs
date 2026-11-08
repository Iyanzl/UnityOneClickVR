using System;
using System.Reflection;

namespace HybridVR.MonoRuntime;

internal enum EyeCameraExperimentMode { Source, Shared, Dual }

internal static class EyeCameraExperiment
{
    internal static EyeCameraExperimentMode Parse(string[] args)
    {
        const string prefix = "--hybrid-eye-camera=";
        var mode = EyeCameraExperimentMode.Source;
        bool found = false;
        foreach (var arg in args)
        {
            if (!arg.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (found) throw new ArgumentException("Duplicate eye camera experiment flag");
            found = true;
            switch (arg.Substring(prefix.Length))
            {
                case "source": mode = EyeCameraExperimentMode.Source; break;
                case "shared": mode = EyeCameraExperimentMode.Shared; break;
                case "dual": mode = EyeCameraExperimentMode.Dual; break;
                default: throw new ArgumentException("Unknown eye camera experiment mode");
            }
        }
        return mode;
    }

    internal static int Slot(EyeCameraExperimentMode mode, bool right)
        => mode == EyeCameraExperimentMode.Dual && right ? 1 : 0;
}

// Use HDRP's supported settings copier, never clone callbacks or private caches.
internal sealed class HdrpEyeSettings
{
    private readonly object _source;
    private readonly MethodInfo _copy;
    private readonly FieldInfo _xr;
    private readonly object[] _arguments;

    internal HdrpEyeSettings(object source, object target)
    {
        var type = source.GetType();
        if (target.GetType() != type || ReferenceEquals(source, target))
            throw new ArgumentException("Eye HDRP data must be a separate object of the same type");
        _copy = type.GetMethod("CopyTo", BindingFlags.Public | BindingFlags.Instance,
            null, new[] { type }, null) ?? throw new NotSupportedException("HDRP CopyTo unavailable");
        if (_copy.ReturnType != typeof(void)) throw new NotSupportedException("Unexpected HDRP CopyTo signature");
        _xr = type.GetField("xrRendering", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new NotSupportedException("HDRP xrRendering unavailable");
        if (_xr.FieldType != typeof(bool)) throw new NotSupportedException("Unexpected HDRP XR field");
        _source = source;
        _arguments = new[] { target };
    }

    internal void Copy()
    {
        _copy.Invoke(_source, _arguments);
        // These are manually submitted mono eye targets, not Unity XR cameras.
        _xr.SetValue(_arguments[0], false);
    }
}
