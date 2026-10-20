# What works and what needs work

**Status: development preview.** The mod is playable, but it is not finished or production ready yet.

## Working

- Quest 3 headset/controller tracking through Steam Link and SteamVR.
- The general VR view, controller laser and main-menu experience.
- Floating menus and a HUD that follow your view.
- Readable settings controls, with controller clicking and scrolling.
- PC mode and tracked-avatar support for modded players.
- The Visual Studio project, build tools, setup/play guides, Git repository and MIT license.
- A setup command that creates a separate modded game copy with verified files and PC/VR launchers.

## Changed, needs another headset check

- The updated Steam game now passes simulated startup, pointer, computer/settings, grip, native NPC damage and rig checks with matching dependencies. Older game copies keep their separate dependency profile.

- Temporary controller capsules (the bean shapes) are removed. The laser now hides during idle gameplay and uses steadier UI aiming, a smaller distance-scaled reticle and more forgiving double clicks. Its feel still needs a headset check.
- Punch input now stops when grip is released, even if trigger stays down. Native unarmed firing, controller aim, cooldown rejection and nonlethal NPC damage have been checked. Knockback now uses controller direction too; PvP, knockouts and other weapons still need checking.
- Left trigger provides world interaction while holding items, separate from the pee modifier. Held-item trigger and secondary actions now use native callbacks, covering food/tool use that weapon-only input skipped. Curry consumption, networked poop/gassy effects and normal expiry work in simulation. Native jump-driven curry boost activation, agent hip swap, extra item-action release and nonfinal cone activation/cooldown also pass simulated checks; trajectories, final cone use and other foods still need checking.
- Ordinary item pickup, release, quick-release pickup and cancellation work in the simulated host. Release velocity now follows physical controller movement instead of rig translation/snap rotation, and recentering resets its motion sample. Native held ownership survives recenter and a stationary release sends zero inherited velocity. Every item type and throwing feel still need checking.
- Native ragdoll entry/recovery, head-position fallback, tracked-pose release/resume, struggle and short give-up cancellation pass simulated checks. Holding left grip before right A starts the native give-up hold; full consequences and headset feel still need checking.
- Tracking reads and UI scans do less repeated work, destroyed menu graphics release their owned material entries, and detached app trees release their saved corner styles. Smooth performance in busy scenes still needs more work.

- Computer screens now stay attached to their desk monitors and follow screen movement and renderer rebinding. An optional floating desktop is available. Clicking, dragging, scrolling and window controls have been checked, but the revised computer experience still needs headset testing.
- A locally rendered browser page passes controller clicking, dragging, off-page release, thumbstick scrolling, focus cancellation, context-click and close-during-drag checks. Closing or hiding a pressed control now cancels its controller press. Live websites, text entry and headset usability still need testing.
- The browser gets more startup time under VR load. New game-copy installs start with a fresh browser cache.
- Paint passes simulated controller drawing, saved-image, undo/redo, held right-B brush resizing and focus-cancellation checks. Printing, artwork browsing, clipboard actions, text entry and headset usability still need checking.
- Right-B UI drags now stay held until B is released, and switching between trigger and B no longer creates accidental double clicks.
- Head turns now drive the local model, with body turning kept separate from view rotation.
- VR options inside General settings provide larger controls for recentering, desktop placement, seated comfort, menu distance, aiming, laser visibility, turning and scene resolution.
- Camera ownership now follows the spectated player; tracked arms release control during ragdoll, hiding and spectating. These transitions still need full gameplay checking.
- Moving around your room now moves the collision body while keeping the headset aligned. Leaning, walls, stairs and in-game chairs need more checking.
- The computer camera app uses the desk webcam independently of the headset camera. Its shutter, saved photo pixels, gallery/Live switching, photo deletion and preview shutdown pass simulated checks. Watermarks, clipboard sharing and headset usability still need checking.
- Scamazon week navigation and controller hold-to-buy input pass simulated checks, including early release and focus cancellation. Spending, delivery, purchased-store tabs and actual purchase outcomes still need checking.
- Phone language-popup selection, caller-audio mute/unmute, volume dragging and phone-input mute pass simulated checks with an available PC microphone. Real native networked callers also pass controller decline, answer/hang-up and talker/workspace cleanup checks with AI/TTS suppressed. Transfers, live dialogue, speech recognition, other languages and headset audio still need checking.
- Remote voice position follows the tracked head, but how that sounds to other players still needs checking.

## Not finished

- Every computer app and every action inside those apps.
- Combat damage against players, charged/ranged weapons, other food variants, boost trajectories/final cone use and all held-item variants in VR.
- Combat input reliability needs more checking after an intermittent simulated hold-input failure; a repeat passed, but its cause is unresolved.
- Complete multiplayer checks across separate Steam accounts, including reconnecting and joining late.
- Consistently smooth performance, especially in busy scenes.
- Violent ragdoll/final cone impacts, completed give-up outcomes, hiding variants, spectating, tracking loss and every seated/standing transition.
- Headset/controller compatibility beyond the setup already tried.
- Headset acceptance of the updated Steam game using Unity 6000.3.26f1 and Input System 1.20.0.

## Not included right now

- Grabbing the desk mouse or touch typing on the desk keyboard. That experiment was removed.
- A VR keyboard by default. Use a physical PC keyboard for text entry; the optional floating keyboard remains experimental and disabled.
- A standalone Quest app or an IL2CPP version.
- The separate general game optimization mod. That comes after the VR mod is finished.

[Back to the project](../README.md) · [How to play](HOW-TO-PLAY.md)
