using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool tackleFixture, tackleGrip, tackleSecondary, tackleLeftGrip;
        private static Component tackleProbe;
        private static int tackleStarts;
        private static void KeepTackleInput(object input)
        {
            if (!tackleFixture) return;
            void Set(string name, object value) => AccessTools.Property(input.GetType(), name).SetValue(input, value);
            Set("Grip", tackleGrip); Set("GripDown", false); Set("GripUp", false); Set("LeftGrip", tackleLeftGrip);
            Set("Secondary", tackleSecondary); Set("SecondaryDown", false); Set("Move", Vector2.zero); Set("Turn", Vector2.zero);
        }
        private static void ObserveTackle(object __instance, bool __0)
        { if (__0 && ReferenceEquals(__instance, tackleProbe)) tackleStarts++; }

        private IEnumerator TestNativeTackle()
        {
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            var interactor = player.GetComponent(Find("PlayerInteractor"));
            var mode = player.GetComponent(Find("PlayerModeManager"));
            var tackle = player.GetComponent(Find("PlayerTackle"));
            Require(tackle != null && !(bool)Member(tackle, "LocalIsTackling") && Member(interactor, "CurrentHeldItem") == null,
                "Tackle fixture requires native component and empty hands");
            bool originalRole = (bool)Member(mode, "isAgent"), focus = manager.Input.HasInputFocus, paused = false;
            Vector3 originalPosition = player.transform.position, isolated = new Vector3(1050, 50, 1050);
            var setRole = AccessTools.Method(mode.GetType(), "ServerSetIsAgent");
            object controller = Instance("Metater.MetaPlayerController");
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            var body = Member(controller, "CharacterController") as CharacterController;
            Require(body != null, "Tackle fixture requires native body");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            var canTackle = AccessTools.Method(tackle.GetType(), "CanTackle");
            var probe = new Harmony("com.swyf.vr.tacklefixture");
            GameObject floor = null, item = null, obstruction = null;
            object inputBlockers = Member(Member(controller, "blocker"), "InputBlockers");
            bool ownsBlocker = false;
            bool secondaryBefore = false;
            void Drive(bool value, bool grip = true, bool leftGrip = false)
            {
                tackleGrip = grip; tackleLeftGrip = leftGrip; tackleSecondary = value;
                KeepTackleInput(manager.Input);
                AccessTools.Property(manager.Input.GetType(), "SecondaryDown").SetValue(manager.Input, value && !secondaryBefore);
                manager.Rig.Right.Aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.right * .35f, Quaternion.LookRotation(Vector3.up));
                tick.Invoke(manager.Pointer, null); secondaryBefore = value;
                AccessTools.Property(manager.Input.GetType(), "SecondaryDown").SetValue(manager.Input, false);
            }
            try
            {
                tackleFixture = true; manager.Pointer.Cancel(); tackleProbe = tackle; tackleStarts = 0;
                probe.Patch(AccessTools.Method(tackle.GetType(), "CmdSetIsDiving"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveTackle)));
                setRole.Invoke(mode, new object[] { true });
                yield return new WaitForSecondsRealtime(.8f);
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "SWYF isolated tackle floor";
                floor.transform.localScale = new Vector3(100, .2f, 100);
                floor.transform.position = isolated + Vector3.up * (body.bounds.min.y - player.transform.position.y - .12f);
                Physics.SyncTransforms();
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                teleport.Invoke(controller, new object[] { isolated, true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(1.2f);
                Require((bool)Member(tackle, "IsAgent") && (bool)canTackle.Invoke(tackle, null) && !manager.Rig.HeadObstructed,
                    "Native agent tackle did not become eligible");
                Drive(true, false);
                Require(tackleStarts == 0 && (bool)Member(interactor, "emptyHandZoomHeld"), "Bare secondary lost native empty-hand behaviour");
                Drive(false, false); Drive(true, true, true); Drive(false, true, true);
                Require(tackleStarts == 0, "Left-grip hip-swap chord became a tackle");
                toggle.Invoke(null, null); paused = true; Drive(true);
                Require(tackleStarts == 0, "Settings accepted a tackle press");
                toggle.Invoke(null, null); paused = false;
                yield return new WaitForSecondsRealtime(.2f);
                Require(tackleStarts == 0, "Closing settings replayed held tackle input");
                Drive(false);
                Logger.LogInfo("TACKLE GUARD PASS: bare secondary and left-grip hip swap retain native routing; settings block tackle and held input does not resume it");
                ownsBlocker = (bool)AccessTools.Method(inputBlockers.GetType(), "Add").Invoke(inputBlockers, new object[] { this });
                Require(ownsBlocker, "Tackle fixture did not acquire its own native input blocker");
                Drive(true);
                Require(tackleStarts == 0 && !(bool)Member(interactor, "emptyHandZoomHeld"), "Blocked tackle became a native dive or zoom/grab");
                Drive(false); AccessTools.Method(inputBlockers.GetType(), "Remove").Invoke(inputBlockers, new object[] { this }); ownsBlocker = false;
                obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube); obstruction.name = "SWYF tackle head obstruction";
                obstruction.transform.position = manager.Rig.Head.transform.position; obstruction.transform.localScale = Vector3.one * .5f;
                Physics.IgnoreCollision(obstruction.GetComponent<Collider>(), body, true); Physics.SyncTransforms();
                float deadline = Time.realtimeSinceStartup + 3;
                while (!manager.Rig.HeadObstructed)
                { Require(Time.realtimeSinceStartup < deadline, "Actual tackle head-obstruction fixture was not detected"); yield return null; }
                Drive(true); Require(tackleStarts == 0, "Obstructed headset allowed native tackle"); Drive(false);
                Destroy(obstruction); obstruction = null; deadline = Time.realtimeSinceStartup + 3;
                while (manager.Rig.HeadObstructed)
                { Require(Time.realtimeSinceStartup < deadline, "Tackle head obstruction did not clear"); yield return null; }
                Logger.LogInfo("TACKLE BLOCK PASS: native input blockers consume the gesture without zoom/grab and actual headset obstruction rejects the dive");
                var cameraType = Find("PolaroidInteractable");
                var prefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(cameraType) != null);
                Require(prefab != null, "Tackle item-routing check needs native polaroid prefab");
                item = Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity); NetworkServer.Spawn(item);
                var held = item.GetComponent(Find("HeldItemInteractable"));
                var cameraItem = item.GetComponent(cameraType);
                AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                deadline = Time.realtimeSinceStartup + 6;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native held item did not attach for tackle routing check"); yield return null; }
                bool selfie = (bool)Member(cameraItem, "NetworkselfieMode");
                Drive(true); Drive(false); deadline = Time.realtimeSinceStartup + 4;
                while ((bool)Member(cameraItem, "NetworkselfieMode") == selfie)
                { Require(Time.realtimeSinceStartup < deadline, "Grip + secondary did not retain the held item's native action"); yield return null; }
                Require(tackleStarts == 0, "Holding an item allowed tackle to steal its secondary action");
                AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                yield return new WaitForSecondsRealtime(.2f); NetworkServer.Destroy(item); item = null;
                Logger.LogInfo("TACKLE ITEM PASS: grip + secondary switches native polaroid selfie mode without starting a dive");
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.Euler(0, 35, 0));
                yield return new WaitForSecondsRealtime(.4f); yield return new WaitForEndOfFrame();
                Vector3 before = player.transform.position, heading = Vector3.ProjectOnPlane(manager.Rig.Head.transform.forward, Vector3.up).normalized;
                Vector3 eyeOffset = manager.Rig.Head.transform.position - before;
                float headYaw = manager.Rig.Head.transform.eulerAngles.y;
                Drive(true);
                Require((bool)Member(tackle, "LocalIsTackling") && tackleStarts == 1 && !(bool)Member(interactor, "emptyHandZoomHeld"),
                    "Empty-handed grip + secondary failed to start exactly one native tackle");
                Drive(false); Drive(true);
                Require(tackleStarts == 1 && !(bool)Member(interactor, "emptyHandZoomHeld"), "Native tackle cooldown became zoom/grab or allowed another dive");
                deadline = Time.realtimeSinceStartup + 6;
                while ((bool)Member(tackle, "LocalIsTackling") || !(bool)canTackle.Invoke(tackle, null))
                { Require(Time.realtimeSinceStartup < deadline, "Native tackle animation/blocker/cooldown did not recover"); yield return null; }
                Vector3 moved = Vector3.ProjectOnPlane(player.transform.position - before, Vector3.up);
                Require(moved.magnitude > 1 && Vector3.Dot(moved.normalized, heading) > .95f,
                    "Native tackle motor did not move along headset heading; displacement=" + moved + "; heading=" + heading);
                Require(tackleStarts == 1, "Held tackle button restarted after native cooldown expired");
                Require(Mathf.Abs(Mathf.DeltaAngle(headYaw, manager.Rig.Head.transform.eulerAngles.y)) < 1
                    && Vector3.Distance(manager.Rig.Head.transform.position - player.transform.position, eyeOffset) < .15f,
                    "Native tackle recovery rotated the view or displaced the standing eye anchor");
                Logger.LogInfo("TACKLE MOTOR PASS: controller press starts one native dive/authority command, moves along headset heading and recovers without held-input replay; distance=" + moved.magnitude);
                Drive(false); Drive(true);
                Require(tackleStarts == 2 && (bool)Member(tackle, "LocalIsTackling"), "Fresh tackle did not recover after native cooldown");
                Drive(false); deadline = Time.realtimeSinceStartup + 6;
                while ((bool)Member(tackle, "LocalIsTackling") || !(bool)canTackle.Invoke(tackle, null))
                { Require(Time.realtimeSinceStartup < deadline, "Second native tackle did not recover"); yield return null; }
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, false); Drive(true);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus);
                yield return new WaitForSecondsRealtime(.2f);
                Require(tackleStarts == 2, "Focus recovery replayed held tackle input");
                Drive(false);
                setRole.Invoke(mode, new object[] { false }); yield return new WaitForSecondsRealtime(.8f);
                Drive(true);
                Require(tackleStarts == 2 && (bool)Member(interactor, "emptyHandZoomHeld"), "Scammer secondary input was consumed as an agent tackle");
                Drive(false);
                Logger.LogInfo("TACKLE RECOVERY PASS: fresh press recovers after cooldown; focus restoration does not replay input and scammer secondary stays native");
            }
            finally
            {
                if (paused) toggle.Invoke(null, null);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus);
                Drive(false, false); manager.Pointer.Cancel(); tackleFixture = false;
                if (ownsBlocker) AccessTools.Method(inputBlockers.GetType(), "Remove").Invoke(inputBlockers, new object[] { this });
                if (obstruction != null) Destroy(obstruction);
                AccessTools.Method(tackle.GetType(), "LocalStopTackleAnimationForRagdoll").Invoke(tackle, null);
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item.GetComponent(Find("HeldItemInteractable"))))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                probe.UnpatchSelf(); tackleProbe = null;
                setRole.Invoke(mode, new object[] { originalRole });
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter();
                if (floor != null) Destroy(floor);
            }
            Logger.LogInfo("TACKLE COMPLETE PASS: native controller gesture, held-item routing, motor/cooldown, focus/role recovery and owned cleanup checked");
        }
    }
}
