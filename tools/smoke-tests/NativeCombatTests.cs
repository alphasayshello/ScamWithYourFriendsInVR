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
        private static void ObserveCombatRay(object __instance, Ray __0)
        {
            if (!ReferenceEquals(__instance, combatWeapon)) return;
            var aim = VRManager.Instance.Rig.Right.Aim;
            combatRays++;
            combatOriginError = Mathf.Max(combatOriginError, Vector3.Distance(__0.origin, aim.position));
            combatDirectionError = Mathf.Max(combatDirectionError, Vector3.Angle(__0.direction, aim.forward));
        }

        private IEnumerator TestNativeCombat()
        {
            var manager = VRManager.Instance;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var attack = AccessTools.Method(adapter, "Attack");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            Type weaponType = Find("RaycastWeapon");
            combatWeapon = Mirror.NetworkClient.localPlayer.GetComponents(weaponType).Cast<Component>()
                .FirstOrDefault(w => w is Behaviour enabled && enabled.enabled && Member(w, "IsUnarmedWeapon") is bool unarmed && unarmed
                    && Member(w, "HasUsableWeapon") is bool usable && usable);
            Require(combatWeapon != null, "No native empty-hand weapon available");
            var nextFire = AccessTools.Method(weaponType, "GetCurrentNextFireTime");
            var pointerTick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            var pointerAttacking = AccessTools.Field(manager.Pointer.GetType(), "attacking");
            var rayMethod = AccessTools.Method(weaponType, "FireRayFromRay");
            var probe = new Harmony("com.swyf.vr.combatprobe");
            probe.Patch(rayMethod, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveCombatRay)));
            combatRays = 0; combatOriginError = combatDirectionError = 0;
            bool paused = false;
            void Input(string key, bool value) => manager.Input.GetType().GetProperty(key).SetValue(manager.Input, value);
            try
            {
                manager.Pointer.Cancel();
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

                attack.Invoke(null, new object[] { true });
                Require(Member(combatWeapon, "isAttackHeld") is bool held && held, "Native attack hold missing before grip release");
                pointerAttacking.SetValue(manager.Pointer, true);
                Input("Trigger", true); Input("TriggerDown", false); Input("TriggerUp", false); Input("Grip", false);
                pointerTick.Invoke(manager.Pointer, null);
                Require(Member(combatWeapon, "isAttackHeld") is bool released && !released
                    && !(bool)pointerAttacking.GetValue(manager.Pointer), "Releasing grip left native attack held while trigger remained down");
                toggle.Invoke(null, null); paused = true;
                attack.Invoke(null, new object[] { true });
                Require(Member(combatWeapon, "isAttackHeld") is bool blocked && !blocked, "Settings accepted a VR attack hold");
                Logger.LogInfo("COMBAT PASS: grip release cancels native attack while trigger stays held; settings reject attack input");
            }
            finally
            {
                Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false); Input("Grip", false);
                attack.Invoke(null, new object[] { false }); manager.Pointer.Cancel();
                if (paused) toggle.Invoke(null, null);
                probe.UnpatchSelf(); combatWeapon = null;
            }
        }
    }
}
