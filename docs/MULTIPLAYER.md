# Mixed VR and PC lobbies

The host and every player who wants to see tracked avatars install the same mod build. The host may use PC mode. VR players transmit head and hand poses; PC players keep normal controls and receive those poses. Unmodded players retain ordinary avatar animations. Game movement, ownership, pickups, voice and authentication remain native.

This implementation is experimental. Codec, synthetic avatar IK and local-host checks are not a two-machine Steam certification. Latest Quest 3 feedback accepted the general view/menu experience but reported computer and body/room-movement issues. Full multiplayer and performance acceptance remain open.

## Configuration

Start once, close the game, then edit `BepInEx/config/com.swyf.vr.cfg`:

```ini
[General]
Enabled = true
Mode = VR

[Multiplayer]
SyncPoses = true
PoseRate = 20
DirectHandshake = false
```

Set `Mode = PC` on flat-screen clients and PC hosts. Restart after changing mode. PC mode skips OpenXR initialization and VR camera/input/UI patches. `SyncPoses = false` disables pose networking entirely.

Use matching game and mod versions, then join through the game's normal Steam lobby. A modded host advertises the protocol in a separate Steam lobby metadata key. Clients only negotiate after seeing that capability; the host only relays poses to clients that negotiated. Unknown Mirror message IDs can disconnect unmodded players, so no custom pose messages are sent to unnegotiated Steam peers. An unmodded host does not relay VR poses.

For direct/LAN testing against a known modded host, clients can set `DirectHandshake = true`. Keep it false for normal Steam play and unmodded hosts. It bypasses Steam capability discovery; an unmodded direct host can disconnect a client sending unknown messages.

## Protocol

- Explicit version 1 handshake, unreliable poses at 20 Hz by default, 93-byte pose payload before Mirror/transport framing.
- Server derives player identity from the authenticated connection; clients cannot select another player's ID.
- Rejects invalid masks, nonfinite/excessive positions, invalid rotations, duplicate/out-of-order sequences and excessive packet rates.
- Poses are relative to the native player root and only animate bones; they do not teleport remote players or grant item authority.
- Smoothed head orientation and two-bone arm IK run after native animation. Native poses resume after 0.75 seconds without valid tracking.
- Current IK recognizes the inspected Mixamo bone names. Wrist alignment, role changes, seating and ragdoll still need visual testing. Finger tracking is not transmitted.

## Two-machine acceptance

1. Host in PC mode, second player in VR mode. Check logs for `VR pose protocol negotiated` and `Modded host accepted VR pose protocol`.
2. Watch the VR player's head and both arms from the PC. Compare with their physical movement, crossed arms, full reach and tracking loss.
3. Repeat with VR host/PC client, then two VR clients and a PC host.
4. Test late join, rejoin, respawn, role/model changes, seating, ragdoll, SteamVR dashboard and disconnect. Native animation must recover when tracking stops.
5. Test movement, pickups, telephone and voice on host and client.
6. Join an unmodded PC through Steam: it must remain connected with native animations and no unknown-message errors. Join an unmodded host with `DirectHandshake = false`: gameplay must remain native and pose negotiation must not occur.
7. Measure frame time and bandwidth at the intended lobby size before marking multiplayer supported.

The mod assigns explicit codecs because this build does not run Unity's Weaver. See Mirror's [network messages](https://mirror-networking.gitbook.io/docs/manual/guides/communications/network-messages) and [serialization](https://mirror-networking.gitbook.io/docs/manual/guides/serialization) documentation.
