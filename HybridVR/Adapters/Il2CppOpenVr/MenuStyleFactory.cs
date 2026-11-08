using System;
using System.Reflection;

namespace HybridVR.Adapters.Il2CppOpenVr;

// IL2CPP stripping can remove the copy constructor while leaving native styles usable.
internal static class MenuStyleFactory
{
    internal static object Create(Type type, object template, Action<string> write)
    {
        var copy = type.GetConstructor(new[] { type });
        if (copy != null)
        {
            try { return copy.Invoke(new[] { template }); }
            catch (Exception error) { write("settings style copy unavailable: " + error.GetBaseException().Message); }
        }
        var style = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Cannot create menu style");
        // Only copy presentation values. Never mutate the game's shared GUISkin.
        foreach (string name in new[] { "font", "fontStyle", "fontSize", "alignment", "wordWrap", "clipping",
            "richText", "contentOffset", "border", "padding", "margin", "overflow" })
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.CanRead != true || !property.CanWrite) continue;
            try { property.SetValue(style, property.GetValue(template)); }
            catch (Exception error) { write("settings style property skipped: " + name + "; " + error.GetBaseException().Message); }
        }
        foreach (string name in new[] { "normal", "hover", "active", "focused", "onNormal", "onHover", "onActive", "onFocused" })
        {
            try
            {
                var property = type.GetProperty(name);
                object? source = property?.GetValue(template), target = property?.GetValue(style);
                if (source == null || target == null) continue;
                foreach (string field in new[] { "background", "textColor" })
                {
                    var member = property!.PropertyType.GetProperty(field);
                    if (member?.CanRead == true && member.CanWrite) member.SetValue(target, member.GetValue(source));
                }
            }
            catch (Exception error) { write("settings style state skipped: " + name + "; " + error.GetBaseException().Message); }
        }
        write("settings style: native default constructor with copied properties");
        return style;
    }
}
