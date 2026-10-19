using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestBodyButton()
        {
            var input = Own(new GameObject("body button fixture")).AddComponent<Input.VRInputManager>();
            input.enabled = false;
            var step = input.GetType().GetMethod("UpdateBodyPrimary", BindingFlags.Instance | BindingFlags.NonPublic);
            void Grip(bool value) => input.GetType().GetProperty("LeftGrip").SetValue(input, value);
            void Button(bool value) => step.Invoke(input, new object[] { value });
            Grip(false); Button(true);
            Check(input.Jump && input.JumpDown && !input.GiveUp, "Ordinary A press did not jump");
            Grip(true); Button(true);
            Check(input.Jump && !input.JumpDown && !input.GiveUp, "Adding a modifier to a held jump started give-up");
            Button(false); Button(true);
            Check(input.GiveUp && input.GiveUpDown && !input.Jump, "Fresh modified A press did not select give-up");
            Button(true); Check(input.GiveUp && !input.GiveUpDown, "Held give-up repeated its press edge");
            Grip(false); Button(true);
            Check(!input.GiveUp && !input.Jump && !input.JumpDown, "Releasing give-up modifier started a jump");
            Grip(true); Button(true); Check(!input.GiveUp, "Canceled give-up restarted without a fresh button press");
            Button(false); Grip(false); Button(true);
            Check(input.Jump && input.JumpDown && !input.GiveUp, "Normal jump did not recover after canceled chord");
        }
    }
}
