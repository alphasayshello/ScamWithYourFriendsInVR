using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static Component flightFixture;
        private static Vector2 flightMove, flightTurn;
        private static bool flightJump, flightJumpDown, flightLeftTrigger, flightLeftTriggerDown, flightAllowExplosion, flightHoldAfterDespawn;
        private static bool flightWorldProbe;
        private static int flightWorldPresses;
        private static void ObserveFlightWorldUse(bool __0) { if (flightWorldProbe && __0) flightWorldPresses++; }
        private static int flightForceCalls;
        private static Vector3 flightForce;
        private static int flightImpacts, flightImpactCommands;
        private static float flightImpactSpeed;
        private static void ObserveFlightImpact(object __instance, Collision __0)
        {
            if (!ReferenceEquals(__instance, flightFixture)) return;
            flightImpacts++; flightImpactSpeed = Mathf.Max(flightImpactSpeed, __0.relativeVelocity.magnitude);
        }
        private static void ObserveFlightImpactCommand(object __instance)
        { if (ReferenceEquals(__instance, flightFixture)) flightImpactCommands++; }
        private static void ObserveFlightForce(object __instance, Vector3 __0)
        { if (ReferenceEquals(__instance, flightFixture)) { flightForceCalls++; flightForce = __0; } }
        private static bool GuardFixtureExplosion(object __instance)
            => !ReferenceEquals(__instance, flightFixture) || flightAllowExplosion;
        private static void KeepFlightInput(object input)
        {
            if (flightFixture == null && !flightHoldAfterDespawn) return;
            input.GetType().GetProperty("Move").SetValue(input, flightMove);
            input.GetType().GetProperty("Turn").SetValue(input, flightTurn);
            input.GetType().GetProperty("Jump").SetValue(input, flightJump);
            input.GetType().GetProperty("JumpDown").SetValue(input, flightJumpDown);
            input.GetType().GetProperty("LeftTrigger").SetValue(input, flightLeftTrigger);
            input.GetType().GetProperty("LeftTriggerDown").SetValue(input, flightLeftTriggerDown);
        }

        private IEnumerator TestNativeFlight()
        {
            float readyDeadline = Time.realtimeSinceStartup + 90;
            while (Member(Instance("LoadingScreenManager"), "targetGo") is GameObject loading && loading.activeInHierarchy
                || Find("NetworkingInitializer")?.GetProperty("IsNetworkTransitionInProgress")?.GetValue(null) is bool transition && transition)
            { Require(Time.realtimeSinceStartup < readyDeadline, "Native loading did not clear before FPV checks"); yield return null; }
            var manager = VRManager.Instance;
            Type type = Find("FpvInteractable");
            var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            GameObject prefab = NetworkManager.singleton.spawnPrefabs.Where(go => go != null && go.GetComponent(type) != null)
                .OrderBy(go => go.name).FirstOrDefault();
            Require(prefab != null && Member(interactor, "CurrentHeldItem") == null, "FPV fixture needs a native prefab and empty hands");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            object controller = AccessTools.Property(adapter, "Controller").GetValue(null);
            var controllerTick = AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches"), "BeforeController");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Drive(Vector2 move, Vector2 turn, bool climb)
            {
                flightMove = move; flightTurn = turn; flightJumpDown = climb && !flightJump; flightJump = climb;
                KeepFlightInput(manager.Input); controllerTick.Invoke(null, new[] { controller });
                flightJumpDown = false; Input("JumpDown", false);
            }
            void TriggerClimb(bool held)
            {
                flightLeftTriggerDown = held && !flightLeftTrigger; flightLeftTrigger = held;
                KeepFlightInput(manager.Input); controllerTick.Invoke(null, new[] { controller });
                flightLeftTriggerDown = false; Input("LeftTriggerDown", false);
            }
            GameObject item = null, obstacle = null, menuObstacle = null;
            var effects = new List<GameObject>();
            bool focused = manager.Input.HasInputFocus, paused = false, feedbackOwned = false;
            bool originalOcclusion = Plugin.OccludeFloatingUI.Value;
            float originalSteering = Plugin.FlightSteeringSpeed.Value;
            Type feedback = Find("FeedbackUi");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            // Previous native bag/ragdoll callbacks and body physics can run
            // before the rig's LateUpdate. Snapshot both in the same finished frame.
            yield return new WaitForEndOfFrame();
            Vector3 pilotBody = NetworkClient.localPlayer.transform.position;
            Vector3 pilotEye = manager.Rig.AuthoredEye;
            Vector3 pilotEyeOffset = pilotEye - pilotBody;
            Logger.LogInfo("FLIGHT PILOT START: eye=" + pilotEye + "; body=" + NetworkClient.localPlayer.transform.position
                + "; grounded=" + Member(controller, "isGrounded"));
            var probe = new Harmony("com.swyf.vr.flightfixture");
            try
            {
                probe.Patch(AccessTools.Method(adapter, "Interact"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveFlightWorldUse)));
                item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity);
                flightFixture = item.GetComponent(type); flightAllowExplosion = false;
                foreach (var method in AccessTools.GetDeclaredMethods(type).Where(m => m.Name == "ServerExplode"))
                    probe.Patch(method, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(GuardFixtureExplosion)));
                probe.Patch(AccessTools.Method(type, "ApplyForce"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveFlightForce)));
                probe.Patch(AccessTools.Method(type, "OnCollisionEnter"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveFlightImpact)));
                probe.Patch(AccessTools.Method(type, "CmdReportImpactExplosion"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveFlightImpactCommand)));
                Component held = item.GetComponent(Find("HeldItemInteractable"));
                NetworkServer.Spawn(item);
                AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 8;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native FPV pickup did not complete"); yield return null; }
                // The cached head query ran before this frame's pickup. Wait
                // for ownership-aware collision filtering before pressing use.
                yield return new WaitForEndOfFrame();
                manager.Pointer.Cancel();
                manager.Rig.Right.Aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.up * 2, Quaternion.LookRotation(Vector3.up));
                Input("LeftGrip", false); Input("Grip", false); Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                AccessTools.Method(manager.Pointer.GetType(), "LateUpdate").Invoke(manager.Pointer, null);
                Logger.LogInfo("FLIGHT INPUT: blocked=" + manager.Rig.HeadObstructed + "; panel=" + manager.Pointer.HasPanelHit
                    + "; held=" + ReferenceEquals(Member(interactor, "CurrentHeldItem"), held) + "; pressed=" + Member(interactor, "wasAttackPressed"));
                Input("TriggerDown", false); Input("Trigger", false); manager.Pointer.Cancel();
                deadline = Time.realtimeSinceStartup + 8;
                while (!(Member(flightFixture, "WasLaunched") is bool launched && launched)
                    || !(Member(flightFixture, "Flyer") is Component flyer && flyer.gameObject == NetworkClient.localPlayer.gameObject))
                { Require(Time.realtimeSinceStartup < deadline, "Controller trigger did not launch native FPV with local flyer ownership; launched="
                    + Member(flightFixture, "WasLaunched") + "; flyer=" + (Member(flightFixture, "Flyer") != null)); yield return null; }
                Logger.LogInfo("FLIGHT LAUNCH PASS: native controller pickup/trigger, launch authority, flyer ownership and held release");
                Drive(new Vector2(.25f, .75f), Vector2.zero, false);
                Require(Member(flightFixture, "localMoveInput") is Vector2 moveInput && moveInput == flightMove,
                    "VR movement does not reach the native FPV flight event");
                Require(Member(controller, "movementVector") is Vector2 bodyMove && bodyMove == Vector2.zero,
                    "FPV control moved the blocked pilot body");
                Logger.LogInfo("FLIGHT MOVE PASS: controller stick reaches native FPV movement while pilot body remains blocked");
                var body = Member(Member(held, "ephemeralRigidbody"), "Rigidbody") as Rigidbody;
                Require(body != null && !body.isKinematic, "Native FPV did not obtain an active flight rigidbody");
                flightForceCalls = 0;
                Vector3 velocity = body.linearVelocity;
                deadline = Time.realtimeSinceStartup + 3;
                while (flightForceCalls == 0 || Vector3.ProjectOnPlane(body.linearVelocity - velocity, Vector3.up).magnitude < .01f)
                { Require(Time.realtimeSinceStartup < deadline, "Native FPV stick input produced no physical horizontal acceleration"); yield return new WaitForFixedUpdate(); }
                Require(flightForce.sqrMagnitude > 0, "Native FPV force callback had no force");
                var eye = Member(flightFixture, "cameraGameObject") as GameObject;
                yield return new WaitForEndOfFrame();
                Require(eye != null && Vector3.Distance(manager.Rig.AuthoredEye, eye.transform.position) < .4f,
                    "VR view did not follow native FPV camera position");
                var pose = (Multiplayer.PoseFrame)AccessTools.Field(typeof(Multiplayer.PoseNetwork), "local").GetValue(Multiplayer.PoseNetwork.Instance);
                Require(pose.Tracked == 0, "FPV view retained tracked-avatar ownership on the grounded pilot body");
                Logger.LogInfo("FLIGHT PHYSICS PASS: native stick force changes rigidbody velocity, view follows aircraft camera and tracked pilot pose is released");
                var headRenderers = new List<Renderer>();
                foreach (Component visibility in ((Array)Member(controller, "meshRendererToggles")).Cast<Component>())
                    if (visibility != null && visibility.gameObject.activeInHierarchy)
                        foreach (var renderer in visibility.GetComponentsInChildren<Renderer>())
                            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                                if ((bool)AccessTools.Method(visibility.GetType(), "IsHeadVisual").Invoke(visibility, new object[] { renderer.transform })
                                    && !headRenderers.Contains(renderer)) headRenderers.Add(renderer);
                Require(headRenderers.Count > 0 && headRenderers.All(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),
                    "Remote FPV view left the pilot's native head visuals hidden");
                if (Environment.GetCommandLineArgs().Contains("--swyf-flight-despawn-tests"))
                {
                    Drive(Vector2.zero, Vector2.zero, false);
                    Vector3 displacement = NetworkClient.localPlayer.transform.position + Vector3.up * 10 - item.transform.position;
                    item.transform.position += displacement; body.position += displacement;
                    body.linearVelocity = body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
                    yield return new WaitForSecondsRealtime(.9f);
                    Require(Vector3.Distance(manager.Rig.AuthoredEye, pilotEye) > 2, "Despawn fixture did not establish a remote aircraft view");
                    var polar = AccessTools.Property(adapter, "Polar").GetValue(null) as Transform;
                    Quaternion pilotRotation = polar.rotation;
                    flightHoldAfterDespawn = true; Drive(Vector2.up, Vector2.right, true); TriggerClimb(true);
                    NetworkServer.Destroy(item); item = null;
                    yield return null; yield return new WaitForEndOfFrame();
                    Require(Member(controller, "movementVector") is Vector2 heldMove && heldMove == Vector2.zero
                        && Member(controller, "isJumping") is bool heldJump && !heldJump && Quaternion.Angle(polar.rotation, pilotRotation) < .1f,
                        "Held flight controls became pilot movement, jump or snap-turn after aircraft destruction");
                    flightWorldPresses = 0; flightWorldProbe = true;
                    try
                    {
                        Input("LeftTriggerDown", true);
                        AccessTools.Method(manager.Pointer.GetType(), "LateUpdate").Invoke(manager.Pointer, null);
                    }
                    finally { Input("LeftTriggerDown", false); flightWorldProbe = false; }
                    Require(!manager.Pointer.HasPanelHit && flightWorldPresses == 0
                        && !(bool)AccessTools.Field(manager.Pointer.GetType(), "leftWorldDown").GetValue(manager.Pointer),
                        "Flight handoff routed held climb into a world interaction");
                    Type releaseHudType = Find("ScammerGameHudController");
                    var releaseHud = UnityEngine.Object.FindAnyObjectByType(releaseHudType) as Component;
                    AccessTools.Method(releaseHudType, "RefreshPlayerMenuHint").Invoke(releaseHud, null);
                    var releaseHints = Member(releaseHud, "playerMenuHint") as VisualElement;
                    Require(releaseHints?.Q<Label>("swyf-vr-flight-release-key")?.text == manager.Input.FlightReleaseHint,
                        "Flight handoff did not explain its neutral-release gate on the HUD");
                    Drive(Vector2.zero, Vector2.zero, false);
                    Drive(Vector2.up, Vector2.right, false);
                    Require(Member(controller, "movementVector") is Vector2 triggerHeldMove && triggerHeldMove == Vector2.zero
                        && !(bool)Member(controller, "isJumping") && Quaternion.Angle(polar.rotation, pilotRotation) < .1f,
                        "Held left-trigger climb released the pilot handoff gate");
                    TriggerClimb(false); Drive(Vector2.zero, Vector2.zero, false);
                    Drive(Vector2.up, Vector2.zero, true);
                    Require(Member(controller, "movementVector") is Vector2 freshMove && freshMove.sqrMagnitude > .1f
                        && Member(controller, "isJumping") is bool freshJump && freshJump,
                        "Fresh neutral-then-press input did not restore pilot movement/jump");
                    Drive(Vector2.zero, Vector2.zero, false); flightHoldAfterDespawn = false;
                    AccessTools.Method(releaseHudType, "RefreshPlayerMenuHint").Invoke(releaseHud, null);
                    Require(releaseHints.Q("swyf-vr-flight-release") == null, "Released flight controls retained the handoff HUD prompt");
                    deadline = Time.realtimeSinceStartup + 3;
                    while (Vector3.Distance(manager.Rig.AuthoredEye, pilotEye) >= 1)
                    { Require(Time.realtimeSinceStartup < deadline, "Destroying the active aircraft left the camera at its stale flight offset"); yield return null; }
                    yield return new WaitForSecondsRealtime(.15f);
                    Require(Member(Member(controller, "blocker"), "CanTakeInput") is bool unblocked && unblocked,
                        "Destroying the aircraft retained the native pilot input blocker");
                    pose = (Multiplayer.PoseFrame)AccessTools.Field(typeof(Multiplayer.PoseNetwork), "local").GetValue(Multiplayer.PoseNetwork.Instance);
                    Require(pose.Tracked != 0, "Destroying the aircraft retained tracked-pilot suppression");
                    Logger.LogInfo("FLIGHT DESPAWN PASS: actual active-aircraft destruction clears native input blockers, returns pilot view and resumes tracked avatar");
                    yield break;
                }
                Type hudType = Find("ScammerGameHudController");
                var hud = UnityEngine.Object.FindAnyObjectByType(hudType) as Component;
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                var hints = Member(hud, "playerMenuHint") as VisualElement;
                Require(hints?.Q<Label>("swyf-vr-flight-move-key")?.text == manager.Input.MoveHint
                    && hints.Q<Label>("swyf-vr-flight-turn-key")?.text == manager.Input.TurnHint
                    && hints.Q<Label>("swyf-vr-flight-climb-key")?.text == manager.Input.FlightClimbHint, "Native HUD lacks controller flight controls");
                float yaw = Convert.ToSingle(Member(flightFixture, "lookYawDegrees"));
                float pitch = Convert.ToSingle(Member(flightFixture, "lookPitchDegrees"));
                Drive(new Vector2(.25f, .75f), new Vector2(.5f, .25f), false);
                // Arrange the old foot-jump hold that can still be present on
                // the first controller frame after flight ownership changes.
                Input("Jump", true); Input("JumpDown", true);
                AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRJumpControls"), "Read")
                    .Invoke(null, new object[] { controller, manager.Input, true });
                AccessTools.Field(controller.GetType(), "isJumping").SetValue(controller, true);
                Input("Jump", false); Input("JumpDown", false); TriggerClimb(true);
                deadline = Time.realtimeSinceStartup + 3;
                while (!(Member(flightFixture, "NetworkverticalAccelerationActive") is bool triggerLift && triggerLift))
                { Require(Time.realtimeSinceStartup < deadline, "Left trigger did not reach native FPV climb while both sticks are in use"); yield return new WaitForFixedUpdate(); }
                Require(Member(flightFixture, "localMoveInput") is Vector2 triggerMove && triggerMove == flightMove
                    && Convert.ToSingle(Member(flightFixture, "lookYawDegrees")) < yaw,
                    "Left-trigger climb prevented simultaneous native movement/steering");
                TriggerClimb(false); Drive(Vector2.zero, Vector2.zero, false);
                deadline = Time.realtimeSinceStartup + 3;
                while (Member(flightFixture, "NetworkverticalAccelerationActive") is bool triggerLift && triggerLift)
                { Require(Time.realtimeSinceStartup < deadline, "Releasing left trigger left native FPV climb active"); yield return new WaitForFixedUpdate(); }
                Logger.LogInfo("FLIGHT TRIGGER PASS: left trigger controls native climb/release with movement and steering available on both sticks");
                yaw = Convert.ToSingle(Member(flightFixture, "lookYawDegrees")); pitch = Convert.ToSingle(Member(flightFixture, "lookPitchDegrees"));
                Drive(Vector2.zero, new Vector2(.5f, .25f), true);
                deadline = Time.realtimeSinceStartup + 3;
                while (!(Member(flightFixture, "NetworkverticalAccelerationActive") is bool climb && climb))
                { Require(Time.realtimeSinceStartup < deadline, "Controller climb did not reach native vertical acceleration"); yield return new WaitForFixedUpdate(); }
                Require(Convert.ToSingle(Member(flightFixture, "lookYawDegrees")) < yaw
                    && Convert.ToSingle(Member(flightFixture, "lookPitchDegrees")) > pitch,
                    "Right-stick steering did not reach the native yaw/pitch controller");
                Require(Vector3.Angle(manager.Rig.Head.transform.up, Vector3.up) < .1f,
                    "Aircraft steering imposed native pitch/roll on the upright mock headset");
                Drive(Vector2.zero, Vector2.zero, false);
                deadline = Time.realtimeSinceStartup + 3;
                while (Member(flightFixture, "NetworkverticalAccelerationActive") is bool climb && climb)
                { Require(Time.realtimeSinceStartup < deadline, "Releasing climb left native vertical acceleration active"); yield return new WaitForFixedUpdate(); }
                Logger.LogInfo("FLIGHT STEER PASS: right stick drives native yaw/pitch; held climb and release synchronize native vertical acceleration");
                TriggerClimb(true);
                toggle.Invoke(null, null); paused = true;
                yaw = Convert.ToSingle(Member(flightFixture, "lookYawDegrees"));
                Drive(Vector2.up, Vector2.one, true);
                Require(Member(flightFixture, "localMoveInput") is Vector2 menuMove && menuMove == Vector2.zero
                    && !(bool)Member(flightFixture, "localIsJumpPressed") && Convert.ToSingle(Member(flightFixture, "lookYawDegrees")) == yaw,
                    "Settings allowed native flight movement/steering/climb");
                yield return new WaitForSecondsRealtime(.6f);
                var settingsDoc = UnityEngine.Object.FindObjectsByType(Find("PauseMenuToolkitController"), FindObjectsSortMode.None)
                    .OfType<Component>().Where(c => c.gameObject.activeInHierarchy).Select(c => c.GetComponent<UIDocument>())
                    .FirstOrDefault(d => d?.rootVisualElement?.Q<Button>("swyf-vr-flight-steering") != null);
                Require(settingsDoc != null, "Native flight steering settings UI unavailable");
                var settingsSurface = manager.UI.Panels.FirstOrDefault(p => p != null && p.Settings == settingsDoc.panelSettings);
                Button steeringControl = settingsDoc.rootVisualElement.Q<Button>("swyf-vr-flight-steering");
                var savedSurface = appFixtureSurface; var savedPoint = appFixturePoint; bool savedAim = appFixtureAim;
                try
                {
                    appFixtureSurface = settingsSurface; appFixturePoint = steeringControl.worldBound.center; appFixtureAim = true; AimAppFixture();
                }
                finally { appFixtureSurface = savedSurface; appFixturePoint = savedPoint; appFixtureAim = savedAim; }
                menuObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                menuObstacle.name = "SWYF owned aircraft menu-occlusion probe";
                menuObstacle.transform.position = manager.Rig.Right.Aim.position + manager.Rig.Right.Aim.forward * .15f;
                menuObstacle.transform.localScale = Vector3.one * .12f;
                menuObstacle.transform.SetParent(item.transform, true);
                var menuCollider = menuObstacle.GetComponent<Collider>();
                foreach (var collider in item.GetComponentsInChildren<Collider>().Concat(body.GetComponentsInChildren<Collider>()))
                    if (collider != menuCollider) Physics.IgnoreCollision(menuCollider, collider);
                Physics.SyncTransforms(); Plugin.OccludeFloatingUI.Value = true;
                var clickSteering = ClickAppControl(settingsSurface, steeringControl);
                try { while (clickSteering.MoveNext()) yield return clickSteering.Current; }
                finally { (clickSteering as IDisposable)?.Dispose(); }
                Require(Plugin.FlightSteeringSpeed.Value != originalSteering, "Aircraft geometry blocked controller menu clicking with occlusion enabled");
                Plugin.FlightSteeringSpeed.Value = originalSteering; Plugin.OccludeFloatingUI.Value = originalOcclusion;
                menuCollider.enabled = false; Destroy(menuObstacle); menuObstacle = null;
                Logger.LogInfo("FLIGHT MENU PASS: controller changes native VR steering option through owned-aircraft geometry with menu occlusion enabled");
                toggle.Invoke(null, null); paused = false;
                Drive(Vector2.zero, Vector2.zero, true);
                Require(!(bool)Member(flightFixture, "localIsJumpPressed"), "Held climb resumed after settings without a fresh press");
                Input("HasInputFocus", false); Drive(Vector2.up, Vector2.one, true);
                Require(Member(flightFixture, "localMoveInput") is Vector2 focusMove && focusMove == Vector2.zero
                    && !(bool)Member(flightFixture, "localIsJumpPressed") && Convert.ToSingle(Member(flightFixture, "lookYawDegrees")) == yaw,
                    "Focus loss allowed native flight movement/steering/climb");
                Input("HasInputFocus", focused); Drive(Vector2.zero, Vector2.zero, true);
                Require(!(bool)Member(flightFixture, "localIsJumpPressed"), "Held climb resumed after focus returned without a fresh press");
                AccessTools.Method(feedback, "Show").Invoke(null, null); feedbackOwned = true;
                Drive(Vector2.up, Vector2.one, true);
                Require(Member(flightFixture, "localMoveInput") is Vector2 modalMove && modalMove == Vector2.zero
                    && !(bool)Member(flightFixture, "localIsJumpPressed"), "Native feedback allowed flight movement/climb");
                AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null); feedbackOwned = false;
                Drive(Vector2.zero, Vector2.zero, true);
                Require(!(bool)Member(flightFixture, "localIsJumpPressed"), "Held climb resumed after feedback without a fresh press");
                TriggerClimb(false); Drive(Vector2.zero, Vector2.zero, false); TriggerClimb(true);
                Require((bool)Member(flightFixture, "localIsJumpPressed"), "Fresh left-trigger press did not restore climb after overlays");
                TriggerClimb(false);
                Logger.LogInfo("FLIGHT INPUT PASS: native movement, steering and climb stop behind settings, feedback and lost input focus");
                Type controls = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRFlightControls", true);
                Drive(Vector2.up, Vector2.zero, true);
                AccessTools.Method(controls, "Clear").Invoke(null, null);
                Require(Member(flightFixture, "localMoveInput") is Vector2 cleared && cleared == Vector2.zero && !(bool)Member(flightFixture, "localIsJumpPressed"),
                    "Flight cleanup left native input held");
                AccessTools.Method(controls, "Initialize").Invoke(null, null);
                Require(ReferenceEquals(AccessTools.Property(controls, "Current").GetValue(null), flightFixture), "Session initialization did not recover the existing local flyer");
                Drive(Vector2.zero, Vector2.zero, false);
                Logger.LogInfo("FLIGHT RECOVERY PASS: session input cleanup clears native holds and initialization rediscovers the existing local flight");
                deadline = Time.realtimeSinceStartup + Convert.ToSingle(Member(flightFixture, "impactExplodeDelayAfterLaunchSeconds")) + 3;
                while (!(bool)AccessTools.Method(type, "CanExplodeFromImpactNow").Invoke(flightFixture, null))
                { Require(Time.realtimeSinceStartup < deadline, "Native crash arming delay did not expire"); yield return null; }
                // Place this owned aircraft far from the pilot/office before
                // allowing its actual native explosive end-of-flight path.
                Vector3 detonation = NetworkClient.localPlayer.transform.position + new Vector3(100, 40, 0);
                Vector3 relocation = detonation - item.transform.position;
                item.transform.position = detonation; body.position += relocation;
                body.linearVelocity = body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
                yield return new WaitForFixedUpdate(); yield return new WaitForEndOfFrame();
                Require(Vector3.Distance(item.transform.position, NetworkClient.localPlayer.transform.position) > 80,
                    "Native FPV root did not relocate away from the pilot before detonation");
                var existing = new HashSet<uint>(NetworkServer.spawned.Keys);
                var solid = body.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled && !c.isTrigger);
                Require(solid != null, "Native FPV has no solid flight collider");
                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name = "SWYF isolated native FPV crash obstacle";
                obstacle.transform.position = body.position - Vector3.up * 4;
                obstacle.transform.localScale = new Vector3(20, 1, 20);
                int layer = Enumerable.Range(0, 32).FirstOrDefault(candidate => !Physics.GetIgnoreLayerCollision(solid.gameObject.layer, candidate));
                Require(!Physics.GetIgnoreLayerCollision(solid.gameObject.layer, layer), "Native aircraft has no colliding physics layer");
                obstacle.layer = layer; Physics.SyncTransforms();
                flightImpacts = flightImpactCommands = 0; flightImpactSpeed = 0;
                flightAllowExplosion = true;
                float crashSpeed = Convert.ToSingle(Member(flightFixture, "kaboomSpeedThreshold")) + 15;
                body.linearVelocity = Vector3.down * crashSpeed;
                deadline = Time.realtimeSinceStartup + 8;
                while (!(Member(flightFixture, "Networkexploded") is bool exploded && exploded))
                { Require(Time.realtimeSinceStartup < deadline, "Actual FPV collision did not trigger native explosion; impacts=" + flightImpacts + "; commands=" + flightImpactCommands); yield return new WaitForFixedUpdate(); }
                Require(flightImpacts > 0 && flightImpactSpeed > Convert.ToSingle(Member(flightFixture, "kaboomSpeedThreshold")),
                    "Native FPV explosion did not originate from a physical threshold-crossing impact");
                Logger.LogInfo("FLIGHT IMPACT PASS: actual owned obstacle collision crosses native speed/arming gates and triggers the native explosion; impact commands=" + flightImpactCommands);
                foreach (var pair in NetworkServer.spawned)
                    if (!existing.Contains(pair.Key) && pair.Value != null
                        && (pair.Value.GetComponent(Find("DynamiteInteractable")) != null || pair.Value.GetComponent(Find("SmokeBombInteractable")) != null))
                        effects.Add(pair.Value.gameObject);
                Require((bool)Member(flightFixture, "Networkexploded"), "Native FPV did not enter its exploded state");
                Require(effects.Count > 0 || Member(flightFixture, "dynamitePrefab") == null && Member(flightFixture, "smokePrefab") == null,
                    "Native aircraft end did not spawn its configured explosive/smoke effect");
                Logger.LogInfo("FLIGHT DETONATION: prefab=" + prefab.name + "; aircraft=" + item.transform.position
                    + "; pilot=" + NetworkClient.localPlayer.transform.position + "; effects=" + effects.Count);
                deadline = Time.realtimeSinceStartup + 8;
                while (Member(flightFixture, "Flyer") is Component flyer && flyer != null)
                { Require(Time.realtimeSinceStartup < deadline, "Native FPV did not release its flyer after exploding"); yield return null; }
                yield return new WaitForSecondsRealtime(.3f);
                Logger.LogInfo("FLIGHT RETURN: before=" + pilotEye + "; after=" + manager.Rig.AuthoredEye + "; body=" + NetworkClient.localPlayer.transform.position
                    + "; ragdoll=" + Member(NetworkClient.localPlayer.GetComponent(Find("PlayerRagdollManager")), "IsRagdollActive"));
                Require(AccessTools.Property(controls, "Current").GetValue(null) == null
                    && Vector3.Distance(manager.Rig.AuthoredEye - NetworkClient.localPlayer.transform.position, pilotEyeOffset) < .2f,
                    "Native flight end did not restore the pilot view and clear VR flight ownership; owned="
                    + (AccessTools.Property(controls, "Current").GetValue(null) != null) + "; before=" + pilotEye
                    + "; after=" + manager.Rig.AuthoredEye + "; body=" + NetworkClient.localPlayer.transform.position
                    + "; beforeBody=" + pilotBody + "; beforeOffset=" + pilotEyeOffset
                    + "; afterOffset=" + (manager.Rig.AuthoredEye - NetworkClient.localPlayer.transform.position));
                pose = (Multiplayer.PoseFrame)AccessTools.Field(typeof(Multiplayer.PoseNetwork), "local").GetValue(Multiplayer.PoseNetwork.Instance);
                Require(pose.Tracked != 0, "Native flight end did not restore tracked-avatar ownership");
                Require(headRenderers.All(r => r == null || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),
                    "Returning from FPV exposed the pilot's head inside the first-person camera");
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                Require(hints.Q("swyf-vr-flight-move") == null && hints.Q("swyf-vr-flight-climb") == null, "Native flight end retained its HUD keys");
                Logger.LogInfo("FLIGHT END PASS: native explosion/release clears flyer controls, restores pilot camera and resumes tracked avatar; aircraft relocated away from pilot before detonation");
            }
            finally
            {
                flightMove = flightTurn = Vector2.zero; flightJump = flightJumpDown = flightLeftTrigger = flightLeftTriggerDown = false;
                KeepFlightInput(manager.Input);
                flightHoldAfterDespawn = false;
                Input("Trigger", false); Input("TriggerDown", false); Input("Jump", false); Input("HasInputFocus", focused); manager.Pointer.Cancel();
                if (paused) toggle.Invoke(null, null);
                Plugin.OccludeFloatingUI.Value = originalOcclusion; Plugin.FlightSteeringSpeed.Value = originalSteering;
                if (feedbackOwned) AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null);
                if (flightFixture != null)
                    AccessTools.Method(type, "ServerReleaseFlyerAfterExplosion").Invoke(flightFixture, null);
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item.GetComponent(Find("HeldItemInteractable"))))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                probe.UnpatchSelf(); flightFixture = null;
                foreach (var effect in effects) if (effect != null) NetworkServer.Destroy(effect);
                if (obstacle != null) Destroy(obstacle);
                if (menuObstacle != null) Destroy(menuObstacle);
                controllerTick.Invoke(null, new[] { controller });
            }
        }
    }
}
