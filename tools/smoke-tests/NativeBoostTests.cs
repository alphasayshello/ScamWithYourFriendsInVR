using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static Component boostCone;
        private static readonly List<GameObject> boostObjects = new List<GameObject>();
        private static void BeforeConeSpawn(object __instance, out HashSet<uint> __state)
            => __state = ReferenceEquals(__instance, boostCone) ? new HashSet<uint>(NetworkServer.spawned.Keys) : null;
        private static void AfterConeSpawn(HashSet<uint> __state)
        {
            if (__state == null) return;
            foreach (var entry in NetworkServer.spawned)
                if (!__state.Contains(entry.Key) && entry.Value != null) boostObjects.Add(entry.Value.gameObject);
        }

        private IEnumerator TestNativeBoost()
        {
            var manager = VRManager.Instance;
            Component interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            Component mode = NetworkClient.localPlayer.GetComponent(Find("PlayerModeManager"));
            Component fart = NetworkClient.localPlayer.GetComponent(Find("PlayerFartManager"));
            Require(Member(interactor, "CurrentHeldItem") == null && Member(interactor, "CurrentHipItem") == null,
                "Cone fixture requires empty native hand and hip slots");
            bool wasAgent = (bool)Member(mode, "isAgent");
            Type coneType = Find("ConeEnshittification"), heldType = Find("HeldItemInteractable");
            GameObject prefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null
                && go.GetComponent(coneType) != null && Member(go.GetComponent(heldType), "ItemName") as string == "Traffic Cone");
            Require(prefab != null, "Native traffic-cone prefab unavailable");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var beforeController = AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches"), "BeforeController");
            var pointerTick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            var setAgent = AccessTools.Method(mode.GetType(), "ServerSetIsAgent");
            var probe = new Harmony("com.swyf.vr.boostprobe");
            probe.Patch(AccessTools.Method(coneType, "ServerSpawnOneShit"),
                prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BeforeConeSpawn)),
                postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AfterConeSpawn)));
            GameObject fixture = null;
            Component held = null;
            object controller = Instance("Metater.MetaPlayerController");
            Vector3 originalPosition = NetworkClient.localPlayer.transform.position;
            bool paused = false;
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Swap()
            {
                Transform aim = manager.Rig.Right.Aim;
                Vector3 savedPosition = aim.position; Quaternion savedRotation = aim.rotation;
                try
                {
                    aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.up * 1000, Quaternion.LookRotation(Vector3.up, Vector3.forward));
                    Input("LeftGrip", true); Input("Secondary", true); Input("SecondaryDown", true); pointerTick.Invoke(manager.Pointer, null);
                    Input("SecondaryDown", false); Input("Secondary", false); pointerTick.Invoke(manager.Pointer, null); Input("LeftGrip", false);
                }
                finally { aim.SetPositionAndRotation(savedPosition, savedRotation); }
            }
            try
            {
                setAgent.Invoke(mode, new object[] { true });
                yield return new WaitForSecondsRealtime(.8f);
                fixture = Instantiate(prefab, interactor.transform.position + interactor.transform.forward * 1.5f, Quaternion.identity);
                held = fixture.GetComponent(heldType); boostCone = fixture.GetComponent(coneType); boostObjects.Clear();
                Require(!(bool)Member(boostCone, "NextWillDestroy"), "Cone fixture must permit a nonfinal activation");
                NetworkServer.Spawn(fixture);
                AccessTools.Method(heldType, "CmdInteract").Invoke(held, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 8;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native cone pickup did not replicate"); yield return null; }
                manager.Pointer.Cancel(); Swap();
                deadline = Time.realtimeSinceStartup + 8;
                while (!ReferenceEquals(Member(interactor, "CurrentHipItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Controller hip swap did not reach native slot authority"); yield return null; }
                Require((bool)AccessTools.Method(fart.GetType(), "HasConeHipItem").Invoke(fart, null), "Native cone eligibility rejected the equipped fixture");
                toggle.Invoke(null, null); paused = true; Input("FartDown", true); beforeController.Invoke(null, new[] { controller }); Input("FartDown", false);
                Require(Convert.ToInt32(Member(boostCone, "NetworkenshittifyRequestCount")) == 0, "Settings allowed cone activation");
                toggle.Invoke(null, null); paused = false;
                Input("FartDown", true);
                var pressed = AccessTools.Method(Find("GameInputBindings"), "Pressed", new[] { typeof(string), typeof(string) });
                Require(!(bool)pressed.Invoke(null, new object[] { "Shortcuts", "ManualFart" }), "VR cone boost also injected the ordinary manual-fart shortcut");
                beforeController.Invoke(null, new[] { controller });
                Require((Member(controller, "knockbackEnvelopes") as IList)?.Count > 0, "Controller cone action did not add the native boost envelope");
                AccessTools.Method(controller.GetType(), "ClearKnockback").Invoke(controller, null);
                float cooldown = Convert.ToSingle(Member(fart, "nextAllowedFartTime"));
                beforeController.Invoke(null, new[] { controller }); Input("FartDown", false);
                Require((Member(controller, "knockbackEnvelopes") as IList)?.Count == 0 && Convert.ToSingle(Member(fart, "nextAllowedFartTime")) == cooldown,
                    "Repeated input bypassed native boost cooldown");
                deadline = Time.realtimeSinceStartup + 8;
                while (Convert.ToInt32(Member(boostCone, "NetworkenshittifyRequestCount")) == 0 || boostObjects.Count == 0)
                { Require(Time.realtimeSinceStartup < deadline, "Native cone action did not synchronize its counter/spawned effect"); yield return null; }
                Require(Convert.ToInt32(Member(boostCone, "NetworkenshittifyRequestCount")) == 1 && (bool)Member(boostCone, "NetworkisEnshittified"),
                    "Native cone use did not retain one authoritative activation");
                Swap(); deadline = Time.realtimeSinceStartup + 8;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Reverse controller hip swap did not replicate"); yield return null; }
                Logger.LogInfo("BOOST PASS: native hip swap/reverse, controller cone activation, authority counter/effect spawn, cooldown and settings suppression; trajectory/final use not covered");
            }
            finally
            {
                Input("FartDown", false); Input("LeftGrip", false); Input("Secondary", false); Input("SecondaryDown", false); manager.Pointer.Cancel();
                if (paused) toggle.Invoke(null, null);
                AccessTools.Method(controller.GetType(), "ClearKnockback").Invoke(controller, null);
                if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), held)) AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                if (ReferenceEquals(Member(interactor, "CurrentHipItem"), held)) AccessTools.Method(interactor.GetType(), "ServerClearHipSlot").Invoke(interactor, null);
                if (fixture != null) NetworkServer.Destroy(fixture);
                foreach (GameObject effect in boostObjects) if (effect != null) NetworkServer.Destroy(effect);
                boostObjects.Clear(); boostCone = null; probe.UnpatchSelf();
                setAgent.Invoke(mode, new object[] { wasAgent });
                AccessTools.Method(controller.GetType(), "Teleport").Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter();
            }
        }
    }
}
