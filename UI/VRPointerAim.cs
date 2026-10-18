using UnityEngine;

namespace ScamWithYourFriendsInVR.UI
{
    internal sealed class VRPointerAim
    {
        private bool initialized;
        private Quaternion filtered, previous;
        internal void Reset() => initialized = false;
        internal Quaternion Sample(Quaternion raw, float seconds, float deltaTime)
        {
            if (!initialized || seconds <= 0 || Quaternion.Angle(filtered, raw) > 25)
            {
                initialized = true; previous = filtered = raw; return raw;
            }
            float step = Mathf.Clamp(deltaTime, .001f, .1f);
            float speed = Quaternion.Angle(previous, raw) / step;
            float response = seconds / (1 + speed / 90);
            filtered = Quaternion.Slerp(filtered, raw, 1 - Mathf.Exp(-step / Mathf.Max(.001f, response)));
            previous = raw;
            return filtered;
        }
        internal static bool Visible(bool always, bool context, bool hit, bool held, bool worldTarget)
            => always || context || hit || held || worldTarget;
    }
}
