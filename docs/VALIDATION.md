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
- Native shotgun/sniper pickup, charging, early release, settings/focus cancellation, controller-origin shots and cooldown rejection pass simulated checks. Shotgun spread uses the controller's up/right axes, including when aiming upward. Both weapons also hit a native shooting-target fixture through the normal authority/feedback path, accept one hit per shot and restore the target afterward. Player/NPC damage from these weapons, other weapons and headset handling still need checking.
- Native weapon firing also passes with no PC mouse registered in the game's input system. VR supplies a compatibility device when needed; simulated checks cover one-device ownership, inactive-VR guarding, cleanup/recovery and restoration of the original mouse objects.
- Javelin targeting now follows the right controller for the locally held item. Simulated checks cover the selected surface, one native trigger launch, the target sent to the authority command, synchronized fired state and normal held-item release. Inactive VR, lost focus/tracking and another unheld item retain camera targeting. Missile flight, explosions, recoil feel and the multi-missile variant still need checking.
- Left trigger provides world interaction while holding items, separate from the pee modifier. Held-item trigger and secondary actions now use native callbacks, covering food/tool use that weapon-only input skipped. Curry consumption, networked poop/gassy effects and normal expiry work in simulation. Native jump-driven curry boost activation, agent hip swap, extra item-action release and nonfinal cone activation/cooldown also pass simulated checks; trajectories, final cone use and other foods still need checking.
- Ordinary item pickup, release, quick-release pickup and cancellation work in the simulated host. Release velocity now follows physical controller movement instead of rig translation/snap rotation, and recentering resets its motion sample. Native held ownership survives recenter and a stationary release sends zero inherited velocity. Every item type and throwing feel still need checking.
- Native ragdoll entry/recovery, head-position fallback, tracked-pose release/resume, struggle and short give-up cancellation pass simulated checks. Holding left grip before right A starts the native give-up hold; full consequences and headset feel still need checking.
- Tracking reads and UI scans do less repeated work, floating/native UI occlusion shares one physics query, destroyed menu graphics release their owned material entries, and detached app trees release their saved corner styles. Smooth performance in busy scenes still needs more work.
- Discovery of texture-backed world panels now spreads renderer checks across frames, keeps active document targets together per batch, and drops unfinished scans on scene changes or shutdown. Mock profiling showed smaller individual discovery spikes with a small average CPU-cost increase; overall frame times were essentially unchanged. This is a hitch-reduction change, not a smooth-performance or headset frame-rate guarantee.

- Computer screens now stay attached to their desk monitors and follow screen movement and renderer rebinding. An optional floating desktop is available. Clicking, dragging, scrolling and window controls have been checked, but the revised computer experience still needs headset testing.
- A locally rendered browser page passes controller clicking, dragging, off-page release, thumbstick scrolling, focus cancellation, context-click and close-during-drag checks. Closing or hiding a pressed control now cancels its controller press. Live websites, text entry and headset usability still need testing.
- The browser gets more startup time under VR load. New game-copy installs start with a fresh browser cache.
- Paint passes simulated controller drawing, saved-image, undo/redo, held right-B brush resizing and focus-cancellation checks. Printing, artwork browsing, clipboard actions, text entry and headset usability still need checking.
- Right-B UI drags now stay held until B is released, and switching between trigger and B no longer creates accidental double clicks.
- Selected uGUI controls now receive the per-frame updates used for physical keyboard editing, with input paused when XR focus is lost. Actual physical typing, keyboard layouts and IME still need checking.
- Eighteen native scam-app windows pass controller window controls. Four enabled forms also pass controller focus, scrolling to clipped controls and empty-submit validation. Native notification dismissal works. Successful completion across the remaining apps and dialogue-specific controls still need checking.
- Car registration, credit card, gift card and identity pass synthetic Unity key entry, native server mismatch/success responses, verified-field locking and duplicate-request protection. The three dialogue-gated forms reject entries before disclosure; validated synthetic caller turns then grant their allowed fictional disclosure through native logic. Each completed form calculates its reward and requests payment once; saved money and medals are excluded from the test. Other successful forms, Windows keyboard input and live AI/voice dialogue still need checking.
- AnyViewer passes native wrong-code rejection, connection to a fictional caller desktop, controller folder/file double-clicks, explorer back/forward, text editing/reopening, disconnect and reconnect without a second reward request. Generated click events retain the VR pointer's double-click count. Remote windows fit above their taskbar when AnyViewer is maximised/restored, and the audio app scrolls to its volume controls.
- Remote bundled/generated photos pass image decoding and fit/fill controls. Bundled audio/video pass native playback, controller pause/resume, held seeking, volume changes and playback cleanup on close. Test audio is muted. Other media formats, photo previous/next, audible output, saved payouts, live dialogue and headset usability still need checking.
- Tic-Tac-Toe passes a complete native solo round with controller moves, opponent replies, a locked result and New Game reset. Multiplayer search reaches the native server's waiting state and cancels back to idle. Matched games, rematches and headset usability still need checking.
- Head turns now drive the local model, with body turning kept separate from view rotation.
- Eye calibration rejects displaced mesh bounds and keeps a sensible body-relative eye offset while native camera smoothing catches up after a teleport. Ragdoll head fallback now uses the current role's chest hierarchy. Long teleports, role changes and headset comfort still need more checking.
- VR options inside General settings provide larger controls for recentering, desktop placement, seated comfort, menu distance, aiming, laser visibility, turning and scene resolution.
- Controller menu/back now dismisses native results and feedback overlays before changing underlying settings, players or computer state. Simulated checks cover results dismissal, feedback over settings, feedback over results and native same-frame escape consumption. Feedback submission and full results-screen usability are not covered.
- Results and feedback keep the controller laser visible even when it misses the panel. Their modal state also blocks world-pointer actions and body actions, including cancelling a held give-up charge without restarting it on close. Simulated checks also confirm movement, jumping and native pee/fart bindings are blocked by these screens. Native ragdoll checks cover give-up cancellation and suppressed struggle input; headset feel still needs checking.
- Opening or closing the top results/feedback overlay now cancels the previous UI press/drag and clears its click history. Held trigger/context input waits for release before acting in the new layer. Native-panel probes cover held buttons/sliders in settings and results, cancellation without activating or moving the old control, and recovery with fresh single clicks. Other widget types and headset ergonomics still need checking.
- Fresh trigger presses aimed at controls behind an open feedback overlay also leave those underlying buttons/sliders unchanged. This has been checked separately from held-input cancellation; other modal combinations still need checking.
- Focus/head/controller recovery now requires released action buttons and centred sticks, preventing automatic firing, grabs, jumps, menu toggles or snap turns from held input. Simulated input frames cover analog release thresholds, unavailable control values, connected menu access during pose loss and fresh-input recovery. Real SteamVR overlay and tracking-loss feel still need checking.
- The fart/players choice stays with the fresh left-X press. Releasing or adding its grip modifier while X stays held no longer opens players or starts another action.
- The office-recovery HUD shows the controller give-up combo, including right-pad alternatives. Players/settings/recovery captions restore their original PC keys when XR stops or the HUD is replaced, while preserving changed bindings and captions another owner has changed. Headset readability still needs checking.
- Vive wand and original WMR layouts now provide pad alternatives for players, fart, jump/give-up and secondary/context-click actions. Pad presses keep their original action until release; Vive action clicks suppress simultaneous movement/turning, and WMR retains its separate sticks. HUD hints follow the selected layout. Automatic profile-name detection and a configuration override are included. Physical controllers and headset usability still need checking.
- Camera ownership now follows the spectated player; tracked arms release control during ragdoll, hiding and spectating. These transitions still need full gameplay checking.
- Moving around your room now moves the collision body while keeping the headset aligned. Leaning, walls, stairs and in-game chairs need more checking.
- The computer camera app uses the desk webcam independently of the headset camera. Its shutter, saved photo pixels, gallery/Live switching, photo deletion and preview shutdown pass simulated checks. Watermarks, clipboard sharing and headset usability still need checking.
- Banditcam passes simulated live preview, controller record/stop, native video encoding/finalization, nonblank gallery playback, pause/play, seeking, deletion and preview release on close. Seek dragging now reaches the app before the slider consumes the event, and cancels on focus/readiness loss. Test recordings use muted audio, with watermarks and clipboard copying disabled. Real audio, automatic call recording, sharing and headset usability still need checking.
- Scamazon week navigation and controller hold-to-buy input pass simulated checks, including early release and focus cancellation. Spending, delivery, purchased-store tabs and actual purchase outcomes still need checking.
- Phone language-popup selection, caller-audio mute/unmute, volume dragging and phone-input mute pass simulated checks with an available PC microphone. Real native networked callers also pass controller decline, answer/hang-up and talker/workspace cleanup checks with AI/TTS and automatic recording suppressed for test callers. Transfers, recording, live dialogue, speech recognition, other languages and headset audio still need checking.
- Remote voice position follows the tracked head, but how that sounds to other players still needs checking.

## Not finished

- Every computer app and every action inside those apps.
- Combat damage against players, charged/ranged hit outcomes and remaining weapons, other food variants, boost trajectories/final cone use and all held-item variants in VR.
- Headset combat feel and player-versus-player reliability still need checking. Controller attacks are rejected while the head is inside world geometry.
- Complete multiplayer checks across separate Steam accounts, including reconnecting and joining late.
- Consistently smooth performance, especially in busy scenes.
- Violent ragdoll/final cone impacts, completed give-up outcomes, hiding variants, spectating, tracking loss and every seated/standing transition.
- Headset/controller compatibility beyond the setup already tried.
- Hardware verification of the new Vive wand/original WMR alternate controls, including runtime-reported controller names, touchpad behaviour and menu availability.
- Headset acceptance of the updated Steam game using Unity 6000.3.26f1 and Input System 1.20.0.

## Not included right now

- Grabbing the desk mouse or touch typing on the desk keyboard. That experiment was removed.
- A VR keyboard by default. Use a physical PC keyboard for text entry; the optional floating keyboard remains experimental and disabled.
- A standalone Quest app or an IL2CPP version.
- The separate general game optimization mod. That comes after the VR mod is finished.

[Back to the project](../README.md) · [How to play](HOW-TO-PLAY.md)
