# Getting started

[Back to the project](../README.md) · [Controls](HOW-TO-PLAY.md)

## Supported target

This implementation targets the Windows x64 **Mono** game, Unity **6000.3.26f1** or **6000.3.10f1**. It rejects IL2CPP installations containing `GameAssembly.dll`. It is a development preview, not a universal installer or standalone Quest APK.

## Install the staged runtime

1. [Prepare dependencies, build and stage](BUILD.md).
2. Close the game, then run the command below from this source folder. Choose an unmodified source game, your completed stage and a **new destination** with enough room for another full game copy.
3. Open **Play VR.cmd** in the new folder after connecting your headset, or **Play PC.cmd** for flat-screen play with tracked-avatar support. The launchers check the installation, preserve your config and select the mode automatically.

```powershell
./tools/Install-GameCopy.ps1 -SourceGameDir 'C:\Program Files (x86)\Steam\steamapps\common\Scam With Your Friends' -StageDir 'D:\staging\ScamWithYourFriendsInVR' -Destination 'D:\Games\SWYF-VR'
```

The installer checks the stage hashes and exact engine identity before copying, leaves your original game untouched, and refuses existing destinations or a running game. Its browser cache starts fresh; generated browser data from the source installation is skipped. The copy has its own launch tools, so moving the source repository does not break its PC/VR launchers. PowerShell 7 is required. It does not launch the game during setup.

Keep Steam running when playing the copied game. To uninstall an isolated copy, close it and remove that copy; your original installation is unchanged. Your game may share saves/settings between copies.

For an existing manually managed copy, back up every destination named in `swyf-vr-manifest.json` outside that folder before copying the stage contents over it. Use `./tools/Launch-Game.ps1 -GameDir 'D:\Games\SWYF-VR' -Mode VR` or `-Mode PC` from this source folder.

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

Old `PhysicalMouse` / `MouseTravel` keys are obsolete and ignored. After updating an existing config, set `PhysicalDesktop = true` to keep the desktop on its monitor, or change it in General settings → VR options. Text entry uses a physical PC keyboard unless the experimental VR keyboard is enabled.

If anything fails, include the relevant player log and `BepInEx/LogOutput.log`. [Troubleshooting →](TROUBLESHOOTING.md)
