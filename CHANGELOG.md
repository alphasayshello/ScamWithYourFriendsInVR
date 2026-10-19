# Changelog

## Unreleased

- Allow the local browser engine at least twenty seconds to start in VR, preserving longer configured timeouts.
- Start newly installed game copies with a fresh browser cache instead of cloning generated browser profiles/output.
- Verify controller clicking, dragging, off-page release, scrolling, focus cancellation and context input on a locally rendered CEF webpage with a temporary profile.
- Clamp captured controller releases to the browser image boundary so dragging off the page still delivers the native mouse-up, preserving normal mouse behaviour.
- Forward controller scrolling over the embedded browser to native pixel scrolling at the laser position, preserving fine input across frames and ordinary scrolling in other controls.
- Added an isolated-copy installer with stage/engine verification and portable PC/VR launchers.
- Desktop attachment follows monitor movement and replaced stream renderers while preserving the game's hidden local presenter.
- Send VR jump edges to native struggle/hiding-escape input and add a captured left-grip + right-A give-up hold.
- Cancel give-up on menu, focus loss, component disable and shutdown; require a fresh press after cancellation.
- Follow physical head/chest bones when a player prefab has no native ragdoll camera marker, and verify view/tracked-pose recovery.

- Map agent hip swap and extra held-item actions to left-grip modifiers with right B / right trigger.
- Route the fart chord to the native cone boost callback when eligible, retaining native cooldowns, role checks and menu suppression. Avoid injecting the ordinary fart shortcut during eligible cone use.
- Check actual native jump/curry boost activation, authoritative cone effects, hip swap/reverse and modified-action release.

- Route controller primary/secondary use through native PlayerInteractor callbacks, preserving held-item use events, weapon selection and native gameplay gates.
- Add left-trigger world interaction while holding items, with separate left-grip/pee modifier handling.
- Let trigger use a held item without grip; keep grip + trigger for empty-hand combat. Deliver held secondary press/release with right B away from UI.
- Clear callback dispatch ownership before removing VR patches, and block gameplay input behind the optional keyboard.
- Verify native curry consumption, spawned poop/gassy effects and normal expiry through the actual pointer input path.

- Add a separate pinned dependency profile for Unity 6000.3.26f1 and Input System 1.20.0, retaining the 6000.3.10f1 / 1.19.0 target.
- Select dependencies from the actual game version, isolate default caches by engine, and reject incompatible Input System assemblies before staging or launching.
- Account for the moved InputForUI source directory in newer Input System packages.

- Redirect native punch knockback camera-forward reads to the controller, matching hit detection.
- Verify a nonlethal controller punch reaches a real NPC through the native authority command and reduces native health; restore target health after the check.

- Release attack input when grip is released or the pointer enters UI/menu context, even if trigger remains held.
- Prevent world clicks and new grip pickups through menu backdrops; reject VR attacks without interactive tracking or during unavailable avatar states.
- Check native unarmed controller rays/cooldowns and grip-release cancellation in a single private mock host.

- Remove temporary capsule controller visuals; use native tracked avatar hands in gameplay.
- Add contextual laser visibility, adaptive UI aim smoothing, a smaller distance-scaled reticle and adjustable double-click timing.
- Ignore decorative uGUI graphics during pointer selection and keep capture visible during UI dragging.
- Release controller button state when a pressed panel disappears, preventing stuck input.
- Reuse tracking/UI scan buffers, avoid duplicate tracking reads and release material entries for destroyed menu graphics.
- Reuse typed game-member cache keys instead of constructing lookup strings during normal gameplay.

- Keep computer UI attached to its desk monitor by default, with controller-pointer input and an optional floating desktop.
- Remove experimental physical desk mouse and touch-keyboard code. The optional floating keyboard is off by default.
- Add standing room-scale collision-body following and separate body/head turning from tracking-origin yaw.
- Prevent animated eye meshes from feeding tracked head position back into the camera anchor.
- Track local head bones and remote voice mouths with reversible pose ownership.
- Keep floating HUD and menus at 0.9m by default, with VR players/settings hints.
- Repair white UI Toolkit controls in owned render textures, native settings state, pointer capture/clicks and independent webcam output.
- Add setup, play, build, troubleshooting and contribution guides and the project banner.
- Add large VR controls inside General settings, saved automatically.
- Respect native camera parent changes and spectated-player anchors; release tracked bone/voice ownership during incompatible native states.
- Add a setup checker for staged hashes, game version and OpenXR selection, plus a mode-selecting launcher.
- Reject updated game versions and already restored input libraries during staging so engine interface restoration uses original game contracts.

See [validation](docs/VALIDATION.md) for the exact evidence and remaining gates. These entries describe source changes, not universal production or hardware certification.
