# Initial Publication Status

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

## Pending Before a Complete Public Installer

1. Establish exact source/license provenance of the specialized UnityHooks `openvr_api.dll` used by IL2CPP. Do not substitute a stock DLL with a different ABI.
2. Complete loader/native dependency inventory and corresponding-source/license material.
3. Produce a clean binary package without local Unity/game caches, generated interop, private reports or development snapshots.
4. Run clean installation, uninstall/restore and headset checks on that package.

The public repository is intentionally labeled a **source preview**, not a ready-to-use installer release. The tested private package remains unchanged.
