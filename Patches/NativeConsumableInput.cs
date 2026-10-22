using HarmonyLib;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Patches
{
    internal static class NativeConsumableInput
    {
        private sealed class Scope
        {
            internal Scope Parent;
            internal Component Player, PendingPassout;
        }
        [System.ThreadStatic] private static Scope current;

        internal static void Install(Harmony harmony)
        {
            current = null;
            var useType = ReflectionTypes.Find("SpeedBoostOnLeftClick");
            var playerType = ReflectionTypes.Find("Metater.MetaPlayer");
            var use = useType == null ? null : AccessTools.Method(useType, "OnLeftClickChanged", new[] { typeof(bool) });
            var passout = playerType == null ? null : AccessTools.Method(playerType, "TriggerAlcoholPassOut", System.Type.EmptyTypes);
            if (use == null || passout == null)
            {
                Plugin.Log.LogWarning("Native consumable ordering hooks unavailable; alcohol pass-out consumption is unverified.");
                return;
            }
            try
            {
                harmony.Patch(use, prefix: new HarmonyMethod(typeof(NativeConsumableInput), nameof(BeforeUse)),
                    finalizer: new HarmonyMethod(typeof(NativeConsumableInput), nameof(AfterUse)));
                harmony.Patch(passout, prefix: new HarmonyMethod(typeof(NativeConsumableInput), nameof(BeforePassout)));
            }
            catch (System.Exception ex) { GameAdapter.Report("Native consumable ordering", ex); }
        }

        internal static void Clear() => current = null;

        private static void BeforeUse(object __instance, bool __0, out Scope __state)
        {
            __state = null;
            if (!__0 || !NativeItemInput.IsPrimaryDispatch || VRManager.Instance?.Ready != true) return;
            Component player = GameAdapter.LocalPlayer;
            object held = GameAdapter.Read(__instance, "heldItem");
            if (player == null || !ReferenceEquals(GameAdapter.Read(held, "Holder"), GameAdapter.Interactor)) return;
            __state = new Scope { Parent = current, Player = player };
            current = __state;
        }

        private static bool BeforePassout(object __instance)
        {
            if (current == null || !ReferenceEquals(current.Player, __instance)) return true;
            current.PendingPassout = current.Player;
            return false;
        }

        private static void AfterUse(Scope __state)
        {
            if (__state == null) return;
            current = __state.Parent;
            Component player = __state.PendingPassout;
            __state.PendingPassout = null;
            // Native use must queue its ownership-checked consume command
            // before ragdoll can drop the last drink. Keep the native pass-out
            // routine and its recovery/effects intact after that command.
            if (player != null && ReferenceEquals(player, GameAdapter.LocalPlayer))
                GameAdapter.Call(player, "TriggerAlcoholPassOut");
        }
    }
}
