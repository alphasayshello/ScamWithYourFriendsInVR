# How to play

[Back to the project](../README.md)

## Before you launch

Connect your headset to the PC runtime first. For Quest through Steam Link, reach SteamVR before launching. Run one game client for normal headset testing. Face forward in a clear area and hold left stick click for about **0.65 seconds** to recenter. Vive wand users can open settings and choose **General → VR options → Recenter view** instead.

## Move and look

Move with the left stick and snap turn with the right stick. Your head controls where you look; the revised standing body follows head yaw separately from tracking-origin rotation. Physical steps move the native collision body, with a small allowance for leaning. Walls still block the body. Large tracking jumps trigger recalibration rather than a sudden body move.

Standing room movement is disabled while seated or when native gameplay input is blocked. The revised alignment requires headset acceptance, including chairs, stairs and reconnects. Use `[Comfort] Seated = true` for seated height compensation; this is separate from sitting in an in-game chair.

## Menus and HUD

Aim the right controller at a button and press the trigger. Hold the trigger to drag a slider or scroll bar. Use the left stick vertically to scroll while pointing at UI; right-stick vertical scrolling also works. Let the stick return to centre afterward.

The laser stays visible in menus and computers. During normal play it appears for usable targets and hides when idle. Temporary controller capsules have been removed; the game's tracked avatar hands remain. UI aiming uses light adaptive smoothing while hands stay directly tracked. Set `UI.AlwaysShowPointer = true` for a permanent beam, or `UI.PointerSmoothing = 0` for direct aiming. Controller double clicks allow 0.4 seconds between presses by default (`UI.DoubleClickWindow`).

**Left X** opens players. **Left Y** or the controller menu backs out of players, exits a captured computer, or toggles settings. Floating menus/HUD follow your view at **0.9 metres** by default. Adjust `[UI] PanelDistance` and `PanelWidth` if needed.

Menu/back closes an open VR keyboard, feedback overlay or results panel before changing the menu behind it. Use a fresh press for each layer; dismissing results does not also open settings.

The laser stays visible while feedback or results are open, including when pointing away from the panel. Gameplay actions pause behind these overlays; a cancelled give-up hold needs a fresh press after closing them.

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
| Settings / back / exit computer | Controller menu | Controller menu |
| Recenter | Settings → General → VR options | Hold left stick click |

Trigger and grip keep their normal interaction/grab functions. A pad click keeps the action chosen when pressed until you release it. Vive pad presses pause movement/scrolling/turning so they do not also move you while using an action; lift your thumb or return the pad to centre before moving again. WMR keeps its separate thumbsticks available.

## Computers

1. Point at the computer's native interaction target and press the right trigger to enter it.
2. Use the right-controller laser on the desk monitor. Press trigger to click; use two quick presses for double-click actions.
3. Hold trigger and move the laser to drag windows or controls. Use the stick to scroll. Press right B for a context click, or hold it while moving the laser for right-mouse drags such as paint brush resizing.
4. Use your physical PC keyboard for text entry. The VR keyboard is disabled by default. No desk mouse grabbing or touch typing is required.
5. Press left Y / menu to leave the computer.

Point inside a browser webpage and use the stick to scroll that page. Browser toolbar controls and other apps retain ordinary UI scrolling. Native browser privacy approval and availability still apply.

In Paint, hold trigger over the canvas to draw. Use its Undo and Redo buttons to change a stroke, or hold right B and move sideways over the canvas to change brush size. Painting keeps the game's normal save behaviour. A physical PC keyboard is still needed for text and shortcuts.

In Camera, click **Show** to open its controls. The shutter takes a picture from the desk webcam. Select a gallery thumbnail to view a saved picture, choose **Live** to return to the webcam, or use the trash button to delete the selected picture. Closing Camera stops its preview rendering.

In Scamazon, use the week buttons to preview the day-unlock pages. For **Hold to buy**, keep the trigger held until its progress completes; release early to cancel. Store access, prices and delivery still use the game's normal rules.

In Phone, use the language button to open its language list. The speaker icon mutes or restores caller audio; point at it to expose the volume slider, then hold trigger and drag the slider. The microphone icon toggles the game's phone-input mute when a recording device is available.

When a caller rings, point at the incoming call card's answer button and press trigger, or choose **Decline** to reject it. Answering opens the phone from that card; use **Hang Up** inside the phone to end the call. Older builds may show **Pick Up** directly inside the phone instead. Call ownership and permissions stay with the native game.

In AnyViewer, enter the fictional connection code supplied by your current caller, then click **Connect**. Double-click a desktop folder to open its files, and double-click a text file to open Notepad. Use the explorer arrows to go back, forward or up. The power/unplug button in the remote desktop taskbar disconnects. If another app covers a control, minimise that app first.

Remote photos have fit/fill controls. Audio and video use play/pause buttons, a seek bar and a volume slider. Point inside the audio window and use the stick to scroll to controls below the view. Remote windows stay within AnyViewer's usable desktop area, including when you maximise or restore it.

`[UI] PhysicalDesktop = true` keeps the UI attached to the original monitor by default. In settings → General → VR options, change COMPUTER SCREEN to FLOATING if you prefer a closer desktop. App licences, sign-in and game progression still follow the native game; opening a window does not guarantee that every workflow has passed VR testing.

## Interact and fight

Use right grip for native pickup/hold interactions, then release to drop. While holding an item, right trigger sends its native primary action, including food/tool use. Right B sends the native secondary action away from UI; hold and release it for actions that need a held button. With empty hands, right grip + trigger punches.

For charged weapons such as the shotgun and sniper, keep the right trigger held until the game fires. Release early to cancel charging. Opening settings or losing VR input focus cancels the charge too; release and press again to start a new shot. Shotgun spread follows the controller's orientation as well as its aim direction.

You can aim and fire with the controllers even when no PC mouse is connected. No extra setup is needed for this.

Physical controller movement supplies inherited release speed. Snap turns, rig repositioning and recentering no longer count as a hand swing; recentering keeps your grip-held item held. The game's normal drop impulse and velocity limit still apply.

Left trigger interacts with the world under your right-hand aim, so you can open a computer while holding an item. Like the native interact key, it can drop your held item when aimed at empty space. Left grip + left trigger is reserved for pee and does not also interact. Release either grip or trigger to end attack input. Opening a menu or pointing onto UI also cancels it. Right A jumps. Cooldowns, stamina, ownership and server rules still apply.

Left grip + left trigger holds the native pee action; release to stop. Left grip + left X requests a fart, or the native cone boost when an eligible agent has a traffic cone in the hip slot. Left grip + right B swaps the agent hand/hip slots. Left grip + right trigger sends the native extra held-item action; releasing its modifier ends that action. Food-related poop effects retain the game's existing rules and jump triggers; no debug/cheat poop shortcut is enabled. Curry consumption and its timed poop/gassy effect pass simulated checks. VR jump now reaches the native jump event and activates the curry boost. Hip swap, nonfinal cone boost activation, cooldowns and effect spawning pass simulated checks. Full boost trajectories, final cone use/ragdoll, other foods and full combat acceptance are still outstanding.

## Ragdoll and recovery

Right A also sends the native struggle/escape action when ragdolled or hiding. To use the native give-up/unstuck action, hold left grip **before** pressing and holding right A. Its normal game timer and recovery, respawn or jail rules still apply. Release either control to cancel. Menus, focus loss and VR shutdown also release the hold; closing a menu while the buttons remain held does not restart it.

If a ragdoll camera marker is missing from the player prefab, the view follows its physical head, with native chest/hips as a backup. Basic simulated ragdoll/recovery checks pass; violent impacts, completed give-up outcomes, hiding variants and real-headset comfort still need acceptance.

## Play with friends

The host must have the mod to relay poses. Modded PC players use `Mode = PC`; VR players use `Mode = VR`. Both are in `BepInEx/config/com.swyf.vr.cfg`, under `[General]`. Restart after changing mode. Use the game's normal lobby/invite flow. [Read multiplayer details](MULTIPLAYER.md).
