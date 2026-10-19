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

For a single mock VR client, the workspace launcher `outputs/Run-Single-VR-Checks.ps1` runs sixteen core groups without a PC client, including browser scroll coordinates/fine input, cached game-member dispatch, adaptive pointer/visibility checks, native room-scale collision and remote voice pose/restore checks. Its `-Computer` option starts one private local host and exercises pointer operation on the desk monitor, available application window controls, settings tabs/rendered colours, VR option buttons, head-follow placement, gameplay bindings and HUD hints. It also checks native body/head yaw and room movement with explicit mock headset poses. The physical desk mouse/keyboard experiment has been removed. The optional floating keyboard fixture explicitly enables its feature and restores the disabled default. Full app workflows and headset acceptance remain separate requirements. Remove the optional smoke-test DLL before a normal hardware run.

The world-panel group creates a native world-space UIDocument with a real font, rotated/nonuniform document transform and a tilted, depth-offset button. It checks Unity's 3D picking, world distance and local pointer coordinates, click, cancellation without click, captured off-element dragging, disabled/transparent controls and document disable. Its renderer is left native and no render texture is assigned. In the simulated VR pair it also waits for normal pointer readiness and document discovery, activates that button through the actual controller-ray tick, and checks that an intervening world collider blocks the ray. Lifecycle tests now require the native-world document registry to be cleared as well. This does not verify all native game panels or texture-backed physical monitor surfaces.

## authored texture presenter regression

The texture group uses a real texture-backed UIDocument and the native review-projector shader on a rotated, nonuniform mesh with cropped UVs and a vertical texture flip. It checks world hit distance, UV/panel coordinates, native button dispatch, keyboard document routing, captured off-mesh drag, disabled-renderer input rejection and preservation of native renderer/texture ownership during component removal. The VR client also waits for automatic mesh discovery, clicks through the production controller pointer with a frame-separated release, checks cancellation when the renderer is hidden during a press and verifies world occlusion. This fixture does not validate an entire authored projector/monitor gameplay workflow or arbitrary shaders.


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

The separate rendered-browser fixture starts the real bundled CEF engine at `about:blank`, using a fresh temporary incognito profile. It loads self-contained HTML with no external resources, checks the actual page texture and drives a DOM button, drag target, off-page release, page scrolling, focus cancellation and context click through the controller ray. DOM state returns through native browser title events; transport calls are not intercepted. It checks the production startup allowance, preserves saved privacy settings and restores native URL/history fields. Window-only checks suppress browser startup so they do not open saved sessions in the background. External websites, text entry, browser detach/close during a drag and real-headset usability remain separate requirements.

## native paint controls

The private computer fixture temporarily unlocks native app runtimes within its owned lobby and constructs an isolated native Paint instance. Controller rays draw actual canvas pixels, verify the matching saved PNG, operate Undo/Redo, hold right B to resize the brush and cancel a stroke on focus loss. An unrelated trigger release must not cancel a B drag. The fixture restores the original paint instance and lobby access flag, and confines artwork to a temporary directory. It does not purchase licences or modify saved artwork. Printing, galleries, clipboard operations, text input and headset acceptance remain outstanding.

## combat transition diagnostics

`outputs/Run-Single-VR-Checks.ps1 -Combat` starts a focused private mock host and repeats twenty native role changes and teleports. Each cycle checks actual punch input, cooldown, grip-release cancellation and settings suppression. It excludes the NPC damage portion and restores role/position on exit. This helps investigate an intermittent hold-input failure in the larger suite; passing these cycles does not explain that failure or establish PvP reliability.

Add `-CombatEye 'x,y,z'` to replay a scene position. The fixture keeps the native collision body, settles each role transition and approaches the requested eye position in short tracking steps. Obstructions can prevent reaching that point; inspect the actual eye/root and collision state rather than assuming the requested coordinates were attained. Tracking pose, role and position are restored on exit.
