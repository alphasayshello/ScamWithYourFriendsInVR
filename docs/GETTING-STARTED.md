# Getting started

[Back to the project](../README.md) · [Controls](HOW-TO-PLAY.md)

## Supported target

This implementation targets the Windows x64 **Mono** game, Unity **6000.3.10f1**. It rejects IL2CPP installations containing `GameAssembly.dll`. It is a development preview, not a universal installer or standalone Quest APK.

## Install the staged runtime

1. [Prepare dependencies, build and stage](BUILD.md).
2. Make a separate game copy and close it before installing. Back up every existing destination listed in `swyf-vr-manifest.json` outside that folder.
3. Copy the staged folder's contents into that copy, alongside the game executable. Review any existing mod loader/config before replacing it.
4. Launch with `-force-d3d11`. The game generates `BepInEx/config/com.swyf.vr.cfg`.
5. Set `[General] Mode = VR`. For flat-screen play with VR pose support, set `Mode = PC`. Restart after changes.

The restored engine and base libraries are essential. A plugin-only install or uninstall is insufficient. To uninstall, restore backed-up files and remove newly added manifest files. Steam verification can restore stock files, but does not remove mod-added files.

## Headset setup

For **Quest 3 + Steam Link**, start SteamVR, set SteamVR as the active OpenXR runtime, connect Steam Link and reach the headset's SteamVR view before opening the game. Use one client during headset testing.

Quest Link/Air Link can use the Meta PC OpenXR runtime. Index/Vive use a suitable SteamVR OpenXR setup. WMR requires an OS and runtime that still provide WMR support. These configurations are targets; the [validation record](VALIDATION.md) does not certify every headset.

## Useful config

| Section / key | Default | Purpose |
| --- | --- | --- |
| `General / Mode` | `VR` | Headset mode or normal PC mode |
| `UI / PanelDistance` | `0.9` | Floating UI distance, metres |
| `UI / PanelWidth` | `1.6` | Floating UI width, metres |
| `UI / PhysicalDesktop` | `false` | Optional desktop on the native monitor |
| `UI / VirtualKeyboard` | `false` | Experimental floating keyboard; PC keyboard otherwise |
| `Comfort / Seated` | `false` | Recenter with seated height compensation |
| `Comfort / SnapDegrees` | `30` | Snap-turn angle |
| `Performance / SceneRenderScale` | `0.85` | VR scene render scale |
| `Performance / DesktopCameraFPS` | `15` | In-game webcam preview cap |

Old `PhysicalMouse` / `MouseTravel` keys are obsolete and ignored. After updating an existing config, explicitly set `PhysicalDesktop = false` to use the new pointer-first default. Text entry uses a physical PC keyboard unless the experimental VR keyboard is enabled.

If anything fails, include the relevant player log and `BepInEx/LogOutput.log`. [Troubleshooting →](TROUBLESHOOTING.md)
