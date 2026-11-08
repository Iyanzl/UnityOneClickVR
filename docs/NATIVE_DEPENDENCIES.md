# Native Dependency Provenance

## IL2CPP Legacy `openvr_api.dll`

The file is an **unchanged legacy Valve OpenVR Unity loader**, internally named `openvr_api_unity.dll`. It exposes `UnityHooks_GetRenderEventFunc`, `UnityHooks_SetSubmitParams` and `UnityHooks_SetColorSpace`. It is not interchangeable with a current stock OpenVR SDK DLL.

- Exact upstream distribution: [Eusth/VRGIN, commit 7b040b7e990a2d8b9c36b021c49b17401392a129](https://github.com/Eusth/VRGIN/blob/7b040b7e990a2d8b9c36b021c49b17401392a129/VRGIN/Libs/Data/Plugins/x86_64/openvr_api.dll).
- Git blob: `826cf9c8b8c14c4028e1cb79f1e256524a89ef97`.
- Size: 318272 bytes.
- SHA-256: `0D65D0C74B880ABB3C59A9B371E99248A3358DF4399FEA341083A730EC0D9675`.
- The file was downloaded again from that pinned raw URL and matched the accepted runtime DLL byte-for-byte. It was not copied from a commercial game's private binary.
- VRGIN repository notice: MIT, copyright 2016 Eusth. Valve OpenVR / SteamVR Unity plugin notice: BSD-3-Clause. Both are included. VRGIN's MIT notice does not override Valve's copyright.
- The old binary's original Valve build system/source is not claimed to be reconstructed. This project redistributes the unchanged upstream binary and documents its distribution chain.

## Mono Stock `openvr_api.dll`

The Mono path uses the Valve OpenVR SDK's stock Windows x64 loader, not the UnityHooks file above.

- SDK checkout: `ValveSoftware/openvr`, revision `0924064316de3effbcd1acf1e309182a2deb1c05`.
- Binary SHA-256: `BAB8AC6EF64E68A9CA53315B0014D131088584B2EFDFA6DB511D67EC03CFCB4A`.
- License: Valve BSD-3-Clause, included in `licenses/`.

## Loaders and Native Bridges

- BepInEx 5 Windows x64 official ZIP: SHA-256 `82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4`; verified against the official GitHub release digest.
- BepInEx IL2CPP be.785 official ZIP: SHA-256 `2A7CBF74D26ABE4765C3E662DB1721B923BAC39849EBFEF2CA5DC7DE7E2D9B7F`; freshly downloaded from the official BepInEx build service and matched to the existing loader payload.
- `Uuvr.OpenVR.NativeBridge.dll` and `Uuvr.OpenVR.InputBridge.dll` are this derivative project's GPL-covered bridges. Their source/build instructions are in this repository. The release retains the previously tested native payload; the public managed builds replace only private cursor/keyboard artwork and clean build paths.
- See the package's `BINARY_INVENTORY.json`, `MANIFEST.sha256`, `licenses/sources.json` and `sources/upstream/` for the remaining binary versions, notices and source snapshots.
