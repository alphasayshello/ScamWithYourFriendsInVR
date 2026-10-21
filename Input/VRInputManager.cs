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
        private bool menu, recenter, secondary, leftPrimaryHeld;
        private bool recenterHeld;
        private float recenterStarted;
        private bool turnLatched;
        private bool rightPrimaryHeld, giveUpChord, giveUpCanceled;
        private VRButtonReleaseGate triggerGate, gripGate, leftTriggerGate, rightPrimaryGate, secondaryGate;
        private VRButtonReleaseGate leftPrimaryGate, leftSecondaryGate, leftMenuGate, rightMenuGate, recenterGate;
        private VRStickReleaseGate moveGate, turnGate;
        private VRControllerIdentity leftIdentity, rightIdentity;
        private VRPadBindings leftPad, rightPad;
        public Plugin.ControllerLayoutKind LeftLayout { get; private set; }
        public Plugin.ControllerLayoutKind RightLayout { get; private set; }
        public string PlayersHint => LeftLayout == Plugin.ControllerLayoutKind.ViveWand || LeftLayout == Plugin.ControllerLayoutKind.Wmr ? "LEFT PAD" : "LEFT X";
        public string SettingsHint => LeftLayout == Plugin.ControllerLayoutKind.ViveWand || LeftLayout == Plugin.ControllerLayoutKind.Wmr ? "MENU" : "LEFT Y";
        public string JumpHint => RightLayout == Plugin.ControllerLayoutKind.ViveWand || RightLayout == Plugin.ControllerLayoutKind.Wmr ? "RIGHT PAD UP" : "RIGHT A";
        public string MoveHint => LeftLayout == Plugin.ControllerLayoutKind.ViveWand ? "LEFT PAD" : "LEFT STICK";
        public string TurnHint => RightLayout == Plugin.ControllerLayoutKind.ViveWand ? "RIGHT PAD" : "RIGHT STICK";
        public string FlightClimbHint => "LEFT TRIGGER";
        public string FlightReleaseHint => RightLayout == Plugin.ControllerLayoutKind.ViveWand || RightLayout == Plugin.ControllerLayoutKind.Wmr ? "TRIGGER / PAD UP" : "TRIGGER / A";
        public string GiveUpHint => RightLayout == Plugin.ControllerLayoutKind.ViveWand || RightLayout == Plugin.ControllerLayoutKind.Wmr ? "LEFT GRIP + RIGHT PAD UP" : "LEFT GRIP + RIGHT A";

        private void Update()
        {
            VRMouseFallback.Ensure();
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (focusKnown == null)
            {
                var type = ReflectionTypes.Find("ScamWithYourFriendsInVR.Provider.VRSessionFocusFeature");
                focusKnown = type?.GetProperty("HasSessionState");
                focusValue = type?.GetProperty("HasInputFocus");
            }
            bool focused = focusKnown?.GetValue(null) is bool known && known
                ? focusValue.GetValue(null) is bool inputFocused && inputFocused : Application.isFocused;
            bool headTracked = Tracked(head);
            ApplyState(new VRRawInputState { HeadTracked = headTracked, Focused = focused,
                Left = ReadHand(left, focused && headTracked, leftIdentity.Read(left)), Right = ReadHand(right, focused && headTracked, rightIdentity.Read(right)) }, Time.unscaledTime);
        }

        private static VRRawHandState ReadHand(InputDevice device, bool controlsActive, Plugin.ControllerLayoutKind layout)
        {
            var state = new VRRawHandState { Connected = device.isValid, Tracked = Tracked(device), Layout = layout };
            if (!state.Connected || !controlsActive) return state;
            state.HasTriggerButton = device.TryGetFeatureValue(CommonUsages.triggerButton, out state.TriggerButton);
            state.HasGripButton = device.TryGetFeatureValue(CommonUsages.gripButton, out state.GripButton);
            state.HasMenu = device.TryGetFeatureValue(CommonUsages.menuButton, out state.Menu);
            state.HasPrimary = device.TryGetFeatureValue(CommonUsages.primaryButton, out state.Primary);
            state.HasSecondary = device.TryGetFeatureValue(CommonUsages.secondaryButton, out state.Secondary);
            state.HasStickClick = device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out state.StickClick);
            state.HasTrigger = device.TryGetFeatureValue(CommonUsages.trigger, out state.TriggerValue);
            state.HasGrip = device.TryGetFeatureValue(CommonUsages.grip, out state.GripValue);
            state.HasStick = device.TryGetFeatureValue(CommonUsages.primary2DAxis, out state.Stick);
            if (layout == Plugin.ControllerLayoutKind.Wmr)
            {
                state.HasPad = device.TryGetFeatureValue(CommonUsages.secondary2DAxis, out state.Pad);
                state.HasPadClick = device.TryGetFeatureValue(CommonUsages.secondary2DAxisClick, out state.PadClick);
            }
            return state;
        }

        private void ApplyState(VRRawInputState state, float now)
        {
            HeadTracked = state.HeadTracked; HasInputFocus = state.Focused; RightTracked = state.Right.Tracked;
            bool active = HeadTracked && HasInputFocus;
            leftPad.Apply(ref state.Left, true, active); rightPad.Apply(ref state.Right, false, active);
            LeftLayout = state.Left.Layout;
            RightLayout = state.Right.Layout;
            bool leftActive = active && state.Left.Tracked, rightActive = active && state.Right.Tracked;
            Move = Stick(moveGate.Read(state.Left.Stick, leftActive && state.Left.HasStick, Plugin.Deadzone.Value));
            Turn = Stick(turnGate.Read(state.Right.Stick, rightActive && state.Right.HasStick, Plugin.Deadzone.Value));
            bool rawTrigger = state.Right.HasTriggerButton && state.Right.TriggerButton || state.Right.HasTrigger && state.Right.TriggerValue > (Trigger || !triggerGate.Released ? .45f : .6f);
            bool rawGrip = state.Right.HasGripButton && state.Right.GripButton || state.Right.HasGrip && state.Right.GripValue > (Grip || !gripGate.Released ? .45f : .6f);
            bool nextTrigger = triggerGate.Read(rawTrigger, rightActive && (state.Right.HasTrigger || state.Right.HasTriggerButton));
            bool gripPress = gripGate.Read(rawGrip, rightActive && (state.Right.HasGrip || state.Right.HasGripButton));
            // A held grip remains a modifier, but acquisition needs a fresh edge.
            // This preserves fresh punch/item input without auto-grabbing when
            // controller tracking or application focus returns.
            bool nextGrip = rightActive && rawGrip;
            TriggerDown = nextTrigger && !Trigger;
            TriggerUp = !nextTrigger && Trigger;
            Trigger = nextTrigger;
            GripDown = gripPress && !Grip;
            GripUp = !nextGrip && Grip;
            Grip = nextGrip;
            bool leftMenu = leftMenuGate.Read(state.Left.Menu, active && state.Left.Connected && state.Left.HasMenu);
            bool rightMenu = rightMenuGate.Read(state.Right.Menu, active && state.Right.Connected && state.Right.HasMenu);
            bool leftSecondary = leftSecondaryGate.Read(state.Left.Secondary, active && state.Left.Connected && state.Left.HasSecondary);
            bool nextMenu = leftMenu || rightMenu || leftSecondary;
            MenuDown = nextMenu && !menu;
            menu = nextMenu;
            LeftGrip = leftActive && (state.Left.HasGripButton && state.Left.GripButton || state.Left.HasGrip && state.Left.GripValue > .6f);
            bool rightPrimary = rightPrimaryGate.Read(state.Right.Primary, rightActive && state.Right.HasPrimary);
            UpdateBodyPrimary(rightPrimary);
            bool rawLeftTrigger = state.Left.HasTriggerButton && state.Left.TriggerButton || state.Left.HasTrigger && state.Left.TriggerValue > (LeftTrigger || !leftTriggerGate.Released ? .45f : .6f);
            bool nextLeftTrigger = leftTriggerGate.Read(rawLeftTrigger, leftActive && (state.Left.HasTrigger || state.Left.HasTriggerButton));
            LeftTriggerDown = nextLeftTrigger && !LeftTrigger;
            LeftTrigger = nextLeftTrigger;
            Pee = LeftGrip && LeftTrigger;
            bool leftPrimary = leftPrimaryGate.Read(state.Left.Primary, leftActive && state.Left.HasPrimary);
            bool leftPrimaryDown = leftPrimary && !leftPrimaryHeld;
            FartDown = leftPrimaryDown && LeftGrip;
            PlayersDown = leftPrimaryDown && !LeftGrip;
            leftPrimaryHeld = leftPrimary;
            bool nextRecenter = recenterGate.Read(state.Left.StickClick, leftActive && state.Left.HasStickClick);
            if (nextRecenter && !recenterHeld) recenterStarted = now;
            RecenterDown = nextRecenter && !recenter && now - recenterStarted >= .65f;
            recenter = nextRecenter && (recenter || RecenterDown);
            recenterHeld = nextRecenter;
            bool nextSecondary = secondaryGate.Read(state.Right.Secondary, rightActive && state.Right.HasSecondary);
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

        private static Vector2 Stick(Vector2 value)
        {
            float length = value.magnitude;
            float dead = Plugin.Deadzone.Value;
            return length <= dead ? Vector2.zero : value.normalized * Mathf.Clamp01((length - dead) / (1f - dead));
        }
    }
}
