# third-party dependencies

No game source, game assets or game DLLs are included in the source distribution. Local assembly inspection was used to select reflection targets; the generated mod code is original implementation code.

| component | version/source | license information |
| --- | --- | --- |
| BepInEx | 5.4.23.5, BepInEx/BepInEx | [LGPL-2.1](https://github.com/BepInEx/BepInEx/blob/master/LICENSE) and bundled dependencies' notices |
| HarmonyX | supplied by BepInEx | [HarmonyX license](https://github.com/BepInEx/HarmonyX/blob/master/LICENSE) |
| Unity engine/Mono player libraries | locally provided exact Unity editor | Unity editor/runtime and bundled Mono terms; not covered by the mod's MIT license |
| Unity Input System | 1.19.0 / 1.20.0 official Unity package, selected by game target | package `LICENSE.md` and `Third Party Notices.md` |
| Unity XR Management | 4.4.0 official Unity package | package `LICENSE.md` |
| Unity OpenXR | 1.15.1 official Unity package | package `LICENSE.md` and third-party notices, including the native loader |

Preparation pins archive hashes in `tools/dependencies.lock.json`. It downloads dependency material into an ignored local work directory. The staged runtime contains binaries provided or built locally from these dependencies; review their licenses before distributing that folder. The repository's MIT license applies only to its own source and tooling.
