# Contributing

Start with the [build guide](docs/BUILD.md) and [validation record](docs/VALIDATION.md). Keep changes focused and preserve native game authority, ownership and cooldowns. Do not redistribute game code/assets or add machine paths, credentials or dependency binaries to Git.

For a change, describe the problem, resulting behaviour, supported game/runtime version, tests and remaining limits. Exercise cleanup on disable, focus/tracking loss and scene changes. A mock pass verifies only the behaviour it covers; headset and multiplayer claims need matching evidence.

Use the existing C# style and conditional reflection/Harmony integration. Update player-facing controls and setup docs when defaults change. Keep diagnostics and optional test plugins out of normal gameplay flows.

Original contributions use the repository's [MIT license](LICENSE); third-party material retains its own terms.
