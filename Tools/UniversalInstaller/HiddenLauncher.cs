using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class HiddenLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1 || (args[0] != "Launch" && args[0] != "Desktop" && args[0] != "SkinDiagnostics"))
                throw new ArgumentException("Expected Launch, Desktop or SkinDiagnostics.");
            string directory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetFileName(directory), "_HybridVR", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The launcher must remain in the game's _HybridVR directory.");
            string script = Path.Combine(directory, "Universal.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("HybridVR launch script is missing.", script);
            string root = Path.GetDirectoryName(directory);
            // Encode a fixed command; game paths with spaces, quotes or Unicode
            // are PowerShell literals, never executable command fragments.
            string command = "& '" + script.Replace("'", "''") + "' -Mode " +
                (args[0] == "Desktop" ? "Desktop" : "Launch") + " -GameRoot '" + root.Replace("'", "''") + "'" +
                (args[0] == "SkinDiagnostics" ? " -SkinDiagnostics" : "");
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(command)),
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            // A PowerShell 7 parent can pass its incompatible module search path to
            // this GUI helper. Let Windows PowerShell construct its own defaults.
            start.EnvironmentVariables.Remove("PSModulePath");
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Unable to start HybridVR supervisor.");
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "HybridVR launch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
