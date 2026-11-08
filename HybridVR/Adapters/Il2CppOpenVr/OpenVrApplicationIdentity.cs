using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
#if HYBRID_MONO
using Newtonsoft.Json;
#else
using System.Text.Json;
#endif

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed class OpenVrApplicationIdentity
{
    internal string Key { get; }
    internal string Manifest { get; }

    internal OpenVrApplicationIdentity(string executable, string actionManifest)
    {
        executable = Path.GetFullPath(executable);
        Key = KeyForExecutable(executable);
        Manifest = Path.Combine(Path.GetDirectoryName(actionManifest)!, "hybridvr.vrmanifest");
        var value = new
        {
            source = "builtin",
            applications = new[] { new
            {
                app_key = Key, launch_type = "binary", binary_path_windows = executable,
                working_directory = Path.GetDirectoryName(executable),
                action_manifest_path = Path.GetFullPath(actionManifest),
                strings = new Dictionary<string, object>
                {
                    ["en_us"] = new { name = "HybridVR - " + Path.GetFileNameWithoutExtension(executable) }
                }
            } }
        };
#if HYBRID_MONO
        string json = JsonConvert.SerializeObject(value, Formatting.Indented);
#else
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
#endif
        // Only this generated registration is owned here; user's action/binding JSON stays intact.
        if (!File.Exists(Manifest) || File.ReadAllText(Manifest) != json)
            File.WriteAllText(Manifest, json, new UTF8Encoding(false));
    }

    internal static string KeyForExecutable(string executable)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(executable).Replace('/', '\\').ToLowerInvariant()));
        return "org.hybridvr.controls.v1." + BitConverter.ToString(bytes, 0, 16).Replace("-", "").ToLowerInvariant();
    }
}
