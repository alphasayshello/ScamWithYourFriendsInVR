# How to play

[Back to the project](../README.md)

## Before you launch

Connect your headset to the PC runtime first. For Quest through Steam Link, reach SteamVR before launching. Run one game client for normal headset testing. Face forward in a clear area and hold left stick click for about **0.65 seconds** to recenter.

## Move and look

Move with the left stick and snap turn with the right stick. Your head controls where you look; the revised standing body follows head yaw separately from tracking-origin rotation. Physical steps move the native collision body, with a small allowance for leaning. Walls still block the body. Large tracking jumps trigger recalibration rather than a sudden body move.

Standing room movement is disabled while seated or when native gameplay input is blocked. The revised alignment requires headset acceptance, including chairs, stairs and reconnects. Use `[Comfort] Seated = true` for seated height compensation; this is separate from sitting in an in-game chair.

## Menus and HUD

Aim the right controller at a button and press the trigger. Hold the trigger to drag a slider or scroll bar. Use the left stick vertically to scroll while pointing at UI; right-stick vertical scrolling also works. Let the stick return to centre afterward.

The laser stays visible in menus and computers. During normal play it appears for usable targets and hides when idle. Temporary controller capsules have been removed; the game's tracked avatar hands remain. UI aiming uses light adaptive smoothing while hands stay directly tracked. Set `UI.AlwaysShowPointer = true` for a permanent beam, or `UI.PointerSmoothing = 0` for direct aiming. Controller double clicks allow 0.4 seconds between presses by default (`UI.DoubleClickWindow`).

**Left X** opens players. **Left Y** or the controller menu backs out of players, exits a captured computer, or toggles settings. Floating menus/HUD follow your view at **0.9 metres** by default. Adjust `[UI] PanelDistance` and `PanelWidth` if needed.

## Computers

1. Point at the computer's native interaction target and press the right trigger to enter it.
2. Use the right-controller laser on the floating desktop. Press trigger to click; use two quick presses for double-click actions.
3. Hold trigger and move the laser to drag windows or controls. Use the stick to scroll. Right B sends a context click.
4. Use your physical PC keyboard for text entry. The VR keyboard is disabled by default. No desk mouse grabbing or touch typing is required.
5. Press left Y / menu to leave the computer.

`[UI] PhysicalDesktop = true` optionally places the UI on the original monitor. The default is **false** for easier pointer access. App licences, sign-in and game progression still follow the native game; opening a window does not guarantee that every workflow has passed VR testing.

## Interact and fight

Use right grip for native pickup/hold interactions, then release to drop. Away from UI, right grip + trigger sends native punch/weapon input. Right A jumps. Cooldowns, stamina, ownership and server rules still apply.

Left grip + left trigger holds the native pee action; release to stop. Left grip + left X requests a fart. Food-related poop effects retain the game's existing rules and jump triggers; no debug/cheat poop shortcut is enabled. Full combat and food-effect acceptance is still outstanding.

## Play with friends

The host must have the mod to relay poses. Modded PC players use `Mode = PC`; VR players use `Mode = VR`. Both are in `BepInEx/config/com.swyf.vr.cfg`, under `[General]`. Restart after changing mode. Use the game's normal lobby/invite flow. [Read multiplayer details](MULTIPLAYER.md).
