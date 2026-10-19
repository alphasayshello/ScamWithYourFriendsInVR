# What works and what needs work

**Status: development preview.** The mod is playable, but it is not finished or production ready yet.

## Working

- Quest 3 headset/controller tracking through Steam Link and SteamVR.
- The general VR view, controller laser and main-menu experience.
- Floating menus and a HUD that follow your view.
- Readable settings controls, with controller clicking and scrolling.
- PC mode and tracked-avatar support for modded players.
- The Visual Studio project, build tools, setup/play guides, Git repository and MIT license.

## Changed, needs another headset check

- Temporary controller capsules (the bean shapes) are removed. The laser now hides during idle gameplay and uses steadier UI aiming, a smaller distance-scaled reticle and more forgiving double clicks. Its feel still needs a headset check.
- Tracking reads and UI scans do less repeated work, and destroyed menu graphics release their owned material entries. Smooth performance in busy scenes still needs more work.

- Computer screens now stay attached to their desk monitors and use the controller pointer. An optional floating desktop is available. Clicking, dragging, scrolling and window controls have been checked, but the revised computer experience still needs headset testing.
- Head turns now drive the local model, with body turning kept separate from view rotation.
- VR options inside General settings provide larger controls for recentering, desktop placement, seated comfort, menu distance, aiming, laser visibility, turning and scene resolution.
- Camera ownership now follows the spectated player; tracked arms release control during ragdoll, hiding and spectating. These transitions still need full gameplay checking.
- Moving around your room now moves the collision body while keeping the headset aligned. Leaning, walls, stairs and in-game chairs need more checking.
- The computer camera app uses a separate webcam view instead of taking over the headset camera.
- Remote voice position follows the tracked head, but how that sounds to other players still needs checking.

## Not finished

- Every computer app and every action inside those apps.
- Full combat, punch damage/cooldowns, food/poop effects and all held-item interactions in VR.
- Complete multiplayer checks across separate Steam accounts, including reconnecting and joining late.
- Consistently smooth performance, especially in busy scenes.
- Ragdoll, spectating, tracking loss and every seated/standing transition.
- Headset/controller compatibility beyond the setup already tried.
- The updated Steam game using Unity 6000.3.26f1. This preview requires 6000.3.10f1; setup refuses mismatched versions.

## Not included right now

- Grabbing the desk mouse or touch typing on the desk keyboard. That experiment was removed.
- A VR keyboard by default. Use a physical PC keyboard for text entry; the optional floating keyboard remains experimental and disabled.
- A standalone Quest app or an IL2CPP version.
- The separate general game optimization mod. That comes after the VR mod is finished.

[Back to the project](../README.md) · [How to play](HOW-TO-PLAY.md)
