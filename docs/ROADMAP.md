# Current priorities

This remains a development preview. The user's latest headset run accepted the general view/menu experience but rejected computer usability and reported body rotation/room movement problems.

## Current revision

- Keep computer UI on its authored desk monitor with controller-pointer operation, plus an optional floating desktop.
- Remove the physical desk mouse/touch-keyboard experiment; disable the experimental floating keyboard by default.
- Separate body yaw from tracking-origin yaw so turning the model does not turn the view twice.
- Follow physical steps with the native collision body, consume only actual movement, and keep a small leaning allowance.
- Track the local avatar head and remote voice mouth; restore owned poses on timeout/disable.
- Provide an honest, usable source repository with setup, controls, build instructions, troubleshooting and a project banner.
- Create isolated game copies with validated stages and portable PC/VR launchers.
- Preserve native struggle/give-up controls and follow the fallen avatar when its prefab lacks a ragdoll camera marker.

## Still required

- Headset acceptance of the revised body/room movement, native chairs, stairs, recenter and tracking recovery.
- Controller interaction inside every relevant computer workflow, not just opening/closing windows.
- PvP/other weapons, other food and held-item variants, boost trajectories/final cone use, violent ragdolls, completed give-up outcomes, hiding and spectating.
- Two-account Steam host/client acceptance, reconnect and voice spatialization.
- Sustained frame-time profiling and supported headset/runtime acceptance.
- Finish headset acceptance of the newer Steam target using Unity 6000.3.26f1 and Input System 1.20.0.
- Review staged third-party redistribution terms before publishing runtime packages.

Use one VR client during headset testing. After the VR conversion is complete, begin the separate general optimization mod; it is not part of an already-finished release.
