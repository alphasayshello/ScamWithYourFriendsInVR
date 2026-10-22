using System;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestVoiceButtons()
        {
            var assembly = typeof(Input.VRInputManager).Assembly;
            Type handType = assembly.GetType("ScamWithYourFriendsInVR.Input.VRRawHandState", true);
            Type frameType = assembly.GetType("ScamWithYourFriendsInVR.Input.VRRawInputState", true);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            void Set(object target, string field, object value) => target.GetType().GetField(field, fields).SetValue(target, value);
            foreach (Plugin.ControllerLayoutKind layout in new[] { Plugin.ControllerLayoutKind.FaceButtons, Plugin.ControllerLayoutKind.ViveWand, Plugin.ControllerLayoutKind.Wmr })
            {
                var input = Own(new GameObject("Voice button fixture " + layout)).AddComponent<Input.VRInputManager>(); input.enabled = false;
                object Hand()
                {
                    object hand = Activator.CreateInstance(handType); Set(hand, "Layout", layout);
                    foreach (string field in new[] { "Connected", "Tracked", "HasStick", "HasStickClick", "HasPad", "HasPadClick", "HasMenu", "HasPrimary", "HasSecondary" }) Set(hand, field, true);
                    return hand;
                }
                object left = Hand(), right = Hand(), frame = Activator.CreateInstance(frameType);
                Set(frame, "HeadTracked", true); Set(frame, "Focused", true);
                var apply = input.GetType().GetMethod("ApplyState", fields); float now = 0;
                void Step() { Set(frame, "Left", left); Set(frame, "Right", right); apply.Invoke(input, new[] { frame, (object)(now += .1f) }); }
                Step(); Set(right, "StickClick", true); Step();
                Check(input.Voice && input.VoiceDown && !input.JumpDown && !input.SecondaryDown && !input.RecenterDown, "Voice press overlapped another controller action: " + layout);
                if (layout == Plugin.ControllerLayoutKind.ViveWand)
                {
                    Set(right, "Stick", new Vector2(0, .8f)); Step();
                    Check(input.Voice && !input.Jump && !input.Secondary && input.Turn == Vector2.zero, "Vive centre voice press changed into jump/turn after thumb drift");
                    Set(right, "StickClick", false); Step(); Set(right, "StickClick", true); Step();
                    Check(input.JumpDown && !input.Voice, "Fresh Vive upper-pad jump was lost");
                    Set(right, "StickClick", false); Set(right, "Stick", Vector2.zero); Step(); Set(right, "StickClick", true); Step();
                }
                Step(); Check(input.Voice && !input.VoiceDown, "Held voice repeated its press edge");
                foreach (string loss in new[] { "Focused", "HeadTracked", "Tracked" })
                {
                    object target = loss == "Tracked" ? right : frame;
                    Set(target, loss, false); Step(); Check(!input.Voice, "Input loss retained voice hold: " + loss);
                    Set(target, loss, true); Step(); Check(!input.Voice && !input.VoiceDown, "Input recovery replayed a held voice button: " + loss);
                    Set(right, "StickClick", false); Step(); Set(right, "StickClick", true); Step(); Check(input.VoiceDown, "Fresh voice did not recover: " + loss);
                }
                Set(right, "StickClick", false); Step(); Check(!input.Voice, "Voice button release was lost");
                Check(input.VoiceHint == (layout == Plugin.ControllerLayoutKind.ViveWand ? "RIGHT PAD CENTRE" : "RIGHT STICK CLICK"), "Voice hint does not match controller layout");
            }
        }
    }
}
