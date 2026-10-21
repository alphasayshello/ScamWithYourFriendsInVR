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
        private static Component bagFixture, bagRagdollFixture;
        private static bool injectBagGrip, bagCleaning;
        private static Vector2 bagMove;
        private static int bagForceCommands, bagExitCommands, bagExitAcknowledgements;
        private static void ObserveBagForce(object __instance)
        { if (ReferenceEquals(__instance, bagFixture)) bagForceCommands++; }
        private static void ObserveBagExit(object __instance)
        {
            if (!ReferenceEquals(__instance, bagFixture)) return;
            if (ReferenceEquals(Member(bagRagdollFixture, "NetworkisHiding"), bagFixture)) bagExitCommands++;
            else bagExitAcknowledgements++;
        }
        private static bool SkipBagCleanupRpc(object __instance) => !bagCleaning || !ReferenceEquals(__instance, bagFixture);

        private IEnumerator TestNativeBag()
        {
            var manager = VRManager.Instance;
            Type type = Find("DuffleInteractable"), heldType = Find("HeldItemInteractable");
            GameObject prefab = NetworkManager.singleton.spawnPrefabs.Where(go => go != null && go.GetComponent(type) != null)
                .OrderBy(go => go.name).FirstOrDefault(go => Member(go.GetComponent(heldType), "canHideInsideBag") is bool hide && hide);
            var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            var ragdoll = NetworkClient.localPlayer.GetComponent(Find("PlayerRagdollManager"));
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            object controller = AccessTools.Property(adapter, "Controller").GetValue(null);
            var controllerTick = AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches"), "BeforeController");
            var pointerTick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            Require(prefab != null && Member(interactor, "CurrentHeldItem") == null && !(bool)Member(ragdoll, "IsRagdollActive"),
                "Native bag fixture requires a hideable prefab, empty hands and recovered body");
            GameObject item = null, wall = null;
            bool paused = false, focused = manager.Input.HasInputFocus;
            var probe = new Harmony("com.swyf.vr.bagfixture");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Move(Vector2 value)
            { bagMove = value; manager.Input.GetType().GetProperty("Move").SetValue(manager.Input, value); controllerTick.Invoke(null, new[] { controller }); }
            try
            {
                item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity);
                bagFixture = item.GetComponent(type);
                bagRagdollFixture = ragdoll;
                Component held = item.GetComponent(heldType);
                NetworkServer.Spawn(item);
                AccessTools.Method(heldType, "CmdInteract").Invoke(held, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 8;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native bag pickup did not complete"); yield return null; }
                probe.Patch(AccessTools.Method(type, "CmdApplyForce"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveBagForce)));
                probe.Patch(AccessTools.Method(type, "CmdExitBag"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveBagExit)));
                probe.Patch(AccessTools.Method(type, "TargetExitBag"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipBagCleanupRpc)));
                manager.Pointer.Cancel();
                manager.Rig.Right.Aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.up * 2, Quaternion.LookRotation(Vector3.up));
                injectBagGrip = holdInjectedTrigger = true;
                Input("LeftGrip", true); Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                pointerTick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                Logger.LogInfo("BAG HOLD: blocked=" + manager.Rig.HeadObstructed + "; panel=" + manager.Pointer.HasPanelHit
                    + "; extra=" + Member(bagFixture, "localIsLeftClicking") + "; held=" + ReferenceEquals(Member(interactor, "CurrentHeldItem"), held));
                float charge = Convert.ToSingle(AccessTools.Method(type, "GetClimbDurationSeconds").Invoke(bagFixture, null));
                deadline = Time.realtimeSinceStartup + charge + 8;
                while (!ReferenceEquals(Member(ragdoll, "NetworkisHiding"), bagFixture) || !(bool)Member(bagFixture, "localIsInBag"))
                { Require(Time.realtimeSinceStartup < deadline, "Controller extra-use hold did not enter native bag; blocked=" + manager.Rig.HeadObstructed
                    + "; extra=" + Member(bagFixture, "localIsLeftClicking") + "; cached=" + Member(bagFixture, "cachedIsLeftClicking")
                    + "; didHide=" + Member(bagFixture, "localDidHideThisHold") + "; local=" + Member(bagFixture, "localIsInBag")
                    + "; synchronized=" + ReferenceEquals(Member(ragdoll, "NetworkisHiding"), bagFixture)
                    + "; overlaps=" + string.Join(",", Physics.OverlapSphere(manager.Rig.Head.transform.position, .11f, Plugin.CollisionMask.Value,
                        QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(NetworkClient.localPlayer.transform))
                        .Select(c => c.name + "/bag=" + c.transform.IsChildOf(item.transform)))); yield return null; }
                injectBagGrip = holdInjectedTrigger = false; Input("Trigger", false); Input("LeftGrip", false); manager.Pointer.Cancel();
                yield return new WaitForSecondsRealtime(.3f);
                var cameraObject = Member(bagFixture, "bagCamera") as GameObject;
                var bagEye = cameraObject != null ? cameraObject.transform : Member(bagFixture, "bagCenter") as Transform;
                Logger.LogInfo("BAG VIEW: eye=" + manager.Rig.AuthoredEye + "; head=" + manager.Rig.Head.transform.position
                    + "; bag=" + (bagEye != null ? bagEye.position.ToString() : "missing")
                    + "; nativeCamera=" + manager.Rig.Head.name);
                Logger.LogInfo("BAG ENTRY PASS: controller extra-use hold reaches native hide authority, drops held ownership and enables local bag mode");
                Move(Vector2.up);
                Require(Member(bagFixture, "localMoveInput") is Vector2 move && move == Vector2.up,
                    "VR movement does not reach the native hidden-bag movement event");
                bagForceCommands = 0; deadline = Time.realtimeSinceStartup + 3;
                while (bagForceCommands == 0)
                { Require(Time.realtimeSinceStartup < deadline, "Native hidden-bag input did not issue a movement command"); yield return null; }
                Move(Vector2.zero);
                Require(bagEye != null && Vector3.Distance(manager.Rig.AuthoredEye, bagEye.position) < .35f,
                    "VR view does not follow the native bag camera anchor");
                Logger.LogInfo("BAG MOVE PASS: controller stick reaches native bag input/force authority and view follows the authored bag camera");
                yield return new WaitForEndOfFrame();
                Require(!manager.Rig.HeadObstructed, "The occupied bag still blocks its own tracked view");
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "SWYF bag external head-obstruction probe";
                wall.transform.position = manager.Rig.Head.transform.position;
                wall.transform.localScale = Vector3.one * .15f;
                int collisionMask = Plugin.CollisionMask.Value;
                int wallLayer = 0; while (wallLayer < 31 && (collisionMask & (1 << wallLayer)) == 0) wallLayer++;
                Require((collisionMask & (1 << wallLayer)) != 0, "Head collision mask empty"); wall.layer = wallLayer;
                foreach (var collider in item.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(wall.GetComponent<Collider>(), collider);
                Physics.SyncTransforms();
                yield return null; yield return new WaitForEndOfFrame();
                Require(manager.Rig.HeadObstructed, "Ignoring self-owned bag geometry also ignored an external wall");
                Destroy(wall); wall = null;
                yield return null; yield return new WaitForEndOfFrame();
                Require(!manager.Rig.HeadObstructed, "Head obstruction did not clear after removing the external wall");
                Logger.LogInfo("BAG COLLISION PASS: occupied bag geometry does not black out its own view; external geometry still blocks and clears normally");
                Type hudType = Find("ScammerGameHudController");
                var hud = UnityEngine.Object.FindAnyObjectByType(hudType) as Component;
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                var hints = Member(hud, "playerMenuHint") as VisualElement;
                Require(hints?.Q<Label>("swyf-vr-bag-exit-key")?.text == manager.Input.JumpHint
                    && hints.Q<Label>("swyf-vr-bag-move-key")?.text == manager.Input.MoveHint,
                    "Native HUD does not show controller bag movement/exit keys");
                toggle.Invoke(null, null); paused = true; Move(Vector2.up);
                Require(Member(bagFixture, "localMoveInput") is Vector2 menuMove && menuMove == Vector2.zero, "Settings allowed hidden-bag movement");
                toggle.Invoke(null, null); paused = false;
                Input("HasInputFocus", false); Move(Vector2.up);
                Require(Member(bagFixture, "localMoveInput") is Vector2 focusMove && focusMove == Vector2.zero, "Focus loss allowed hidden-bag movement");
                Input("HasInputFocus", focused); Move(Vector2.zero);
                Logger.LogInfo("BAG INPUT PASS: settings and focus loss clear native movement without exiting the bag");
                bagExitCommands = bagExitAcknowledgements = 0;
                Input("Jump", true); Input("JumpDown", true);
                AccessTools.Method(manager.BodyActions.GetType(), "Update").Invoke(manager.BodyActions, null);
                Input("JumpDown", false); controllerTick.Invoke(null, new[] { controller }); Input("Jump", false);
                controllerTick.Invoke(null, new[] { controller });
                deadline = Time.realtimeSinceStartup + 8;
                while (Member(ragdoll, "NetworkisHiding") is Component hiding && hiding != null || (bool)Member(bagFixture, "localIsInBag"))
                { Require(Time.realtimeSinceStartup < deadline, "Controller jump did not exit the native bag"); yield return null; }
                yield return new WaitForSecondsRealtime(2);
                Require(!(bool)Member(ragdoll, "IsRagdollActive"), "Native bag-exit ragdoll did not recover");
                Require(bagExitCommands == 1 && bagExitAcknowledgements == 1,
                    "Controller exit/native cleanup count changed: requests=" + bagExitCommands + "; acknowledgements=" + bagExitAcknowledgements);
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                Require(hints.Q("swyf-vr-bag-exit") == null && hints.Q("swyf-vr-bag-move") == null, "Native bag exit retained its VR HUD hints");
                Logger.LogInfo("BAG EXIT PASS: controller jump uses native bag exit authority and recovers from its normal exit ragdoll");
            }
            finally
            {
                bagMove = Vector2.zero; injectBagGrip = holdInjectedTrigger = false;
                Input("Trigger", false); Input("TriggerDown", false); Input("LeftGrip", false); Input("Jump", false); Input("JumpDown", false); Input("HasInputFocus", focused);
                manager.Pointer.Cancel();
                if (paused) toggle.Invoke(null, null);
                if (item != null)
                {
                    bagCleaning = true;
                    if (ReferenceEquals(Member(ragdoll, "NetworkisHiding"), bagFixture))
                        AccessTools.Method(type, "ServerExitBag").Invoke(bagFixture, new object[] { NetworkClient.localPlayer });
                    AccessTools.Method(type, "ExitBag").Invoke(bagFixture, null);
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item.GetComponent(heldType)))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                bagCleaning = false; probe.UnpatchSelf(); bagFixture = bagRagdollFixture = null;
                if (wall != null) Destroy(wall);
                manager.Input.GetType().GetProperty("Move").SetValue(manager.Input, Vector2.zero);
                controllerTick.Invoke(null, new[] { controller });
            }
        }
    }
}
