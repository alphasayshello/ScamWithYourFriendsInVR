# Build from source

[Back to the project](../README.md)

## Tools

- Windows x64, PowerShell 7 and the .NET 10 SDK.
- An unmodified copy of the supported Mono game installed locally. Do not prepare or stage against an already modified VR copy.
- An editor matching the target game exactly, with Windows Mono player support. An extracted editor works. Supported pairs are **6000.3.26f1 (de8167e7440c)** with Input System **1.20.0**, and legacy **6000.3.10f1 (e35f0c77bd8e)** with Input System **1.19.0**.
- Visual Studio 2026 with .NET desktop development, if using the IDE.

## Build and install together

For a new playable copy, [Setup-Mod.ps1](../tools/Setup-Mod.ps1) runs preparation, Release build, staging and installation in one command. [Setup instructions](GETTING-STARTED.md). It requires the tools above and a matching editor, and leaves the original game untouched. The separate commands below remain useful for development or staging without installation.

## Prepare and build

From the repository root:

```powershell
./tools/Prepare-Dependencies.ps1 -UnityEditorDir 'C:\Program Files\Unity\Hub\Editor\6000.3.26f1'
dotnet build ScamWithYourFriendsInVR.sln -c Release
```

For another Steam library, pass `-GameDir 'D:\SteamLibrary\steamapps\common\Scam With Your Friends'` to preparation. It selects the matching dependency lock, verifies the native Input System version and pinned hashes, and prepares BepInEx 5.4.23.5, the matching Input System, XR Management 4.4.0 and OpenXR 1.15.1. Default caches are separate for each engine version. When overriding `-CacheDir`, use a different folder per target. `Local.Build.props` contains machine-specific paths and is ignored by Git.

Open `ScamWithYourFriendsInVR.sln` in Visual Studio, select **Release | Any CPU**, then **Build Solution**. Runtime files appear in `bin/Release/netstandard2.1` for the currently prepared target. Re-run preparation and rebuild when switching game versions. F5 does not launch a standalone game.

## Stage

Choose a destination that does not exist:

```powershell
./tools/Stage-Mod.ps1 -Destination 'D:\staging\ScamWithYourFriendsInVR'
```

Staging assembles the required engine/BCL restoration, providers, BepInEx and mod plugins, and writes a file-hash manifest and exact engine identity. It rejects a provider built against the wrong Input System version. Use `tools/Install-GameCopy.ps1` to create an isolated game copy with PC/VR launchers. See [installation](GETTING-STARTED.md) and [runtime architecture](XR-RUNTIME.md).

The source archive contains no game DLLs or game assets. The locally staged runtime contains third-party libraries; review their licences before redistributing it. MIT applies to this project's own code, not those dependencies.

## Setup workflow regression checks

```powershell
./tools/tests/Workflow.Tests.ps1
```

These use temporary non-executable fixtures to check workflow ordering, paths with spaces, existing/nested destinations, input/cache isolation, linked-cache rejection, active-game/SDK checks and stopping after build failure. They do not launch a game or download dependencies.

## Setup checker regression checks

```powershell
./tools/tests/Setup.Tests.ps1 -GameDir 'D:\Games\SWYF-VR'
./tools/tests/Install.Tests.ps1 -GameDir 'C:\Program Files (x86)\Steam\steamapps\common\Scam With Your Friends' -StageDir 'D:\staging\ScamWithYourFriendsInVR'
```

Use a compatible game copy. The setup checks verify missing/corrupted files, manifest containment and runtime selection. The installation checks use a tiny game fixture and the full stage to verify copy isolation, engine matching, generated launchers and invalid inputs. Both clean their temporary fixtures without launching a game.

## Tests and source export

Optional in-game checks live in [tools/smoke-tests](../tools/smoke-tests/README.md). Mock checks are not hardware or production certification. Remove the optional smoke-test plugin before normal headset runs.

Run `./tools/tests/AtomicOutput.Tests.ps1` on Windows with PowerShell 7 to check staging output replacement. Temporary sharing locks retry briefly; a failed writer or persistent lock preserves the previous file and cleans up its temporary output.

```powershell
./tools/Export-Source.ps1 -Destination 'D:\exports\ScamWithYourFriendsInVR-source.zip'
```

Use a new archive path. The exporter excludes dependencies, machine paths, build outputs and Git metadata.
