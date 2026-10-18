# Changelog

## Unreleased

- Default computer operation returns to a floating controller-pointer desktop.
- Remove experimental physical desk mouse and touch-keyboard code. The optional floating keyboard is off by default.
- Add standing room-scale collision-body following and separate body/head turning from tracking-origin yaw.
- Prevent animated eye meshes from feeding tracked head position back into the camera anchor.
- Track local head bones and remote voice mouths with reversible pose ownership.
- Keep floating HUD and menus at 0.9m by default, with VR players/settings hints.
- Repair white UI Toolkit controls in owned render textures, native settings state, pointer capture/clicks and independent webcam output.
- Add setup, play, build, troubleshooting and contribution guides plus artwork slots.

See [validation](docs/VALIDATION.md) for the exact evidence and remaining gates. These entries describe source changes, not universal production or hardware certification.
