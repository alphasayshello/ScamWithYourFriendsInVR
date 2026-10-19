using System;
using HarmonyLib;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Patches
{
    internal static class DesktopUIPatches
    {
        internal static void Install(Harmony harmony)
        {
            try
            {
                Type browser = ReflectionTypes.Find("Kolkata.UI.KolkataOsBrowserHost");
                var up = browser == null ? null : AccessTools.Method(browser, "HandlePointerUp");
                if (up != null) harmony.Patch(up, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforeBrowserPointerUp)));
                else Plugin.Log.LogWarning("Native browser release method unavailable; controller drag release compatibility unverified.");
            }
            catch (Exception ex) { GameAdapter.Report("Browser controller release patch", ex); }
            try
            {
                Type cameraApp = ReflectionTypes.Find("Kolkata.UI.KolkataOsCameraApp");
                var assign = cameraApp == null ? null : AccessTools.Method(cameraApp, "AssignRenderTexture", Type.EmptyTypes);
                if (assign != null) harmony.Patch(assign, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforeCameraPreview)));
                else Plugin.Log.LogWarning("Native camera preview method unavailable; desktop preview compatibility unverified.");
            }
            catch (Exception ex) { GameAdapter.Report("Desktop camera preview patch", ex); }
            try
            {
                Type type = ReflectionTypes.Find("MonitorInteractable");
                var method = type == null ? null : AccessTools.Method(type, "GetNormalizedMousePosition", Type.EmptyTypes);
                if (method == null) { Plugin.Log.LogWarning("Monitor cursor method unavailable; animation cursor remains native."); return; }
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforeNormalizedMouse)));
            }
            catch (Exception ex) { GameAdapter.Report("Monitor cursor patch", ex); }
        }

        private static void BeforeBrowserPointerUp(object __instance, UnityEngine.UIElements.PointerUpEvent evt)
        {
            if (VRManager.Instance == null || !VRManager.Instance.Ready || evt == null || evt.pointerId != UI.VRPointer.ToolkitPointerId) return;
            var viewport = GameAdapter.Read(__instance, "viewport") as UnityEngine.UIElements.VisualElement;
            var image = GameAdapter.Read(__instance, "browserImage") as UnityEngine.UIElements.VisualElement;
            var content = image ?? viewport;
            if (viewport == null || content == null) return;
            float width = content.resolvedStyle.width, height = content.resolvedStyle.height;
            if (!(width > 0) || !(height > 0)) return;
            Vector2 offset = image != null ? image.layout.position : Vector2.zero;
            Vector3 position = evt.localPosition;
            // The native browser rejects mouse positions outside its image,
            // including release. Captured controller drags must still end.
            position.x = Mathf.Clamp(position.x, offset.x, offset.x + width);
            position.y = Mathf.Clamp(position.y, offset.y, offset.y + height);
            GameAdapter.Write(evt, "localPosition", position);
        }

        private static bool BeforeCameraPreview(object __instance)
        {
            VRManager manager = VRManager.Instance;
            if (manager == null || !manager.Ready) return true;
            if (!(GameAdapter.Read(__instance, "m_renderTexture") is RenderTexture texture)) return false;
            var capture = manager.Rig.GetComponent<Camera.VRDesktopCamera>()
                ?? manager.Rig.gameObject.AddComponent<Camera.VRDesktopCamera>();
            UnityEngine.Camera previous = GameAdapter.Read(__instance, "m_camera") as UnityEngine.Camera;
            UnityEngine.Camera preview = capture.Assign(__instance, texture);
            if (previous != null && previous != preview && previous.targetTexture == texture) previous.targetTexture = null;
            GameAdapter.Write(__instance, "m_camera", preview);
            return false;
        }

        private static bool BeforeNormalizedMouse(ref Vector2 __result)
        {
            VRManager manager = VRManager.Instance;
            if (manager == null || !manager.Interactive || manager.Pointer == null || !manager.Pointer.HasPanelHit) return true;
            __result = manager.Pointer.NormalizedPanelPosition;
            return false;
        }
    }
}
