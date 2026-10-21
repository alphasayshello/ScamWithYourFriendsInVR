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
            Input.VRFlightControls.Initialize();
            Input.VRLadderControls.Initialize();
            Patch(harmony, "LadderInteractable", "DeltaClimber", null, null, nameof(AfterClimberChanged));
            Patch(harmony, "LadderInteractable", "OnDisable", nameof(BeforeLadderDisabled));
            Patch(harmony, "FpvInteractable", "OnFlyerChanged", null, null, nameof(AfterFlyerChanged));
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
            if (type != null)
                foreach (string name in new[] { "GetPunchAimDirection", "TryGetLocalPunchAimTarget" })
                {
                    MethodInfo punch = AccessTools.Method(type, name);
                    if (punch == null) continue; // Older native versions may lack the arm-target helper.
                    try { harmony.Patch(punch, transpiler: new HarmonyMethod(typeof(PlayerPatches), nameof(PunchAim))); }
                    catch (Exception ex) { GameAdapter.Report("Punch aim " + name, ex); }
                }
            Type drawing = ReflectionTypes.Find("JwPrintedDrawing");
            if (drawing != null)
                foreach (string name in new[] { "OnPlacePressed", "OnThrowPressed" })
                {
                    MethodInfo method = AccessTools.Method(drawing, name);
                    if (method == null) continue;
                    try { harmony.Patch(method, transpiler: new HarmonyMethod(typeof(PlayerPatches), nameof(HeldTargetRay))); }
                    catch (Exception ex) { GameAdapter.Report("Printed drawing aim " + name, ex); }
                }
            Type javelin = ReflectionTypes.Find("JavelinInteractable");
            MethodInfo targetRay = javelin == null ? null : AccessTools.Method(javelin, "TryRaycastTarget");
            if (targetRay != null)
            {
                try { harmony.Patch(targetRay, transpiler: new HarmonyMethod(typeof(PlayerPatches), nameof(HeldTargetRay))); }
                catch (Exception ex) { GameAdapter.Report("Javelin targeting patch", ex); }
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
        private static void BeforeLadderDisabled(Component __instance)
        { if (Active) Input.VRLadderControls.Disabled(__instance); }

        private static void AfterClimberChanged(Component __instance, object __1)
            => Input.VRLadderControls.ClimberChanged(__instance, __1);

        private static void AfterFlyerChanged(Component __instance, object __1)
        {
            Input.VRFlightControls.FlyerChanged(__instance, __1);
            // The native ownership callback refreshes visibility before this
            // postfix knows the new view context. Reapply its full-body state.
            if (GameAdapter.Read(__1, "isLocalPlayer") is bool local && local)
                GameAdapter.Call(GameAdapter.Controller, "UpdateMeshRendererVisibility", false);
        }
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
            GameAdapter.RememberHud(__instance);
            if (GameAdapter.Read(__instance, "playerMenuHint") is UnityEngine.UIElements.VisualElement hints)
            {
                UI.VRHudHints.Apply(hints, VRManager.Instance.Input);
                UI.VRHudHints.ApplyBag(hints, VRManager.Instance.Input,
                    GameAdapter.Read(GameAdapter.Read(GameAdapter.LocalPlayer, "RagdollManager"), "IsHiding") is bool hiding && hiding);
                Component flight = Input.VRFlightControls.Current;
                UI.VRHudHints.ApplyFlight(hints, VRManager.Instance.Input, flight != null
                    && !(GameAdapter.Read(flight, "Networkexploded") is bool exploded && exploded));
                UI.VRHudHints.ApplyFlightRelease(hints, VRManager.Instance.Input, Input.VRFlightControls.WaitingForBodyRelease);
                UI.VRHudHints.ApplyLadder(hints, VRManager.Instance.Input, Input.VRLadderControls.Current != null, Input.VRLadderControls.WaitingForBodyRelease);
            }
        }

        private static void AfterMeshVisibility(object __instance)
        {
            if (!Active || !Local(__instance) || GameAdapter.InMainMenu) return;
            if (Input.VRFlightControls.Current != null) return;
            if (GameAdapter.Read(GameAdapter.LocalPlayer, "isSpectating") is bool spectating && spectating) return;
            if (GameAdapter.Read(__instance, "meshRendererToggles") is Array toggles)
                foreach (object toggle in toggles) GameAdapter.Call(toggle, "ShowBodyWithoutHead");
        }

        private static void BeforeController(object __instance)
        {
            if (!Active || !Local(__instance)) return;
            VRManager manager = VRManager.Instance;
            Component bag = GameAdapter.Read(GameAdapter.Read(GameAdapter.LocalPlayer, "RagdollManager"), "NetworkisHiding") as Component;
            bool specialInput = manager.Interactive && !GameAdapter.InMainMenu && !GameAdapter.MonitorCaptured
                && !GameAdapter.SettingsOpen && !GameAdapter.ModalOverlayOpen && !manager.Keyboard.Visible && !manager.Pointer.HasPanelHit
                && !(GameAdapter.Read(GameAdapter.Static("TabMenu", "Instance"), "IsOpen") is bool playersOpen && playersOpen);
            Input.VRBagControls.Update(__instance as Component, bag, bag != null && specialInput);
            Input.VRFlightControls.Update(__instance as Component, Input.VRFlightControls.Current, specialInput);
            Component ladder = Input.VRLadderControls.Current;
            Input.VRLadderControls.Update(__instance as Component, ladder, specialInput);
            bool releaseBody = Input.VRFlightControls.SuppressBodyInput(manager.Input);
            releaseBody |= Input.VRLadderControls.SuppressBodyInput(manager.Input);
            Vector2 move = Vector2.zero;
            Transform polar = GameAdapter.Polar;
            if (manager.Interactive && manager.Rig.Head != null && polar != null
                && !releaseBody && ladder == null && !GameAdapter.MonitorCaptured && !manager.Keyboard.Visible && !manager.Pointer.HasPanelHit && GameplayInput)
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
            bool jump = GameplayInput && !releaseBody && !manager.Keyboard.Visible && !manager.Pointer.HasPanelHit && manager.Input.Jump && (ladder == null || manager.Input.JumpDown);
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

        private static Vector3 PunchForward(Transform value, object owner)
            => ReferenceEquals(owner, GameAdapter.Interactor) ? RayForward(value) : value.forward;
        private static Ray PunchViewportRay(UnityEngine.Camera camera, Vector3 viewport, object owner)
            => ReferenceEquals(owner, GameAdapter.Interactor) && Redirect(camera.transform)
                ? new Ray(VRManager.Instance.Rig.Right.Aim.position, VRManager.Instance.Rig.Right.Aim.forward)
                : camera.ViewportPointToRay(viewport);

        private static IEnumerable<CodeInstruction> PunchAim(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo forward = AccessTools.PropertyGetter(typeof(Transform), "forward");
            MethodInfo viewport = AccessTools.Method(typeof(UnityEngine.Camera), "ViewportPointToRay", new[] { typeof(Vector3) });
            bool target = __originalMethod.Name == "TryGetLocalPunchAimTarget";
            int reads = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(target ? viewport : forward))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(PlayerPatches), target ? nameof(PunchViewportRay) : nameof(PunchForward));
                    reads++;
                }
                yield return instruction;
            }
            if (target ? reads != 1 : reads == 0)
                throw new InvalidOperationException("Native punch camera reads changed: " + __originalMethod.Name);
        }

        private static bool RedirectHeldTarget(Transform value, Component owner)
            => owner != null && Redirect(value) && GameAdapter.Read(GameAdapter.Interactor, "CurrentHeldItem") is Component held
                && held != null && held.gameObject == owner.gameObject;
        private static Vector3 HeldTargetPosition(Transform value, Component owner)
            => RedirectHeldTarget(value, owner) ? VRManager.Instance.Rig.Right.Aim.position : value.position;
        private static Vector3 HeldTargetForward(Transform value, Component owner)
            => RedirectHeldTarget(value, owner) ? VRManager.Instance.Rig.Right.Aim.forward : value.forward;

        private static IEnumerable<CodeInstruction> HeldTargetRay(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo position = AccessTools.PropertyGetter(typeof(Transform), "position");
            MethodInfo forward = AccessTools.PropertyGetter(typeof(Transform), "forward");
            int positions = 0, directions = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                string replacement = null;
                if (instruction.Calls(position)) { replacement = nameof(HeldTargetPosition); positions++; }
                else if (instruction.Calls(forward)) { replacement = nameof(HeldTargetForward); directions++; }
                if (replacement != null)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(PlayerPatches), replacement);
                }
                yield return instruction;
            }
            if (positions != (__originalMethod.Name == "OnThrowPressed" ? 0 : 1) || directions != 1)
                throw new InvalidOperationException("Held item camera reads changed: " + __originalMethod.Name);
        }

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
