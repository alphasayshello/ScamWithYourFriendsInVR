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
        private IEnumerator TestNativeFood()
        {
            var manager = VRManager.Instance;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var attack = AccessTools.Method(adapter, "Attack");
            var secondary = AccessTools.Method(adapter, "SecondaryUse");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var pointerTick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            var fart = NetworkClient.localPlayer.GetComponent(Find("PlayerFartManager"));
            Require(Member(interactor, "CurrentHeldItem") == null, "Food test requires empty hands");
            var trackedPoops = Member(fart, "serverLaxativePoops") as IList;
            Require(trackedPoops != null && trackedPoops.Count == 0 && Member(fart, "curryFartRoutine") == null
                && !(Member(fart, "forcedFartActive") is bool existingEffect && existingEffect),
                "Food test cannot replace an existing curry effect");
            Type foodType = Find("CurryOnLeftClick");
            GameObject prefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(foodType) != null);
            if (prefab == null)
                prefab = Resources.FindObjectsOfTypeAll(foodType).Cast<Component>().FirstOrDefault(c => !c.gameObject.scene.IsValid())?.gameObject;
            Require(prefab != null, "Native curry prefab was not loaded");
            GameObject food = Instantiate(prefab, interactor.transform.position + interactor.transform.forward, Quaternion.identity);
            Component held = food.GetComponent(Find("HeldItemInteractable"));
            Component curry = food.GetComponent(foodType);
            int primaryPresses = 0, secondaryPresses = 0;
            bool secondaryLast = true, paused = false;
            Action<bool> primaryListener = value => { if (value) primaryPresses++; };
            Action<bool> secondaryListener = value => { if (value) secondaryPresses++; secondaryLast = value; };
            EventInfo primaryEvent = held.GetType().GetEvent("ClientOnLeftClickChanged");
            EventInfo secondaryEvent = held.GetType().GetEvent("ClientOnRightClickChanged");
            primaryEvent.AddEventHandler(held, primaryListener); secondaryEvent.AddEventHandler(held, secondaryListener);
            try
            {
                NetworkServer.Spawn(food);
                AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 8;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native curry pickup did not replicate"); yield return null; }
                manager.Pointer.Cancel();
                toggle.Invoke(null, null); paused = true;
                attack.Invoke(null, new object[] { true }); attack.Invoke(null, new object[] { false });
                Require(primaryPresses == 0 && !(bool)Member(curry, "Networkconsumed"), "VR input consumed food through settings");
                toggle.Invoke(null, null); paused = false;

                Transform aim = manager.Rig.Right.Aim;
                Vector3 originalAim = aim.position; Quaternion originalRotation = aim.rotation;
                try
                {
                    aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.up * 1000, Quaternion.LookRotation(Vector3.up, Vector3.forward));
                    Input("LeftGrip", true); Input("LeftTrigger", true); Input("LeftTriggerDown", true); pointerTick.Invoke(manager.Pointer, null);
                    Require(ReferenceEquals(Member(interactor, "CurrentHeldItem"), held), "Pee modifier sent a world-interaction command");
                    Input("LeftTriggerDown", false); Input("LeftTrigger", false); Input("LeftGrip", false); pointerTick.Invoke(manager.Pointer, null);
                    Input("LeftTrigger", true); Input("LeftTriggerDown", true); pointerTick.Invoke(manager.Pointer, null);
                    Input("LeftTriggerDown", false); Input("LeftTrigger", false); pointerTick.Invoke(manager.Pointer, null);
                    deadline = Time.realtimeSinceStartup + 8;
                    while (Member(interactor, "CurrentHeldItem") != null)
                    { Require(Time.realtimeSinceStartup < deadline, "Left trigger did not deliver native world interaction while holding an item"); yield return null; }
                    Require(primaryPresses == 0 && !(bool)Member(curry, "Networkconsumed"), "World-interaction control consumed the held food");
                    AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                    deadline = Time.realtimeSinceStartup + 8;
                    while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                    { Require(Time.realtimeSinceStartup < deadline, "Curry pickup after world interaction did not replicate"); yield return null; }
                    aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.up * 1000, Quaternion.LookRotation(Vector3.up, Vector3.forward));
                    Logger.LogInfo("FOOD PASS: left-trigger native world interaction works while holding food, without consumption or pee-modifier overlap");
                    Input("Secondary", true); Input("SecondaryDown", true); pointerTick.Invoke(manager.Pointer, null);
                    Input("Secondary", false); Input("SecondaryDown", false); pointerTick.Invoke(manager.Pointer, null);
                    Require(secondaryPresses == 1 && !secondaryLast, "VR secondary input did not reach native held-item press/release events");
                    Input("Grip", false); Input("Trigger", true); Input("TriggerDown", true); pointerTick.Invoke(manager.Pointer, null);
                    Input("TriggerDown", false); Input("Trigger", false); Input("TriggerUp", true); pointerTick.Invoke(manager.Pointer, null);
                    Input("TriggerUp", false);
                }
                finally { aim.SetPositionAndRotation(originalAim, originalRotation); }
                Require(primaryPresses == 1, "VR primary input bypassed the native held-item use callback");
                deadline = Time.realtimeSinceStartup + 8;
                while (trackedPoops.Count == 0)
                { Require(Time.realtimeSinceStartup < deadline, "Native curry use did not start its networked food effect"); yield return null; }
                Require(Member(fart, "forcedFartActive") is bool active && active, "Food effect did not reach the native local gassy state");
                Require(trackedPoops.Cast<GameObject>().Any(go => go != null && go.GetComponent<NetworkIdentity>()?.netId > 0), "Native food effect did not network-spawn its poop");
                Logger.LogInfo("FOOD PASS: native primary/secondary use events, menu suppression, curry consumption and native network-spawned poop/gassy state");
                deadline = Time.realtimeSinceStartup + 20;
                while (Member(fart, "curryFartRoutine") != null || Member(fart, "forcedFartActive") is bool stillActive && stillActive)
                { Require(Time.realtimeSinceStartup < deadline, "Native food effect did not expire normally"); yield return null; }
                Logger.LogInfo("FOOD PASS: native curry/gassy duration expires normally");
            }
            finally
            {
                Input("Secondary", false); Input("SecondaryDown", false); Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                Input("LeftTrigger", false); Input("LeftTriggerDown", false); Input("LeftGrip", false);
                attack.Invoke(null, new object[] { false }); secondary.Invoke(null, new object[] { false });
                if (paused) toggle.Invoke(null, null);
                if (food != null)
                {
                    primaryEvent.RemoveEventHandler(held, primaryListener); secondaryEvent.RemoveEventHandler(held, secondaryListener);
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), held)) AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(food);
                }
                AccessTools.Method(fart.GetType(), "ServerDestroyAllLaxativePoop").Invoke(fart, null);
                foreach (string routineName in new[] { "curryFartRoutine", "forcedRoutine" })
                    if (Member(fart, routineName) is Coroutine routine)
                    {
                        ((MonoBehaviour)fart).StopCoroutine(routine);
                        AccessTools.Field(fart.GetType(), routineName).SetValue(fart, null);
                    }
                foreach (string end in new[] { "curryFartEndTime", "forcedFartEndTime", "curryBoostEndTime" })
                    AccessTools.Field(fart.GetType(), end).SetValue(fart, 0f);
                AccessTools.Field(fart.GetType(), "forcedFartActive").SetValue(fart, false);
                AccessTools.Method(fart.GetType(), "UpdateGassyIcon").Invoke(fart, null);
                manager.Pointer.Cancel();
            }
        }
    }
}
