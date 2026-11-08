using System.IO;

namespace HybridVR.Shared;

internal static class SessionLogArchive
{
    internal static void Preserve(string latest)
    {
        // Bounded previous sessions survive a successful restart after failure.
        try
        {
            var file = new FileInfo(latest);
            if (!file.Exists || file.Length == 0 || file.Length > 16 * 1024 * 1024) return;
            var directory = Path.Combine(file.DirectoryName!, "HybridVR-Logs");
            Directory.CreateDirectory(directory);
            for (int i = 2; i >= 1; --i)
            {
                string previous = Path.Combine(directory, file.Name + ".previous" + i);
                if (File.Exists(previous)) File.Copy(previous, Path.Combine(directory, file.Name + ".previous" + (i + 1)), true);
            }
            File.Copy(latest, Path.Combine(directory, file.Name + ".previous1"), true);
        }
        catch { /* Diagnostic retention must not interrupt startup. */ }
    }
}
