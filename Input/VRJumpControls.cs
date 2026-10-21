using System;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRJumpControls
    {
        private static Component controller;
        private static bool held;
        internal static bool Read(Component current, VRInputManager input, bool allowed)
        {
            if (controller != current) Clear();
            controller = current;
            if (!allowed || !input.Jump) held = false;
            else if (input.JumpDown) held = true;
            return held;
        }
        internal static void Clear()
        {
            if (controller != null && held && GameAdapter.Read(controller, "isJumping") is bool jumping && jumping)
            {
                GameAdapter.Write(controller, "isJumping", false);
                (GameAdapter.Read(controller, "OnJumpInputChanged") as Action<bool>)?.Invoke(false);
            }
            controller = null; held = false;
        }
    }
}
