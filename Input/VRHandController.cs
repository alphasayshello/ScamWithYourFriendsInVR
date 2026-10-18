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
        private InputFeatureUsage<Vector3> aimPosition;
        private InputFeatureUsage<Quaternion> aimRotation;
        private bool hasAimPosition, hasAimRotation;
        private GameObject model;
        private Material material;

        private void Awake()
        {
            Aim = new GameObject("Controller aim pose").transform;
            Aim.SetParent(transform, false);
        }

        private void Start()
        {
            model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            model.name = "Procedural controller grip";
            model.layer = 5;
            model.transform.SetParent(transform, false);
            model.transform.localScale = new Vector3(0.045f, 0.075f, 0.045f);
            model.transform.localRotation = Quaternion.Euler(55f, 0, 0);
            Destroy(model.GetComponent<Collider>());
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                material = new Material(shader);
                material.color = Node == XRNode.LeftHand ? new Color(0.15f, 0.55f, 1) : new Color(0.1f, 0.95f, 0.7f);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", material.color);
                model.GetComponent<Renderer>().sharedMaterial = material;
            }
        }

        public void Sync()
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(Node);
            if (device.isValid && device != knownDevice)
            {
                knownDevice = device;
                hasAimPosition = hasAimRotation = false;
                var usages = new List<InputFeatureUsage>();
                if (device.TryGetFeatureUsages(usages))
                    foreach (var usage in usages)
                    {
                        string lower = usage.name.ToLowerInvariant();
                        if (!lower.Contains("pointer") && !lower.Contains("aim")) continue;
                        if (usage.type == typeof(Vector3)) { aimPosition = usage.As<Vector3>(); hasAimPosition = true; }
                        if (usage.type == typeof(Quaternion)) { aimRotation = usage.As<Quaternion>(); hasAimRotation = true; }
                    }
                Plugin.Log.LogInfo(Node + " device=" + device.name + "; aim position=" + hasAimPosition + "; aim rotation=" + hasAimRotation);
            }
            IsTracked = VRInputManager.Tracked(device)
                && device.TryGetFeatureValue(CommonUsages.devicePosition, out _)
                && device.TryGetFeatureValue(CommonUsages.deviceRotation, out _);
            if (model != null) model.SetActive(IsTracked && VRManager.Instance != null && VRManager.Instance.Input.HasInputFocus);
            if (!IsTracked) return;
            device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position);
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
            transform.localPosition = position;
            transform.localRotation = rotation;
            if (Aim != null)
            {
                Quaternion pointerRotation = rotation;
                if (hasAimRotation && device.TryGetFeatureValue(aimRotation, out Quaternion aimOrientation)
                    && ValidRotation(aimOrientation) && Quaternion.Angle(rotation, aimOrientation) < 100) pointerRotation = aimOrientation;
                // Keep the ray attached to the tracked grip. Some runtimes
                // expose an aim location even while that location is invalid.
                Aim.localPosition = Vector3.zero;
                Aim.localRotation = Quaternion.Inverse(rotation) * pointerRotation;
            }
        }

        private static bool ValidRotation(Quaternion value)
        {
            float length = Quaternion.Dot(value, value);
            return !float.IsNaN(length) && !float.IsInfinity(length) && length > .5f && length < 1.5f;
        }

        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
