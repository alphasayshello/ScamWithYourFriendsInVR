# How to play

[Back to the project](../README.md)

## Before you launch

Connect your headset to the PC runtime first. For Quest through Steam Link, reach SteamVR before launching. Run one game client for normal headset testing. Face forward in a clear area and hold left stick click for about **0.65 seconds** to recenter. Vive wand users can open settings and choose **General → VR options → Recenter view** instead.

## Move and look

Move with the left stick and snap turn with the right stick. Your head controls where you look; the revised standing body follows head yaw separately from tracking-origin rotation. Physical steps move the native collision body, with a small allowance for leaning. Walls still block the body. Large tracking jumps trigger recalibration rather than a sudden body move.

Full stick/pad travel now activates native sprint in the direction you are moving; partial travel walks. Auto sprint is on by default and also drives the native fast-climb state on ladders. It releases when you ease off, open a menu or lose input focus. Change **General → VR options → Auto sprint** at any time, or set `[Input] AutoSprint = false` in the BepInEx config to leave ordinary PC sprint input in charge. `[Input] SprintThreshold` defaults to `0.85`; sprint stops `0.15` below the entry threshold so small stick movements do not keep toggling it. Recenter and action-button mappings are unchanged.

Standing room movement is disabled while seated or when native gameplay input is blocked. The revised alignment requires headset acceptance, including chairs, stairs and reconnects. Use `[Comfort] Seated = true` for seated height compensation; this is separate from sitting in an in-game chair.

## Menus and HUD

Aim the right controller at a button and press the trigger. Hold the trigger to drag a slider or scroll bar. Use the left stick vertically to scroll while pointing at UI; right-stick vertical scrolling also works. Let the stick return to centre afterward.

The laser stays visible in menus and computers. During normal play it appears for usable targets and hides when idle. Temporary controller capsules have been removed; the game's tracked avatar hands remain. UI aiming uses light adaptive smoothing while hands stay directly tracked. Set `UI.AlwaysShowPointer = true` for a permanent beam, or `UI.PointerSmoothing = 0` for direct aiming. Controller double clicks allow 0.4 seconds between presses by default (`UI.DoubleClickWindow`).

**Left X** opens players. **Left Y** or the controller menu backs out of players, exits a captured computer, or toggles settings. Floating menus/HUD follow your view at **0.9 metres** by default. Adjust `[UI] PanelDistance` and `PanelWidth` if needed.

Menu/back closes an open VR keyboard, feedback overlay or results panel before changing the menu behind it. Use a fresh press for each layer; dismissing results does not also open settings.

In the laptop's game manager, menu/back cancels an open confirmation before exiting the computer. Release the trigger and press again after a confirmation opens; an old held press cannot activate its Confirm button.

Caller Manager's search keeps its text and filtered results when the app rebuilds. Use the pointer to focus text fields and your PC keyboard for typing.

Character Creator keeps your in-progress editor when its window content refreshes, including drawing history and personality text. Starting a new caller still uses the game's normal new-draft action.

Laptop apps stay on the laptop model's screen in physical desktop mode. If the screen binding disappears, the overlay and its pointer input pause until that binding returns. The floating desktop option remains available in VR settings.

The laser stays visible while feedback or results are open, including when pointing away from the panel. Gameplay actions pause behind these overlays; a cancelled give-up hold needs a fresh press after closing them.

If an overlay opens during a button press or slider drag, that interaction is cancelled. Release the trigger before using the new overlay, then press again. Closing it does not resume the old drag or turn your next click into a double click.

Jump holds are cancelled when settings, players or feedback interrupt gameplay. Release A (right pad up on Vive/original WMR) and press again after returning. A jump pressed inside one of those menus also waits for a fresh press after it closes; keeping the button held does not turn into an unexpected jump.

After input focus or tracking is lost, release action buttons and centre the sticks before using them again. To grab again, release and squeeze grip. This prevents held controls from restarting actions when tracking returns.

### Vive wands and original Windows MR controllers

The bundled OpenXR controller names select these layouts automatically. Quest, Index, Vive Cosmos and WMR controllers with face buttons retain the face-button controls above. If your runtime reports a different controller name, set `[Input] ControllerLayout` in `BepInEx/config/com.swyf.vr.cfg` to `ViveWand` or `Wmr`, then restart. `Auto` is the default; `FaceButtons` forces the usual Quest-style layout. These alternatives pass simulated input checks; physical Vive/WMR testing remains outstanding.

| Action | Vive wand | Original WMR |
| --- | --- | --- |
| Move / scroll | Touch left pad without pressing | Left stick |
| Snap turn | Touch right pad left/right without pressing | Right stick left/right |
| Players / fart with left grip held | Click left pad | Click left pad |
| Jump / give up with left grip held | Click upper half of right pad | Click upper half of right pad |
| Context click / secondary action | Click lower half of right pad | Click lower half of right pad |
| Agent tackle, empty hands | Right grip + lower right pad | Right grip + lower right pad |
| Push-to-talk | Hold centre of right pad | Hold right stick click |
| Settings / back / exit computer | Controller menu | Controller menu |
| Store / retrieve (hold) | Left grip + left menu | Left grip + left menu |
| Recenter | Settings → General → VR options | Hold left stick click |

Trigger and grip keep their normal interaction/grab functions. A pad click keeps the action chosen when pressed until you release it. Vive pad presses pause movement/scrolling/turning so they do not also move you while using an action; lift your thumb or return the pad to centre before moving again. WMR keeps its separate thumbsticks available.

## Computers

1. Point at the computer's native interaction target and press the right trigger to enter it.
2. Use the right-controller laser on the desk monitor. Press trigger to click; use two quick presses for double-click actions.
3. Hold trigger and move the laser to drag windows or controls. Use the stick to scroll. Press right B for a context click, or hold it while moving the laser for right-mouse drags such as paint brush resizing.
4. Use your physical PC keyboard for text entry. The VR keyboard is disabled by default. No desk mouse grabbing or touch typing is required.
5. Press left Y / menu to leave the computer.

Point inside a browser webpage and use the stick to scroll that page. Browser toolbar controls and other apps retain ordinary UI scrolling. Native browser privacy approval and availability still apply.

In Paint, hold trigger over the canvas to draw. Use its Undo and Redo buttons to change a stroke, or hold right B and move sideways over the canvas to change brush size.

Open the colour swatch to drag the hue strip and saturation/value square, then close the picker to draw.

The gallery reopens saved art; Canvas starts a blank drawing. Duplicate Image makes another file, and Delete Image removes the selected artwork and selects a surviving one. Painting keeps the game's normal save behaviour. A physical PC keyboard is still needed for text and shortcuts.

From day five, use Paint’s Print button to send the current drawing to the floor-two printer. It stays busy until its output is collected. Leave the computer and pick up the printed drawing with right grip. While holding it, aim the right controller and press right B to attach it to a nearby flat surface with room for the whole page, or right trigger to throw it. You can pick up placed artwork again. Placement range, ownership and valid-surface rules still come from the game.

In Camera, click **Show** to open its controls. The shutter takes a picture from the desk webcam. Select a gallery thumbnail to view a saved picture, choose **Live** to return to the webcam, or use the trash button to delete the selected picture. Closing Camera stops its preview rendering.

In Zoomy, use the app's join control to enter the meeting. Point and press trigger to toggle microphone mute, deafen, chat or screen sharing. Deafen also mutes your microphone; turning deafen off restores the earlier mute state. Click the chat field, type with your PC keyboard and use Send. Screen sharing uses the native game capture. Closing Zoomy leaves the meeting and releases its webcam and screen preview. Meeting access still follows the game's unlock rules. Remote audio/video and headset use need further testing.
In Disscord, click a contact to open its conversation. Click Search and use your PC keyboard to filter contacts. Point inside message history and use the right stick to scroll; opening a conversation starts at its newest message. Offline contacts keep their history, with sending disabled. For an online contact, click the message field, type with your PC keyboard and click Send. Search and existing draft text survive window content rebuilds. Live-player delivery and headset use still need testing.

In Meteor Cookie, point at the large cookie and click the right trigger to earn cookies. Choose an upgrade category, then use its buy controls when you can afford it. Scroll inside the upgrade list with the right stick. Click a temporary bonus icon before it disappears to collect its native effect. Prices, unlocks, production and saved progress follow the game's normal rules.

In Scamazon, use the week buttons to preview the day-unlock pages. For **Hold to buy**, keep the trigger held until its progress completes; release early to cancel. Store access, prices and delivery still use the game's normal rules.

In Phone, use the language button to open its language list. The speaker icon mutes or restores caller audio; point at it to expose the volume slider, then hold trigger and drag the slider. The microphone icon toggles the game's phone-input mute when a recording device is available.

When a caller rings, point at the incoming call card's answer button and press trigger, or choose **Decline** to reject it. Answering opens the phone from that card; use **Hang Up** inside the phone to end the call. Older builds may show **Pick Up** directly inside the phone instead. Call ownership and permissions stay with the native game.

In AnyViewer, enter the fictional connection code supplied by your current caller, then click **Connect**. Double-click a desktop folder to open its files, and double-click a text file to open Notepad. Use the explorer arrows to go back, forward or up. The power/unplug button in the remote desktop taskbar disconnects. If another app covers a control, minimise that app first.

Remote photos have fit/fill controls. Audio and video use play/pause buttons, a seek bar and a volume slider. Point inside the audio window and use the stick to scroll to controls below the view. Remote windows stay within AnyViewer's usable desktop area, including when you maximise or restore it.

`[UI] PhysicalDesktop = true` keeps the UI attached to the original monitor by default. In settings → General → VR options, change COMPUTER SCREEN to FLOATING if you prefer a closer desktop. App licences, sign-in and game progression still follow the native game; opening a window does not guarantee that every workflow has passed VR testing.

## Interact and fight

Use right grip for native pickup/hold interactions, then release to drop. While holding an item, right trigger sends its native primary action, including food/tool use. Right B sends the native secondary action away from UI; hold and release it for actions that need a held button. With empty hands, right grip + trigger punches. Aim the right controller: both the hit ray and native punch animation target follow it, including the direction sent through the game’s normal animation command.

For charged weapons such as the shotgun and sniper, keep the right trigger held until the game fires. Release early to cancel charging. Opening settings or losing VR input focus cancels the charge too; release and press again to start a new shot. Shotgun spread follows the controller's orientation as well as its aim direction.

As an agent with empty hands, hold **right grip**, then press **right B** to request the native tackle/dive. Vive wand/original WMR hold **right grip**, then click **the lower right pad**. The game controls movement, animation and cooldown; one fresh press starts one attempt, and holding the button does not repeat it. Tackle moves along your headset's horizontal heading. Held items keep their normal secondary action, bare B/pad-down keeps native zoom/grab, and left grip + B/pad-down keeps hip swap.

Menus, focus loss and an obstructed headset prevent new tackles. Release and press again after returning. A dive already underway completes through the native motor; headset comfort and hit outcomes against other players/NPCs still need checking.

For a javelin, aim the right controller at a surface and press the right trigger. The missile target follows your controller instead of where your head is looking. The game's normal valid-target requirement and single-use behaviour still apply.

You can aim and fire with the controllers even when no PC mouse is connected. No extra setup is needed for this.

Physical controller movement supplies inherited release speed. Snap turns, rig repositioning and recentering no longer count as a hand swing; recentering keeps your grip-held item held. The game's normal drop impulse and velocity limit still apply.

Left trigger interacts with the world under your right-hand aim, so you can open a computer while holding an item. Like the native interact key, it can drop your held item when aimed at empty space. Left grip + left trigger is reserved for pee and does not also interact. Release either grip or trigger to end attack input. Opening a menu or pointing onto UI also cancels it. Right A jumps. Cooldowns, stamina, ownership and server rules still apply.

Left grip + left trigger holds the native pee action; release either control to stop. Settings, players, feedback and lost focus cancel it. Release the chord and press again after returning; a chord started inside a menu does not activate when the menu closes. Left grip + left X requests a fart, or the native cone boost when an eligible agent has a traffic cone in the hip slot. Left grip + right B swaps the agent hand/hip slots. Left grip + right trigger sends the native extra held-item action; releasing its modifier ends that action. Food-related poop effects retain the game's existing rules and jump triggers; no debug/cheat poop shortcut is enabled. Curry consumption and its timed poop/gassy effect pass simulated checks. VR jump now reaches the native jump event and activates the curry boost. Hip swap, nonfinal cone boost activation, cooldowns and effect spawning pass simulated checks. Full boost trajectories, final cone use/ragdoll, remaining item variants and full combat acceptance are still outstanding.

### Contraband storage

Hold **left grip, then left Y** to charge the native storage action. On Vive wands/original WMR, hold **left grip, then the left controller menu button** instead. With an eligible held item, the game stores it; with empty hands, the same hold retrieves stored items. Keep holding until the native charge completes. Capacity, eligible items and whether retrieval goes into your hand stay under the game's rules.

Release early to cancel. Menus, tracking/focus loss and session cleanup also cancel a hold; release and press again to resume. Releasing grip while keeping Y/menu held does not open settings or resume storage when you squeeze again. For normal settings/back, release left grip first; the right controller's menu remains a menu/back input.

### Food and drinks

Pick up a food or drink and press right trigger to use it. Settings block consumption. The game controls item removal and each drink's speed, drunk, water-stream or gassy effect; the mod keeps those native rules. Beer, burrito, coffee, coffee mug, coke, margarita, patron, water bottle and wine pass simulated use and expiry checks with shorter test durations. Repeated coffee uses the native stacking and speed cap. Enough alcohol triggers the normal pass-out and recovery sequence. VR queues the final drink's native consume command before pass-out can drop it. Coffee stacking and a two-Patron pass-out/recovery pass simulated checks; authored durations, other combinations and headset feel still need checking.

## Handheld polaroid

Pick up the camera, move and rotate it with your right controller, then press right trigger to take a picture. Right B switches between normal and selfie mode using the game's authored camera grip. Its normal photo cooldown still applies. Settings block both actions. The local head appears in the photo render, then returns to its hidden first-person state; it does not stay visible around the headset.

Controller shutter, native printed photo pixels, camera movement, selfie switching and cleanup/recovery pass simulated checks. Headset handling and how other players see the resulting photos still need checking.

## Ladders

While holding an extension ladder, press right trigger to extend or retract it. Drop it, point at its climb interaction and press right trigger to mount. Use the left stick up/down to climb and **right A** to dismount. Vive/original WMR use right pad up instead of A; the HUD shows your controller controls.

Head tracking stays free while the native ladder controls your body's position. Physical leaning or turning does not walk or rotate that body off the ladder. Settings and lost focus stop further stick input; the native climb motor still decelerates normally. A jump held across a menu does not automatically dismount when it closes. After dismount or ladder removal, centre both sticks and release jump before walking, jumping or snap-turning; the HUD prompts you while a control remains held.

Simulated checks cover the extension ladder, input recovery and occupied ladder disable/removal. Other ladder variants, occupied retraction, remote clients and headset handling still need checking.

## FPV flight

Hold an eligible FPV aircraft and press **right trigger** to launch. The left stick applies movement, the right stick steers yaw/pitch, and holding **left trigger** adds upward thrust, leaving both thumbs free for the sticks. Release the trigger to stop that extra thrust. Right A also works as an alternative. Vive wands use pad touch for movement/steering; Vive and original WMR can also use right pad up for climb. The HUD shows the current controller keys.

Your view follows the aircraft camera with upright head tracking. Looking around does not steer the aircraft. Your grounded avatar's tracked pose pauses during flight and resumes afterward. Native crashes/explosions end flight under the game's normal rules; nearby blasts can still ragdoll the pilot.

When flight ends, centre both sticks and release climb before walking, jumping or snap-turning. The HUD prompts you if a flight control is still held. This prevents a held steering/climb input from becoming an accidental ground action.

Settings, feedback and lost VR focus stop flight input. Release and press climb again after returning; the aircraft's existing physics continue. Occupied aircraft/bag geometry does not block the menu laser, including with menu occlusion enabled. Adjust **General → VR options → FPV steering** with the laser. The default is 90 degrees per second; `[Input] FlightSteeringSpeed` in the BepInEx config also accepts 15–180. Headset crash comfort, other aircraft and remote-client flight still need testing.

## Ragdoll and recovery

To climb into a hideable bag, hold it and keep **left grip + right trigger** held through its native charge. Use the left stick to move the occupied bag and **right A** to exit; the normal short exit ragdoll still applies. Vive wands use the left pad for movement, and Vive/original WMR use right pad up to exit. The HUD shows these controls while you are inside. Settings and lost VR focus stop further movement input. The view follows the bag's camera marker, or its authored centre when that marker is absent.

Right A also sends the native struggle/escape action when ragdolled or hiding. To use the native give-up/unstuck action, hold left grip **before** pressing and holding right A. Its normal game timer and recovery, respawn or jail rules still apply. Release either control to cancel. Menus, focus loss and VR shutdown also release the hold; closing a menu while the buttons remain held does not restart it.

If a ragdoll camera marker is missing from the player prefab, the view follows its physical head, with native chest/hips as a backup. Basic simulated ragdoll/recovery checks pass; violent impacts, completed give-up outcomes, hiding variants and real-headset comfort still need acceptance.

## Voice chat

For push-to-talk, select that voice mode and your input device in the game's audio settings, then **hold right stick click** while speaking. Vive wand users **hold a press at the centre of the right pad**. Release to stop. Open-mic/voice-activity mode and mute remain under the game's settings; the mod does not switch your selected voice mode or microphone.

Voice works while using the desk computer. Settings, lost focus/tracking and session cleanup cancel a held VR voice input; release and press again afterward. Mute always wins, and unmuting while still holding the button requires a fresh press. The native keyboard/gamepad voice controls keep their state independently. The VR gesture is held input, even if native gamepad toggle-talk is enabled.

On Vive, a centre press keeps voice ownership until released, even if your thumb drifts. Use a fresh upper/lower pad press for jump/secondary actions. Software checks cover native voice state with audio samples suppressed; actual headset microphone quality and other players hearing you still need testing.

## Play with friends

The host must have the mod to relay poses. Modded PC players use `Mode = PC`; VR players use `Mode = VR`. Both are in `BepInEx/config/com.swyf.vr.cfg`, under `[General]`. Restart after changing mode. Use the game's normal lobby/invite flow. [Read multiplayer details](MULTIPLAYER.md).
