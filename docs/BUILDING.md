# Building the Source Preview

This is a Windows x64, D3D11 source preview based on local candidate M033-R20. It is not a turnkey binary distribution. Runtime C# sources follow the tested R20 selection; generated build paths and asset preparation are cleaned for publication. The independent GalsCollector color experiment is not merged.

## Requirements

- Windows, PowerShell 7, .NET SDK 9 (or a compatible SDK with .NET 6 / .NET Framework 4.8 targets).
- Visual Studio C++ tools, Windows SDK and CMake 3.24+ for native bridges.
- Official BepInEx 5 core assemblies for Mono; the IL2CPP projects restore BepInEx 6 API packages from its official NuGet feed.
- Unity references supplied locally: a Mono game's `*_Data/Managed`, or a generated IL2CPP `BepInEx/interop` directory. These are build inputs, not redistributable project source. Different Unity versions can expose different APIs.
- Noto Sans SC installed for regenerating the public cursor/keyboard assets. The menu's OFL glyph subset and license are already included.
- Unity native plugin interface headers for the native bridge, supplied separately under their original SDK license.

## Managed Code

```powershell
pwsh -File scripts/Build-PublicAssets.ps1
dotnet build Tools/HybridVrMonoRuntime/HybridVrMonoRuntime.csproj -c Release "-p:UnityReferenceDir=C:/local-references/mono" "-p:BepInExCoreDir=C:/local-references/BepInEx5/core"
dotnet build Uuvr/Uuvr.csproj -c Release "-p:UnityReferenceDir=C:/local-references/interop"
```

The Mono build emits `HybridVR.Mono.Runtime.dll`; the IL2CPP build emits `Uuvr.dll` and `Uuvr.OpenVR.dll`. Output is under `artifacts/`, not in a game directory. The old names are intentionally retained. The API package version is pinned to the tested build's BepInEx API, not a promise of compatibility with every bleeding-edge loader.

To regenerate the glove mesh, install UnityPy 1.20.26 and NumPy in a build-only environment, then run:

```powershell
python scripts/Export-SihHandMeshes.py vendor/SummerInHeat_SiH_VR/OpenXRHandModels/openxr_hands HybridVR/Assets/SihHands.bin
```

The public asset generator does not change the local/private candidate. Cursor artwork and keyboard font are publication-specific replacements; visual acceptance in a headset remains pending.

## Native Bridges

```powershell
cmake -S Uuvr.OpenVR.NativeBridge -B artifacts/native -A x64 "-DUNITY_PLUGIN_HEADERS=C:/local-references/UnityPluginHeaders"
cmake --build artifacts/native --config Release
ctest --test-dir artifacts/native -C Release --output-on-failure
cmake -S Uuvr.OpenVR.InputBridge -B artifacts/input -A x64
cmake --build artifacts/input --config Release
ctest --test-dir artifacts/input -C Release --output-on-failure
```

Use OpenVR SDK headers/import library from the same revision. This snapshot includes their original BSD notice. Native tests do not establish headset compatibility.

## Installer Source

`Tools/UniversalInstaller` contains the transactional installer, preflight, launcher source and templates. The scripts require a generated `catalog.json` with hashes and a vetted payload tree. No ready-made catalog/payload is supplied in this source preview. **Do not run Install-HybridVR on a game from this source checkout.**

Do not copy a random `openvr_api.dll` over the IL2CPP dependency: its UnityHooks ABI is not interchangeable with a stock SDK DLL. Likewise, do not bundle game-generated interop or private Unity library caches in an installer.

## Verification Boundaries

Run the game-independent checks with `dotnet run --project tests/SharedContracts -c Release`. The GitHub workflow runs these checks without game files or credentials.

The private R20 candidate passed compiled runtime, installer-fixture, launcher and archive checks before source publication. Those results do not certify this cleaned build layout or its replacement artwork. Publication validation records the independent build/check results, not a new VR headset test. Please report regressions with the exact commit and runtime details.
