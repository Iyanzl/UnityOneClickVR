using System;
using System.Reflection;

namespace HybridVR.Adapters.Il2CppOpenVr;

// Reflection keeps stripped font overloads out of the menu's JIT call graph.
internal static class MenuFontFactory
{
    internal static object? Create(Type fontType, bool allowOsFactory, Func<object, bool> supports,
        Action<object> discard, Action<string> write, string language = "en")
    {
        string[] names = MenuLanguage.WantsJapanese(language)
            ? new[] { "Yu Gothic", "Yu Gothic UI", "Meiryo", "MS Gothic", "Microsoft YaHei UI" }
            : new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "SimSun", "Yu Gothic" };
        object? Attempt(string path, Func<object?> create)
        {
            object? font = null;
            try
            {
                font = create();
                if (font != null && supports(font)) { write("settings font selected: " + path); return font; }
                write("settings font rejected (missing glyphs): " + path);
            }
            catch (Exception error)
            {
                write("settings font attempt failed: " + path + "; " + (error.InnerException ?? error).Message);
            }
            if (font != null) discard(font);
            return null;
        }

        if (allowOsFactory)
        {
            var arrayFactory = fontType.GetMethod("CreateDynamicFontFromOSFont", new[] { typeof(string[]), typeof(int) });
            if (arrayFactory != null)
            {
                var font = Attempt("OS dynamic font list", () => arrayFactory.Invoke(null, new object[] { names, 16 }));
                if (font != null) return font;
            }
            var factory = fontType.GetMethod("CreateDynamicFontFromOSFont", new[] { typeof(string), typeof(int) });
            if (factory != null)
                foreach (string name in names)
                {
                    var font = Attempt("OS dynamic " + name, () => factory.Invoke(null, new object[] { name, 16 }));
                    if (font != null) return font;
                }
        }

        // Native dynamic constructors can survive when IL2CPP factory wrappers are stripped.
        foreach (var dynamicConstructor in fontType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var parameters = dynamicConstructor.GetParameters();
            if (parameters.Length != 2 || parameters[1].ParameterType != typeof(int)) continue;
            Type arrayType = parameters[0].ParameterType;
            if (arrayType != typeof(string[]) && arrayType.FullName != "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray") continue;
            var font = Attempt("native dynamic font list", () =>
            {
                object families = arrayType == typeof(string[]) ? names : Activator.CreateInstance(arrayType, new object[] { names })!;
                return dynamicConstructor.Invoke(new[] { families, (object)16 });
            });
            if (font != null) return font;
        }

        // Font(name) may resolve a built-in asset rather than an OS family; use it last.
        var constructor = fontType.GetConstructor(new[] { typeof(string) });
        if (constructor != null)
            foreach (string name in names)
            {
                var font = Attempt("native Font " + name, () => constructor.Invoke(new object[] { name }));
                if (font != null) return font;
            }
        write("settings font unavailable; English fallback, language selection retained");
        return null;
    }
}
