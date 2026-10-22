using UnityEngine;

namespace ScamWithYourFriendsInVR.Input
{
    internal struct VRRawHandState
    {
        internal Plugin.ControllerLayoutKind Layout;
        internal bool HasPad, HasPadClick, PadClick;
        internal bool HasVoice, Voice;
        internal Vector2 Pad;
        internal bool Connected, Tracked, TriggerButton, GripButton, Menu, Primary, Secondary, StickClick;
        internal bool HasTrigger, HasGrip, HasTriggerButton, HasGripButton, HasMenu, HasPrimary, HasSecondary, HasStickClick, HasStick;
        internal float TriggerValue, GripValue;
        internal Vector2 Stick;
    }

    internal struct VRRawInputState
    {
        internal bool HeadTracked, Focused;
        internal VRRawHandState Left, Right;
    }

    internal struct VRButtonReleaseGate
    {
        internal bool Released { get; private set; }
        internal bool Read(bool pressed, bool active)
        {
            if (!active) Released = false;
            else if (!pressed) Released = true;
            return active && Released && pressed;
        }
    }

    internal struct VRStickReleaseGate
    {
        private bool centered;
        internal Vector2 Read(Vector2 value, bool active, float deadzone)
        {
            if (float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.x) || float.IsInfinity(value.y))
            { centered = false; return Vector2.zero; }
            if (!active) centered = false;
            else if (value.sqrMagnitude <= deadzone * deadzone) centered = true;
            return active && centered ? value : Vector2.zero;
        }
    }
}
