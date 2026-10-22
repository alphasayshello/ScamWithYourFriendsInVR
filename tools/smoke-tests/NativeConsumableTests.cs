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
        private IEnumerator TestNativeConsumables()
        {
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            var interactor = player.GetComponent(Find("PlayerInteractor"));
            var meta = player.GetComponent(Find("Metater.MetaPlayer"));
            var ragdoll = player.GetComponent(Find("PlayerRagdollManager"));
            var fart = player.GetComponent(Find("PlayerFartManager"));
            var pee = player.GetComponentInChildren(Find("PeeNetworking"), true);
            Type speedType = Find("SpeedBoostOnLeftClick"), gasType = Find("FartOnConsume");
            var prefabs = NetworkManager.singleton.spawnPrefabs.Where(go => go != null
                && (go.GetComponent(speedType) != null || go.GetComponent(gasType) != null)).OrderBy(go => go.name).ToArray();
            Require(prefabs.Length > 0 && pee != null && fart != null, "Native consumable prefabs/effect components unavailable");
            Require(Member(interactor, "CurrentHeldItem") == null && !(bool)Member(fart, "forcedFartActive")
                && !(bool)Member(pee, "NetworkwaterBoostActive") && !(bool)Member(pee, "forcedPeeing")
                && !(bool)AccessTools.Method(meta.GetType(), "HasActiveCoffeeStack").Invoke(meta, null)
                && !(bool)AccessTools.Method(meta.GetType(), "HasActiveAlcoholStack").Invoke(meta, null),
                "Consumable check requires empty hands and no pre-existing consumable effects");
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            Require(body != null, "Consumable check needs native collision body");
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            var reset = AccessTools.Method(meta.GetType(), "ResetStackedSpeedEffects");
            Vector3 originalPosition = player.transform.position;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated consumable floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - originalPosition.y - .12f, 1050);
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            GameObject item = null;
            Component held = null;
            bool paused = false;
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Click()
            {
                manager.Rig.Right.Aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.right * .35f, Quaternion.LookRotation(Vector3.up));
                Input("Grip", false); Input("LeftGrip", false);
                Input("Trigger", true); Input("TriggerDown", true); tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
            }
            void Release()
            {
                holdInjectedTrigger = false;
                Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", true); tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
            }
            try
            {
                manager.Pointer.Cancel();
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.8f);
                Logger.LogInfo("CONSUMABLE PREFABS: " + string.Join(", ", prefabs.Select(p => p.name)));
                foreach (GameObject prefab in prefabs)
                {
                    item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity);
                    held = item.GetComponent(Find("HeldItemInteractable"));
                    Component speed = item.GetComponent(speedType), gas = item.GetComponent(gasType);
                    Require(held != null, "Native consumable has no held item: " + prefab.name);
                    // Shorten only this owned instance's effect duration. Native
                    // consumption, destruction, effects and expiry stay in charge.
                    if (speed != null) AccessTools.Field(speedType, "timeWithEffect").SetValue(speed, 2f);
                    if (gas != null) AccessTools.Field(gasType, "gassyDurationSeconds").SetValue(gas, 2f);
                    NetworkServer.Spawn(item);
                    AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                    float deadline = Time.realtimeSinceStartup + 6;
                    while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                    { Require(Time.realtimeSinceStartup < deadline, "Native consumable pickup failed: " + prefab.name); yield return null; }
                    float legacyMultiplier = speed != null ? Convert.ToSingle(Member(speed, "multiplyValue")) : 1;
                    bool legacyDrunk = speed != null && (bool)Member(speed, "causesDrunkEffect");
                    string effect = speed != null ? AccessTools.Method(speedType, "ResolveConsumableEffect").Invoke(speed, null).ToString() : "Gassy";
                    toggle.Invoke(null, null); paused = true; Click(); Release();
                    Require(!(bool)Member(speed ?? gas, "useRequested") && !(bool)Member(speed ?? gas, "NetworkhasBeenUsed"),
                        "Controller consumed an item through settings: " + prefab.name);
                    toggle.Invoke(null, null); paused = false; yield return new WaitForSecondsRealtime(.2f);
                    holdInjectedTrigger = true; Click();
                    Require((bool)Member(speed ?? gas, "useRequested"), "Fresh controller trigger did not request native consumption: " + prefab.name);
                    deadline = Time.realtimeSinceStartup + 2;
                    bool EffectActive()
                    {
                        if (gas != null && !(bool)Member(fart, "forcedFartActive")) return false;
                        if (effect == "Water") return (bool)Member(pee, "NetworkwaterBoostActive") && (bool)Member(pee, "NetworkisPeeing");
                        if (effect == "Coffee") return Convert.ToInt32(Member(meta, "coffeeStackCount")) == 1 && Convert.ToSingle(Member(meta, "currentSpeedEffectMultiplier")) > 1;
                        if (effect == "Beer" || effect == "Wine" || effect == "Patron")
                            return Convert.ToInt32(Member(meta, "alcoholUnitCount")) == (effect == "Patron" ? 2 : 1)
                                && (bool)Member(ragdoll, "NetworkisDrunk") && Convert.ToSingle(Member(meta, "currentSpeedEffectMultiplier")) < 1;
                        if (effect == "LegacySpeed")
                        {
                            float current = Convert.ToSingle(Member(meta, "currentSpeedEffectMultiplier"));
                            bool direction = legacyMultiplier > 1 ? current > 1 : legacyMultiplier < 1 ? current < 1 : Mathf.Approximately(current, 1);
                            return direction && (!legacyDrunk || (bool)Member(ragdoll, "NetworkisDrunk"))
                                && (Member(meta, "speedEffects") as ICollection)?.Count > 0;
                        }
                        return (bool)Member(fart, "forcedFartActive");
                    }
                    while (!EffectActive())
                    { Require(Time.realtimeSinceStartup < deadline, "Native consumable effect did not start: " + prefab.name + "/" + effect); yield return null; }
                    yield return new WaitForSecondsRealtime(.15f);
                    Require(EffectActive(), "Held trigger duplicated or cancelled native consumable effect: " + prefab.name);
                    Release();
                    deadline = Time.realtimeSinceStartup + 6;
                    while (item != null || Member(interactor, "CurrentHeldItem") != null)
                    { Require(Time.realtimeSinceStartup < deadline, "Native authority did not clear/destroy consumed item: " + prefab.name); yield return null; }
                    Logger.LogInfo("CONSUMABLE USE PASS: controller settings block, fresh trigger, native " + effect + " effect and authority ownership/destruction; item=" + prefab.name);
                    deadline = Time.realtimeSinceStartup + 8;
                    while ((bool)Member(pee, "NetworkwaterBoostActive") || (bool)Member(fart, "forcedFartActive")
                        || (bool)Member(ragdoll, "NetworkisDrunk") || !Mathf.Approximately(Convert.ToSingle(Member(meta, "currentSpeedEffectMultiplier")), 1))
                    { Require(Time.realtimeSinceStartup < deadline, "Native consumable effect failed to expire: " + prefab.name); yield return null; }
                    if (effect == "Water") Require(!(bool)Member(pee, "NetworkisPeeing"), "Water stream stayed on after native boost expiry");
                    Logger.LogInfo("CONSUMABLE EXPIRY PASS: native timed effect returns to normal; item=" + prefab.name);
                    item = null; held = null;
                }
            }
            finally
            {
                Release(); if (paused) toggle.Invoke(null, null); manager.Pointer.Cancel();
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), held)) AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                reset.Invoke(meta, null);
                if (Member(pee, "waterBoostRoutine") is Coroutine water) ((MonoBehaviour)pee).StopCoroutine(water);
                AccessTools.Field(pee.GetType(), "waterBoostRoutine").SetValue(pee, null);
                AccessTools.Field(pee.GetType(), "involuntaryPeeActive").SetValue(pee, false);
                AccessTools.Property(pee.GetType(), "NetworkwaterBoostActive").SetValue(pee, false);
                AccessTools.Method(pee.GetType(), "ApplyWaterBoostVisual").Invoke(pee, null);
                AccessTools.Method(pee.GetType(), "SetPeeingServer").Invoke(pee, new object[] { false });
                if (Member(fart, "forcedRoutine") is Coroutine forced) ((MonoBehaviour)fart).StopCoroutine(forced);
                AccessTools.Field(fart.GetType(), "forcedRoutine").SetValue(fart, null);
                AccessTools.Field(fart.GetType(), "forcedFartActive").SetValue(fart, false);
                AccessTools.Field(fart.GetType(), "forcedFartEndTime").SetValue(fart, 0f);
                AccessTools.Method(fart.GetType(), "UpdateGassyIcon").Invoke(fart, null);
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter(); Destroy(floor);
            }
            Logger.LogInfo("CONSUMABLE COMPLETE PASS: " + prefabs.Length + " loaded native consumables checked; owned items, effects and player position cleaned up");
        }
    }
}
