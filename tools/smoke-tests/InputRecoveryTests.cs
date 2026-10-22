using System;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestInputRecovery()
        {
            Type handType = typeof(Input.VRInputManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRRawHandState", true);
            Type frameType = typeof(Input.VRInputManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRRawInputState", true);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            void Set(object target, string field, object value) => target.GetType().GetField(field, fields).SetValue(target, value);
            object Hand()
            {
                object hand = Activator.CreateInstance(handType);
                Set(hand, "Connected", true); Set(hand, "Tracked", true);
                foreach (string field in new[] { "HasTrigger", "HasGrip", "HasTriggerButton", "HasGripButton", "HasMenu", "HasPrimary", "HasSecondary", "HasStickClick", "HasStick" })
                    Set(hand, field, true);
                return hand;
            }
            foreach (string loss in new[] { "focus", "head", "right controller" })
            {
                var input = Own(new GameObject("Input recovery fixture " + loss)).AddComponent<Input.VRInputManager>();
                input.enabled = false;
                object left = Hand(), right = Hand(), frame = Activator.CreateInstance(frameType);
                Set(frame, "HeadTracked", true); Set(frame, "Focused", true);
                var apply = input.GetType().GetMethod("ApplyState", fields);
                void Step(float now)
                {
                    Set(frame, "Left", left); Set(frame, "Right", right);
                    apply.Invoke(input, new[] { frame, (object)now });
                }
                void Buttons(bool value)
                {
                    foreach (object hand in new[] { left, right })
                    {
                        foreach (string field in new[] { "TriggerButton", "GripButton", "Menu", "Primary", "Secondary", "StickClick" }) Set(hand, field, value);
                        Set(hand, "TriggerValue", value ? .8f : 0); Set(hand, "GripValue", value ? .8f : 0);
                        Set(hand, "Stick", value ? new Vector2(.8f, 0) : Vector2.zero);
                    }
                }
                Step(0); Buttons(true); Step(1);
                Check(input.TriggerDown && input.GripDown && input.LeftTriggerDown && input.GiveUpDown && input.SecondaryDown && input.FartDown,
                    "Fresh active controls did not reach input state: " + loss);
                if (loss == "focus") Set(frame, "Focused", false);
                else if (loss == "head") Set(frame, "HeadTracked", false);
                else Set(right, "Tracked", false);
                Step(1.1f);
                Check(!input.Trigger && input.TriggerUp && input.GripUp && !input.Jump && !input.GiveUp && !input.Secondary && input.Turn == Vector2.zero,
                    "Losing input did not release right-hand actions: " + loss);
                if (loss != "right controller") Check(input.Move == Vector2.zero && !input.Pee && !input.MenuDown && !input.FartDown, "Losing global input retained actions");
                Set(frame, "Focused", true); Set(frame, "HeadTracked", true); Set(right, "Tracked", true);
                Step(2); Step(2.9f);
                Check(!input.Trigger && !input.TriggerDown && input.Grip && !input.GripDown && !input.GiveUp && !input.Jump
                    && !input.SecondaryDown && input.Turn == Vector2.zero && input.ConsumeSnap() == 0,
                    "Held right-hand input rearmed after recovery: " + loss);
                if (loss != "right controller")
                    Check(!input.LeftTrigger && !input.Pee && !input.MenuDown && !input.PlayersDown && !input.FartDown && !input.RecenterDown && input.Move == Vector2.zero,
                        "Held global input rearmed after recovery: " + loss);
                Set(right, "TriggerButton", false); Set(right, "TriggerValue", .5f);
                Set(left, "TriggerButton", false); Set(left, "TriggerValue", .5f);
                Step(3);
                Check(!input.Trigger && (loss == "right controller" || !input.LeftTrigger), "Half-released analog trigger bypassed recovery hysteresis");
                Buttons(false); Step(4); Buttons(true); Step(4.1f);
                Check(input.TriggerDown && input.GripDown && input.LeftTriggerDown && input.GiveUpDown && input.SecondaryDown
                    && input.MenuDown && input.FartDown && input.Move.sqrMagnitude > 0 && input.Turn.sqrMagnitude > 0,
                    "Fresh release/press did not recover normal input: " + loss);
                Check(input.ConsumeSnap() != 0 && input.ConsumeSnap() == 0, "Fresh centred stick recovery did not preserve snap-turn latching");
            }

            var menuInput = Own(new GameObject("Untracked controller menu fixture")).AddComponent<Input.VRInputManager>();
            menuInput.enabled = false;
            object menuLeft = Hand(), menuRight = Hand(), menuFrame = Activator.CreateInstance(frameType);
            Set(menuFrame, "HeadTracked", true); Set(menuFrame, "Focused", true);
            void MenuStep(float time)
            {
                Set(menuFrame, "Left", menuLeft); Set(menuFrame, "Right", menuRight);
                menuInput.GetType().GetMethod("ApplyState", fields).Invoke(menuInput, new[] { menuFrame, (object)time });
            }
            MenuStep(0); Set(menuRight, "Tracked", false); Set(menuRight, "Menu", true); MenuStep(1);
            Check(menuInput.MenuDown, "Connected untracked controller could not open/back out of menus");
            Set(menuRight, "Connected", false); MenuStep(2); Set(menuRight, "Connected", true); MenuStep(3);
            Check(!menuInput.MenuDown, "Reconnecting a held menu button reopened a menu");
            Set(menuRight, "Menu", false); MenuStep(4); Set(menuRight, "Menu", true); MenuStep(5);
            Check(menuInput.MenuDown, "Reconnected menu button did not recover after release");
            Set(menuRight, "Tracked", true); Set(menuRight, "Stick", new Vector2(float.NaN, 0)); MenuStep(6);
            Check(menuInput.Turn == Vector2.zero, "Malformed stick state reached locomotion");
            Set(menuRight, "HasStick", false); Set(menuRight, "Stick", Vector2.zero);
            Set(menuRight, "HasPrimary", false); Set(menuRight, "Primary", false); MenuStep(7);
            Set(menuRight, "HasStick", true); Set(menuRight, "Stick", new Vector2(.8f, 0));
            Set(menuRight, "HasPrimary", true); Set(menuRight, "Primary", true); MenuStep(8);
            Check(!menuInput.Jump && !menuInput.GiveUp && menuInput.Turn == Vector2.zero,
                "Unavailable controls were incorrectly treated as a release/centre before held data returned");
            Set(menuRight, "Stick", Vector2.zero); Set(menuRight, "Primary", false); MenuStep(9);
            Set(menuRight, "Stick", new Vector2(.8f, 0)); Set(menuRight, "Primary", true); MenuStep(10);
            Check(menuInput.JumpDown && menuInput.Turn.sqrMagnitude > 0, "Unavailable controls did not recover after a valid neutral sample");

            var leftInput = Own(new GameObject("Left controller recovery fixture")).AddComponent<Input.VRInputManager>();
            leftInput.enabled = false;
            object leftHand = Hand(), rightHand = Hand(), leftFrame = Activator.CreateInstance(frameType);
            Set(leftFrame, "HeadTracked", true); Set(leftFrame, "Focused", true);
            void LeftStep(float time)
            {
                Set(leftFrame, "Left", leftHand); Set(leftFrame, "Right", rightHand);
                leftInput.GetType().GetMethod("ApplyState", fields).Invoke(leftInput, new[] { leftFrame, (object)time });
            }
            LeftStep(0);
            Set(leftHand, "GripButton", true); Set(leftHand, "Primary", true); Set(leftHand, "TriggerButton", true);
            Set(leftHand, "StickClick", true); Set(leftHand, "Stick", new Vector2(.8f, 0)); LeftStep(1);
            Check(leftInput.FartDown && leftInput.Pee && leftInput.PeeDown && !leftInput.PlayersDown && leftInput.Move.sqrMagnitude > 0, "Fresh left-hand chord did not activate");
            LeftStep(1.05f);
            Check(leftInput.Pee && !leftInput.PeeDown, "Held pee chord repeated its press edge");
            Set(leftHand, "Tracked", false); LeftStep(1.1f);
            Check(!leftInput.FartDown && !leftInput.Pee && !leftInput.PeeDown && !leftInput.PlayersDown && leftInput.Move == Vector2.zero, "Left tracking loss changed a fart into a players press");
            Set(leftHand, "Tracked", true); LeftStep(2); LeftStep(2.9f);
            Check(!leftInput.FartDown && !leftInput.Pee && !leftInput.PeeDown && !leftInput.PlayersDown && !leftInput.RecenterDown && leftInput.Move == Vector2.zero,
                "Held left-hand input restarted after tracking recovery");
            Set(leftHand, "Primary", false); Set(leftHand, "TriggerButton", false); Set(leftHand, "StickClick", false); Set(leftHand, "Stick", Vector2.zero); LeftStep(3);
            Set(leftHand, "Primary", true); LeftStep(3.1f); Check(leftInput.FartDown, "Fresh left chord did not recover");
            Set(leftHand, "GripButton", false); LeftStep(3.2f);
            Check(!leftInput.PlayersDown && !leftInput.FartDown, "Releasing the fart modifier opened players while X stayed held");
            Set(leftHand, "Primary", false); LeftStep(4); Set(leftHand, "Primary", true); LeftStep(4.1f);
            Check(leftInput.PlayersDown && !leftInput.FartDown, "Fresh unmodified X did not open players");
            Set(leftHand, "GripButton", true); LeftStep(4.2f);
            Check(!leftInput.FartDown && !leftInput.PlayersDown, "Adding a modifier changed a held players press into a fart");
            Set(leftHand, "TriggerButton", true); LeftStep(5);
            Check(leftInput.Pee && leftInput.PeeDown, "Fresh trigger with held grip did not begin pee chord");
            Set(leftHand, "GripButton", false); LeftStep(5.1f);
            Check(!leftInput.Pee && !leftInput.PeeDown, "Grip release retained pee chord");
            Set(leftHand, "GripButton", true); LeftStep(5.2f);
            Check(leftInput.Pee && leftInput.PeeDown, "Fresh grip with held trigger did not begin pee chord");
            LeftStep(5.3f);
            Check(leftInput.Pee && !leftInput.PeeDown, "Sustained modifier chord repeated its edge");
            Set(leftHand, "TriggerButton", false); LeftStep(5.4f);
            Check(!leftInput.Pee && !leftInput.PeeDown, "Trigger release retained pee chord");
        }
    }
}
