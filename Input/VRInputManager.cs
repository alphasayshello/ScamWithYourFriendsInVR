using UnityEngine;
using UnityEngine.XR;
using System.Reflection;

namespace ScamWithYourFriendsInVR.Input
{
    [DefaultExecutionOrder(-31000)]
    public sealed class VRInputManager : MonoBehaviour
    {
        public Vector2 Move { get; private set; }
        public Vector2 Turn { get; private set; }
        public bool Trigger { get; private set; }
        public bool TriggerDown { get; private set; }
        public bool TriggerUp { get; private set; }
        public bool Grip { get; private set; }
        public bool GripDown { get; private set; }
        public bool GripUp { get; private set; }
        public bool MenuDown { get; private set; }
        public bool PlayersDown { get; private set; }
        public bool LeftGrip { get; private set; }
        public bool LeftTrigger { get; private set; }
        public bool LeftTriggerDown { get; private set; }
        public bool Jump { get; private set; }
        public bool JumpDown { get; private set; }
        public bool GiveUp { get; private set; }
        public bool GiveUpDown { get; private set; }
        public bool Pee { get; private set; }
        public bool FartDown { get; private set; }
        public bool RecenterDown { get; private set; }
        public bool SecondaryDown { get; private set; }
        public bool Secondary { get; private set; }
        public bool HeadTracked { get; private set; }
        public bool RightTracked { get; private set; }
        public bool HasInputFocus { get; private set; }
        private PropertyInfo focusKnown, focusValue;
        private bool menu, recenter, secondary, players, fart;
        private bool recenterHeld;
        private float recenterStarted;
        private bool turnLatched;
        private bool rightPrimaryHeld, giveUpChord, giveUpCanceled;

        private void Update()
        {
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            HeadTracked = Tracked(head);
            RightTracked = Tracked(right);
            if (focusKnown == null)
            {
                var type = ReflectionTypes.Find("ScamWithYourFriendsInVR.Provider.VRSessionFocusFeature");
                focusKnown = type?.GetProperty("HasSessionState");
                focusValue = type?.GetProperty("HasInputFocus");
            }
            HasInputFocus = focusKnown?.GetValue(null) is bool known && known
                ? focusValue.GetValue(null) is bool focused && focused : Application.isFocused;
            bool active = HeadTracked && HasInputFocus;
            Move = active && Tracked(left) ? Stick(left) : Vector2.zero;
            Turn = active && RightTracked ? Stick(right) : Vector2.zero;
            bool nextTrigger = active && RightTracked && Button(right, CommonUsages.triggerButton);
            if (active && RightTracked && right.TryGetFeatureValue(CommonUsages.trigger, out float analogTrigger))
                nextTrigger |= analogTrigger > (Trigger ? 0.45f : 0.6f);
            bool nextGrip = active && RightTracked && Button(right, CommonUsages.gripButton);
            if (active && RightTracked && right.TryGetFeatureValue(CommonUsages.grip, out float analogGrip))
                nextGrip |= analogGrip > (Grip ? 0.45f : 0.6f);
            TriggerDown = nextTrigger && !Trigger;
            TriggerUp = !nextTrigger && Trigger;
            Trigger = nextTrigger;
            GripDown = nextGrip && !Grip;
            GripUp = !nextGrip && Grip;
            Grip = nextGrip;
            bool nextMenu = active && (Button(left, CommonUsages.menuButton) || Button(right, CommonUsages.menuButton)
                || Button(left, CommonUsages.secondaryButton));
            MenuDown = nextMenu && !menu;
            menu = nextMenu;
            LeftGrip = active && Tracked(left) && (Button(left, CommonUsages.gripButton)
                || left.TryGetFeatureValue(CommonUsages.grip, out float leftGrip) && leftGrip > .6f);
            bool rightPrimary = active && RightTracked && Button(right, CommonUsages.primaryButton);
            UpdateBodyPrimary(rightPrimary);
            bool nextLeftTrigger = active && Tracked(left) && (Button(left, CommonUsages.triggerButton)
                || left.TryGetFeatureValue(CommonUsages.trigger, out float leftTrigger) && leftTrigger > (LeftTrigger ? .45f : .6f));
            LeftTriggerDown = nextLeftTrigger && !LeftTrigger;
            LeftTrigger = nextLeftTrigger;
            Pee = LeftGrip && LeftTrigger;
            bool nextFart = LeftGrip && Button(left, CommonUsages.primaryButton);
            FartDown = nextFart && !fart; fart = nextFart;
            bool nextPlayers = active && !LeftGrip && Button(left, CommonUsages.primaryButton);
            PlayersDown = nextPlayers && !players;
            players = nextPlayers;
            bool nextRecenter = active && Button(left, CommonUsages.primary2DAxisClick);
            if (nextRecenter && !recenterHeld) recenterStarted = Time.unscaledTime;
            RecenterDown = nextRecenter && !recenter && Time.unscaledTime - recenterStarted >= .65f;
            recenter = nextRecenter && (recenter || RecenterDown);
            recenterHeld = nextRecenter;
            bool nextSecondary = active && RightTracked && Button(right, CommonUsages.secondaryButton);
            SecondaryDown = nextSecondary && !secondary;
            Secondary = nextSecondary;
            secondary = nextSecondary;
        }

        private void UpdateBodyPrimary(bool rightPrimary)
        {
            bool down = rightPrimary && !rightPrimaryHeld;
            if (down) { giveUpChord = LeftGrip; giveUpCanceled = false; }
            if (!rightPrimary) { giveUpChord = false; giveUpCanceled = false; }
            if (giveUpChord && !LeftGrip) giveUpCanceled = true;
            bool nextGiveUp = rightPrimary && giveUpChord && !giveUpCanceled;
            GiveUpDown = nextGiveUp && !GiveUp;
            GiveUp = nextGiveUp;
            JumpDown = down && !giveUpChord;
            Jump = rightPrimary && !giveUpChord;
            rightPrimaryHeld = rightPrimary;
        }

        public float ConsumeSnap()
        {
            float x = Turn.x;
            if (Mathf.Abs(x) < 0.3f) turnLatched = false;
            if (turnLatched || Mathf.Abs(x) < 0.7f) return 0;
            turnLatched = true;
            return Mathf.Sign(x) * Plugin.SnapAngle.Value;
        }

        internal static bool Tracked(InputDevice device)
        {
            return device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        private static bool Button(InputDevice device, InputFeatureUsage<bool> usage)
        {
            return device.isValid && device.TryGetFeatureValue(usage, out bool value) && value;
        }

        private static Vector2 Stick(InputDevice device)
        {
            if (!device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 value)) return Vector2.zero;
            float length = value.magnitude;
            float dead = Plugin.Deadzone.Value;
            return length <= dead ? Vector2.zero : value.normalized * Mathf.Clamp01((length - dead) / (1f - dead));
        }
    }
}
