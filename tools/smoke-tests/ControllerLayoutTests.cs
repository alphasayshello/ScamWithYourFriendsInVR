using System;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestControllerLayouts()
        {
            var assembly = typeof(Input.VRInputManager).Assembly;
            var identity = assembly.GetType("ScamWithYourFriendsInVR.Input.VRControllerIdentity", true);
            var identify = identity.GetMethod("Identify", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var pair in new[] {
                ("HTC Vive Controller OpenXR", Plugin.ControllerLayoutKind.ViveWand),
                ("windows mr controller openxr", Plugin.ControllerLayoutKind.Wmr),
                ("Vive Cosmos Controller OpenXR", Plugin.ControllerLayoutKind.FaceButtons),
                ("Oculus Touch Controller OpenXR", Plugin.ControllerLayoutKind.FaceButtons),
                ("Valve Index Controller OpenXR", Plugin.ControllerLayoutKind.FaceButtons),
                ("", Plugin.ControllerLayoutKind.FaceButtons) })
                Check((Plugin.ControllerLayoutKind)identify.Invoke(null, new object[] { pair.Item1 }) == pair.Item2,
                    "Controller identity selected the wrong layout: " + pair.Item1);

            Type handType = assembly.GetType("ScamWithYourFriendsInVR.Input.VRRawHandState", true);
            Type frameType = assembly.GetType("ScamWithYourFriendsInVR.Input.VRRawInputState", true);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            void Set(object target, string field, object value) => target.GetType().GetField(field, fields).SetValue(target, value);
            foreach (var layout in new[] { Plugin.ControllerLayoutKind.ViveWand, Plugin.ControllerLayoutKind.Wmr })
            {
                var input = Own(new GameObject("Controller layout " + layout)).AddComponent<Input.VRInputManager>();
                input.enabled = false;
                object Hand()
                {
                    object hand = Activator.CreateInstance(handType);
                    Set(hand, "Layout", layout); Set(hand, "Connected", true); Set(hand, "Tracked", true);
                    foreach (string feature in new[] { "HasStick", "HasStickClick", "HasPad", "HasPadClick", "HasMenu", "HasGripButton", "HasTriggerButton" }) Set(hand, feature, true);
                    return hand;
                }
                object left = Hand(), right = Hand(), frame = Activator.CreateInstance(frameType);
                Set(frame, "HeadTracked", true); Set(frame, "Focused", true);
                var apply = input.GetType().GetMethod("ApplyState", fields);
                void Step(float time)
                {
                    Set(frame, "Left", left); Set(frame, "Right", right); apply.Invoke(input, new[] { frame, (object)time });
                }
                void Pad(object hand, bool click, float y)
                {
                    Set(hand, layout == Plugin.ControllerLayoutKind.ViveWand ? "StickClick" : "PadClick", click);
                    Set(hand, layout == Plugin.ControllerLayoutKind.ViveWand ? "Stick" : "Pad", new Vector2(0, y));
                }
                Step(0);
                Check(input.PlayersHint == "LEFT PAD" && input.SettingsHint == "MENU", "Pad-controller HUD hints kept face buttons");
                Pad(right, true, .8f); Step(1);
                Check(input.JumpDown && !input.Secondary && input.Turn == Vector2.zero, "Upper right pad did not jump without turning: " + layout);
                Pad(right, true, -.8f); Step(1.1f);
                Check(input.Jump && !input.JumpDown && !input.Secondary, "Sliding held jump switched to secondary: " + layout);
                Pad(right, false, 0); Step(1.2f); Pad(right, true, -.8f); Step(1.3f);
                Check(input.SecondaryDown && input.Secondary && !input.Jump, "Lower right pad did not begin secondary: " + layout);
                Pad(right, true, .8f); Step(1.4f);
                Check(input.Secondary && !input.SecondaryDown && !input.Jump, "Sliding held secondary started jump: " + layout);
                Set(frame, "Focused", false); Step(2); Set(frame, "Focused", true); Step(3);
                Check(!input.Secondary && !input.Jump && !input.GiveUp, "Held pad action restarted after focus recovery: " + layout);
                Pad(right, false, 0); Step(3.1f); Pad(right, true, -.8f); Step(3.2f);
                Check(input.SecondaryDown, "Pad did not rearm after physical release: " + layout);
                Set(right, layout == Plugin.ControllerLayoutKind.ViveWand ? "HasStick" : "HasPad", false); Step(3.3f);
                Set(right, layout == Plugin.ControllerLayoutKind.ViveWand ? "HasStick" : "HasPad", true); Step(3.4f);
                Check(!input.Secondary && !input.Jump, "Unavailable pad coordinates rearmed held action: " + layout);
                Pad(right, false, 0); Step(3.5f); Pad(right, true, .8f); Step(3.6f);
                Set(right, "Tracked", false); Step(3.7f); Set(right, "Tracked", true); Step(3.8f);
                Check(!input.Jump && !input.GiveUp && !input.Secondary, "Held pad restarted after pose recovery: " + layout);
                Pad(right, false, 0); Step(4);
                Pad(left, true, -.8f); Step(4.1f);
                Check(input.PlayersDown && !input.FartDown && !input.RecenterDown, "Left pad did not open players: " + layout);
                Step(5); Check(!input.RecenterDown && !input.PlayersDown, "Held left pad repeated or recentered: " + layout);
                Pad(left, false, 0); Step(5.1f); Set(left, "GripButton", true); Pad(left, true, .8f); Step(5.2f);
                Check(input.FartDown && !input.PlayersDown, "Grip plus left pad did not fart: " + layout);
                Pad(left, false, 0); Pad(right, false, 0); Step(5.3f); Pad(right, true, .8f); Step(5.4f);
                Check(input.GiveUpDown && !input.Jump, "Grip plus upper right pad did not use give-up: " + layout);
                Pad(right, false, 0); Pad(left, false, 0); Set(left, "GripButton", false); Step(6);
                Set(left, "Menu", true); Set(right, "TriggerButton", true); Set(right, "GripButton", true); Step(6.1f);
                Check(input.MenuDown && input.TriggerDown && input.GripDown, "Pad layout swallowed native menu/trigger/grip: " + layout);
                Set(left, "Menu", false); Set(right, "TriggerButton", false); Set(right, "GripButton", false); Step(6.2f);
                Set(left, "Stick", new Vector2(0, .8f)); Set(right, "Stick", new Vector2(.8f, 0)); Step(7);
                Check(input.Move.y > 0 && input.Turn.x > 0, "Pad layout lost movement/snap axes: " + layout);
                if (layout == Plugin.ControllerLayoutKind.ViveWand)
                {
                    Set(left, "StickClick", true); Set(right, "StickClick", true); Step(7.1f);
                    Check(input.Move == Vector2.zero && input.Turn == Vector2.zero, "Vive pad click also moved/turned");
                    Step(8); Check(!input.RecenterDown, "Vive players press also recentered");
                    Set(left, "StickClick", false); Set(right, "StickClick", false); Step(8.1f);
                    Check(input.Move == Vector2.zero && input.Turn == Vector2.zero, "Vive movement resumed before the physical pad returned to neutral");
                    Set(left, "Stick", Vector2.zero); Set(right, "Stick", Vector2.zero); Step(8.2f);
                    Set(left, "Stick", new Vector2(0, .8f)); Set(right, "Stick", new Vector2(.8f, 0)); Step(8.3f);
                    Check(input.Move.y > 0 && input.Turn.x > 0, "Vive pad movement did not recover after neutral input");
                }
                else
                {
                    Set(left, "StickClick", true); Step(7.1f); Step(8);
                    Check(input.RecenterDown && input.Move.y > 0, "WMR lost separate stick recenter/movement");
                    Pad(right, true, -.8f); Step(8.1f);
                    Check(input.SecondaryDown && input.Turn.x > 0, "WMR pad consumed its independent thumbstick");
                }
            }
        }
    }
}
