# Build from source

[Back to the project](../README.md)

## Tools

- Windows x64, PowerShell 7 and the .NET 10 SDK.
- An unmodified copy of the supported Mono game installed locally. Do not prepare or stage against an already modified VR copy.
- The exact **Unity 6000.3.10f1 (e35f0c77bd8e)** editor with Windows Mono player support. An extracted editor works. A newer engine is not a substitute.
- Visual Studio 2026 with .NET desktop development, if using the IDE.

## Prepare and build

From the repository root:

```powershell
./tools/Prepare-Dependencies.ps1 -UnityEditorDir 'C:\Program Files\Unity\Hub\Editor\6000.3.10f1'
dotnet build ScamWithYourFriendsInVR.sln -c Release
```

For another Steam library, pass `-GameDir 'D:\SteamLibrary\steamapps\common\Scam With Your Friends'` to preparation. It verifies pinned hashes and prepares BepInEx 5.4.23.5, Input System 1.19.0, XR Management 4.4.0 and OpenXR 1.15.1. `Local.Build.props` contains machine-specific paths and is ignored by Git.

Open `ScamWithYourFriendsInVR.sln` in Visual Studio, select **Release | Any CPU**, then **Build Solution**. Runtime files appear in `bin/Release/netstandard2.1`. F5 does not launch a standalone game.

## Stage

Choose a destination that does not exist:

```powershell
./tools/Stage-Mod.ps1 -Destination 'D:\staging\ScamWithYourFriendsInVR'
```

Staging assembles the required engine/BCL restoration, providers, BepInEx and mod plugins, and writes a file-hash manifest. Test in an isolated game copy. See [installation](GETTING-STARTED.md) and [runtime architecture](XR-RUNTIME.md).

The source archive contains no game DLLs or game assets. The locally staged runtime contains third-party libraries; review their licences before redistributing it. MIT applies to this project's own code, not those dependencies.

## Setup checker regression checks

```powershell
./tools/tests/Setup.Tests.ps1 -GameDir 'D:\Games\SWYF-VR'
```

Use a compatible game copy. These checks create a temporary fixture using its UnityPlayer version resource and verify missing/corrupted files, manifest path containment and runtime selection without launching a game.

## Tests and source export

Optional in-game checks live in [tools/smoke-tests](../tools/smoke-tests/README.md). Mock checks are not hardware or production certification. Remove the optional smoke-test plugin before normal headset runs.

```powershell
./tools/Export-Source.ps1 -Destination 'D:\exports\ScamWithYourFriendsInVR-source.zip'
```

Use a new archive path. The exporter excludes dependencies, machine paths, build outputs and Git metadata.
