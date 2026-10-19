# runtime integration

The supported Windows x64 Mono targets are Unity 6000.3.26f1 with Input System 1.20.0, and legacy Unity 6000.3.10f1 with Input System 1.19.0. Build each target against its matching editor libraries and dependency lock. A mismatched Input System can crash native startup asset loading before BepInEx initializes; preparation, staging and setup checks reject that mismatch. The stock engine XR assembly contains `InputDevice` but omits `GetDeviceAtXRNode`, `InputFeatureUsage<T>` and related APIs. Native OpenXR binaries and subsystem manifests are absent. The base class library also omits reflection methods required by BepInEx.

XR support needs three distinct layers:

1. The player must load matching unstripped managed engine APIs and their Mono/BCL dependencies. Compilation reference assemblies alone are not sufficient runtime replacements. The exact Windows editor's Mono player libraries and BCL are the intended source.
2. Unity must discover the OpenXR input/display descriptors at `Scam With Your Friends_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json`, with `UnityOpenXR.dll` in `Plugins/x86_64` and the Windows OpenXR loader available to the native plugin.
3. After graphics initialization, the mod supplies configured OpenXR settings, profiles, loader and manager, initializes the loader, and starts the subsystems. The active PC OpenXR runtime must provide an HMD session.

The official XR provider source build is included in `tools/provider`. It uses the package's runtime source without editor-only features, mocks, runtime debugger or Android-only feature assemblies. Input System is rebuilt at the game's exact package version with XR support enabled. The mod uses `UnityEngine.XR` feature values rather than OS mouse injection.

Restoring full libraries also restores abstract interface members that the linker removed together with implementations in game assemblies. This can invalidate existing types before a plugin starts. The staging compatibility tool compares stock and full interface contracts and gives newly restored abstract members explicit throwing default implementations. Existing concrete engine implementations still take precedence. Calls to an implementation stripped from a game type fail with a descriptive `NotSupportedException`; this does not reconstruct deleted game logic. This approach requires validation on the exact Mono player and is experimental. Do not replace this step by deleting interface members: complete libraries such as BepInEx and LINQ call some of those members.

The bootstrap creates standard Oculus Touch, Index, Vive, Microsoft motion controller, simple controller, HP Reverb G2, Touch Pro and Touch Plus profiles. Required extension strings for the latter three are taken from Unity's feature declarations. Unsupported profile extensions can be rejected by an individual runtime. Multipass rendering is used to reduce dependence on stereo-instancing shader variants that the game may have stripped.

`XR.SettingsBundle` is an advanced alternative for an existing serialized manager. It cannot add native engine support by itself. A runtime descriptor or device mismatch is a startup failure, and gameplay patching is not activated until a display is running.

The game integration deliberately retains the game's CharacterController simulation, movement blockers, pickup validation and Mirror commands. It does not directly set network-owned item transforms or manufacture a local player by scanning remote objects. Reflection methods are cached and failures are logged once; absent Harmony targets are skipped and logged.

## known limits

- SteamVR and Meta compatibility goes through their OpenXR runtime. There is no OpenVR fallback.
- IL2CPP needs a separate native/interop and BepInEx 6 build. The current Mono plugin must not be installed into it.
- Local desktop UI follows its authored desk-monitor geometry, with an optional floating fallback. Existing world-space uGUI is retained; texture-backed native panels keep their renderer/UV mapping. Arbitrary replacement monitor geometry still requires validation.
- Standard controller profiles drive the native avatar hands. Temporary grip capsules are removed. Anatomical hand tracking and controller-vendor model assets are not supplied.
- The AudioListener follows the tracked camera. Local and modded remote `PlayerModeManager.voiceChatMouthAudioSource` transforms follow tracked head poses after animation and restore when ownership ends. Native voice transport remains unchanged. Cross-account spatialization still needs listening tests.
- Game-native pickup and phone behavior remain subject to game integration and multiplayer validation. Source presence alone does not establish correctness.
- BepInEx, Unity library restoration and graphics driver behavior must be validated before installing into a primary game directory.

References: [Unity's manual XR initialization lifecycle](https://docs.unity3d.com/Packages/com.unity.xr.management@4.4/manual/EndUser.html), [Unity OpenXR manual](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.15/manual/index.html), [BepInEx development and references](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html).
