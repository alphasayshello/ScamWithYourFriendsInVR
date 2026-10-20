# Optional in-player pointer tests

These checks run Unity's actual event implementations inside an isolated game copy. They exercise the pointer module directly without a headset. They do not verify controller tracking, ray intersection accuracy, stereo rendering, desktop apps or multiplayer behavior.

1. Build the Release mod and stage its dependencies as described in the repository README.
2. Apply the stage to a separate game copy. Never use the Steam installation for this test.
3. Run `dotnet build tools/smoke-tests/SmokeTests.csproj -c Release`.
4. Copy only `tools/smoke-tests/bin/Release/netstandard2.1/ScamWithYourFriendsInVR.SmokeTests.dll` into that copy's `BepInEx/plugins/ScamWithYourFriendsInVR/` folder.
5. Launch that game copy with `-force-d3d11 --swyf-vr-smoke-tests`. Batch mode is optional. Set `XR_RUNTIME_JSON` only in the launch process environment to a nonexistent JSON path when checking missing-runtime fallback; restore the parent process environment immediately after launch.
6. Inspect `BepInEx/LogOutput.log` for `SMOKE PASS` or `SMOKE FAIL`. Both the uGUI and UI Toolkit groups must pass. Check the player log separately for engine or game exceptions.
7. Remove the optional test DLL after testing.

The tests create temporary UI objects under their plugin, then destroy them. The uGUI group verifies click ordering/counts, drag and drop, release over another target, scroll, pressed cancellation and drag cancellation. The UI Toolkit group verifies synthesized pointer events and the pressed-button mask across down, held move, up and released move. Each failure includes the actual event sequence or exception.

The keyboard group tests UI Toolkit selection replacement, backspace, forward delete, arrow/home/end navigation, max length, read-only fields and delayed commit on Enter. It tests uGUI InputField selection, deletion, integer/custom validation, limits and read-only guards; TMP selection, deletion, integer validation, read-only and submit; and automatic keyboard closure when a field is hidden or disabled. Fields get character layout before selection tests. A simulated browser client verifies text punctuation, key down/up pairing and disposed-client cleanup. This checks the adapter contract, not typing into an actual embedded browser page.

The shared-panel group verifies element-to-document ownership and transparent clearing/restoration of newly allocated UI render textures. Pointer state checks include cancelling a press after its target leaves the panel, ensuring the global pressed-button mask returns to zero.

The canvas-ownership group verifies that inactive native canvases stay inactive, converted canvases retain their original parent and inherit its visibility, world width compensates for parent scale, and native transforms/render modes restore. It also checks that authored texture-backed and native world-space panels are not duplicated into floating HUD panels. A passing input test does not prove a surface is visible: computer probes save both the native texture and rendered camera view for separate inspection. See the validation document for intermittent keyboard failures and unresolved floating-surface rendering.

The pose group tests explicit Mirror codec round-trip/wire size, invalid floats/rotations/offsets/flags, sequence rollover, and a synthetic remote arm's reach/restore behavior. In an isolated PC-mode run, add `--swyf-vr-pose-host-tests` to wait for the game's one-player local host, send an actual Mirror pose message, and check duplicate/invalid rejection. It refuses synthetic poses if other connections are present. This does not replace a two-machine Steam test.

For a software-only lifecycle run, point the launch process's `XR_RUNTIME_JSON` at the pinned OpenXR package's `Runtime/MockRuntime/unity-mock-runtime.json` and add `--swyf-vr-mock-lifecycle-tests` as well. This additional test refuses to stop any runtime whose name is not `Unity Mock Runtime`. It waits for session/camera startup, stops the owned XR subsystems, then checks camera settings, converted UI cleanup and gameplay patch removal. A mock result does not certify real headset support.

The workspace's `outputs/Launch-Local-Pair.ps1 -RunSmokeTests` updates the canonical plugin directory and launches the private PC/VR loopback pair with these checks. `-RunLifecycleTests` additionally stops mock XR and checks frame pacing, camera culling mask and visibility-mesh restoration. `-RunComputerTests` seats the client through native monitor interaction, waits for workstation loading, uses the controller ray to double-click an available desktop app and closes the monitor with the controller menu path. Lifecycle and computer tests run separately. The computer test respects app availability/licensing: if paint is unavailable, it tests the checklist app and leaves paint drawing unverified. These flags are optional test automation; omit them for normal playtesting.
## camera and caret regressions

The native computer probe also requires placement on the current workstation's authored WebRTC display geometry. It checks center alignment, aspect ratio, fit within the screen, world depth testing, rejection of back-side input, recenter stability, temporary floating-mode fallback and placement release on computer close. Toggling placement must not change the native stream renderer's visibility or shared material. The probe restores its original config value. Saved full mock-eye images provide visual placement evidence; they are not headset acceptance.

The computer probe checks that the native camera app uses an auxiliary preview camera, leaves the tracked camera's target unset, disables XR on the preview, and excludes floating UI from its feed. Its saved image is PC-capture evidence only. Mock hands use explicit seated poses during this probe.

The preview-rate check temporarily sets `Performance.DesktopCameraFPS` to 5 and counts actual URP end-camera render callbacks for two seconds. It requires preview renders within that cap and more headset renders than preview renders, then restores the previous setting in `finally`. It verifies render scheduling, not a sustained frame-rate or GPU-performance improvement.

Keyboard checks supply an actual font (a bare PanelSettings has none), repeat character-limit input across layout frames, type a full URL, verify Tab follows native field focus, and exercise a change callback that closes the keyboard during dispatch. The simulated browser-disposal warning is expected; no website is opened by that probe.

The temporary keyboard test document stops unrelated native key-down events outside its own dispatch calls; an explicit non-test `S` event verifies that isolation. This prevents global/native keyboard input from contaminating frame-separated typing assertions. Production keyboard dispatch and ordinary game documents retain physical keyboard input.

## native held-item and pose lifecycle regression

The workspace launcher accepts `-RunGripTests`, optionally together with `-RunComputerTests -RunSmokeTests`. It refuses combination with lifecycle stop tests. This optional loopback-only probe picks up an ordinary item through the native pickup command (or native scene-pickup registry), waits for replicated ownership, adopts it with grip, and verifies release does not happen until the grip is released. It then uses the controller interaction ray for a quick-release pickup and waits for server acknowledgement/drop, checks cancellation, and disables/re-enables the client pose component to verify state cleanup and renegotiation with the live host. It changes only the isolated test scene. It does not validate throw calibration, every held item, or two-machine Steam behavior.

## native world-space UI regression

For a single mock VR client, the workspace launcher `outputs/Run-Single-VR-Checks.ps1` runs twenty core groups without a PC client, including browser scroll coordinates/fine input, cached game-member dispatch, adaptive pointer/visibility checks, native room-scale collision and remote voice pose/restore checks. Its `-Computer` option starts one private local host and exercises pointer operation on the desk monitor, available application window controls, settings tabs/rendered colours, VR option buttons, head-follow placement, gameplay bindings and HUD hints. It also checks native body/head yaw and room movement with explicit mock headset poses. The physical desk mouse/keyboard experiment has been removed. The optional floating keyboard fixture explicitly enables its feature and restores the disabled default. Full app workflows and headset acceptance remain separate requirements. Remove the optional smoke-test DLL before a normal hardware run.

The world-panel group creates a native world-space UIDocument with a real font, rotated/nonuniform document transform and a tilted, depth-offset button. It checks Unity's 3D picking, world distance and local pointer coordinates, click, cancellation without click, captured off-element dragging, disabled/transparent controls and document disable. Its renderer is left native and no render texture is assigned. The core single-client run and simulated VR pair both wait for pointer readiness and document discovery, activate the button through the controller-ray tick, and check world occlusion with the floating-menu option enabled and disabled. The single-client run initializes the pinned mock headset pose before waiting for tracked input. Lifecycle tests now require the native-world document registry to be cleared as well. This does not verify all native game panels or texture-backed physical monitor surfaces.

## authored texture presenter regression

The texture group uses a real texture-backed UIDocument and the native review-projector shader on a rotated, nonuniform mesh with cropped UVs and a vertical texture flip. It checks world hit distance, UV/panel coordinates, native button dispatch, keyboard document routing, captured off-mesh drag, disabled-renderer input rejection and preservation of native renderer/texture ownership during component removal. The core single-client run and simulated VR pair both wait for automatic mesh discovery, click through the production controller pointer with a frame-separated release, check cancellation when the renderer is hidden during a press and verify world occlusion with the floating-menu option enabled and disabled. This covers both shared and independent native physics-query paths. This fixture does not validate an entire authored projector/monitor gameplay workflow or arbitrary shaders.


## native combat and single-host grip checks

The single private mock host now checks the real native unarmed weapon: VR attack input reaches its firing routine, the ray matches the controller, and repeat input cannot bypass the native cooldown. Releasing grip while trigger stays held must clear the native attack hold; settings must reject a new attack hold. A separate target step selects a healthy native NPC, fires through the real controller-directed weapon path, waits for a native health reduction through the authority command, and checks controller-directed knockback. It restores the target health and local position in `finally`. This does not prove charged/ranged weapons, PvP rules, knockouts or headset feel.

`outputs/Run-Single-VR-Checks.ps1 -Computer -Grip` also runs the existing native held-item replication and pose-component lifecycle checks in that same single host. The workspace launcher is local tooling outside this source export; the equivalent game flags are `--swyf-single-computer-tests --swyf-pair-grip-tests --swyf-pair-ready="C:\tests\ready.txt"` with a parent folder that exists and the pinned mock OpenXR runtime selected only for that child process. Never pass test flags for normal play.


## native held-item and food use

The single private host creates a native curry prefab as an owned test fixture, picks it up through its normal command and drives primary/secondary buttons through the actual VR pointer. Trigger use without grip must reach the held-item event; secondary press/release must reach the native alternate-action event. Settings must prevent consumption. Left trigger must retain native world interaction while holding food, without consuming it or firing under the pee modifier. The test waits for native network-spawned poop and the local gassy state, then requires normal effect expiry. It cleans up its fixture/effects even on failure. This does not validate every food, jump/cone boosts, cross-account replication or headset feel. These fixtures are only in the optional test plugin; normal play has no test-food spawning command.


## native boost activation

The curry fixture holds the VR jump input until the actual native `onJumped` event fires, then checks the native boost envelope and cooldown. Its observation callback clears that new envelope before physics applies the boost, so this verifies activation, not trajectory or comfort.

A separate owned traffic-cone fixture temporarily uses the native agent role, drives controller hip swap and reverse swap, and requires the actual cone callback to add a boost envelope, synchronize one use and network-spawn its effect. Repeated input must obey the native cooldown, and settings must suppress activation. The fixture clears its impulse before physics, restores the original role/position and removes only the effects observed inside its own cone spawn call. Final cone use, its ragdoll transition, boost trajectories and headset acceptance remain unverified.


## native ragdoll and recovery

`outputs/Run-Single-VR-Checks.ps1 -Ragdoll` runs a focused private mock host. It starts a short zero-impulse ragdoll on an owned floor fixture, checks camera proximity to the native body and release of tracked pose ownership, sends one native struggle command, and verifies short give-up charge/cancellation on menus, focus loss and component disable. Recovery must restore collision, tracked poses and authored eye alignment. Give-up completion, violent impacts, hiding variants and headset feel remain separate gates. The native floor/position and any unfinished ragdoll are cleaned up in `finally`.

## browser scrolling

The core group uses a real panel with an offset browser image and a transport probe to check page coordinates, direction, fractional pixels, host changes and detached/chrome/readiness guards. The private computer host also resolves the real browser host and viewport through the production pointer router, intercepting only the native transport call. That probe requires no live CEF connection and does not change the game's saved browser privacy setting.

The native browser fixture also sends controller down/up through its actual viewport callbacks, releases outside the image and requires exactly one transport release plus cleared pointer capture. It checks an ordinary in-page click and verifies that the correction leaves physical-mouse behaviour unchanged. The transport is intercepted; this is not an end-to-end rendered webpage or browser-detach check.

The separate rendered-browser fixture starts the real bundled CEF engine at `about:blank`, using a fresh temporary incognito profile. It loads self-contained HTML with no external resources, checks the actual page texture and drives a DOM button, drag target, off-page release, page scrolling, focus cancellation and context click through the controller ray. It also closes the real browser while a controller drag is held and requires press ownership and pointer capture to clear. DOM state returns through native browser title events; transport calls are not intercepted. It checks the production startup allowance, preserves saved privacy settings and restores native URL/history fields. Window-only checks suppress browser startup so they do not open saved sessions in the background. External websites, text entry and real-headset usability remain separate requirements.

## native paint controls

The private computer fixture temporarily unlocks native app runtimes within its owned lobby and constructs an isolated native Paint instance. Controller rays draw actual canvas pixels, verify the matching saved PNG, operate Undo/Redo, hold right B to resize the brush and cancel a stroke on focus loss. An unrelated trigger release must not cancel a B drag. The fixture restores the original paint instance and lobby access flag, and confines artwork to a temporary directory. It does not purchase licences or modify saved artwork. Printing, galleries, clipboard operations, text input and headset acceptance remain outstanding.

## combat transition diagnostics

The full computer host checks positive controller combos on an owned floor away from scene furniture, requiring an unobstructed head first. NPC damage still uses a real native NPC and its server command; player position and health are restored. This keeps the positive input assertion separate from intentional head-collision rejection.

`outputs/Run-Single-VR-Checks.ps1 -Combat` starts a focused private mock host and repeats twenty native role changes and teleports. With a clear head, each cycle checks actual punch input, cooldown, grip-release cancellation and settings suppression. With the head inside world geometry, it instead requires the controller press to leave native attack state and firing clear. It excludes NPC damage and restores role/position on exit. The earlier hold-input failure was reproduced at eye position `2.48,6.96,-18.42`: the head overlapped the native desk Phone collider, so the production head-collision guard correctly rejected the attempted attack. That replay now checks rejection explicitly. This does not establish PvP reliability or headset feel.

Add `-CombatEye 'x,y,z'` to replay a scene position. The fixture keeps the native collision body, settles each role transition and approaches the requested eye position in short tracking steps. Obstructions can prevent reaching that point; inspect the actual eye/root and collision state rather than assuming the requested coordinates were attained. Tracking pose, role and position are restored on exit.

## native camera controls

The private computer fixture uses a separate native Camera instance with an isolated picture folder. Controller rays open the tools tray, take a photo, switch between Live and the gallery, and delete the fixture photo. Saved PNG pixels must match the observed native capture and contain a nonuniform image. The preview must match the desk webcam's pose while the headset keeps its own output; closing Camera must stop the preview render. The fixture restores the original app instance, content and window state. Watermarking is disabled only on the fixture instance, and native clipboard finalization is intercepted to preserve the user's clipboard. Watermarks, clipboard sharing and headset acceptance remain unverified.

## native store controls

The private computer fixture builds a separate native Scamazon storefront, switches its three week pages through controller rays and checks the actual page visibility. A temporary zero-price button uses the game's native hold-to-buy handler. Short release and focus loss must stop charging and release capture; a completed hold must reach `BuyProduct` exactly once. That UI dispatch is intercepted for the fixture instance, so no money is spent and no delivery or entitlement is created. Original content and window state are restored. This checks store input, not actual purchase authority, purchased-store tabs or delivery outcomes.

## native phone controls

The private computer fixture opens the real idle Phone app and uses controller rays to open/select its current language, toggle caller audio, drag the native speaker-volume slider and toggle phone microphone mute when the native game detects a recording device. The slider must own the press, and its value must match the actual native audio setting. Cleanup restores native audio state, automatic/explicit language selection, and the original saved values or absent preference keys. Live transcription, other languages and headset audio remain separate acceptance checks.

The call fixture spawns the game's actual victim-call prefab on its owned idle workstation and calls native server initialization. Controller Decline must remove the ringing caller without assigning a talker or starting dialogue. A fresh caller then uses the incoming popup's Answer control when present, or the older phone's Pick Up control, waits for native server talker ownership and uses Hang Up. Both server/client spawn registries, workstation binding and local player call state must clear. AI dialogue task execution is intercepted only for the fixture caller, TTS warmup only for its local voice component, and automatic Banditcam recording only for that test caller. The fixture requires that it never acquires automatic recording ownership. Tutorial progression must be inactive; the fixture does not commit switchboard caller history, restores workstation cooldown and force-cleans an unfinished caller on failure. It verifies real call authority and UI input, not AI/TTS responses, transfers, completed scams or cross-account networking.

## grip release motion

The core suite moves and rotates the live tracking frame while keeping the controller's local pose still; release velocity must stay zero. Real local controller movement must map through the current tracking frame, and motion reset/recenter must discard a pose jump. It also checks that calling the rig's real Recenter method resets the live pointer's grip-motion sample. The private host retains a native held item through recenter, observes the real drop command without intercepting it, and requires one zero-inherited-velocity release from a stationary controller. This is motion/authority coverage, not headset throwing feel or every item variant.

## ui tree cleanup

The core suite repeats twenty attach/repair/detach cycles on a nested button tree. Each detach must release its corner snapshots and restore the native styles. Reattachment must reapply the render correction. VR shutdown must restore attached styles and remove callbacks so later native style changes survive a detach.

## native scam forms and notifications

While an owned native caller is talking, the computer fixture temporarily enables the lobby's developer app-access flag and opens every exposed conversation-scam app. Each must render populated objectives, a checklist and progress, and pass controller maximize/restore/minimize/close controls. Genuinely enabled entry fields must accept controller focus; a blank submit must show the native local validation error without issuing a verification request. Clipped controls are revealed with actual controller-stick scrolling. Locked or dialogue-only fields remain gated. The fixture restores app access, window state/layout and the claim-input cache. Current coverage is eighteen windows and four enabled forms; typing, valid submissions, successful scams, payouts and special dialogue controls are not covered.

A separate native notification center checks controller dismissal without triggering its body action, then removes only its own notification layer. All app fixtures hold the mock ray at its selected target throughout each frame-separated press, avoiding artificial controller jumps between down and up.

## selected uGUI input

The core suite requires the VR input module to dispatch one selected-control update per Process call, stop dispatch on XR focus loss and follow selection changes or clearing. This restores the event pump used by uGUI/TMP keyboard editing; it does not establish physical Windows keyboard input, keyboard-layout or IME compatibility. UI Toolkit has a separate input path.

## native tic-tac-toe

Within the same temporary lobby app-access scope, an isolated native Tic-Tac-Toe owner replaces the app content while preserving the original board and children. Controller clicks play a complete solo round through native opponent logic, require the displayed marks/enabled squares to agree with the native board, and restart using New Game. Multiplayer mode must disable solo moves; Find Coworker must reach the real server's Waiting snapshot, and Cancel Search must return to Idle. Cleanup closes/disposes only the fixture owner, restores the original content/owner and releases queue participation through native close handling. No saved licence is bought or granted. This does not cover matched network games, rematches, cross-account play or headset usability.

## native recording controls

An idle private workstation uses a separate native Banditcam owner and recorder, retaining the real game-view capture, WebRTC encoder and FFmpeg writer. The fixture recorder receives no voice components and zero audio gains. Saved videos and playback conversions are redirected into an owned temporary directory; clipboard copying and watermarks are intercepted. The original app's recording subscription is temporarily detached, and the original recorder, app content, selection state and subscription are restored on cleanup.

Controller record/stop must reach a native keyframe and finalized WebM, without redirecting the headset's render target. Controller gallery selection must decode nonblank video pixels, then operate the actual pause/play and delete controls. Native playback-button enablement is awaited after preparation rather than bypassed. Delete must restore Live, and closing the app must release its capture-preview lease. Temporary files are removed after native playback/encoding resources close. This does not establish recording audio quality, watermark output, automatic call recordings, seek behaviour, clipboard sharing, long-session performance or real-headset usability.

## stale avatar eye bounds

The core fixture averages two valid eye renderers, replaces one and then both world bounds with positions far from the owner, and requires stale bounds to be ignored. ResetBounds must restore valid anchoring. It checks the small, scale-aware mesh-to-camera allowance and rejects displaced/transient standing offsets. Two simultaneously active role skeletons must resolve the head under the selected native chest, rather than the first active head. Standing calibration retains a sensible previous body-relative offset, or temporarily uses the native capsule top, until the smoothed camera marker is plausible. The native combat floor requires the centered mock headset to remain within half a metre horizontally of its body after teleporting. These checks cover calibration guards, not every animation, ragdoll variant or real-headset transition.
