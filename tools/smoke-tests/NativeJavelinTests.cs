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
        private static Component javelinFixture;
        private static Vector3 javelinOrigin, javelinDirection, javelinSpawnTarget, javelinCommandTarget;
        private static int javelinSpawns, javelinCommands;
        private static void AimJavelinFixture(object __instance)
        {
            if (ReferenceEquals(__instance, javelinFixture))
                VRManager.Instance.Rig.Right.Aim.SetPositionAndRotation(javelinOrigin, Quaternion.LookRotation(javelinDirection));
        }
        private static bool CaptureJavelinMissile(object __instance, Vector3 __2)
        {
            if (!ReferenceEquals(__instance, javelinFixture)) return true;
            javelinSpawns++; javelinSpawnTarget = __2; return false;
        }
        private static void ObserveJavelinCommand(object __instance, Vector3 __2)
        {
            if (!ReferenceEquals(__instance, javelinFixture)) return;
            javelinCommands++; javelinCommandTarget = __2;
        }
        private static bool SuppressFixtureJavelinRecoil(object __instance) => !ReferenceEquals(__instance, javelinFixture);

        // Runs on the held-weapon suite's isolated floor. Keep native equipment,
        // trigger callbacks and authority; suppress only owned missile effects
        // and the launcher's large local recoil during this targeting probe.
        private IEnumerator TestNativeJavelin()
        {
            Type type = Find("JavelinInteractable");
            var manager = VRManager.Instance;
            Component interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            GameObject prefab = NetworkManager.singleton.spawnPrefabs.Where(go => go != null && go.GetComponent(type) != null)
                .OrderBy(go => go.name).FirstOrDefault(go => Member(go.GetComponent(type), "daukeyMode") is bool mode && !mode);
            Require(prefab != null && Member(interactor, "CurrentHeldItem") == null, "Native single-missile javelin prefab or empty hands unavailable");
            GameObject item = null, target = null, unheld = null;
            var probe = new Harmony("com.swyf.vr.javelinfixture");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            try
            {
                item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity);
                javelinFixture = item.GetComponent(type);
                Component held = item.GetComponent(Find("HeldItemInteractable"));
                NetworkServer.Spawn(item);
                AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 8;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native javelin pickup did not complete"); yield return null; }
                manager.Pointer.Cancel();
                javelinOrigin = manager.Rig.Head.transform.position + Vector3.up * .5f;
                javelinDirection = Vector3.right;
                target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                target.name = "SWYF isolated javelin aim target";
                target.transform.position = javelinOrigin + javelinDirection * 3;
                target.transform.localScale = Vector3.one * .5f;
                int mask = ((LayerMask)Member(javelinFixture, "raycastLayerMask")).value;
                int layer = 0; while (layer < 31 && (mask & (1 << layer)) == 0) layer++;
                Require((mask & (1 << layer)) != 0, "Native javelin target mask empty");
                target.layer = layer; Physics.SyncTransforms();
                AimJavelinFixture(javelinFixture);
                Require(Physics.Raycast(javelinOrigin, javelinDirection, out RaycastHit expected, 5, mask, QueryTriggerInteraction.Ignore)
                    && expected.collider == target.GetComponent<Collider>(), "Isolated javelin controller ray is obstructed");
                object[] args = { Vector3.zero };
                bool found = (bool)AccessTools.Method(type, "TryRaycastTarget").Invoke(javelinFixture, args);
                Require(found && Vector3.Distance((Vector3)args[0], expected.point) < .01f,
                    "Native javelin targeting does not follow controller ray");
                Logger.LogInfo("JAVELIN AIM PASS: native targeting resolves the controller's isolated collider instead of head-camera aim");
                var camera = UnityEngine.Camera.main;
                Require(camera != null && Vector3.Angle(camera.transform.forward, javelinDirection) > 45,
                    "Javelin fallback probe needs distinct head and controller directions");
                void ExpectNative(Component owner, string context)
                {
                    bool nativeFound = Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit,
                        Convert.ToSingle(Member(owner, "maxDistance")), (LayerMask)Member(owner, "raycastLayerMask"), QueryTriggerInteraction.Ignore);
                    object[] nativeArgs = { Vector3.zero };
                    bool actual = (bool)AccessTools.Method(type, "TryRaycastTarget").Invoke(owner, nativeArgs);
                    Require(actual == nativeFound && (!actual || Vector3.Distance((Vector3)nativeArgs[0], hit.point) < .01f),
                        "Javelin camera fallback changed for " + context);
                }
                bool ready = manager.Ready, focused = manager.Input.HasInputFocus, tracked = manager.Rig.Right.IsTracked;
                try
                {
                    AccessTools.Property(typeof(VRManager), "Ready").SetValue(manager, false);
                    ExpectNative(javelinFixture, "inactive VR");
                    AccessTools.Property(typeof(VRManager), "Ready").SetValue(manager, ready);
                    Input("HasInputFocus", false); ExpectNative(javelinFixture, "lost focus"); Input("HasInputFocus", focused);
                    AccessTools.Property(manager.Rig.Right.GetType(), "IsTracked").SetValue(manager.Rig.Right, false);
                    ExpectNative(javelinFixture, "untracked right controller");
                }
                finally
                {
                    AccessTools.Property(typeof(VRManager), "Ready").SetValue(manager, ready);
                    Input("HasInputFocus", focused);
                    AccessTools.Property(manager.Rig.Right.GetType(), "IsTracked").SetValue(manager.Rig.Right, tracked);
                }
                unheld = Instantiate(prefab, interactor.transform.position + Vector3.down * 10, Quaternion.identity);
                unheld.SetActive(false);
                ExpectNative(unheld.GetComponent(type), "another unheld item");
                Destroy(unheld); unheld = null;
                Logger.LogInfo("JAVELIN FALLBACK PASS: inactive VR, lost focus, lost right tracking and an unheld item preserve native camera targeting");
                probe.Patch(AccessTools.Method(type, "Update"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AimJavelinFixture)));
                probe.Patch(AccessTools.Method(type, "SpawnMissile"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(CaptureJavelinMissile)));
                probe.Patch(AccessTools.Method(type, "ApplyLocalRecoil"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SuppressFixtureJavelinRecoil)));
                probe.Patch(AccessTools.Method(type, "CmdShootMissile"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveJavelinCommand)));
                javelinSpawns = javelinCommands = 0;
                Input("Grip", false); Input("LeftGrip", false);
                holdInjectedTrigger = true; Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                deadline = Time.realtimeSinceStartup + 5;
                while (javelinFixture != null && !(Member(javelinFixture, "wasShot") is bool shot && shot))
                { Require(Time.realtimeSinceStartup < deadline, "Controller trigger did not synchronize native javelin firing"); yield return null; }
                Require(javelinFixture != null && javelinSpawns == 1 && javelinCommands == 1
                    && Vector3.Distance(javelinSpawnTarget, expected.point) < .01f && Vector3.Distance(javelinCommandTarget, expected.point) < .01f,
                    "Native javelin launch duplicated or lost the controller-selected target");
                Require(Member(interactor, "CurrentHeldItem") == null, "Fired javelin did not follow native held-item release");
                Logger.LogInfo("JAVELIN FIRE PASS: controller trigger launches once, carries controller target through the native command, synchronizes wasShot and releases the held item; missile effects/recoil intercepted");
            }
            finally
            {
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                manager.Pointer.Cancel();
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item.GetComponent(Find("HeldItemInteractable"))))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                probe.UnpatchSelf(); javelinFixture = null;
                if (unheld != null) Destroy(unheld);
                if (target != null) Destroy(target);
            }
        }
    }
}
