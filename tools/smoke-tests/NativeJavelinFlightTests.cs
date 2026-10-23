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
        private static Component flightLauncher;
        private static bool launcherGrip, launcherSpawnScope;
        private static readonly List<Component> launcherMissiles = new List<Component>();
        private static readonly HashSet<GameObject> launcherImpacts = new HashSet<GameObject>();
        private static readonly List<Vector3> launcherBlastCalls = new List<Vector3>();
        private static readonly List<object> launcherRecoil = new List<object>();
        private static bool launcherRecoilScope;
        private static Vector3 launcherAimPosition;
        private static Quaternion launcherAimRotation;

        private static void HoldLauncherGrip(Input.VRHandController __instance)
        {
            if (!launcherGrip || !ReferenceEquals(__instance, VRManager.Instance?.Rig?.Right)) return;
            __instance.transform.SetPositionAndRotation(launcherAimPosition, launcherAimRotation);
            __instance.Aim.SetPositionAndRotation(launcherAimPosition, launcherAimRotation);
        }
        private static void BeginLauncherSpawn(object __instance, ref bool __state)
        {
            __state = launcherSpawnScope;
            launcherSpawnScope = ReferenceEquals(__instance, flightLauncher);
        }
        private static void EndLauncherSpawn(bool __state) => launcherSpawnScope = __state;
        private static void BeginLauncherRecoil(object __instance, ref bool __state)
        { __state = launcherRecoilScope; launcherRecoilScope = ReferenceEquals(__instance, flightLauncher); }
        private static void EndLauncherRecoil(bool __state) => launcherRecoilScope = __state;
        private static void ObserveLauncherRecoil(object __0)
        { if (launcherRecoilScope) launcherRecoil.Add(__0); }
        private static void ObserveLauncherMissile(Component __instance)
        { if (launcherSpawnScope) launcherMissiles.Add(__instance); }
        private static void ObserveLauncherImpact(Component __instance)
        {
            if (!launcherMissiles.Contains(__instance)) return;
            Type dynamite = Find("DynamiteInteractable");
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
                if (identity != null && identity.GetComponent(dynamite) != null
                    && Vector3.Distance(identity.transform.position, __instance.transform.position) < .1f)
                    launcherImpacts.Add(identity.gameObject);
        }
        private static void ObserveLauncherBlast(Vector3 __0)
        {
            if (launcherImpacts.Any(go => go != null && Vector3.Distance(
                (Vector3)Member(go.GetComponent(Find("DynamiteInteractable")), "ExplosionPosition"), __0) < .01f))
                launcherBlastCalls.Add(__0);
        }

        private IEnumerator TestNativeJavelinFlight()
        {
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            var interactor = player.GetComponent(Find("PlayerInteractor"));
            Type launcherType = Find("JavelinInteractable"), missileType = Find("BezierMissile"), heldType = Find("HeldItemInteractable");
            var prefabs = NetworkManager.singleton.spawnPrefabs.Where(go => go != null && go.GetComponent(launcherType) != null)
                .OrderBy(go => go.name).ToArray();
            Require(prefabs.Length > 0 && Member(interactor, "CurrentHeldItem") == null, "Launcher flight requires native prefabs and empty hands");
            Logger.LogInfo("LAUNCHER PREFABS: " + string.Join(", ", prefabs.Select(go => go.name + "; triple=" + Member(go.GetComponent(launcherType), "daukeyMode"))));
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            var clearKnockback = AccessTools.Method(controller.GetType(), "ClearKnockback");
            Require(body != null && ((IList)Member(controller, "knockbackEnvelopes")).Count == 0, "Launcher check needs a settled native body");
            Vector3 original = player.transform.position;
            GameObject floor = null, item = null, target = null;
            var probe = new Harmony("com.swyf.vr.launcherflightfixture");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            bool paused = false;
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Press()
            {
                Input("Grip", false); Input("LeftGrip", false); holdInjectedTrigger = true;
                Input("Trigger", true); Input("TriggerDown", true); tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
            }
            void Release()
            {
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", true);
                tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
            }
            void CleanupProjectiles()
            {
                foreach (GameObject impact in launcherImpacts) if (impact != null) NetworkServer.Destroy(impact);
                foreach (Component missile in launcherMissiles) if (missile != null) Destroy(missile.gameObject);
                launcherImpacts.Clear(); launcherMissiles.Clear(); launcherBlastCalls.Clear(); launcherRecoil.Clear();
            }
            try
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "SWYF isolated launcher floor";
                floor.transform.localScale = new Vector3(240, .2f, 240);
                floor.transform.position = new Vector3(1200, 50 + body.bounds.min.y - original.y - .12f, 1200);
                Physics.SyncTransforms();
                probe.Patch(AccessTools.Method(typeof(Input.VRHandController), "Sync"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(HoldLauncherGrip)));
                probe.Patch(AccessTools.Method(launcherType, "SpawnMissile"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BeginLauncherSpawn)),
                    postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(EndLauncherSpawn)));
                probe.Patch(AccessTools.Method(launcherType, "ApplyLocalRecoil"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BeginLauncherRecoil)),
                    postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(EndLauncherRecoil)));
                probe.Patch(AccessTools.Method(controller.GetType(), "AddKnockback"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveLauncherRecoil)));
                probe.Patch(AccessTools.Method(missileType, "LaunchAtPosition"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveLauncherMissile)));
                probe.Patch(AccessTools.Method(missileType, "ServerSpawnImpactDynamite"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveLauncherImpact)));
                probe.Patch(AccessTools.Method(Find("WorldExplosionManager"), "ServerTriggerExplosion"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveLauncherBlast)));
                foreach (GameObject prefab in prefabs)
                {
                    manager.Pointer.Cancel();
                    teleport.Invoke(controller, new object[] { new Vector3(1200, 50, 1200), true }); manager.Rig.Recenter();
                    yield return new WaitForSecondsRealtime(1.2f);
                    Vector3 standingEye = manager.Rig.Head.transform.position - player.transform.position;
                    launcherAimPosition = manager.Rig.Head.transform.position + new Vector3(.3f, -.2f, .1f);
                    launcherAimRotation = Quaternion.LookRotation(Vector3.right); launcherGrip = true;
                    HoldLauncherGrip(manager.Rig.Right);
                    item = Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity); NetworkServer.Spawn(item);
                    flightLauncher = item.GetComponent(launcherType);
                    var held = item.GetComponent(heldType);
                    AccessTools.Method(heldType, "CmdInteract").Invoke(held, new object[] { interactor });
                    float deadline = Time.realtimeSinceStartup + 6;
                    while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                    { Require(Time.realtimeSinceStartup < deadline, "Native launcher pickup failed"); yield return null; }
                    yield return new WaitForSecondsRealtime(.7f); yield return new WaitForEndOfFrame();
                    Transform muzzle = Member(flightLauncher, "missileSpawnPoint") as Transform;
                    Transform authoredGrip = AccessTools.Method(heldType, "GetHoldPointTransform").Invoke(held, null) as Transform;
                    Require(muzzle != null && authoredGrip != null && Vector3.Distance(authoredGrip.position, launcherAimPosition) < .03f,
                        "Native launcher model does not follow the tracked grip");
                    Vector3 muzzleOffset = Quaternion.Inverse(launcherAimRotation) * (muzzle.position - launcherAimPosition);
                    Quaternion muzzleRotation = Quaternion.Inverse(launcherAimRotation) * muzzle.rotation;
                    launcherAimPosition += Vector3.forward * .25f;
                    launcherAimRotation = Quaternion.AngleAxis(25, Vector3.up) * launcherAimRotation;
                    yield return new WaitForSecondsRealtime(.6f); yield return new WaitForEndOfFrame();
                    Require(Vector3.Distance(muzzle.position, launcherAimPosition + launcherAimRotation * muzzleOffset) < .03f
                        && Quaternion.Angle(muzzle.rotation, launcherAimRotation * muzzleRotation) < 1,
                        "Native launcher muzzle does not follow controller translation and rotation");
                    Logger.LogInfo("LAUNCHER MODEL PASS: tracked grip carries native launcher/muzzle through 25cm movement and 25-degree rotation; item=" + prefab.name);
                    target = GameObject.CreatePrimitive(PrimitiveType.Cube); target.name = "SWYF isolated launcher endpoint";
                    target.transform.position = launcherAimPosition + launcherAimRotation * Vector3.forward * 50;
                    target.transform.localScale = Vector3.one * 2;
                    int mask = ((LayerMask)Member(flightLauncher, "raycastLayerMask")).value;
                    int layer = 0; while (layer < 31 && (mask & (1 << layer)) == 0) layer++;
                    Require((mask & (1 << layer)) != 0, "Launcher targeting mask empty"); target.layer = layer; Physics.SyncTransforms();
                    Require(Physics.Raycast(launcherAimPosition, launcherAimRotation * Vector3.forward, out RaycastHit endpoint, 55, mask, QueryTriggerInteraction.Ignore)
                        && endpoint.collider == target.GetComponent<Collider>(), "Launcher target is obstructed");
                    int expected = (bool)Member(flightLauncher, "daukeyMode") ? 3 : 1;
                    toggle.Invoke(null, null); paused = true; Press(); Release();
                    Require(!(bool)Member(flightLauncher, "localHasShot") && launcherMissiles.Count == 0, "Settings fired native launcher");
                    toggle.Invoke(null, null); paused = false;
                    Vector3 recoilBefore = player.transform.position;
                    Vector3 recoilBack = Vector3.ProjectOnPlane(-muzzle.forward, Vector3.up).normalized;
                    Require(recoilBack.sqrMagnitude > .9f, "Launcher recoil fixture needs a horizontal muzzle heading");
                    Vector3 expectedRecoil = Vector3.Lerp(recoilBack, Vector3.up, Convert.ToSingle(Member(flightLauncher, "recoilUpwardBlend"))).normalized;
                    float recoilForce = Convert.ToSingle(Member(flightLauncher, "recoilStrength"));
                    float recoilDuration = Convert.ToSingle(Member(flightLauncher, "recoilDuration"));
                    Press();
                    deadline = Time.realtimeSinceStartup + 10;
                    while (launcherMissiles.Count < expected || launcherImpacts.Count < expected || item != null
                        || launcherImpacts.Any(go => go != null && Member(go.GetComponent(Find("DynamiteInteractable")), "dynamiteState").ToString() != "Exploded"))
                    { Require(Time.realtimeSinceStartup < deadline, "Native launch/impact/expiry incomplete: item=" + prefab.name + "; missiles=" + launcherMissiles.Count
                        + "; impacts=" + launcherImpacts.Count + "; blasts=" + launcherBlastCalls.Count + "; itemAlive=" + (item != null)
                        + "; states=" + string.Join(",", launcherImpacts.Select(go => go == null ? "destroyed" : Member(go.GetComponent(Find("DynamiteInteractable")), "dynamiteState").ToString()))); yield return null; }
                    Release();
                    bool worldExplosions = Instance("WorldExplosionManager") is Component world && world != null;
                    Require(launcherMissiles.Count == expected && launcherImpacts.Count == expected && launcherBlastCalls.Count == (worldExplosions ? expected : 0),
                        "Native launcher duplicated projectiles or impact commands");
                    if (!worldExplosions) Logger.LogInfo("LAUNCHER LIMIT: native WorldExplosionManager absent in this lobby; environment/victim blast outcomes remain unverified");
                    foreach (Component missile in launcherMissiles)
                        Require(missile != null && (bool)Member(missile, "impactReached")
                            && Vector3.Distance(missile.transform.position, endpoint.point) < .05f
                            && !(Member(missile, "missileModel") as GameObject).activeSelf,
                            "Native projectile did not reach selected endpoint and hide its spent model");
                    Require(launcherBlastCalls.All(p => launcherImpacts.Any(go => go != null && Vector3.Distance(
                            (Vector3)Member(go.GetComponent(Find("DynamiteInteractable")), "ExplosionPosition"), p) < .01f))
                        && launcherImpacts.All(go => go != null && Vector3.Distance(go.transform.position, endpoint.point) < .05f
                            && Member(go.GetComponent(Find("DynamiteInteractable")), "dynamiteState").ToString() == "Exploded"),
                        "Native impact dynamite did not explode at the selected endpoint");
                    Logger.LogInfo("LAUNCHER FLIGHT PASS: native controller shot flies to selected endpoint, creates " + expected
                        + " native exploding impact(s), avoids duplicate host launches and expires launcher; item=" + prefab.name);
                    while (((IList)Member(controller, "knockbackEnvelopes")).Count > 0)
                    { Require(Time.realtimeSinceStartup < deadline, "Native recoil failed to expire"); yield return null; }
                    yield return new WaitForSecondsRealtime(1.2f); yield return new WaitForEndOfFrame();
                    Require(Vector3.ProjectOnPlane(player.transform.position - recoilBefore, Vector3.up).magnitude > 1,
                        "Native launcher recoil did not move the real collision body");
                    Require(launcherRecoil.Count == 1 && Vector3.Distance((Vector3)Member(launcherRecoil[0], "vector"), expectedRecoil) < .01f
                        && Mathf.Abs(Convert.ToSingle(Member(launcherRecoil[0], "force")) - recoilForce) < .01f
                        && Mathf.Abs(Convert.ToSingle(Member(launcherRecoil[0], "duration")) - recoilDuration) < .01f,
                        "Native launcher recoil changed its authored muzzle-relative vector, force or duration");
                    Require(Vector3.Dot(Vector3.ProjectOnPlane(player.transform.position - recoilBefore, Vector3.up).normalized, recoilBack) > .9f,
                        "Native collision-body recoil did not follow the tracked launcher's muzzle heading");
                    Require(Vector3.Distance(manager.Rig.Head.transform.position - player.transform.position, standingEye) < .15f,
                        "Native launcher recoil recovery displaced the body-relative VR eye");
                    Require(Member(interactor, "CurrentHeldItem") == null, "Expired launcher retained held ownership");
                    Logger.LogInfo("LAUNCHER RECOVERY PASS: authored recoil moves native collision body, expires and restores standing eye alignment/empty hands; item=" + prefab.name);
                    launcherGrip = false; flightLauncher = null; CleanupProjectiles(); Destroy(target); target = null;
                }
            }
            finally
            {
                Release(); manager.Pointer.Cancel(); if (paused) toggle.Invoke(null, null);
                launcherGrip = launcherSpawnScope = launcherRecoilScope = false;
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item.GetComponent(heldType)))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                CleanupProjectiles(); flightLauncher = null; probe.UnpatchSelf();
                clearKnockback.Invoke(controller, null); teleport.Invoke(controller, new object[] { original, true }); manager.Rig.Recenter();
                if (target != null) Destroy(target); if (floor != null) Destroy(floor);
            }
            Logger.LogInfo("LAUNCHER COMPLETE PASS: registered launcher variants use native model tracking, controller target, real projectile/impact/expiry and recoil; remote victims and headset feel remain unverified");
        }
    }
}
