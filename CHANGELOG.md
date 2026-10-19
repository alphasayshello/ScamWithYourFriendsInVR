# Changelog

## Unreleased

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

See [validation](docs/VALIDATION.md) for the exact evidence and remaining gates. These entries describe source changes, not universal production or hardware certification.
