# Building UnityOneClickVR

This is the Windows x64, D3D11 source for the M033-R20 public package. Runtime C# sources follow the tested R20 selection; generated build paths and asset preparation are cleaned for publication. The independent GalsCollector color experiment is not merged. End users should download the full installer asset from Releases, rather than build this checkout.

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

`Tools/UniversalInstaller` contains the transactional installer, preflight, launcher source and templates. The scripts require a generated `catalog.json` with hashes and a vetted payload tree. **Do not run Install-HybridVR on a game from this source checkout.** The release ZIP supplies the complete catalog and payload.

To rebuild a full package, use an extracted, verified full release as `BasePackage` for its unchanged loader/native dependency payload, build the managed projects above, run `scripts/Prepare-ReleaseDependencies.ps1` to obtain the upstream notices/source archives, and use `scripts/New-FullPackage.ps1`. It takes explicit artifact directories and a source archive generated with `git archive --format=zip --output=source.zip HEAD`. It validates every base payload file, replaces only the selected managed builds, recompiles the hidden launcher, and regenerates all payload hashes. It never copies the base package's caches, diagnostics or development snapshots.

```powershell
pwsh -File scripts/Prepare-ReleaseDependencies.ps1
pwsh -File scripts/New-FullPackage.ps1 -BasePackage C:/local-references/full-release -MonoArtifacts ./artifacts/HybridVrMonoRuntime -Il2CppArtifacts ./artifacts/Uuvr -DependencyNotices ./dependencies/release-notices -SourceArchive ./source.zip -Destination ./artifacts/full-package
```

Do not copy a random `openvr_api.dll` over the IL2CPP dependency: its UnityHooks ABI is not interchangeable with a stock SDK DLL. Likewise, do not bundle game-generated interop or private Unity library caches in an installer.

## Verification Boundaries

Run the game-independent checks with `dotnet run --project tests/SharedContracts -c Release`. The GitHub workflow runs these checks without game files or credentials.

On Windows, `powershell -NoProfile -File tests/InstallerCache.ps1` checks approved-download/cache integrity behavior with a mocked download (no internet or game execution).

The private R20 candidate passed compiled runtime, installer-fixture, launcher and archive checks before source publication. Those results do not certify this cleaned build layout or its replacement artwork. Publication validation records the independent build/check results, not a new VR headset test. Please report regressions with the exact commit and runtime details.
