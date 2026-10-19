# Getting started

[Back to the project](../README.md) Â· [Controls](HOW-TO-PLAY.md)

## Supported target

This implementation targets the Windows x64 **Mono** game, Unity **6000.3.26f1** or **6000.3.10f1**. It rejects IL2CPP installations containing `GameAssembly.dll`. It is a development preview, not a universal installer or standalone Quest APK.

## Install the staged runtime

1. [Prepare dependencies, build and stage](BUILD.md).
2. Make a separate game copy and close it before installing. Back up every existing destination listed in `swyf-vr-manifest.json` outside that folder.
3. Copy the staged folder's contents into that copy, alongside the game executable. Review any existing mod loader/config before replacing it.
4. From this source folder, run `./tools/Launch-Game.ps1 -GameDir 'D:\Games\SWYF-VR' -Mode VR`, using your actual game-copy path. It checks staged-file hashes and the active OpenXR runtime, selects the mode and starts one client with DirectX 11.
5. For flat-screen play with tracked-avatar support, use the same command with `-Mode PC`. It skips the headset-runtime check. Existing config settings are preserved, with a backup made before the mode changes.

The restored engine and base libraries are essential. A plugin-only install or uninstall is insufficient. To uninstall, restore backed-up files and remove newly added manifest files. Steam verification can restore stock files, but does not remove mod-added files.

## Headset setup

For **Quest 3 + Steam Link**, start SteamVR, set SteamVR as the active OpenXR runtime, connect Steam Link and reach the headset's SteamVR view before opening the game. Use one client during headset testing.

Quest Link/Air Link can use the Meta PC OpenXR runtime. Index/Vive use a suitable SteamVR OpenXR setup. WMR requires an OS and runtime that still provide WMR support. These configurations are targets; the [validation record](VALIDATION.md) does not certify every headset.

## Check without launching

```powershell
./tools/Check-Setup.ps1 -GameDir 'D:\Games\SWYF-VR' -Mode VR
```

This reads the installed files without changing them. It checks the staged manifest and OpenXR library; it cannot tell whether a headset is connected. Close other clients and remove the optional smoke-test plugin before normal play.

Setup selects a pinned dependency profile for the game: **6000.3.26f1 + Input System 1.20.0**, or legacy **6000.3.10f1 + Input System 1.19.0**. Build and stage separately for each target. Do not copy an older staged runtime over an updated game. The newer target passes simulated checks; headset acceptance is still outstanding.

## In-game VR options

Press left Y / menu, choose **General**, then use the **VR OPTIONS** controls at the top. Recenter, seated comfort, menu distance, desktop placement, aim smoothing, laser visibility, snap turning and scene resolution save automatically. Scroll down for the game's normal settings. Scene resolution uses the balanced VR profile.

## Useful config

| Section / key | Default | Purpose |
| --- | --- | --- |
| `General / Mode` | `VR` | Headset mode or normal PC mode |
| `UI / PanelDistance` | `0.9` | Floating UI distance, metres |
| `UI / PanelWidth` | `1.6` | Floating UI width, metres |
| `UI / PhysicalDesktop` | `true` | Keep the desktop attached to its desk monitor |
| `UI / VirtualKeyboard` | `false` | Experimental floating keyboard; PC keyboard otherwise |
| `UI / AlwaysShowPointer` | `false` | Keep the laser visible during ordinary gameplay |
| `UI / PointerSmoothing` | `0.025` | Adaptive UI aim smoothing, seconds; 0 disables |
| `UI / DoubleClickWindow` | `0.4` | Allowed time between controller double clicks, seconds |
| `Comfort / Seated` | `false` | Recenter with seated height compensation |
| `Comfort / SnapDegrees` | `30` | Snap-turn angle |
| `Performance / SceneRenderScale` | `0.85` | VR scene render scale |
| `Performance / DesktopCameraFPS` | `15` | In-game webcam preview cap |

Old `PhysicalMouse` / `MouseTravel` keys are obsolete and ignored. After updating an existing config, set `PhysicalDesktop = true` to keep the desktop on its monitor, or change it in General settings â†’ VR options. Text entry uses a physical PC keyboard unless the experimental VR keyboard is enabled.

If anything fails, include the relevant player log and `BepInEx/LogOutput.log`. [Troubleshooting â†’](TROUBLESHOOTING.md)
