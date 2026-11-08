# Shared OpenVR renderer

Source moved without behavioural changes from the GC13 IL2CPP build staging tree.
Both runtime builds must compile these files, not maintain separate renderer copies.
`HYBRID_MONO` selects only managed-runtime interoperability and framework compatibility.
Native submit ABI 4 and input ABI 1 remain the accepted GC12 binaries.

Mono is a candidate backend, not an in-place patch of UnityXR/OpenXR ownership.
It must not run beside UUVR, UnityVRMod or VrginModern in the same process.
