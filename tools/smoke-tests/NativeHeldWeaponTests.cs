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
        private static Component heldWeaponFixture;
        private static readonly List<Ray> heldWeaponRays = new List<Ray>();
        private static UnityEngine.Random.State heldWeaponRandomState;
        private static bool heldWeaponRandomScoped;
        private static bool CaptureHeldWeaponRay(object __instance, Ray __0)
        {
            if (!ReferenceEquals(__instance, heldWeaponFixture)) return true;
            heldWeaponRays.Add(__0); return false;
        }
        private static void BeginHeldSpread(object __instance)
        {
            if (!ReferenceEquals(__instance, heldWeaponFixture)) return;
            heldWeaponRandomState = UnityEngine.Random.state; heldWeaponRandomScoped = true;
            UnityEngine.Random.InitState(21834);
        }
        private static void EndHeldSpread(object __instance)
        {
            if (!ReferenceEquals(__instance, heldWeaponFixture) || !heldWeaponRandomScoped) return;
            UnityEngine.Random.state = heldWeaponRandomState; heldWeaponRandomScoped = false;
        }
        private static void AimHeldWeapon()
        {
            if (heldWeaponFixture == null) return;
            var camera = Member(heldWeaponFixture, "cam") as UnityEngine.Camera;
            var rig = VRManager.Instance.Rig;
            // Aim along the native camera's up axis, where camera-based shotgun
            // spread collapses onto one plane instead of the controller cone.
            rig.Right.Aim.SetPositionAndRotation(rig.Head.transform.position + camera.transform.right * .35f,
                Quaternion.LookRotation(camera.transform.up, camera.transform.forward));
        }
        private static void AimHeldWeaponForFire(object __instance)
        { if (ReferenceEquals(__instance, heldWeaponFixture)) AimHeldWeapon(); }

        private IEnumerator TestNativeHeldWeapons()
        {
            Type dataType = Find("RaycastWeaponData");
            Require(dataType != null, "Native held weapon data missing");
            var prefabs = NetworkManager.singleton.spawnPrefabs.Where(go => go != null && go.GetComponent(dataType) != null)
                .OrderBy(go => go.name).ToArray();
            Require(prefabs.Length > 0, "Native held weapon prefabs unavailable");
            var manager = VRManager.Instance;
            var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            Require(Member(interactor, "CurrentHeldItem") == null, "Held weapon fixture requires empty hands");
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            Vector3 originalPosition = NetworkClient.localPlayer.transform.position;
            Require(body != null, "Held weapon fixture requires the native collision body");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube), item = null;
            floor.name = "SWYF isolated held weapon floor";
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - originalPosition.y - .12f, 1050);
            floor.transform.localScale = new Vector3(20, .2f, 20);
            Physics.SyncTransforms();
            Type weaponType = Find("RaycastWeapon");
            var probe = new Harmony("com.swyf.vr.heldweaponfixture");
            var pointerTick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            var toggle = AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter"), "TogglePause");
            bool originalFocus = manager.Input.HasInputFocus, paused = false;
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Press()
            {
                holdInjectedTrigger = true; Input("Grip", false); Input("LeftGrip", false);
                Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                AimHeldWeapon(); pointerTick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
            }
            void Release()
            {
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", true);
                AimHeldWeapon(); pointerTick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
            }
            try
            {
                probe.Patch(AccessTools.Method(weaponType, "FireRayFromRay"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(CaptureHeldWeaponRay)));
                probe.Patch(AccessTools.Method(weaponType, "FireShotgun"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BeginHeldSpread)),
                    postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(EndHeldSpread)));
                probe.Patch(AccessTools.Method(weaponType, "FireChargedWeapon"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AimHeldWeaponForFire)));
                manager.Pointer.Cancel();
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.8f);
                Require(!manager.Rig.HeadObstructed, "Held weapon fixture did not obtain a clear head position");
                foreach (string name in new[] { "Shotgun", "Sniper" })
                {
                    GameObject prefab = prefabs.SingleOrDefault(p => p.name == name);
                    Require(prefab != null, "Native charged weapon prefab missing: " + name);
                    item = Instantiate(prefab, interactor.transform.position + interactor.transform.forward, Quaternion.identity);
                    Component held = item.GetComponent(Find("HeldItemInteractable"));
                    NetworkServer.Spawn(item);
                    AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                    float deadline = Time.realtimeSinceStartup + 8;
                    while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held) || Member(interactor, "weapon") is not Component candidate
                        || candidate is not Behaviour enabled || !enabled.enabled || !(bool)Member(candidate, "HasUsableWeapon"))
                    { Require(Time.realtimeSinceStartup < deadline, "Native held weapon pickup/equipment did not become ready: " + name); yield return null; }
                    heldWeaponFixture = Member(interactor, "weapon") as Component;
                    float charge = Convert.ToSingle(Member(heldWeaponFixture, "chargeSeconds"));
                    Require(charge > .15f && !(bool)Member(heldWeaponFixture, "requireAttackLayerHit"), "Native charged weapon fixture preconditions changed: " + name);
                    heldWeaponRays.Clear(); manager.Pointer.Cancel();
                    Press();
                    Require((bool)Member(heldWeaponFixture, "isCharging"), "Controller did not start native weapon charging: " + name);
                    yield return null; Release();
                    yield return new WaitForSecondsRealtime(charge + .1f);
                    Require(!(bool)Member(heldWeaponFixture, "isCharging") && heldWeaponRays.Count == 0, "Early trigger release fired native charged weapon: " + name);
                    Press(); toggle.Invoke(null, null); paused = true; pointerTick.Invoke(manager.Pointer, null);
                    yield return new WaitForSecondsRealtime(charge + .1f);
                    Require(!(bool)Member(heldWeaponFixture, "isCharging") && heldWeaponRays.Count == 0, "Settings failed to cancel native weapon charge: " + name);
                    toggle.Invoke(null, null); paused = false;
                    yield return new WaitForSecondsRealtime(.1f);
                    Require(!(bool)Member(heldWeaponFixture, "isCharging"), "Closing settings restarted held weapon input: " + name);
                    Release(); Press(); Input("HasInputFocus", false); pointerTick.Invoke(manager.Pointer, null); Input("HasInputFocus", originalFocus);
                    yield return new WaitForSecondsRealtime(charge + .1f);
                    Require(!(bool)Member(heldWeaponFixture, "isCharging") && heldWeaponRays.Count == 0, "Focus loss failed to cancel native weapon charge: " + name);
                    Release();
                    Logger.LogInfo("HELD WEAPON PASS: native " + name + " pickup, charging, early release, settings/focus cancellation and no held-input restart");
                    Press(); deadline = Time.realtimeSinceStartup + charge + 3;
                    while (heldWeaponRays.Count == 0)
                    { Require(Time.realtimeSinceStartup < deadline, "Native charged weapon did not fire: " + name); yield return null; }
                    Release();
                    AimHeldWeapon(); Transform aim = manager.Rig.Right.Aim;
                    Require(heldWeaponRays.All(ray => Vector3.Distance(ray.origin, aim.position) < .001f), "Native held weapon rays originated outside the controller: " + name);
                    Require(Vector3.Angle(heldWeaponRays[0].direction, aim.forward) < .01f, "Native central weapon ray did not use controller aim: " + name);
                    if ((bool)Member(heldWeaponFixture, "enableShotgun"))
                    {
                        float spread = Convert.ToSingle(Member(heldWeaponFixture, "shotgunSpreadAngle"));
                        Require(heldWeaponRays.Count == Convert.ToInt32(Member(heldWeaponFixture, "shotgunPelletCount")), "Native shotgun pellet count changed");
                        float horizontal = heldWeaponRays.Max(ray => Mathf.Abs(Vector3.Dot(ray.direction, aim.right)));
                        float vertical = heldWeaponRays.Max(ray => Mathf.Abs(Vector3.Dot(ray.direction, aim.up)));
                        Logger.LogInfo("HELD SPREAD: horizontal=" + horizontal + "; vertical=" + vertical + "; angle=" + spread);
                        Require(horizontal > Mathf.Sin(spread * .3f * Mathf.Deg2Rad) && vertical > Mathf.Sin(spread * .3f * Mathf.Deg2Rad),
                            "Native shotgun spread collapsed onto camera axes instead of the controller cone");
                    }
                    int rays = heldWeaponRays.Count;
                    Press(); Release();
                    Require(heldWeaponRays.Count == rays, "Native held weapon cooldown was bypassed: " + name);
                    Logger.LogInfo("HELD WEAPON PASS: native " + name + " charged fire, controller origin/aim/spread and cooldown; hit effects intercepted");
                    manager.Pointer.Cancel();
                    AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    // Let queued native animation/audio commands for the held
                    // identity reach the host before destroying this fixture.
                    yield return new WaitForSecondsRealtime(.2f);
                    NetworkServer.Destroy(item); item = null; heldWeaponFixture = null;
                    yield return null;
                }
            }
            finally
            {
                Release(); manager.Pointer.Cancel();
                Input("HasInputFocus", originalFocus);
                if (paused) toggle.Invoke(null, null);
                if (heldWeaponRandomScoped) { UnityEngine.Random.state = heldWeaponRandomState; heldWeaponRandomScoped = false; }
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item.GetComponent(Find("HeldItemInteractable"))))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                probe.UnpatchSelf(); heldWeaponFixture = null; heldWeaponRays.Clear();
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter(); Destroy(floor);
            }
        }
    }
}
