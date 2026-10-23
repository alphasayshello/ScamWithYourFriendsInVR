# Troubleshooting

[Back to the project](../README.md)

| Symptom | What to check |
| --- | --- |
| Flat screen / no VR session | Connect the headset first; confirm the active OpenXR runtime, `Mode = VR`, complete staged runtime and `-force-d3d11`. Read the startup log. |
| SteamVR says another app is launching | Close duplicate game/Home windows. If the transition remains stuck, close the game, restart SteamVR, reconnect Steam Link, then launch once. |
| Launcher reports that the game closed during startup | The launcher now watches for an immediate exit and includes its exit code. Keep Steam running and signed in, resolve any game/Steam error and retry. A successful file check alone does not establish that the game can start. |
| Computer is awkward to reach | Set `UI.PhysicalDesktop = false`; enter the computer normally, then point at the floating desktop. |
| Computer screen briefly disappears | Desk mode waits for the native monitor binding to return instead of putting a clickable screen in front of your face. Exit/re-enter the computer if it stays missing; select **FLOATING** in VR General settings if needed. |
| A VR keyboard obscures the screen | Set `UI.VirtualKeyboard = false`, restart, and use the PC keyboard. |
| UI is too far / too small | Adjust `UI.PanelDistance` and `PanelWidth`. The shipped distance is 0.9m. |
| Body drifts after moving in your room | Hold left stick click to recenter. The revised body-follow path needs headset checks; report whether standing, seated or in a native chair. |
| Height is wrong | Recenter while at your intended playing height; check seated mode and runtime floor calibration. |
| Low frame rate | Run one client, lower SteamVR render resolution or mod scene render scale, and keep the webcam cap modest. Record frame samples; performance acceptance remains open. |
| Others cannot see tracked hands | Host and observing PC need the mod, pose sync enabled and compatible builds. PC mode does not start XR. |
| Game breaks after an update | Compare its Unity/runtime build with the supported target. Restore the backup rather than mixing engine versions. |

Report headset/runtime, game build, mod revision, steps, expected/actual behaviour and relevant log excerpts. Remove account tokens, lobby codes and personal information before sharing logs. Do not publish entire unreviewed player logs.
