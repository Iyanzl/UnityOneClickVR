# Upstream Snapshot Information

This is an independently maintained derivative. The source history was curated from an active development workspace; experimental branches, game files and private diagnostics are excluded.

- OpenVR SDK header/import library: local vendor checkout `ValveSoftware/openvr`, commit `0924064316de3effbcd1acf1e309182a2deb1c05`. BSD-3-Clause notice is in `licenses/`.
- SiH hand bundle: local vendor checkout `a1156883061/SummerInHeat_SiH_VR`, commit `1abc5464b6d5528d06374f1256b01d5fd22eabd1`. Bundle and derived mesh hashes are recorded in `HybridVR/Assets/SihHands.json`.
- UUVR, UnityVRMod and SiH code lineage: see ACKNOWLEDGMENTS.md. Runtime files are modified integration sources, not verbatim latest upstream checkouts.
- Source selection: the stable UI replay files used by M033-R20 are placed at their normal compilation paths. Unselected compatibility experiments are not included.
- Internal identifiers remain HybridVR/Uuvr to preserve existing configurations and plugin compatibility.

Please report attribution or provenance corrections with a source link and file reference.
