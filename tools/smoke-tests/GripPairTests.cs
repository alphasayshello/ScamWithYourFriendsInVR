using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestGrip()
        {
            float until = Time.realtimeSinceStartup + 90;
            while (!NetworkClient.ready || NetworkClient.localPlayer == null || VRManager.Instance?.Ready != true || VRManager.Instance.Rig.Head == null)
            {
                Require(Time.realtimeSinceStartup < until, "Grip test session did not become ready");
                yield return null;
            }
            Require(NetworkManager.singleton.networkAddress == "127.0.0.1", "Grip test requires loopback transport");
            yield return new WaitForSecondsRealtime(3);
            var manager = VRManager.Instance;
            Component interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            Type itemType = Find("HeldItemInteractable");
            Component item = UnityEngine.Object.FindObjectsByType(itemType, FindObjectsSortMode.None).Cast<Component>()
                .Where(candidate => candidate.GetType() == itemType && candidate.gameObject.activeInHierarchy
                    && Member(candidate, "Networkholder") == null
                    && AccessTools.Method(itemType, "CanInteract").Invoke(candidate, new object[] { interactor }) is bool can && can)
                .OrderBy(candidate => Vector3.SqrMagnitude(candidate.transform.position - interactor.transform.position)).FirstOrDefault();
            Component pickup = null;
            if (item == null)
            {
                Type pickupType = Find("ScenePickup");
                pickup = UnityEngine.Object.FindObjectsByType(pickupType, FindObjectsSortMode.None).Cast<Component>()
                    .Where(candidate => Member(candidate, "prefab") is Component prefab && prefab.GetType() == itemType
                        && AccessTools.Method(pickupType, "CanInteract").Invoke(candidate, new object[] { interactor }) is bool can && can)
                    .OrderBy(candidate => Vector3.SqrMagnitude(candidate.transform.position - interactor.transform.position)).FirstOrDefault();
            }
            Require(item != null || pickup != null, "No ordinary native held item or scene pickup available");
            Require(Member(interactor, "CurrentHeldItem") == null, "Grip test requires empty hands");
            Type gripType = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRGripInteractor");
            object grip = Activator.CreateInstance(gripType, true);
            MethodInfo tick = AccessTools.Method(gripType, "Tick");
            MethodInfo cancel = AccessTools.Method(gripType, "Cancel");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            try
            {
                // Seed through the game's real command and wait for authority
                // replication. The test never edits held-item SyncVars.
                if (item != null) AccessTools.Method(itemType, "CmdInteract").Invoke(item, new object[] { interactor });
                else
                {
                    object controller = Instance("Metater.MetaPlayerController");
                    AccessTools.Method(controller.GetType(), "Teleport").Invoke(controller, new object[] { pickup.transform.position - Vector3.forward * 0.5f, true });
                    yield return new WaitForSecondsRealtime(0.3f);
                    object registry = Member(pickup, "registry");
                    AccessTools.Method(registry.GetType(), "CmdPickUp").Invoke(registry, new object[] { Member(pickup, "id"), null });
                }
                until = Time.realtimeSinceStartup + 8;
                while (Member(interactor, "CurrentHeldItem") == null)
                {
                    Require(Time.realtimeSinceStartup < until, "Native pickup command was not replicated");
                    yield return null;
                }
                item = Member(interactor, "CurrentHeldItem") as Component;
                Input("Grip", true); Input("GripDown", true); Input("GripUp", false);
                tick.Invoke(grip, new object[] { false });
                Input("GripDown", false);
                Require(ReferenceEquals(gripType.GetField("acquired", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(grip), item), "Grip did not adopt the native held item");
                yield return new WaitForSecondsRealtime(0.25f);
                Require(ReferenceEquals(Member(interactor, "CurrentHeldItem"), item), "Grip press dropped an already-held item");
                Input("Grip", false); Input("GripUp", true);
                tick.Invoke(grip, new object[] { false });
                Input("GripUp", false);
                until = Time.realtimeSinceStartup + 8;
                while (Member(interactor, "CurrentHeldItem") != null)
                {
                    Require(Time.realtimeSinceStartup < until, "Grip release drop command was not replicated");
                    yield return null;
                }
                Logger.LogInfo("GRIP PASS: native pickup replication, grip adoption without premature drop, release through native drop command; item=" + item.name);
                yield return new WaitForSecondsRealtime(0.4f);
                Collider collider = item.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled && !c.isTrigger);
                Require(collider != null, "Dropped item has no pickup collider");
                // Start outside the complete collider bounds. A fixed 20 cm
                // offset can put the test ray inside a large pickup collider.
                Vector3 point = Vector3.zero;
                until = Time.realtimeSinceStartup + 4;
                while (true)
                {
                    Vector3 direction = Vector3.down;
                    point = collider.bounds.center;
                    float offset = collider.bounds.extents.magnitude + 0.15f;
                    manager.Rig.Right.Aim.SetPositionAndRotation(point - direction * offset, Quaternion.LookRotation(direction, Vector3.forward));
                    AccessTools.Method(interactor.GetType(), "HandleInteractionRay").Invoke(interactor, null);
                    if (ReferenceEquals(Member(interactor, "currentInteractable"), item)) break;
                    Require(Time.realtimeSinceStartup < until, "Test ray did not acquire the dropped item's native hover state");
                    yield return null;
                }
                Input("Grip", true); Input("GripDown", true);
                tick.Invoke(grip, new object[] { false });
                Logger.LogInfo("GRIP diagnostics: hover=" + Member(interactor, "currentInteractable") + "; scenePickup=" + Member(interactor, "currentScenePickup")
                    + "; pending=" + AccessTools.Field(gripType, "pending").GetValue(grip) + "; held=" + Member(interactor, "CurrentHeldItem")
                    + "; point=" + point + "; collider=" + collider.name);
                Input("GripDown", false); Input("Grip", false); Input("GripUp", true);
                tick.Invoke(grip, new object[] { false });
                Input("GripUp", false);
                bool sawPickup = false;
                until = Time.realtimeSinceStartup + 5;
                do
                {
                    yield return null;
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item)) sawPickup = true;
                    tick.Invoke(grip, new object[] { false });
                } while ((!sawPickup || Member(interactor, "CurrentHeldItem") != null) && Time.realtimeSinceStartup < until);
                Require(sawPickup && Member(interactor, "CurrentHeldItem") == null, "Quick-release pickup was not acknowledged and dropped");
                Logger.LogInfo("GRIP PASS: controller interaction ray pickup with grip released before server acknowledgement drops the item after replication");

                AccessTools.Method(itemType, "CmdInteract").Invoke(item, new object[] { interactor });
                until = Time.realtimeSinceStartup + 8;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), item))
                {
                    Require(Time.realtimeSinceStartup < until, "Native pickup for cancellation was not replicated");
                    yield return null;
                }
                Input("Grip", true); Input("GripDown", true);
                tick.Invoke(grip, new object[] { false });
                Input("GripDown", false);
                cancel.Invoke(grip, null);
                until = Time.realtimeSinceStartup + 8;
                while (Member(interactor, "CurrentHeldItem") != null)
                {
                    Require(Time.realtimeSinceStartup < until, "Grip cancellation left its acquired item held");
                    yield return null;
                }
                Logger.LogInfo("GRIP PASS: cancellation releases the grip-owned item through native authority");
                var poses = Multiplayer.PoseNetwork.Instance;
                Require(poses != null, "Pose network missing before lifecycle test");
                poses.enabled = false;
                Require(Multiplayer.PoseNetwork.Instance == null, "Disabled pose component still accepts local samples");
                Require(!(bool)AccessTools.Field(typeof(Multiplayer.PoseNetwork), "welcomed").GetValue(poses), "Disabled pose component retained negotiation state");
                Require(!(bool)AccessTools.Field(typeof(Multiplayer.PoseNetwork), "clientRegistered").GetValue(poses), "Disabled pose component retained client handlers");
                poses.enabled = true;
                until = Time.realtimeSinceStartup + 10;
                while (!(bool)AccessTools.Field(typeof(Multiplayer.PoseNetwork), "welcomed").GetValue(poses))
                {
                    Require(Time.realtimeSinceStartup < until, "Re-enabled pose component did not renegotiate with the live host");
                    yield return null;
                }
                Logger.LogInfo("GRIP PASS: pose component disable clears handlers/session state and re-enable renegotiates with the live host");
            }
            finally
            {
                Input("Grip", false); Input("GripDown", false); Input("GripUp", false);
                cancel.Invoke(grip, null);
                if (item != null && ReferenceEquals(Member(interactor, "CurrentHeldItem"), item))
                    AccessTools.Method(interactor.GetType(), "CmdDropHeldItem").Invoke(interactor, new object[] { Vector3.forward, Vector3.zero, item.transform.position, item.transform.rotation, null });
            }
        }
    }
}
