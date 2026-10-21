using System;
using UnityEngine;
using UnityEngine.XR;

namespace ScamWithYourFriendsInVR.Input
{
    internal struct VRControllerIdentity
    {
        private InputDevice previous;
        private bool known;
        private Plugin.ControllerLayoutKind detected;

        internal Plugin.ControllerLayoutKind Read(InputDevice device)
        {
            if (Plugin.ControllerLayout.Value != Plugin.ControllerLayoutKind.Auto) return Plugin.ControllerLayout.Value;
            if (!device.isValid) return Plugin.ControllerLayoutKind.FaceButtons;
            if (!known || !device.Equals(previous))
            {
                previous = device; known = true; detected = Identify(device.name);
            }
            return detected;
        }

        internal static Plugin.ControllerLayoutKind Identify(string name)
        {
            // Match the bundled profiles rather than treating a failed feature
            // read as proof that a controller has no face buttons.
            if (string.Equals(name, "HTC Vive Controller OpenXR", StringComparison.OrdinalIgnoreCase)) return Plugin.ControllerLayoutKind.ViveWand;
            if (string.Equals(name, "Windows MR Controller OpenXR", StringComparison.OrdinalIgnoreCase)) return Plugin.ControllerLayoutKind.Wmr;
            return Plugin.ControllerLayoutKind.FaceButtons;
        }
    }

    internal struct VRPadBindings
    {
        private Plugin.ControllerLayoutKind layout;
        private bool released;
        private int action;

        internal void Apply(ref VRRawHandState hand, bool left, bool active)
        {
            if (layout != hand.Layout) { layout = hand.Layout; released = false; action = 0; }
            if (layout != Plugin.ControllerLayoutKind.ViveWand && layout != Plugin.ControllerLayoutKind.Wmr)
            { released = false; action = 0; return; }

            bool vive = layout == Plugin.ControllerLayoutKind.ViveWand;
            bool hasClick = vive ? hand.HasStickClick : hand.HasPadClick;
            bool click = vive ? hand.StickClick : hand.PadClick;
            bool hasAxis = vive ? hand.HasStick : hand.HasPad;
            Vector2 axis = vive ? hand.Stick : hand.Pad;
            bool validAxis = hasAxis && !float.IsNaN(axis.x) && !float.IsNaN(axis.y) && !float.IsInfinity(axis.x) && !float.IsInfinity(axis.y);
            bool valid = active && hand.Connected && hand.Tracked && hasClick;
            if (!valid || click && !validAxis) { released = false; action = 0; }
            else if (!click) { released = true; action = 0; }
            else if (released && action == 0) action = left || axis.y >= 0 ? 1 : 2;

            hand.HasPrimary = hand.HasSecondary = valid;
            hand.Primary = action == 1;
            hand.Secondary = !left && action == 2;
            if (vive)
            {
                // Touch moves/scrolls/turns; pressing the pad owns its action
                // until release, without simultaneous locomotion or recenter.
                if (click || !valid) { hand.Stick = Vector2.zero; hand.HasStick = false; }
                hand.HasStickClick = false; hand.StickClick = false;
            }
        }
    }
}
