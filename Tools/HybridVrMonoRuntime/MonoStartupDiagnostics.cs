using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Uuvr.OpenVR;

namespace HybridVR.MonoRuntime;

internal static class MonoStartupDiagnostics
{
    internal static void Write(string directory)
    {
        try
        {
            string registry = Environment.GetEnvironmentVariable("VR_PATHREG_OVERRIDE") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath");
            string? error = null;
            JObject? paths = null;
            try { paths = JObject.Parse(File.ReadAllText(registry)); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var report = new
            {
                schema = 1, time = DateTimeOffset.Now, processId = process.Id, executable = process.MainModule?.FileName,
                unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(),
                renderer = "manual-openvr-original-camera", headsetVerified = false,
                apiModule = MonoOpenVrExports.Module.ToInt64().ToString("X"),
                registry, registryError = error, writeAccessTested = false,
                paths = new[] { "runtime", "config", "log" }.Select(kind => new
                {
                    kind, entries = (paths?[kind] as JArray ?? new JArray()).Select(p => new
                    { path = p.ToString(), exists = Directory.Exists(p.ToString()) }).ToArray()
                }).ToArray(),
                overrides = new[] { "VR_OVERRIDE", "VR_CONFIG_PATH", "VR_LOG_PATH", "VR_PATHREG_OVERRIDE" }
                    .ToDictionary(k => k, Environment.GetEnvironmentVariable)
            };
            string destination = Path.Combine(directory, "HybridVR.OpenVR.startup.json");
            File.WriteAllText(destination, JsonConvert.SerializeObject(report, Formatting.Indented));
            OpenVrDiagnostics.Write("OpenVR startup environment: " + destination + "; registryError=" + (error ?? "none"));
        }
        catch (Exception e) { OpenVrDiagnostics.Write("Startup environment diagnostic unavailable: " + e.Message); }
    }
}
