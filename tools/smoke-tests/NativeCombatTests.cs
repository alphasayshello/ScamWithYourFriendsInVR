using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static Component combatWeapon;
        private static int combatRays;
        private static float combatOriginError, combatDirectionError;
        private static Collider combatTargetCollider;
        private static Mirror.NetworkIdentity combatVictim;
        private static float combatImpulseError;
        private static int combatDamageCommands;
        private static void AimCombatTarget(object __instance)
        {
            if (!ReferenceEquals(__instance, combatWeapon) || combatTargetCollider == null) return;
            Bounds bounds = combatTargetCollider.bounds;
            VRManager.Instance.Rig.Right.Aim.SetPositionAndRotation(bounds.center - Vector3.right * (bounds.extents.x + .25f), Quaternion.LookRotation(Vector3.right));
        }
        private static void ObserveDamageCommand(object __instance, Mirror.NetworkIdentity __0, Vector3 __1)
        {
            if (!ReferenceEquals(__instance, combatWeapon) || __0 != combatVictim) return;
            combatDamageCommands++;
            combatImpulseError = Mathf.Max(combatImpulseError, Vector3.Angle(__1, VRManager.Instance.Rig.Right.Aim.forward));
        }
        private static void ObserveCombatRay(object __instance, Ray __0)
        {
            if (!ReferenceEquals(__instance, combatWeapon)) return;
            var aim = VRManager.Instance.Rig.Right.Aim;
            combatRays++;
            combatOriginError = Mathf.Max(combatOriginError, Vector3.Distance(__0.origin, aim.position));
            combatDirectionError = Mathf.Max(combatDirectionError, Vector3.Angle(__0.direction, aim.forward));
        }

        private IEnumerator TestCombatTransitions()
        {
            Component player = Mirror.NetworkClient.localPlayer;
            Component mode = player.GetComponent(Find("PlayerModeManager"));
            bool originalRole = (bool)Member(mode, "isAgent");
            Vector3 originalPosition = player.transform.position;
            object controller = Instance("Metater.MetaPlayerController");
            MethodInfo setRole = AccessTools.Method(mode.GetType(), "ServerSetIsAgent");
            MethodInfo teleport = AccessTools.Method(controller.GetType(), "Teleport");
            Vector3 testPosition = originalPosition;
            Vector3? requestedEye = null;
            string eyeArgument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--swyf-combat-eye="));
            if (eyeArgument != null)
            {
                string[] values = eyeArgument.Substring("--swyf-combat-eye=".Length).Split(',');
                Require(values.Length == 3, "Combat eye diagnostic requires x,y,z");
                float Coordinate(int index)
                {
                    float value = float.Parse(values[index], System.Globalization.CultureInfo.InvariantCulture);
                    Require(!float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) < 10000, "Invalid combat eye diagnostic coordinate");
                    return value;
                }
                Vector3 targetEye = new Vector3(Coordinate(0), Coordinate(1), Coordinate(2));
                requestedEye = targetEye;
                testPosition += Vector3.ProjectOnPlane(targetEye - VRManager.Instance.Rig.AuthoredEye, Vector3.up);
            }
            try
            {
                for (int iteration = 0; iteration < 20; iteration++)
                {
                    SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                    setRole.Invoke(mode, new object[] { !originalRole });
                    yield return new WaitForSecondsRealtime(.8f);
                    setRole.Invoke(mode, new object[] { originalRole });
                    teleport.Invoke(controller, new object[] { testPosition, true }); VRManager.Instance.Rig.Recenter();
                    if (requestedEye.HasValue)
                    {
                        yield return new WaitForSecondsRealtime(1f);
                        Vector3 from = new Vector3(0, 1.65f, 0);
                        Vector3 to = VRManager.Instance.Rig.Origin.InverseTransformPoint(requestedEye.Value);
                        // Move in short tracking steps, as an actual crouch would,
                        // rather than tripping the large-pose-jump recenter guard.
                        for (int step = 1; step <= 10; step++)
                        { SetMockHeadPose(Vector3.Lerp(from, to, step / 10f), Quaternion.identity); yield return null; }
                        yield return new WaitForEndOfFrame();
                    }
                    IEnumerator test = TestNativeCombat(true);
                    try { while (test.MoveNext()) yield return test.Current; }
                    finally { (test as IDisposable)?.Dispose(); }
                    Logger.LogInfo("COMBAT TRANSITION PASS: native role/teleport cycle " + (iteration + 1));
                }
            }
            finally
            {
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                setRole.Invoke(mode, new object[] { originalRole });
                teleport.Invoke(controller, new object[] { originalPosition, true }); VRManager.Instance.Rig.Recenter();
            }
        }

        private IEnumerator TestNativeCombat(bool inputOnly = false)
        {
            var manager = VRManager.Instance;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var attack = AccessTools.Method(adapter, "Attack");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            Type weaponType = Find("RaycastWeapon");
            float equipmentDeadline = Time.realtimeSinceStartup + 5;
            do
            {
                combatWeapon = Mirror.NetworkClient.localPlayer.GetComponents(weaponType).Cast<Component>()
                    .FirstOrDefault(w => w is Behaviour enabled && enabled.enabled && Member(w, "IsUnarmedWeapon") is bool unarmed && unarmed
                        && Member(w, "HasUsableWeapon") is bool usable && usable);
                if (combatWeapon != null) break;
                yield return null;
            } while (Time.realtimeSinceStartup < equipmentDeadline);
            Require(combatWeapon != null, "No native empty-hand weapon available");
            var nextFire = AccessTools.Method(weaponType, "GetCurrentNextFireTime");
            var pointerTick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            var pointerAttacking = AccessTools.Field(manager.Pointer.GetType(), "attacking");
            var rayMethod = AccessTools.Method(weaponType, "FireRayFromRay");
            var probe = new Harmony("com.swyf.vr.combatprobe");
            probe.Patch(rayMethod, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveCombatRay)));
            probe.Patch(AccessTools.Method(weaponType, "FireRay"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AimCombatTarget)));
            probe.Patch(AccessTools.Method(weaponType, "CmdRagdollTarget"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveDamageCommand)));
            combatRays = 0; combatOriginError = combatDirectionError = 0;
            bool paused = false;
            void Input(string key, bool value) => manager.Input.GetType().GetProperty(key).SetValue(manager.Input, value);
            object fixtureController = Instance("Metater.MetaPlayerController");
            var fixtureTeleport = AccessTools.Method(fixtureController.GetType(), "Teleport");
            var fixtureBody = Member(fixtureController, "CharacterController") as CharacterController;
            Vector3 fixtureOriginalPosition = Mirror.NetworkClient.localPlayer.transform.position;
            GameObject fixtureFloor = null;
            try
            {
                manager.Pointer.Cancel();
                if (inputOnly && manager.Rig.HeadObstructed)
                {
                    Transform blockedAim = manager.Rig.Right.Aim;
                    Vector3 before = blockedAim.position; Quaternion rotation = blockedAim.rotation;
                    try
                    {
                        blockedAim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.up * 1000,
                            Quaternion.LookRotation(Vector3.up, Vector3.forward));
                        Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false); Input("Grip", true);
                        pointerTick.Invoke(manager.Pointer, null);
                        Require(Member(combatWeapon, "isAttackHeld") is bool headBlockedHold && !headBlockedHold
                            && !(bool)pointerAttacking.GetValue(manager.Pointer) && combatRays == 0,
                            "A head-obstructed controller attack reached the native weapon");
                        Logger.LogInfo("COMBAT BLOCK PASS: actual head obstruction rejects controller attack; eye=" + manager.Rig.Head.transform.position
                            + "; overlaps=" + string.Join(",", Physics.OverlapSphere(manager.Rig.Head.transform.position, .11f,
                                Plugin.CollisionMask.Value, QueryTriggerInteraction.Ignore)
                                .Where(c => !c.transform.IsChildOf(Mirror.NetworkClient.localPlayer.transform))
                                .Select(c => c.name + "/" + c.transform.parent?.name)));
                    }
                    finally { blockedAim.SetPositionAndRotation(before, rotation); }
                    yield break;
                }
                if (!inputOnly)
                {
                    Require(fixtureBody != null && fixtureBody.enabled, "Combat input fixture requires the native collision body");
                    fixtureFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    fixtureFloor.name = "SWYF isolated combat input floor";
                    fixtureFloor.transform.position = new Vector3(1050, 50 + fixtureBody.bounds.min.y - fixtureOriginalPosition.y - .12f, 1050);
                    fixtureFloor.transform.localScale = new Vector3(20, .2f, 20);
                    Physics.SyncTransforms();
                    fixtureTeleport.Invoke(fixtureController, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                    yield return new WaitForSecondsRealtime(.8f);
                    Require(!manager.Rig.HeadObstructed, "Positive combat input fixture did not obtain a clear head position");
                }
                float deadline = Time.realtimeSinceStartup + 10;
                while (Mirror.NetworkTime.time < Convert.ToDouble(nextFire.Invoke(combatWeapon, null)))
                { Require(Time.realtimeSinceStartup < deadline, "Native punch cooldown never expired"); yield return null; }
                attack.Invoke(null, new object[] { true });
                deadline = Time.realtimeSinceStartup + 10;
                while (combatRays == 0)
                { Require(Time.realtimeSinceStartup < deadline, "Native punch input never fired"); yield return null; }
                attack.Invoke(null, new object[] { false });
                Require(combatOriginError < .001f && combatDirectionError < .01f, "Native punch ray did not use controller aim");
                int firstCount = combatRays;
                double cooldown = Convert.ToDouble(nextFire.Invoke(combatWeapon, null));
                Require(cooldown > Mirror.NetworkTime.time, "Native punch did not schedule its cooldown");
                attack.Invoke(null, new object[] { true }); attack.Invoke(null, new object[] { false });
                Require(combatRays == firstCount, "Repeated VR attack bypassed the native cooldown");
                Logger.LogInfo("COMBAT PASS: native unarmed attack fires from controller ray and rejects repeat input during its own cooldown");

                Transform aim = manager.Rig.Right.Aim;
                Vector3 savedAim = aim.position; Quaternion savedAimRotation = aim.rotation;
                try
                {
                    aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.up * 1000, Quaternion.LookRotation(Vector3.up, Vector3.forward));
                    Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false); Input("Grip", true);
                    pointerTick.Invoke(manager.Pointer, null);
                    if (inputOnly)
                        Logger.LogInfo("COMBAT INPUT STATE: eye=" + manager.Rig.Head.transform.position + "; authored=" + manager.Rig.AuthoredEye
                            + "; root=" + Mirror.NetworkClient.localPlayer.transform.position + "; blocked=" + manager.Rig.HeadObstructed
                            + "; overlaps=" + string.Join(",", Physics.OverlapSphere(manager.Rig.Head.transform.position, .11f,
                                Plugin.CollisionMask.Value, QueryTriggerInteraction.Ignore)
                                .Select(c => c.name + "/" + c.transform.parent?.name + "/own=" + c.transform.IsChildOf(Mirror.NetworkClient.localPlayer.transform))));
                    Require(Member(combatWeapon, "isAttackHeld") is bool held && held, "Controller combo did not start native attack hold; interactive=" + manager.Interactive
                        + "; headBlocked=" + manager.Rig.HeadObstructed + "; rightTracked=" + manager.Rig.Right.IsTracked
                        + "; panel=" + manager.Pointer.HasPanelHit + "; pointerAttack=" + pointerAttacking.GetValue(manager.Pointer)
                        + "; leftGrip=" + manager.Input.LeftGrip + "; settings=" + AccessTools.Property(adapter, "SettingsOpen").GetValue(null));
                    Input("TriggerDown", false); Input("Grip", false); pointerTick.Invoke(manager.Pointer, null);
                }
                finally { aim.SetPositionAndRotation(savedAim, savedAimRotation); }
                Require(Member(combatWeapon, "isAttackHeld") is bool released && !released
                    && !(bool)pointerAttacking.GetValue(manager.Pointer), "Releasing grip left native attack held while trigger remained down");
                toggle.Invoke(null, null); paused = true;
                attack.Invoke(null, new object[] { true });
                Require(Member(combatWeapon, "isAttackHeld") is bool blocked && !blocked, "Settings accepted a VR attack hold");
                attack.Invoke(null, new object[] { false });
                Logger.LogInfo("COMBAT PASS: grip release cancels native attack while trigger stays held; settings reject attack input");
                toggle.Invoke(null, null); paused = false;

                if (inputOnly) yield break;

                Type npcType = Find("NpcRagdollManager");
                Component npc = FindObjectsByType(npcType, FindObjectsSortMode.None).Cast<Component>().FirstOrDefault(candidate =>
                    candidate.gameObject.activeInHierarchy && !(Member(candidate, "IsRagdolled") is bool down && down)
                    && Member(candidate, "Health") is Component health && Convert.ToSingle(Member(health, "CurrentHealth")) > 80
                    && !(Member(health, "IsDamageBlocked") is bool blockedHealth && blockedHealth)
                    && candidate.GetComponent(Find("MainMenuSecurityNpc")) == null
                    && candidate.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger));
                Require(npc != null, "No healthy native NPC available for nonlethal punch integration");
                Component npcHealth = Member(npc, "Health") as Component;
                float healthBefore = Convert.ToSingle(Member(npcHealth, "CurrentHealth"));
                object controller = Instance("Metater.MetaPlayerController");
                var teleport = AccessTools.Method(controller.GetType(), "Teleport");
                Transform localBody = Mirror.NetworkClient.localPlayer.transform;
                Vector3 originalPosition = localBody.position;
                try
                {
                    combatTargetCollider = npc.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger
                        && ((1 << c.gameObject.layer) & ((LayerMask)Member(combatWeapon, "layerMask")).value) != 0);
                    combatVictim = npc.GetComponent<Mirror.NetworkIdentity>();
                    Require(combatVictim != null && combatVictim.netId != 0, "Punch victim is not natively network spawned");
                    combatImpulseError = 0; combatDamageCommands = 0;
                    teleport.Invoke(controller, new object[] { npc.transform.position - Vector3.right * 1.5f, true });
                    manager.Rig.Recenter();
                    yield return new WaitForSecondsRealtime(.8f);
                    deadline = Time.realtimeSinceStartup + 10;
                    while (Mirror.NetworkTime.time < Convert.ToDouble(nextFire.Invoke(combatWeapon, null)))
                    { Require(Time.realtimeSinceStartup < deadline, "Punch cooldown did not expire before target test"); yield return null; }
                    attack.Invoke(null, new object[] { true });
                    deadline = Time.realtimeSinceStartup + 8;
                    while (Convert.ToSingle(Member(npcHealth, "CurrentHealth")) >= healthBefore)
                    { Require(Time.realtimeSinceStartup < deadline, "Controller punch did not damage native NPC through the server command"); yield return null; }
                    attack.Invoke(null, new object[] { false });
                    float after = Convert.ToSingle(Member(npcHealth, "CurrentHealth"));
                    Require(after > 0 && combatDamageCommands > 0 && combatImpulseError < .01f,
                        "NPC damage bypassed the native controller-directed punch command: direction error=" + combatImpulseError);
                    Logger.LogInfo("COMBAT PASS: controller punch reaches native NPC through authority command, reduces health " + healthBefore + " -> " + after
                        + " and uses controller knockback direction; PvP/knockout not covered");
                }
                finally
                {
                    combatTargetCollider = null; combatVictim = null;
                    AccessTools.Method(npcHealth.GetType(), "ServerSetHealth").Invoke(npcHealth, new object[] { healthBefore });
                    teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter();
                }
            }
            finally
            {
                Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false); Input("Grip", false);
                attack.Invoke(null, new object[] { false }); manager.Pointer.Cancel();
                if (paused) toggle.Invoke(null, null);
                probe.UnpatchSelf(); combatWeapon = null;
                if (fixtureFloor != null)
                {
                    fixtureTeleport.Invoke(fixtureController, new object[] { fixtureOriginalPosition, true }); manager.Rig.Recenter();
                    Destroy(fixtureFloor);
                }
            }
        }
    }
}
