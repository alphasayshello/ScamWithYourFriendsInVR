using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

namespace ScamWithYourFriendsInVR.Input
{
    public sealed class VRHandController : MonoBehaviour
    {
        public XRNode Node { get; set; }
        public bool IsTracked { get; private set; }
        public Transform Aim { get; private set; }
        private InputDevice knownDevice;
        private InputFeatureUsage<Quaternion> aimRotation;
        private bool hasAimRotation;
        private readonly List<InputFeatureUsage> usages = new List<InputFeatureUsage>();

        private void Awake()
        {
            Aim = new GameObject("Controller aim pose").transform;
            Aim.SetParent(transform, false);
        }

        public void Sync()
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(Node);
            if (device.isValid && device != knownDevice)
            {
                knownDevice = device; hasAimRotation = false; usages.Clear();
                if (device.TryGetFeatureUsages(usages))
                    foreach (var usage in usages)
                    {
                        if (usage.type != typeof(Quaternion)) continue;
                        string lower = usage.name.ToLowerInvariant();
                        if (lower.Contains("pointer") || lower.Contains("aim")) { aimRotation = usage.As<Quaternion>(); hasAimRotation = true; break; }
                    }
                Plugin.Log.LogInfo(Node + " device=" + device.name + "; aim rotation=" + hasAimRotation);
            }
            bool hasPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position);
            bool hasRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
            IsTracked = VRInputManager.Tracked(device) && hasPosition && hasRotation;
            if (!IsTracked) { if (!device.isValid) knownDevice = default; return; }
            if (!ValidPosition(position) || !ValidRotation(rotation)) { IsTracked = false; return; }
            transform.SetLocalPositionAndRotation(position, rotation);
            Quaternion pointerRotation = rotation;
            if (hasAimRotation && device.TryGetFeatureValue(aimRotation, out Quaternion orientation)
                && ValidRotation(orientation) && Quaternion.Angle(rotation, orientation) < 100) pointerRotation = orientation;
            // Grip position is reliable even on runtimes with an invalid aim location.
            Aim.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Inverse(rotation) * pointerRotation);
        }

        private static bool ValidPosition(Vector3 value)
            => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);
        private static bool ValidRotation(Quaternion value)
        {
            float length = Quaternion.Dot(value, value);
            return !float.IsNaN(length) && !float.IsInfinity(length) && length > .5f && length < 1.5f;
        }
    }
}
