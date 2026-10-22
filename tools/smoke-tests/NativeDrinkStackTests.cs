using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static Component stackedDrinkProbe, stackedRagdollProbe;
        private static bool stackedObserveOnlyPassout;
        private static int stackedImmediatePassoutCalls;
        private static bool ObserveUnscopedPassout(object __instance)
        {
            if (!stackedObserveOnlyPassout || !ReferenceEquals(__instance, stackedRagdollProbe)) return true;
            stackedImmediatePassoutCalls++;
            return false;
        }
        private static int stackedServerCalls;
        private static bool stackedServerDetached;
        private static void ObserveStackedServerUse(object __instance, NetworkConnectionToClient __0)
        {
            if (!ReferenceEquals(__instance, stackedDrinkProbe)) return;
            stackedServerCalls++;
            var user = __0?.identity?.GetComponent(Find("PlayerInteractor"));
            object held = Member(__instance, "heldItem");
            stackedServerDetached |= user == null || !ReferenceEquals(Member(user, "CurrentHeldItem"), held) && !ReferenceEquals(Member(user, "CurrentHipItem"), held);
        }
        private IEnumerator TestNativeDrinkStacks()
        {
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            var interactor = player.GetComponent(Find("PlayerInteractor"));
            var meta = player.GetComponent(Find("Metater.MetaPlayer"));
            var ragdoll = player.GetComponent(Find("PlayerRagdollManager"));
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            Type speedType = Find("SpeedBoostOnLeftClick");
            GameObject coffee = NetworkManager.singleton.spawnPrefabs.SingleOrDefault(go => go != null && go.name == "Coffee" && go.GetComponent(speedType) != null);
            GameObject patron = NetworkManager.singleton.spawnPrefabs.SingleOrDefault(go => go != null && go.name == "Patron" && go.GetComponent(speedType) != null);
            Require(coffee != null && patron != null && body != null && body.enabled && Member(interactor, "CurrentHeldItem") == null
                && !(bool)Member(ragdoll, "IsRagdollActive") && !(bool)Member(ragdoll, "NetworkisDrunk")
                && !(bool)AccessTools.Method(meta.GetType(), "HasActiveCoffeeStack").Invoke(meta, null)
                && !(bool)AccessTools.Method(meta.GetType(), "HasActiveAlcoholStack").Invoke(meta, null), "Drink stacking needs native prefabs and a fresh recovered player");
            Vector3 original = player.transform.position;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated drink stacking floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - original.y - .12f, 1050);
            var owned = new List<GameObject>();
            var probe = new Harmony("com.swyf.vr.drinkstackprobe");
            probe.Patch(AccessTools.Method(speedType, "UserCode_CmdUseItem__NetworkConnectionToClient"),
                prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveStackedServerUse)));
            probe.Patch(AccessTools.Method(ragdoll.GetType(), "CmdBeginRagdoll"),
                prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveUnscopedPassout)));
            stackedRagdollProbe = ragdoll;
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            Multiplayer.PoseFrame Pose() => (Multiplayer.PoseFrame)AccessTools.Field(typeof(Multiplayer.PoseNetwork), "local").GetValue(Multiplayer.PoseNetwork.Instance);
            IEnumerator Drink(GameObject prefab, float duration)
            {
                var item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity); owned.Add(item);
                var held = item.GetComponent(Find("HeldItemInteractable"));
                var effect = item.GetComponent(speedType);
                AccessTools.Field(speedType, "timeWithEffect").SetValue(effect, duration);
                NetworkServer.Spawn(item);
                AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 5;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Drink stack pickup did not complete: " + prefab.name); yield return null; }
                manager.Pointer.Cancel();
                manager.Rig.Right.Aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.right * .35f, Quaternion.LookRotation(Vector3.up));
                stackedDrinkProbe = effect; stackedServerCalls = 0; stackedServerDetached = false;
                Input("Grip", false); Input("LeftGrip", false);
                Input("Trigger", true); Input("TriggerDown", true); tick.Invoke(manager.Pointer, null);
                Input("TriggerDown", false); Input("Trigger", false); Input("TriggerUp", true); tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                Require((bool)Member(effect, "useRequested"), "Controller did not request stacked drink: " + prefab.name);
                bool sawRagdoll = false;
                deadline = Time.realtimeSinceStartup + 6;
                while (item != null || Member(interactor, "CurrentHeldItem") != null)
                { sawRagdoll |= (bool)Member(ragdoll, "IsRagdollActive"); Require(Time.realtimeSinceStartup < deadline, "Native authority did not consume stacked drink: " + prefab.name + "; ragdollSeen=" + sawRagdoll + "; serverCalls=" + stackedServerCalls + "; detached=" + stackedServerDetached); yield return null; }
                Require(stackedServerCalls == 1 && !stackedServerDetached, "Stacked drink command did not retain native held ownership");
            }
            try
            {
                manager.Pointer.Cancel(); teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.8f);
                float cap = Mathf.Max(Convert.ToSingle(Member(coffee.GetComponent(speedType), "multiplyValue")),
                    Convert.ToSingle(Member(coffee.GetComponent(speedType), "maxStackedSpeedMultiplier")));
                var targetSpeed = AccessTools.Method(meta.GetType(), "GetCoffeeSpeedMultiplier");
                int count = 0; float first = 0;
                do
                {
                    var drink = Drink(coffee, 3f);
                    try { while (drink.MoveNext()) yield return drink.Current; } finally { (drink as IDisposable)?.Dispose(); }
                    count++;
                    Require(Convert.ToInt32(Member(meta, "coffeeStackCount")) == count, "One controller coffee changed the native stack more than once");
                    float target = Convert.ToSingle(targetSpeed.Invoke(meta, null));
                    Require(target <= cap + .001f && !(bool)Member(ragdoll, "IsRagdollActive"), "Native coffee exceeded its authored cap or triggered ragdoll");
                    if (count == 1) first = target;
                    if (count == 2) Require(target > first, "Second native coffee did not increase its stack target");
                    if (target >= cap - .001f) break;
                    Require(count < 32, "Native coffee stack did not reach its authored cap within fixture limit");
                } while (true);
                float deadline = Time.realtimeSinceStartup + 2;
                while (Mathf.Abs(Convert.ToSingle(Member(meta, "currentSpeedEffectMultiplier")) - cap) > .02f)
                { Require(Time.realtimeSinceStartup < deadline, "Actual native speed effect did not approach its stacked coffee cap"); yield return null; }
                Logger.LogInfo("DRINK STACK COFFEE PASS: fresh controller use adds one stack per native consumed item, increases speed and reaches authored cap=" + cap + "; drinks=" + count);
                deadline = Time.realtimeSinceStartup + 8;
                while ((bool)AccessTools.Method(meta.GetType(), "HasActiveCoffeeStack").Invoke(meta, null)
                    || !Mathf.Approximately(Convert.ToSingle(Member(meta, "currentSpeedEffectMultiplier")), 1))
                { Require(Time.realtimeSinceStartup < deadline, "Stacked coffee effect did not expire normally"); yield return null; }
                Require(Convert.ToInt32(Member(meta, "coffeeStackCount")) == 0, "Expired coffee retained its native stack count");
                Logger.LogInfo("DRINK STACK EXPIRY PASS: native coffee duration expires, count clears and normal speed returns");
                for (int i = 0; i < 2; i++)
                {
                    var drink = Drink(patron, 8f);
                    try { while (drink.MoveNext()) yield return drink.Current; } finally { (drink as IDisposable)?.Dispose(); }
                    if (i == 0) Require(Convert.ToInt32(Member(meta, "alcoholUnitCount")) == 2 && !(bool)Member(ragdoll, "IsRagdollActive"),
                        "First native Patron did not add two units without passing out");
                }
                deadline = Time.realtimeSinceStartup + 3;
                while (!(bool)Member(ragdoll, "IsRagdollActive"))
                { Require(Time.realtimeSinceStartup < deadline, "Native stacked alcohol did not trigger pass-out"); yield return null; }
                yield return new WaitForSecondsRealtime(.6f); yield return new WaitForEndOfFrame();
                Require(Pose().Tracked == 0, "Alcohol pass-out retained tracked-avatar control");
                Transform eye = AccessTools.Method(ragdoll.GetType(), "GetCurrentRagdollCameraTransform").Invoke(ragdoll, null) as Transform;
                Transform chest = AccessTools.Method(ragdoll.GetType(), "GetCurrentChestTransform").Invoke(ragdoll, null) as Transform;
                Require(eye != null ? Vector3.Distance(manager.Rig.Head.transform.position, eye.position) < .35f
                    : chest != null && Vector3.Distance(manager.Rig.Head.transform.position, chest.position) < .75f,
                    "Alcohol pass-out camera did not follow the native ragdoll anchor");
                Logger.LogInfo("DRINK STACK PASSOUT PASS: controller-consumed alcohol triggers native ragdoll, releases tracked pose and follows the native camera anchor");
                deadline = Time.realtimeSinceStartup + 12;
                while ((bool)Member(ragdoll, "IsRagdollActive") || !body.enabled || (Pose().Tracked & 1) == 0
                    || (bool)Member(ragdoll, "NetworkisDrunk") || (bool)AccessTools.Method(meta.GetType(), "HasActiveAlcoholStack").Invoke(meta, null))
                { Require(Time.realtimeSinceStartup < deadline, "Native alcohol recovery did not restore body, tracked pose and consumable state"); yield return null; }
                yield return new WaitForSecondsRealtime(.8f); yield return new WaitForEndOfFrame();
                Require(Vector3.Distance(manager.Rig.Head.transform.position, manager.Rig.AuthoredEye) < .15f
                    && Mathf.Approximately(Convert.ToSingle(Member(meta, "currentSpeedEffectMultiplier")), 1), "Alcohol recovery retained a camera or speed offset");
                Logger.LogInfo("DRINK STACK RECOVERY PASS: native recovery restores collision, tracked pose, authored eye and ordinary speed while clearing drunk/stack state");
                // Observe the command boundary without starting a second ragdoll.
                // A leaked VR consume scope would swallow this ordinary request.
                stackedObserveOnlyPassout = true; stackedImmediatePassoutCalls = 0;
                AccessTools.Method(meta.GetType(), "TriggerAlcoholPassOut").Invoke(meta, null);
                stackedObserveOnlyPassout = false;
                Require(stackedImmediatePassoutCalls == 1, "Native pass-out outside controller consumption was deferred or lost");
                Logger.LogInfo("DRINK STACK SCOPE PASS: ordinary native pass-out still issues its command immediately outside a controller consume callback; probe suppresses only this negative-check command");
            }
            finally
            {
                probe.UnpatchSelf(); stackedDrinkProbe = stackedRagdollProbe = null; stackedObserveOnlyPassout = false;
                Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false); manager.Pointer.Cancel();
                if ((bool)Member(ragdoll, "IsRagdollActive")) AccessTools.Method(ragdoll.GetType(), "ServerRecover").Invoke(ragdoll, null);
                if (Member(meta, "clearAfterPassOutRoutine") is Coroutine clearing) ((MonoBehaviour)meta).StopCoroutine(clearing);
                AccessTools.Field(meta.GetType(), "clearAfterPassOutRoutine").SetValue(meta, null);
                AccessTools.Method(meta.GetType(), "ResetStackedSpeedEffects").Invoke(meta, null);
                foreach (GameObject item in owned)
                    if (item != null)
                    {
                        if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item.GetComponent(Find("HeldItemInteractable"))))
                            AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                        NetworkServer.Destroy(item);
                    }
                teleport.Invoke(controller, new object[] { original, true }); manager.Rig.Recenter(); Destroy(floor);
            }
        }
    }
}
