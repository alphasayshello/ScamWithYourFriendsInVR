using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static Component ladderFixture;
        private static Vector2 ladderMove;
        private static int ladderDismountCommands;
        private static bool ladderMountAim;
        private static Vector3 ladderAimOrigin;
        private static Quaternion ladderAimRotation;
        private static void AimLadderFixture()
        {
            if (!ladderMountAim) return;
            VRManager.Instance.Rig.Right.Aim.SetPositionAndRotation(ladderAimOrigin, ladderAimRotation);
            AccessTools.Method(VRManager.Instance.Pointer.GetType(), "ResetAim").Invoke(VRManager.Instance.Pointer, null);
        }
        private static void ObserveLadderDismount(object __instance)
        { if (ReferenceEquals(__instance, ladderFixture)) ladderDismountCommands++; }
        private static bool ladderJump, ladderJumpDown, ladderHoldAfterDespawn;
        private static void KeepLadderInput(object input)
        {
            if (ladderFixture == null && !ladderHoldAfterDespawn) return;
            AccessTools.Property(input.GetType(), "Move").SetValue(input, ladderMove);
            AccessTools.Property(input.GetType(), "Jump").SetValue(input, ladderJump);
            AccessTools.Property(input.GetType(), "JumpDown").SetValue(input, ladderJumpDown);
            AccessTools.Property(input.GetType(), "Turn").SetValue(input, Vector2.zero);
        }
        private IEnumerator TestNativeLadder()
        {
            var manager = VRManager.Instance;
            Type extensionType = Find("ExtensionLadderInteractable");
            var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            var prefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(extensionType) != null);
            Require(prefab != null && Member(interactor, "CurrentHeldItem") == null, "Ladder fixture needs native extension ladder and empty hands");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            object controller = AccessTools.Property(adapter, "Controller").GetValue(null);
            var controllerTick = AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches"), "BeforeController");
            var pointerTick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            void Input(string name, bool value) => AccessTools.Property(manager.Input.GetType(), name).SetValue(manager.Input, value);
            void Drive(Vector2 move, bool jump = false)
            {
                ladderMove = move; ladderJumpDown = jump && !ladderJump; ladderJump = jump;
                KeepLadderInput(manager.Input); controllerTick.Invoke(null, new[] { controller });
                ladderJumpDown = false; Input("JumpDown", false);
            }
            var probe = new Harmony("com.swyf.vr.ladderfixture");
            GameObject item = null;
            Component held = null;
            bool paused = false, focus = manager.Input.HasInputFocus;
            try
            {
                item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity);
                Component extension = item.GetComponent(extensionType); ladderFixture = Member(extension, "Ladder") as Component;
                Require(ladderFixture != null, "Native extension ladder has no climb component");
                probe.Patch(AccessTools.Method(interactor.GetType(), "HandleInteractionRay"),
                    prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AimLadderFixture)));
                probe.Patch(pointerTick, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AimLadderFixture)));
                probe.Patch(AccessTools.Method(ladderFixture.GetType(), "CmdDismount"),
                    prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveLadderDismount)));
                ladderDismountCommands = 0;
                held = item.GetComponent(Find("HeldItemInteractable"));
                NetworkServer.Spawn(item);
                AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 5;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native extension ladder pickup failed"); yield return null; }
                yield return new WaitForEndOfFrame();
                Input("Grip", false); Input("LeftGrip", false); Input("Trigger", true); Input("TriggerDown", true);
                pointerTick.Invoke(manager.Pointer, null); Input("TriggerDown", false); Input("Trigger", false); Input("TriggerUp", true);
                pointerTick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                deadline = Time.realtimeSinceStartup + 5;
                while (!(bool)Member(extension, "isExtended"))
                { Require(Time.realtimeSinceStartup < deadline, "Controller trigger did not extend native ladder"); yield return null; }
                yield return new WaitForSecondsRealtime(.4f);
                AccessTools.Method(held.GetType(), "AddEphemeralRigidbodyBlocker").Invoke(held, new object[] { this });
                AccessTools.Method(interactor.GetType(), "ServerDropHeldItem").Invoke(interactor, new object[] { Vector3.zero, Vector3.zero });
                var start = Member(ladderFixture, "startTransform") as Transform;
                var end = Member(ladderFixture, "endTransform") as Transform;
                Require(start != null && end != null, "Native ladder endpoints missing");
                item.transform.rotation = Quaternion.FromToRotation((end.position - start.position).normalized, Vector3.up) * item.transform.rotation;
                item.transform.position += interactor.transform.position + Vector3.up * .8f - start.position;
                Physics.SyncTransforms();
                Require(Convert.ToSingle(Member(ladderFixture, "Distance")) > 2, "Extended native ladder has insufficient climb span");
                var rayTick = AccessTools.Method(interactor.GetType(), "HandleInteractionRay");
                bool selected = false; ladderMountAim = true;
                foreach (var collider in ladderFixture.GetComponentsInChildren<Collider>().Where(c => c.enabled && c.gameObject.activeInHierarchy))
                {
                    if (!ReferenceEquals(collider.GetComponentInParent(Find("Interactable")), ladderFixture)) continue;
                    foreach (Vector3 side in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
                    {
                        Vector3 target = collider.bounds.center;
                        ladderAimOrigin = collider.bounds.ClosestPoint(target + side * 10) + side * .35f;
                        ladderAimRotation = Quaternion.LookRotation(target - ladderAimOrigin);
                        AimLadderFixture(); rayTick.Invoke(interactor, null);
                        if (ReferenceEquals(Member(interactor, "currentInteractable"), ladderFixture)) { selected = true; break; }
                    }
                    if (selected) break;
                }
                Require(selected, "Controller ray could not select native ladder climb interaction; colliders="
                    + ladderFixture.GetComponentsInChildren<Collider>().Length + "; hover=" + Member(interactor, "currentInteractable"));
                holdInjectedTrigger = true; Input("Trigger", true); Input("TriggerDown", true); pointerTick.Invoke(manager.Pointer, null);
                Input("TriggerDown", false);
                deadline = Time.realtimeSinceStartup + 5;
                while (Member(ladderFixture, "Networkclimber") is not Component climber || climber.gameObject != NetworkClient.localPlayer.gameObject)
                { Require(Time.realtimeSinceStartup < deadline, "Controller trigger did not mount native ladder"); yield return null; }
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerUp", true); pointerTick.Invoke(manager.Pointer, null);
                Input("TriggerUp", false); ladderMountAim = false;
                yield return new WaitForEndOfFrame();
                Logger.LogInfo("LADDER MOUNT PASS: controller extends held ladder, selects native climb collider and mounts through trigger/raycast authority");
                Type hudType = Find("ScammerGameHudController");
                var hud = UnityEngine.Object.FindAnyObjectByType(hudType) as Component;
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                var hints = Member(hud, "playerMenuHint") as VisualElement;
                Require(hints?.Q<Label>("swyf-vr-ladder-move-key")?.text == manager.Input.MoveHint
                    && hints.Q<Label>("swyf-vr-ladder-exit-key")?.text == manager.Input.JumpHint,
                    "Native ladder HUD lacks controller move/dismount hints");
                var polar = AccessTools.Property(adapter, "Polar").GetValue(null) as Transform;
                Quaternion ladderFacing = polar.rotation;
                Vector3 ladderBody = NetworkClient.localPlayer.transform.position;
                SetMockHeadPose(new Vector3(.3f, 1.65f, 0), Quaternion.Euler(0, 45, 0));
                yield return null; yield return new WaitForEndOfFrame();
                Require(Vector3.ProjectOnPlane(NetworkClient.localPlayer.transform.position - ladderBody, Vector3.up).magnitude < .01f
                    && Quaternion.Angle(polar.rotation, ladderFacing) < .1f,
                    "Physical head step/turn moved or rotated the body away from native ladder attachment");
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                yield return null; yield return new WaitForEndOfFrame();
                Drive(Vector2.up);
                Require(Member(ladderFixture, "moveInput") is Vector2 move && move.y > .9f,
                    "VR forward stick did not reach native ladder movement event");
                if (Plugin.AutoSprint.Value)
                    Require((bool)Member(ladderFixture, "isSprinting"), "Full VR stick did not reach native fast-climb sprint event");
                Vector3 before = NetworkClient.localPlayer.transform.position;
                float headBeforeClimb = manager.Rig.Head.transform.position.y;
                deadline = Time.realtimeSinceStartup + 3;
                while (NetworkClient.localPlayer.transform.position.y - before.y < .5f)
                { Require(Time.realtimeSinceStartup < deadline, "Native ladder motor did not physically climb from VR stick"); yield return null; }
                yield return new WaitForEndOfFrame();
                Require(Mathf.Abs((manager.Rig.Head.transform.position.y - headBeforeClimb)
                    - (NetworkClient.localPlayer.transform.position.y - before.y)) < .35f,
                    "VR camera did not follow native ladder body height");
                Drive(Vector2.down);
                deadline = Time.realtimeSinceStartup + 3;
                while (Convert.ToSingle(Member(ladderFixture, "climbVelocity")) > -.1f)
                { Require(Time.realtimeSinceStartup < deadline, "VR backward stick did not reverse native climb velocity"); yield return new WaitForFixedUpdate(); }
                Drive(Vector2.zero);
                Require(Member(ladderFixture, "moveInput") is Vector2 stopped && stopped == Vector2.zero, "Released VR stick retained ladder movement");
                Logger.LogInfo("LADDER CLIMB PASS: VR stick drives native climb motor and physical player height, neutral clears climb input");
                int beforeMenuDismounts = ladderDismountCommands;
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = true; Drive(Vector2.up, true);
                Require((Vector2)Member(ladderFixture, "moveInput") == Vector2.zero, "Settings allowed ladder input");
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = false;
                Drive(Vector2.zero, true);
                Require(Member(ladderFixture, "Networkclimber") != null && ladderDismountCommands == beforeMenuDismounts, "Held jump requested ladder dismount when settings closed");
                Input("HasInputFocus", false); Drive(Vector2.down, true);
                Require((Vector2)Member(ladderFixture, "moveInput") == Vector2.zero, "Lost VR focus allowed ladder input");
                Input("HasInputFocus", focus); Drive(Vector2.zero, true);
                Require(Member(ladderFixture, "Networkclimber") != null && ladderDismountCommands == beforeMenuDismounts, "Held jump requested ladder dismount when focus returned");
                Drive(Vector2.zero, false);
                Type controls = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRLadderControls");
                Drive(Vector2.up);
                AccessTools.Method(controls, "Clear").Invoke(null, null);
                Require((Vector2)Member(ladderFixture, "moveInput") == Vector2.zero, "XR cleanup left ladder movement held");
                AccessTools.Method(controls, "Initialize").Invoke(null, null);
                Require(ReferenceEquals(AccessTools.Property(controls, "Current").GetValue(null), ladderFixture), "XR initialization did not recover existing ladder ownership");
                Drive(Vector2.zero, true);
                deadline = Time.realtimeSinceStartup + 5;
                while (Member(ladderFixture, "Networkclimber") != null)
                { Require(Time.realtimeSinceStartup < deadline, "VR jump did not dismount native ladder"); yield return null; }
                Drive(Vector2.zero, false);
                Require(ladderDismountCommands == beforeMenuDismounts + 1, "Fresh jump did not request exactly one native ladder dismount");
                Require((bool)Member(Member(controller, "blocker"), "CanSimulate"), "Native ladder dismount retained simulation blocker");
                Logger.LogInfo("LADDER EXIT PASS: settings/focus suppress native climb input; VR jump dismounts and restores native simulation");
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                Require(hints.Q("swyf-vr-ladder-move") == null && hints.Q("swyf-vr-ladder-exit") == null,
                    "Ladder dismount retained climbing HUD controls");
                AccessTools.Method(ladderFixture.GetType(), "CmdInteract").Invoke(ladderFixture, new object[] { interactor });
                deadline = Time.realtimeSinceStartup + 5;
                while (Member(ladderFixture, "Networkclimber") == null)
                { Require(Time.realtimeSinceStartup < deadline, "Native ladder remount failed"); yield return null; }
                Drive(Vector2.zero);
                int beforeDisableDismounts = ladderDismountCommands;
                ((Behaviour)ladderFixture).enabled = false;
                yield return null; yield return new WaitForEndOfFrame();
                deadline = Time.realtimeSinceStartup + 5;
                while (Member(ladderFixture, "Networkclimber") != null && Time.realtimeSinceStartup < deadline) yield return null;
                Require(ladderDismountCommands == beforeDisableDismounts + 1 && Member(ladderFixture, "Networkclimber") == null && (bool)Member(Member(controller, "blocker"), "CanSimulate")
                    && !((Member(controller, "OnMoveInputChanged") as Action<Vector2>)?.GetInvocationList()
                        .Any(h => ReferenceEquals(h.Target, ladderFixture)) ?? false),
                    "Disabling a spawned ladder retained local ownership, simulation block or event subscription; climber="
                    + (Member(ladderFixture, "Networkclimber") != null) + "; simulate=" + Member(Member(controller, "blocker"), "CanSimulate")
                    + "; handlers=" + string.Join(",", ((Member(controller, "OnMoveInputChanged") as Action<Vector2>)?.GetInvocationList()
                        ?? Array.Empty<Delegate>()).Select(h => h.Target?.GetType().Name)) + "; spawned="
                    + NetworkClient.spawned.ContainsKey(ladderFixture.GetComponentInParent<NetworkIdentity>().netId));
                ((Behaviour)ladderFixture).enabled = true;
                AccessTools.Method(ladderFixture.GetType(), "CmdInteract").Invoke(ladderFixture, new object[] { interactor });
                deadline = Time.realtimeSinceStartup + 5;
                while (Member(ladderFixture, "Networkclimber") == null)
                { Require(Time.realtimeSinceStartup < deadline, "Re-enabled native ladder could not be mounted again"); yield return null; }
                Logger.LogInfo("LADDER DISABLE PASS: spawned-ladder disable uses native dismount and releases callbacks/simulation; re-enable can mount again");
                Drive(Vector2.up); ladderHoldAfterDespawn = true;
                object destroyedLadder = ladderFixture;
                NetworkServer.Destroy(item); item = null;
                yield return null; yield return new WaitForEndOfFrame();
                Drive(Vector2.up);
                var handlers = Member(controller, "OnMoveInputChanged") as Action<Vector2>;
                Require(handlers == null || handlers.GetInvocationList().All(h => !ReferenceEquals(h.Target, destroyedLadder)),
                    "Destroyed native ladder retained its controller event subscription");
                Require((bool)Member(Member(controller, "blocker"), "CanSimulate")
                    && (Vector2)Member(controller, "movementVector") == Vector2.zero,
                    "Destroyed ladder retained simulation blocker or turned held climb into walking");
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                Require(hints.Q<Label>("swyf-vr-ladder-release-key")?.text == manager.Input.JumpHint,
                    "Ladder destruction did not explain the neutral-release gate");
                Drive(Vector2.zero); Drive(Vector2.up, true);
                Require(((Vector2)Member(controller, "movementVector")).sqrMagnitude > .1f && (bool)Member(controller, "isJumping"),
                    "Fresh input did not restore body actions after ladder destruction");
                Drive(Vector2.zero, false); ladderHoldAfterDespawn = false;
                Logger.LogInfo("LADDER DESPAWN PASS: active-ladder destruction releases native event subscriptions/simulation and waits for neutral before walking/jump");
            }
            finally
            {
                holdInjectedTrigger = false; ladderMountAim = false;
                ladderMove = Vector2.zero; ladderJump = ladderJumpDown = false; KeepLadderInput(manager.Input);
                Input("HasInputFocus", focus); Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                manager.Pointer.Cancel();
                if (paused) AccessTools.Method(adapter, "TogglePause").Invoke(null, null);
                if (ladderFixture != null && Member(ladderFixture, "Networkclimber") is Component local && local.gameObject == NetworkClient.localPlayer.gameObject)
                    AccessTools.Method(ladderFixture.GetType(), "LocalDismount").Invoke(ladderFixture, null);
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), held)) AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                probe.UnpatchSelf();
                ladderFixture = null; ladderHoldAfterDespawn = false;
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                controllerTick.Invoke(null, new[] { controller });
            }
        }
    }
}
