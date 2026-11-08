using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Uuvr.OpenVR;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal static class NativeLibraryDiagnostics
{
    private static bool _written;
    internal static void WriteOnce()
    {
        if (_written) return;
        _written = true;
        try
        {
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                string name = module.ModuleName;
                if (!name.Equals("openvr_api.dll", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("Uuvr.OpenVR.InputBridge.dll", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("Uuvr.OpenVR.NativeBridge.dll", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = new FileStream(module.FileName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var sha = SHA256.Create();
                string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                OpenVrDiagnostics.Write($"native module: name={name}, path={module.FileName}, bytes={stream.Length}, sha256={hash}; hash describes disk file, module path comes from current process");
            }
        }
        catch (Exception error)
        {
            OpenVrDiagnostics.Write("native module evidence unavailable: " + error.Message);
        }
    }
}
