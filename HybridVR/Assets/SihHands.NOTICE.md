# SiH Glove Meshes

`SihHands.bin` is generated from the vendored `SummerInHeat_SiH_VR/OpenXRHandModels/openxr_hands`
asset (SHA256 in `SihHands.json`). Upstream: https://github.com/a1156883061/SummerInHeat_SiH_VR .
The vendored upstream distribution is licensed under GPL-3.0, with the full license
at `vendor/SummerInHeat_SiH_VR/LICENSE`; retain this notice and license when distributing
the derived mesh. Do not describe this resource as an independently created asset.

Generation: `scripts/Export-SihHandMeshes.py`, Python, UnityPy 1.20.26 and numpy.
Output includes left/right grip-local neutral, grip, trigger and combined poses.
Finger motion uses approximate vertex interpolation between baked poses, not
optical finger tracking. No hand physics or game object interaction is included.
Only geometry is reused; game-version-specific shaders/prefabs are not loaded.
