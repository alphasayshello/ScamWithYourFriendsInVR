using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.UI
{
    internal static class VRWorldPanelPicker
    {
        private delegate VisualElement Pick3D(UIDocument document, Ray ray, out float distance);
        private static Pick3D pick;
        private static bool resolved;

        internal static bool TryPick(UIDocument document, Ray ray, out VisualElement element,
            out Vector3 world, out Vector3 position, out float distance)
        {
            element = null; world = default; position = default; distance = 0;
            if (!Active(document)) return false;
            if (!resolved)
            {
                resolved = true;
                try
                {
                    Type type = typeof(UIDocument).Assembly.GetType("UnityEngine.UIElements.WorldSpaceInput");
                    MethodInfo method = type?.GetMethod("Pick3D", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(UIDocument), typeof(Ray), typeof(float).MakeByRefType() }, null);
                    if (method != null) pick = (Pick3D)Delegate.CreateDelegate(typeof(Pick3D), method);
                    else Plugin.Log.LogWarning("Native world-space UI picking API unavailable.");
                }
                catch (Exception ex) { Patches.GameAdapter.Report("Native world-space UI picking API", ex); }
            }
            if (pick == null) return false;
            element = pick(document, ray, out distance);
            if (!Usable(element) || element == document.rootVisualElement || float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0 || distance > 10) return false;
            world = ray.GetPoint(distance);
            position = document.transform.InverseTransformPoint(world);
            return true;
        }

        internal static bool TryDrag(UIDocument document, VisualElement element, Ray ray, out Vector3 position)
        {
            position = default;
            if (!Active(document) || !Usable(element) || element.panel != document.rootVisualElement.panel) return false;
            Matrix4x4 matrix = document.transform.localToWorldMatrix * element.worldTransform;
            Vector3 normal = matrix.inverse.transpose.MultiplyVector(Vector3.forward).normalized;
            var plane = new Plane(normal, matrix.MultiplyPoint3x4(Vector3.zero));
            if (!plane.Raycast(ray, out float distance) || distance > 10) return false;
            position = document.transform.InverseTransformPoint(ray.GetPoint(distance));
            return true;
        }

        private static bool Active(UIDocument document) => document != null && document.isActiveAndEnabled
            && document.rootVisualElement?.panel != null && Patches.GameAdapter.Read(document.panelSettings, "renderMode")?.ToString() == "WorldSpace";

        private static bool Usable(VisualElement element)
        {
            if (element == null || !element.enabledInHierarchy) return false;
            float opacity = 1;
            for (VisualElement ancestor = element; ancestor != null; ancestor = ancestor.parent)
            {
                if (!ancestor.visible || ancestor.resolvedStyle.display == DisplayStyle.None) return false;
                opacity *= ancestor.resolvedStyle.opacity;
                if (opacity <= 0.001f) return false;
            }
            return true;
        }
    }
}
