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

## External Dependencies, Not Bundled Here

- BepInEx: LGPL-2.1; obtain the appropriate official Mono or IL2CPP loader and its notices from https://github.com/BepInEx/BepInEx and https://builds.bepinex.dev/ .
- HarmonyX: MIT; BepInEx/HarmonyX. Other BepInEx loader dependencies retain their respective licenses.
- Newtonsoft.Json: MIT; JamesNK/Newtonsoft.Json (NuGet 13.0.3).
- ViGEm.NET / Nefarius.ViGEm.Client: MIT; nefarius/ViGEm.NET. A separately installed virtual-controller driver may be required for Xbox emulation.
- Mono.Cecil: MIT; jbevain/cecil.
- Unity native plugin SDK headers: supplied separately by the developer, under the license of the selected Unity SDK version. No Unity Companion License headers are copied into this source release.
- Unity/game managed assemblies and generated interop: not included and not relicensed. Supply references from a lawful local development environment. Do not upload game code or assets to this repository.
- SteamVR, Meta software and Virtual Desktop: separately obtained products, not bundled or relicensed.
- Python mesh regeneration tools: UnityPy (MIT) and NumPy (BSD-3-Clause); build tools only.

## Why This First Publication Has No Binary Installer

The internal R20 package has not been uploaded. The special IL2CPP `openvr_api.dll` with UnityHooks exports still needs exact upstream binary/source provenance, and local Unity caches must not be treated as project-owned content. A complete installer will be published separately only after the dependency inventory, corresponding source, notices and installation checks are complete. This source preview makes no claim that those pending binaries have passed a redistribution review.
