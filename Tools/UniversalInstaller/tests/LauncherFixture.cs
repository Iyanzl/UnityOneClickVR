using System;
using System.IO;
using System.Linq;

public static class LauncherFixture
{
    public static void Main(string[] args)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string pluginPath = Path.Combine(root, "BepInEx", "plugins");
        string[] dlls = Directory.GetFiles(pluginPath, "*.dll", SearchOption.AllDirectories);
        File.WriteAllLines(Path.Combine(root, "launch-observed.txt"), new[]
        {
            string.Join(" ", args),
            Directory.GetCurrentDirectory(),
            dlls.Count(p => Path.GetFileName(p) != "Translator.dll").ToString(),
            File.Exists(Path.Combine(pluginPath, "Translator.dll")).ToString()
        });
    }
}
