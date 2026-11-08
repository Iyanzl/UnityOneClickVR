# Public Full Package Status

Date: 2026-11-08. Base: private M033-R20 language candidate.

## Verified in the Independent Public Source Directory

- Public cursor/keyboard asset generation: passed; 104-key atlas.
- Mono .NET Framework 4.8 build: passed, with existing nullable warnings.
- IL2CPP .NET 6 renderer and entry-point build: passed, with existing nullable warnings and the known BepInEx transitive Cpp2IL version-resolution warning. Local NuGet cache was used because the workstation proxy prevented the initial online restore.
- Native input bridge build and InputBridgeContracts: passed.
- Native rendering bridge build and NativeBridgeLifecycle: passed.
- Shared pure-code checks: 230 passed, including keyboard layout, panel hit regions and per-eye depth contracts.
- Local builds used external Unity references/SDK headers; none of those reference DLLs or SDK headers are committed.

This is a build and contract check, not a new headset acceptance test. The replacement public cursor and keyboard font have not been tested in a headset. No installed game files were changed during publication.

## Full Installer Validation

- Legacy UnityHooks DLL: pinned VRGIN upstream download matched byte-for-byte; Valve and VRGIN notices retained. See NATIVE_DEPENDENCIES.md.
- Official BepInEx loaders verified; LGPL source archives for loaders, UnityDoorstop and Il2CppInterop are included in the full ZIP.
- No local Unity/game cache, generated interop, report or private development snapshot is copied into the public package.
- First IL2CPP download, install, launcher generation and uninstall/restore: passed through the actual Windows PowerShell CLI using a synthetic game fixture. No real game was executed.
- Download/cache contracts: 10 passed, including checksum rejection and no-network cache reuse.
- Desktop/VR launcher contracts: 114 passed.
- Installer/restore regression suite: 3380 checks passed on the assembled candidate with a prepared official cache.
- Compiled Mono and IL2CPP comparison: all 2752 method bodies match the accepted private R20 build. Publication asset bytes differ intentionally. The IL2CPP projects explicitly retain the tested unoptimized `modern-il2cpp` compilation setting even when built with the public Release configuration.

The full ZIP is installable without a development environment. First-time IL2CPP dependency preparation requires internet approval unless a cache was prepared in advance. The package remains an experimental compatibility release; headset acceptance of the publication-specific cursor/keyboard assets is pending. Installed games and the private test ZIP were not modified during release preparation.
