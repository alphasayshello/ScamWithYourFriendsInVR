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
        private static bool storageFixture, storageHeld, storageEdge;
        private static void KeepStorageInput(object input)
        {
            if (!storageFixture) return;
            AccessTools.Property(input.GetType(), "Storage").SetValue(input, storageHeld);
            AccessTools.Property(input.GetType(), "StorageDown").SetValue(input, storageEdge);
            AccessTools.Property(input.GetType(), "Move").SetValue(input, Vector2.zero);
            AccessTools.Property(input.GetType(), "Turn").SetValue(input, Vector2.zero);
        }

        private IEnumerator TestNativeStorage()
        {
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            var interactor = player.GetComponent(Find("PlayerInteractor"));
            var storage = player.GetComponent(Find("ButtStorage"));
            object items = Member(storage, "storedItems");
            int Count() => Convert.ToInt32(Member(items, "Count"));
            Require(storage != null && items != null && Count() == 0 && Member(interactor, "CurrentHeldItem") == null,
                "Storage fixture requires native component, empty storage and empty hands");
            Type heldType = Find("HeldItemInteractable"), sprayType = Find("PepperSprayInteractable");
            int capacity = Convert.ToInt32(Member(storage, "contrabandCapacityInStars"));
            var prefab = NetworkManager.singleton.spawnPrefabs.Where(go => go != null && go.GetComponent(heldType) is Component held
                && (bool)Member(held, "canPutInButt") && (bool)Member(held, "canBePutInBags")
                && Convert.ToInt32(Member(held, "itemContrabandStars")) > 0 && Convert.ToInt32(Member(held, "itemContrabandStars")) <= capacity)
                .OrderBy(go => go.GetComponent(sprayType) != null ? 0 : 1).ThenBy(go => go.name).FirstOrDefault();
            Require(prefab != null, "No native eligible contraband prefab is registered");
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            Require(body != null && manager.BodyActions.enabled, "Storage fixture requires active body/controller input");
            Vector3 originalPosition = player.transform.position, isolated = new Vector3(1050, 50, 1050);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated storage floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = isolated + Vector3.up * (body.bounds.min.y - originalPosition.y - .12f);
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(manager.BodyActions.GetType(), "Update");
            var before = new HashSet<uint>(NetworkServer.spawned.Keys);
            GameObject item = null;
            Component heldItem = null;
            bool paused = false, focus = manager.Input.HasInputFocus;
            void Drive(bool held)
            {
                storageEdge = held && !storageHeld; storageHeld = held;
                KeepStorageInput(manager.Input); tick.Invoke(manager.BodyActions, null);
                storageEdge = false; AccessTools.Property(manager.Input.GetType(), "StorageDown").SetValue(manager.Input, false);
            }
            bool Holding() => (bool)Member(storage, "holding");
            try
            {
                storageFixture = true; manager.Pointer.Cancel(); Physics.SyncTransforms();
                teleport.Invoke(controller, new object[] { isolated, true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(1.2f);
                item = Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity);
                heldItem = item.GetComponent(heldType);
                Component spray = item.GetComponent(sprayType);
                if (spray != null) AccessTools.Property(sprayType, "SprayTime").SetValue(spray, .7f);
                NetworkServer.Spawn(item);
                uint asset = item.GetComponent<NetworkIdentity>().assetId;
                AccessTools.Method(heldType, "CmdInteract").Invoke(heldItem, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 6;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), heldItem))
                { Require(Time.realtimeSinceStartup < deadline, "Native storage item pickup failed"); yield return null; }
                Require((bool)AccessTools.Method(storage.GetType(), "CanStore").Invoke(storage, new object[] { heldItem }), "Native authority rejects fixture item storage");
                toggle.Invoke(null, null); paused = true; Drive(true);
                Require(!Holding(), "Storage began through settings");
                toggle.Invoke(null, null); paused = false; Drive(true);
                Require(!Holding(), "Closing settings resumed a held storage chord");
                Drive(false); Drive(true); yield return new WaitForSecondsRealtime(.1f);
                Require(Holding() && Convert.ToSingle(Member(storage, "holdTimer")) > 0, "Fresh VR storage chord did not charge native action");
                foreach (bool giveUp in new[] { true, false })
                {
                    AccessTools.Property(manager.Input.GetType(), "GiveUp").SetValue(manager.Input, giveUp);
                    AccessTools.Property(manager.Input.GetType(), "GiveUpDown").SetValue(manager.Input, giveUp);
                    tick.Invoke(manager.BodyActions, null);
                }
                Require(Holding(), "Unrelated give-up release cancelled owned storage charge");
                Drive(false); yield return new WaitForSecondsRealtime(.1f);
                Require(!Holding() && Count() == 0 && ReferenceEquals(Member(interactor, "CurrentHeldItem"), heldItem), "Early storage release consumed or stored the item");
                Logger.LogInfo("STORAGE INPUT PASS: settings reject hold, held chord does not restart and fresh native charge/early release preserve the held item");
                Drive(true); yield return new WaitForSecondsRealtime(.1f);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, false); tick.Invoke(manager.BodyActions, null);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus); Drive(true);
                Require(!Holding() && Count() == 0, "Focus cancellation retained or restarted storage");
                Drive(false); Drive(true); yield return new WaitForSecondsRealtime(.1f);
                manager.BodyActions.enabled = false;
                Require(!Holding(), "Disabling body actions retained storage charge");
                manager.BodyActions.enabled = true; Drive(true);
                Require(!Holding(), "Re-enabling body actions resumed the held storage chord");
                Drive(false); Drive(true); yield return new WaitForSecondsRealtime(.1f); manager.BodyActions.Cancel(); Drive(true);
                Require(!Holding() && Count() == 0, "Session cancellation retained or replayed native storage charge");
                Logger.LogInfo("STORAGE RECOVERY PASS: focus, component disable and session cancellation release native charge and require a fresh chord");
                Drive(false); Drive(true); deadline = Time.realtimeSinceStartup + 12;
                while (Count() == 0 || item != null)
                { Require(Time.realtimeSinceStartup < deadline, "Native storage did not record and destroy the held item"); yield return null; }
                Require(Count() == 1 && Member(interactor, "CurrentHeldItem") == null && !Holding(), "Native store duplicated ownership or retained charge");
                yield return new WaitForSecondsRealtime(.3f);
                Require(Count() == 1, "Held storage chord repeated after a completed store");
                Logger.LogInfo("STORAGE STORE PASS: native held charge stores exactly one authored contraband item, clears the hand and destroys its original network identity; item=" + prefab.name);
                Drive(false); Drive(true); deadline = Time.realtimeSinceStartup + 12;
                NetworkIdentity returned = null;
                while (Count() != 0 || (returned = NetworkServer.spawned.Values.FirstOrDefault(identity => identity != null
                    && !before.Contains(identity.netId) && identity.assetId == asset)) == null)
                { Require(Time.realtimeSinceStartup < deadline, "Fresh empty-handed chord did not retrieve the native stored item"); yield return null; }
                Drive(false); yield return new WaitForSecondsRealtime(.4f);
                Require(returned != null && returned.GetComponent(heldType) != null, "Native retrieval failed to recreate the held item prefab");
                if (spray != null || returned.GetComponent(sprayType) != null)
                    Require(Mathf.Abs(Convert.ToSingle(Member(returned.GetComponent(sprayType), "SprayTime")) - .7f) < .001f,
                        "Native storage roundtrip changed partially used spray state");
                Logger.LogInfo("STORAGE RETRIEVE PASS: fresh empty-handed hold recreates the native prefab and retains its stored item state; item=" + prefab.name);
            }
            finally
            {
                if (paused) toggle.Invoke(null, null);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus);
                manager.BodyActions.enabled = true; Drive(false); manager.BodyActions.Cancel(); storageFixture = false;
                AccessTools.Property(manager.Input.GetType(), "GiveUp").SetValue(manager.Input, false);
                AccessTools.Property(manager.Input.GetType(), "GiveUpDown").SetValue(manager.Input, false);
                AccessTools.Method(items.GetType(), "Clear").Invoke(items, null);
                foreach (var identity in NetworkServer.spawned.Values.ToArray())
                    if (identity != null && !before.Contains(identity.netId) && identity.GetComponent(heldType) != null
                        && Vector3.Distance(identity.transform.position, isolated) < 30)
                    {
                        if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), identity.GetComponent(heldType)))
                            AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                        NetworkServer.Destroy(identity.gameObject);
                    }
                if (item != null) NetworkServer.Destroy(item);
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter(); Destroy(floor);
            }
            Logger.LogInfo("STORAGE COMPLETE PASS: native charge/cancel/store/retrieve and owned-fixture cleanup checked");
        }
    }
}
