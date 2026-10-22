using System;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestStorageButtons()
        {
            var assembly = typeof(Input.VRInputManager).Assembly;
            Type handType = assembly.GetType("ScamWithYourFriendsInVR.Input.VRRawHandState", true);
            Type frameType = assembly.GetType("ScamWithYourFriendsInVR.Input.VRRawInputState", true);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            void Set(object target, string field, object value) => target.GetType().GetField(field, fields).SetValue(target, value);
            foreach (Plugin.ControllerLayoutKind layout in new[] { Plugin.ControllerLayoutKind.FaceButtons, Plugin.ControllerLayoutKind.ViveWand, Plugin.ControllerLayoutKind.Wmr })
            {
                var input = Own(new GameObject("Storage chord fixture " + layout)).AddComponent<Input.VRInputManager>();
                input.enabled = false;
                object Hand()
                {
                    object hand = Activator.CreateInstance(handType); Set(hand, "Layout", layout);
                    foreach (string field in new[] { "Connected", "Tracked", "HasMenu", "HasPrimary", "HasSecondary", "HasGripButton", "HasStick", "HasStickClick", "HasPad", "HasPadClick" }) Set(hand, field, true);
                    return hand;
                }
                object left = Hand(), right = Hand(), frame = Activator.CreateInstance(frameType);
                Set(frame, "HeadTracked", true); Set(frame, "Focused", true);
                var apply = input.GetType().GetMethod("ApplyState", fields);
                float time = 0;
                void Step()
                {
                    Set(frame, "Left", left); Set(frame, "Right", right); apply.Invoke(input, new[] { frame, (object)(time += .1f) });
                }
                void Button(bool value) => Set(left, layout == Plugin.ControllerLayoutKind.FaceButtons ? "Secondary" : "Menu", value);
                Step(); Set(left, "GripButton", true); Button(true); Step();
                Check(input.Storage && input.StorageDown && !input.MenuDown, "Modified left menu did not own storage: " + layout);
                Step(); Check(input.Storage && !input.StorageDown && !input.MenuDown, "Storage hold repeated its edge or opened settings");
                Set(left, "GripButton", false); Step();
                Check(!input.Storage && !input.MenuDown, "Grip release changed storage into settings");
                Set(left, "GripButton", true); Step(); Check(!input.Storage && !input.MenuDown, "Cancelled storage restarted without a fresh button");
                Button(false); Step(); Button(true); Step(); Check(input.StorageDown, "Fresh storage chord did not recover");
                Set(frame, "Focused", false); Step(); Check(!input.Storage, "Focus loss retained storage hold");
                Set(frame, "Focused", true); Step(); Check(!input.Storage && !input.MenuDown, "Focus restoration replayed storage/menu input");
                Button(false); Step(); Button(true); Step(); Check(input.StorageDown, "Fresh storage did not recover after focus loss");
                Set(right, "Menu", true); Step(); Check(input.MenuDown, "Right menu could not open settings during storage hold");
                Set(right, "Menu", false); Button(false); Set(left, "GripButton", false); Step();
                Button(true); Step(); Check(input.MenuDown && !input.Storage, "Ordinary settings press was lost");
                Set(left, "GripButton", true); Step(); Check(!input.Storage, "Adding grip converted an owned menu press into storage");
                Button(false); Step(); Button(true); Step();
                Set(left, "Tracked", false); Step(); Check(!input.Storage && !input.MenuDown, "Controller tracking loss retained storage or opened settings");
                Set(left, "Tracked", true); Step(); Check(!input.Storage, "Tracking restoration resumed a cancelled chord");
                Button(false); Step(); Button(true); Step(); Check(input.StorageDown, "Storage did not recover after controller tracking loss");
                Check(input.StorageHint == (layout == Plugin.ControllerLayoutKind.FaceButtons ? "LEFT GRIP + LEFT Y" : "LEFT GRIP + LEFT MENU"), "Storage hint does not match controller layout");
                Check(input.TackleHint == (layout == Plugin.ControllerLayoutKind.FaceButtons ? "RIGHT GRIP + RIGHT B" : "RIGHT GRIP + RIGHT PAD DOWN"), "Tackle hint does not match controller layout");
            }
        }
    }
}
