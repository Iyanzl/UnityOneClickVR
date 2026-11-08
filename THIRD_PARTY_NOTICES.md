# Licensing and Redistribution

## Project License

UnityOneClickVR's combined derivative work and new integration code are offered under **GPL-3.0-only**. This preserves a GPLv3-compatible combined distribution without expanding rights in any upstream component. UUVR's original code remains available under GPL-3.0-or-later where its upstream notice says so. This is not a relicensing of upstream ownership.

Copyright (C) 2026 Iyanzl, for new contributions. Upstream copyrights and notices remain with their authors. Files without an individual license notice fall under the project license, except the separately identified resources and SDK components below.

The project is provided without warranty. If you distribute modified binaries, also provide the corresponding source and build materials under the applicable GPL terms. Optional tips do not change these obligations. See the full [GPL text](LICENSE) and [GNU license FAQ](https://www.gnu.org/licenses/gpl-faq.en.html).

## Included Components

| Component | License | Location / Source |
| --- | --- | --- |
| UUVR-derived code | GPL-3.0-or-later upstream; combined release GPL-3.0-only | `Uuvr/`, rendering/integration code; Raicuparta/uuvr |
| UnityVRMod / SiH-derived work | GPL-3.0 | See ACKNOWLEDGMENTS.md |
| SiH hand mesh and upstream bundle used by exporter | GPL-3.0 upstream distribution | `HybridVR/Assets/SihHands.*`, `vendor/SummerInHeat_SiH_VR/`; upstream LICENSE retained |
| OpenVR bindings, header and import library | BSD-3-Clause | `licenses/BSD-3-Clause-OpenVR.txt`, ValveSoftware/openvr |
| Noto Sans SC glyph data | SIL OFL-1.1 | `HybridVR/Assets/LICENSE-OFL-Noto.txt` and `MenuGlyphs.NOTICE.md` |

The public cursor is generated from an original geometric shape by `scripts/Build-PublicAssets.ps1`. The developer's screenshot-supplied cursor is not included. Keyboard atlas source is included; regenerate it with Noto Sans SC. No Microsoft font files are redistributed.

## Runtime Dependencies in the Complete Installer

The source checkout does not contain these DLLs. The full release ZIP includes the unchanged loader/dependency payloads, notices in `licenses/`, and corresponding source archives for BepInEx, Il2CppInterop and UnityDoorstop in `sources/upstream/`. Project source is in `sources/UnityOneClickVR-source.zip`. The complete binary inventory and hashes are included in the package. You may replace or modify LGPL libraries under their licenses; this project imposes no extra reverse-engineering restriction for that purpose.

- BepInEx 5.4.23.5 and 6.0.0-be.785: LGPL-2.1-or-later; unmodified official payloads from https://github.com/BepInEx/BepInEx and https://builds.bepinex.dev/ .
- Il2CppInterop 1.5.3: LGPL-3.0; knah/BepInEx contributors. Corresponding source snapshot `dbda1cb353b0f4253345dc45136d170b9e50a5a0` is included.
- UnityDoorstop 4.5.0: LGPL-2.1; NeighTools/UnityDoorstop. Corresponding source is included.
- .NET 6.0.7 runtime and support assemblies: MIT and component notices; dotnet/runtime snapshot `0ec02c8c96e2eda06dc5b5edfdbdba0f36415082`. Runtime LICENSE and THIRD-PARTY-NOTICES are included.
- HarmonyX: MIT; BepInEx/HarmonyX. Other BepInEx loader dependencies retain their respective licenses.
- Newtonsoft.Json: MIT; JamesNK/Newtonsoft.Json (NuGet 13.0.3).
- ViGEm.NET / Nefarius.ViGEm.Client: MIT; nefarius/ViGEm.NET. A separately installed virtual-controller driver may be required for Xbox emulation.
- Mono.Cecil: MIT; jbevain/cecil.
- AsmResolver, AssetRipper.CIL/Primitives, Cpp2IL/LibCpp2IL/StableNameDotNet/WasmDisassembler, Disarm, Capstone.NET, Iced, SemanticVersioning and MonoMod: their MIT notices are included. Dobby uses Apache-2.0; Capstone native code uses its BSD notices.
- Legacy Valve UnityHooks loader: unchanged binary distributed by VRGIN; Valve BSD and VRGIN MIT notices included. It is not authored by this project or relicensed under GPL. See `docs/NATIVE_DEPENDENCIES.md`.
- Unity native plugin SDK headers: supplied separately by the developer, under the license of the selected Unity SDK version. No Unity Companion License headers are copied into this source release.
- Unity/game managed assemblies and generated interop: not included and not relicensed. Supply references from a lawful local development environment. Do not upload game code or assets to this repository.
- SteamVR, Meta software and Virtual Desktop: separately obtained products, not bundled or relicensed.
- Python mesh regeneration tools: UnityPy (MIT) and NumPy (BSD-3-Clause); build tools only.

## Distribution Boundaries

The public installer is rebuilt from a verified payload allowlist; internal development snapshots, reports and Unity/game caches are not included. The UnityHooks binary is byte-identical to the pinned VRGIN upstream download. No claim is made that the legacy Valve binary was built by this project or that its proprietary build tooling is available. Source and notices are provided for the open-source components under their applicable terms. Unity base libraries are fetched only from the official BepInEx service, with user approval, rather than redistributed as project-owned content.
