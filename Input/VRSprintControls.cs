using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRSprintControls
    {
        private static Component controller;
        private static bool running;
        internal static void Update(Component current, bool allowed)
        {
            if (controller != current) Clear();
            if (current == null || !Plugin.AutoSprint.Value) { Clear(); return; }
            controller = current;
            bool canSprint = GameAdapter.Read(GameAdapter.Read(current, "blocker"), "CanSprint") is bool can && can;
            float travel = VRManager.Instance.Input.Move.sqrMagnitude;
            float threshold = Plugin.SprintThreshold.Value;
            float boundary = running ? threshold - .15f : threshold;
            running = allowed && canSprint && travel >= boundary * boundary;
            SetNative(running);
        }
        private static void SetNative(bool value)
        {
            if (controller == null) return;
            bool state = GameAdapter.Read(controller, "isSprinting") is bool sprinting && sprinting;
            bool toggle = GameAdapter.Read(GameAdapter.Static("SettingsUi", "Instance"), "ToggleSprint") is bool enabled && enabled;
            bool latched = GameAdapter.Read(controller, "toggleSprintActive") is bool active && active;
            // Drive the game's real callback. In toggle mode a complete press
            // changes its latch; release alone would leave it sprinting.
            if (toggle)
            {
                if (latched != value)
                {
                    NativeItemInput.SendNamed(controller, "OnSprint", true);
                    NativeItemInput.SendNamed(controller, "OnSprint", false);
                }
                else if (state != value) NativeItemInput.SendNamed(controller, "OnSprint", false);
            }
            else if (state != value || latched) NativeItemInput.SendNamed(controller, "OnSprint", value);
        }
        internal static void Clear()
        {
            SetNative(false); controller = null; running = false;
        }
    }
}
