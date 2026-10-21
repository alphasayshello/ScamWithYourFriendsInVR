using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.Patches
{
    internal static class PlayerPatches
    {
        internal static void Install(Harmony harmony)
        {
            NativeItemInput.Install(harmony);
            Patch(harmony, "Metater.MetaPlayerController", "Update", nameof(BeforeController));
            Patch(harmony, "Metater.MetaPlayerController", "OnMove", nameof(BeforeMove));
            Patch(harmony, "Metater.MetaPlayerController", "ProcessLookInput", nameof(BeforeLook),
                new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
            Patch(harmony, "PlayerInteractor", "GetHoldPoint", null, null, nameof(AfterHoldPoint));
            Patch(harmony, "MaxFpsSetting", "Apply", null, new[] { typeof(int) }, nameof(AfterFrameLimit));
            Patch(harmony, "Metater.MetaPlayerController", "UpdateMeshRendererVisibility", null, new[] { typeof(bool) }, nameof(AfterMeshVisibility));
            Patch(harmony, "ScammerGameHudController", "RefreshPlayerMenuHint", null, null, nameof(AfterHudHints));
            Patch(harmony, "GameInputBindings", "Held", null, new[] { typeof(string), typeof(string) }, nameof(AfterHeldInput));
            Patch(harmony, "GameInputBindings", "Pressed", null, new[] { typeof(string), typeof(string) }, nameof(AfterPressedInput));
            Type weaponType = ReflectionTypes.Find("RaycastWeapon");
            if (weaponType != null)
                foreach (string method in new[] { "FireRay", "FireRayFromRay", "FireShotgun", "IsAttackLayerRaycastHitting", "SpawnProjectile" })
                {
                    MethodInfo weaponMethod = AccessTools.Method(weaponType, method);
                    if (weaponMethod != null)
                        try { harmony.Patch(weaponMethod, transpiler: new HarmonyMethod(typeof(PlayerPatches), nameof(WeaponRays))); }
                        catch (Exception ex) { GameAdapter.Report("Weapon ray " + method, ex); }
                }
            Type type = ReflectionTypes.Find("PlayerInteractor");
            MethodInfo ray = type == null ? null : AccessTools.Method(type, "HandleInteractionRay", Type.EmptyTypes);
            if (ray != null)
            {
                try { harmony.Patch(ray, transpiler: new HarmonyMethod(typeof(PlayerPatches), nameof(InteractionRay))); }
                catch (Exception ex) { GameAdapter.Report("HandleInteractionRay patch", ex); }
            }
        }

        internal static void Patch(Harmony harmony, string typeName, string methodName, string prefix,
            Type[] arguments = null, string postfix = null)
        {
            try
            {
                Type type = ReflectionTypes.Find(typeName);
                MethodInfo method = type == null ? null : AccessTools.Method(type, methodName, arguments);
                if (method == null) { Plugin.Log.LogWarning("Skipped missing game method: " + typeName + "." + methodName); return; }
                harmony.Patch(method,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(PlayerPatches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(PlayerPatches), postfix));
            }
            catch (Exception ex) { GameAdapter.Report(typeName + "." + methodName + " patch", ex); }
        }

        private static bool Local(object instance) => ReferenceEquals(instance, GameAdapter.Controller);
        private static bool Active => VRManager.Instance != null && VRManager.Instance.Ready;

        private static void AfterFrameLimit() => VRPerformance.RememberFlatFramePacing();
        private static bool GameplayInput => Active && VRManager.Instance.Interactive && GameAdapter.Allowed(GameAdapter.Interactor)
            && !GameAdapter.InMainMenu && !GameAdapter.MonitorCaptured && !GameAdapter.SettingsOpen
            && !(GameAdapter.Read(GameAdapter.Static("TabMenu", "Instance"), "IsOpen") is bool open && open);

        private static void AfterHeldInput(string __0, string __1, ref bool __result)
        {
            if (__0 == "Shortcuts" && __1 == "HoldP" && GameplayInput) __result |= VRManager.Instance.Input.Pee;
        }
        private static void AfterPressedInput(string __0, string __1, ref bool __result)
        {
            if (__0 == "Shortcuts" && __1 == "ManualFart" && GameplayInput && VRManager.Instance.Input.FartDown && !GameAdapter.HasConeBoost) __result = true;
        }

        private static void AfterHudHints(object __instance)
        {
            if (!Active) return;
            if (GameAdapter.Read(__instance, "playerMenuHint") is UnityEngine.UIElements.VisualElement hints)
            {
                var players = hints.Q<UnityEngine.UIElements.Label>("player-menu-hint-label")?.parent;
                var settings = hints.Q<UnityEngine.UIElements.Label>("settings-menu-hint-label")?.parent;
                var playersKey = players?.Q<UnityEngine.UIElements.Label>(className: "player-menu-hint__key");
                var settingsKey = settings?.Q<UnityEngine.UIElements.Label>(className: "player-menu-hint__key");
                if (playersKey != null) playersKey.text = VRManager.Instance.Input.PlayersHint;
                if (settingsKey != null) settingsKey.text = VRManager.Instance.Input.SettingsHint;
            }
        }

        private static void AfterMeshVisibility(object __instance)
        {
            if (!Active || !Local(__instance) || GameAdapter.InMainMenu) return;
            if (GameAdapter.Read(GameAdapter.LocalPlayer, "isSpectating") is bool spectating && spectating) return;
            if (GameAdapter.Read(__instance, "meshRendererToggles") is Array toggles)
                foreach (object toggle in toggles) GameAdapter.Call(toggle, "ShowBodyWithoutHead");
        }

        private static void BeforeController(object __instance)
        {
            if (!Active || !Local(__instance)) return;
            VRManager manager = VRManager.Instance;
            Vector2 move = Vector2.zero;
            Transform polar = GameAdapter.Polar;
            if (manager.Interactive && manager.Rig.Head != null && polar != null
                && !GameAdapter.MonitorCaptured && !manager.Keyboard.Visible && !manager.Pointer.HasPanelHit && GameplayInput)
            {
                Vector2 stick = manager.Input.Move;
                Quaternion relative = Quaternion.Euler(0, manager.Rig.Head.transform.eulerAngles.y - polar.eulerAngles.y, 0);
                Vector3 local = relative * new Vector3(stick.x, 0, stick.y);
                move = new Vector2(local.x, local.z);
                object blocker = GameAdapter.Read(__instance, "blocker");
                if (GameAdapter.Read(blocker, "CanTakeInput") is bool take && take
                    && GameAdapter.Read(blocker, "CanStrafeCamera") is bool turn && turn)
                {
                    float snap = manager.Input.ConsumeSnap();
                    if (snap != 0) polar.Rotate(Vector3.up, snap, Space.World);
                }
            }
            GameAdapter.Write(__instance, "movementVector", move);
            if (GameplayInput && !manager.Keyboard.Visible && manager.Input.FartDown) GameAdapter.FartBoost();
            bool jump = GameplayInput && !manager.Keyboard.Visible && !manager.Pointer.HasPanelHit && manager.Input.Jump;
            if (GameAdapter.Read(__instance, "isJumping") is bool previous && previous != jump)
            {
                GameAdapter.Write(__instance, "isJumping", jump);
                (GameAdapter.Read(__instance, "OnJumpInputChanged") as Action<bool>)?.Invoke(jump);
            }
            if (!GameplayInput && GameAdapter.Read(GameAdapter.LocalPlayer, "wasPPressed") is bool peeing && peeing)
            {
                GameAdapter.Write(GameAdapter.LocalPlayer, "wasPPressed", false);
                (GameAdapter.Read(GameAdapter.LocalPlayer, "OnPInputChanged") as Action<bool>)?.Invoke(false);
            }
        }

        private static bool BeforeMove(object __instance) => !Active || !Local(__instance);

        private static bool BeforeLook(object __instance, ref Vector2 __result)
        {
            if (!Active || !Local(__instance)) return true;
            __result = Vector2.zero;
            return false;
        }

        private static void AfterHoldPoint(object __instance, object __0, ref Transform __result)
        {
            VRManager manager = VRManager.Instance;
            if (!Active || !manager.Interactive || !manager.Rig.Right.IsTracked
                || !ReferenceEquals(__instance, GameAdapter.Interactor)) return;
            if (GameAdapter.Read(__0, "UsesNormalHoldPoint") is bool normal && normal)
                __result = manager.Rig.Right.transform;
        }

        private static IEnumerable<CodeInstruction> InteractionRay(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo position = AccessTools.PropertyGetter(typeof(Transform), "position");
            MethodInfo forward = AccessTools.PropertyGetter(typeof(Transform), "forward");
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(position)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(PlayerPatches), nameof(RayPosition)); count++; }
                else if (instruction.Calls(forward)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(PlayerPatches), nameof(RayForward)); count++; }
                yield return instruction;
            }
            Plugin.Log.LogInfo("Interaction ray adapted " + count + " transform reads; wrappers only redirect the local player camera.");
        }

        private static bool Redirect(Transform value)
        {
            VRManager manager = VRManager.Instance;
            if (manager == null || !manager.Interactive || manager.Rig.Head == null || !manager.Rig.Right.IsTracked) return false;
            UnityEngine.Camera playerCamera = GameAdapter.Read(GameAdapter.Interactor, "playerCamera") as UnityEngine.Camera;
            return playerCamera != null && value == playerCamera.transform;
        }
        private static Vector3 RayPosition(Transform value) => Redirect(value) ? VRManager.Instance.Rig.Right.Aim.position : value.position;
        private static Vector3 RayForward(Transform value) => Redirect(value) ? VRManager.Instance.Rig.Right.Aim.forward : value.forward;
        private static Vector3 RayRight(Transform value) => Redirect(value) ? VRManager.Instance.Rig.Right.Aim.right : value.right;
        private static Vector3 RayUp(Transform value) => Redirect(value) ? VRManager.Instance.Rig.Right.Aim.up : value.up;

        private static IEnumerable<CodeInstruction> WeaponRays(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(UnityEngine.Camera), "ScreenPointToRay", new[] { typeof(Vector3) });
            MethodInfo forward = AccessTools.PropertyGetter(typeof(Transform), "forward");
            MethodInfo right = AccessTools.PropertyGetter(typeof(Transform), "right");
            MethodInfo up = AccessTools.PropertyGetter(typeof(Transform), "up");
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(PlayerPatches), nameof(WeaponRay)); }
                else if (instruction.Calls(forward)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(PlayerPatches), nameof(RayForward)); }
                else if (instruction.Calls(right)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(PlayerPatches), nameof(RayRight)); }
                else if (instruction.Calls(up)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(PlayerPatches), nameof(RayUp)); }
                yield return instruction;
            }
        }
        private static Ray WeaponRay(UnityEngine.Camera camera, Vector3 screen)
            => Redirect(camera.transform) ? new Ray(VRManager.Instance.Rig.Right.Aim.position, VRManager.Instance.Rig.Right.Aim.forward) : camera.ScreenPointToRay(screen);
    }
}
