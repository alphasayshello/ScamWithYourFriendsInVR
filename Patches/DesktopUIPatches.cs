using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.Patches
{
    internal static class DesktopUIPatches
    {
        private static void AfterDocumentEnabled(UIDocument __instance)
        {
            var manager = VRManager.Instance;
            if (manager?.UI != null) manager.UI.RememberDocument(__instance);
        }

        internal static void Install(Harmony harmony)
        {
            try
            {
                var enable = AccessTools.Method(typeof(UIDocument), "OnEnable", Type.EmptyTypes);
                if (enable != null)
                {
                    harmony.Patch(enable, postfix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(AfterDocumentEnabled)));
                    VRManager.Instance?.UI?.UseDocumentEvents();
                }
                else Plugin.Log.LogWarning("UI document enable hook unavailable; discovery will use polling.");
            }
            catch (Exception ex) { GameAdapter.Report("UI document discovery hook", ex); }
            try
            {
                Type browser = ReflectionTypes.Find("Kolkata.UI.KolkataOsBrowserHost");
                var up = browser == null ? null : AccessTools.Method(browser, "HandlePointerUp");
                if (up != null) harmony.Patch(up, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforeBrowserPointerUp)));
                else Plugin.Log.LogWarning("Native browser release method unavailable; controller drag release compatibility unverified.");
                Type client = browser == null ? null : AccessTools.Field(browser, "browserClient")?.FieldType;
                var init = client == null ? null : AccessTools.Method(client, "Init", Type.EmptyTypes);
                if (init != null) harmony.Patch(init, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforeBrowserStartup)));
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
                Type zoomy = ReflectionTypes.Find("Kolkata.UI.KolkataOsZoomyApp");
                var webcam = zoomy == null ? null : AccessTools.Method(zoomy, "ResolveWebcamTexture");
                if (webcam != null) harmony.Patch(webcam, postfix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(AfterZoomyWebcam)));
                else Plugin.Log.LogWarning("Native meeting webcam method unavailable; Zoomy capture visibility unverified.");
            }
            catch (Exception ex) { GameAdapter.Report("Meeting webcam capture patch", ex); }
            try
            {
                Type zoomy = ReflectionTypes.Find("Kolkata.UI.KolkataOsZoomyApp");
                var build = zoomy == null ? null : AccessTools.Method(zoomy, "BuildContent");
                var dispose = zoomy == null ? null : AccessTools.Method(zoomy, "Dispose");
                if (build != null && dispose != null)
                {
                    harmony.Patch(build, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforeZoomyContent)));
                    harmony.Patch(dispose, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforeZoomyDispose)));
                }
                else Plugin.Log.LogWarning("Native meeting lifecycle methods unavailable; Zoomy rebuild subscriptions unverified.");
            }
            catch (Exception ex) { GameAdapter.Report("Meeting content lifecycle patch", ex); }
            try
            {
                Type paint = ReflectionTypes.Find("Kolkata.UI.KolkataOsPaintApp");
                var rail = paint == null ? null : AccessTools.Method(paint, "BuildBrushRail");
                if (rail != null) harmony.Patch(rail, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforePaintBrushRail)));
                else Plugin.Log.LogWarning("Native brush rail unavailable; paint content recreation unverified.");
            }
            catch (Exception ex) { GameAdapter.Report("Paint content lifecycle patch", ex); }
            try
            {
                Type app = ReflectionTypes.Find("Kolkata.UI.KolkataOsBanditCamApp");
                var build = app == null ? null : AccessTools.Method(app, "BuildPlaybackControls");
                if (build != null) harmony.Patch(build, postfix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(AfterPlaybackControls)));
                else Plugin.Log.LogWarning("Native playback controls unavailable; controller seek compatibility unverified.");
            }
            catch (Exception ex) { GameAdapter.Report("Desktop playback seek patch", ex); }
            try
            {
                Type app = ReflectionTypes.Find("Kolkata.UI.KolkataOsAnyViewerApp");
                var build = app == null ? null : AccessTools.Method(app, "BuildAudioPlayer");
                if (build != null) harmony.Patch(build, postfix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(AfterRemoteAudioControls)));
                else Plugin.Log.LogWarning("Native remote audio controls unavailable; controller layout compatibility unverified.");
                var create = app == null ? null : AccessTools.Method(app, "CreateRemoteApp");
                if (create != null) harmony.Patch(create, postfix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(AfterRemoteAppCreated)));
                else Plugin.Log.LogWarning("Native remote window creation unavailable; controller window bounds compatibility unverified.");
                if (app != null) PatchRemoteAudioClock(harmony, app);
            }
            catch (Exception ex) { GameAdapter.Report("Remote audio layout patch", ex); }
            try
            {
                Type type = ReflectionTypes.Find("MonitorInteractable");
                var method = type == null ? null : AccessTools.Method(type, "GetNormalizedMousePosition", Type.EmptyTypes);
                if (method == null) { Plugin.Log.LogWarning("Monitor cursor method unavailable; animation cursor remains native."); return; }
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(DesktopUIPatches), nameof(BeforeNormalizedMouse)));
            }
            catch (Exception ex) { GameAdapter.Report("Monitor cursor patch", ex); }
        }

        private static void AfterRemoteAppCreated(object __instance, object __0)
        {
            if (VRManager.Instance == null || !VRManager.Instance.Ready) return;
            string id = GameAdapter.Read(__0, "id") as string;
            var apps = GameAdapter.Read(__instance, "m_appsById") as IDictionary;
            var window = id == null ? null : GameAdapter.Read(apps?[id], "Window") as VisualElement;
            var layer = GameAdapter.Read(__instance, "m_windowLayer") as VisualElement;
            if (window == null || layer == null) return;
            void Fit()
            {
                if (VRManager.Instance == null || !VRManager.Instance.Ready || window.panel == null || layer.panel != window.panel || !window.visible
                    || window.resolvedStyle.display == DisplayStyle.None) return;
                float width = layer.layout.width - 8, height = layer.layout.height - 8;
                if (!(width > 100) || !(height > 100)) return;
                // Native app dimensions assume a larger remote desktop. Keep
                // titlebars and bottom controls above its taskbar after opening,
                // restoring or resizing the enclosing AnyViewer window.
                if (Mathf.Abs(window.style.maxWidth.value.value - width) > .25f) window.style.maxWidth = width;
                if (Mathf.Abs(window.style.maxHeight.value.value - height) > .25f) window.style.maxHeight = height;
                float left = window.resolvedStyle.left, top = window.resolvedStyle.top;
                float x = Mathf.Clamp(left, 4, Mathf.Max(4, layer.layout.width - Mathf.Min(window.layout.width, width) - 4));
                float y = Mathf.Clamp(top, 4, Mathf.Max(4, layer.layout.height - Mathf.Min(window.layout.height, height) - 4));
                if (!float.IsNaN(x) && Mathf.Abs(x - left) > .25f) window.style.left = x;
                if (!float.IsNaN(y) && Mathf.Abs(y - top) > .25f) window.style.top = y;
            }
            window.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            layer.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            Fit();
        }

        private static void PatchRemoteAudioClock(Harmony harmony, Type app)
        {
            bool patched = false;
            foreach (Type closure in app.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var method in closure.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (!method.Name.Contains("<BuildAudioPlayer>") || method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(DesktopUIPatches), nameof(RemoteAudioClock)));
                    patched = true;
                }
            if (!patched) Plugin.Log.LogWarning("Native remote audio clock callback unavailable; empty-clip compatibility unverified.");
        }

        private static IEnumerable<CodeInstruction> RemoteAudioClock(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(AudioSource), "time");
            var safe = AccessTools.Method(typeof(DesktopUIPatches), nameof(ReadRemoteAudioTime));
            bool replaced = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter)) { instruction.opcode = OpCodes.Call; instruction.operand = safe; replaced = true; }
                yield return instruction;
            }
            if (!replaced) Plugin.Log.LogWarning("Native remote audio clock getter changed; empty-clip compatibility unverified.");
        }

        // The closed native audio app still updates its elapsed label. Unity's
        // empty-source time getter emits warnings each tick; its intended value
        // remains zero until a clip is loaded. Loaded playback stays native.
        private static float ReadRemoteAudioTime(AudioSource source) => source != null && source.clip != null ? source.time : 0;

        private static void AfterRemoteAudioControls(object __0)
        {
            if (VRManager.Instance == null || !VRManager.Instance.Ready) return;
            var content = GameAdapter.Read(__0, "WindowContent") as VisualElement;
            var controls = content?.Q<VisualElement>(className: "kolkata-rdp-media--audio");
            if (controls == null || controls.parent != content) return;
            // The native fixed-height audio window clips its volume row. Keep
            // the original controls and timers, with controller scrolling when
            // their minimum content height exceeds the window's client area.
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "swyf-remote-audio-scroll" };
            scroll.style.flexGrow = 1; scroll.style.flexShrink = 1; scroll.style.minHeight = 0;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            controls.style.height = StyleKeyword.Auto;
            controls.style.flexGrow = 0; controls.style.flexShrink = 0;
            controls.RemoveFromHierarchy(); scroll.Add(controls); content.Add(scroll);
        }

        private static void AfterPlaybackControls(object __instance)
        {
            if (!(GameAdapter.Read(__instance, "m_playbackSeek") is Slider seek)) return;
            bool Controller(int pointerId) => pointerId == UI.VRPointer.ToolkitPointerId
                && VRManager.Instance != null && VRManager.Instance.Ready;
            // The slider handle consumes pointer-down before bubble callbacks on
            // the slider. Mark seeking first so its refresh timer cannot fight a drag.
            seek.RegisterCallback<PointerDownEvent>(evt =>
            { if (Controller(evt.pointerId)) GameAdapter.Write(__instance, "m_seekingPlayback", true); }, TrickleDown.TrickleDown);
            seek.RegisterCallback<PointerUpEvent>(evt =>
            { if (evt.pointerId == UI.VRPointer.ToolkitPointerId) GameAdapter.Write(__instance, "m_seekingPlayback", false); }, TrickleDown.TrickleDown);
            seek.RegisterCallback<PointerCaptureOutEvent>(evt =>
            { if (evt.pointerId == UI.VRPointer.ToolkitPointerId) GameAdapter.Write(__instance, "m_seekingPlayback", false); }, TrickleDown.TrickleDown);
        }

        private static void BeforeBrowserStartup(object __instance)
        {
            if (VRManager.Instance == null || !VRManager.Instance.Ready) return;
            object os = GameAdapter.Read(GameAdapter.Static("KolkataOsSingleton", "Instance"), "Controller");
            object host = GameAdapter.Read(os, "m_browserHost");
            if (ReferenceEquals(GameAdapter.Read(host, "browserClient"), __instance)
                && GameAdapter.Read(__instance, "engineStartupTimeout") is int timeout && timeout < 20000)
                GameAdapter.Write(__instance, "engineStartupTimeout", 20000);
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

        private static void AfterZoomyWebcam(object __instance, object __0, Texture __result)
        {
            VRManager manager = VRManager.Instance;
            if (manager == null || !manager.Ready || manager.Rig == null || !(__result is RenderTexture texture)) return;
            var feeds = GameAdapter.Read(__instance, "m_webcamFeeds") as System.Collections.IDictionary;
            object id = GameAdapter.Read(__0, "netId");
            if (feeds == null || id == null || !feeds.Contains(id)) return;
            var camera = GameAdapter.Read(feeds[id], "Camera") as UnityEngine.Camera;
            if (camera != null && camera.targetTexture == texture)
                manager.Rig.GetComponent<Camera.VRDesktopCamera>()?.RegisterCapture(camera, true);
        }

        private static void BeforeZoomyContent(object __instance)
        {
            if (Plugin.Mode?.Value != Plugin.PlayerMode.VR) return;
            DetachZoomyHandlers(__instance);
            (GameAdapter.Read(__instance, "m_tickSchedule") as IVisualElementScheduledItem)?.Pause();
            (GameAdapter.Read(__instance, "m_tooltip") as IDisposable)?.Dispose();
        }

        private static void BeforeZoomyDispose(object __instance)
        {
            if (Plugin.Mode?.Value == Plugin.PlayerMode.VR) DetachZoomyHandlers(__instance);
        }

        private static void BeforePaintBrushRail(object __instance)
        {
            if (Plugin.Mode?.Value != Plugin.PlayerMode.VR) return;
            // The native builder adds every brush again after replacing the
            // window content. Discard its detached button references only.
            (GameAdapter.Read(__instance, "m_brushButtons") as System.Collections.IDictionary)?.Clear();
        }

        private static void DetachZoomyHandlers(object owner)
        {
            Type participant = ReflectionTypes.Find("KolkataZoomyPlayer");
            if (participant == null || owner == null) return;
            foreach (string name in new[] { "ParticipantsChanged", "ChatMessageReceived" })
            {
                var field = AccessTools.Field(participant, name);
                var changed = participant.GetEvent(name, BindingFlags.Public | BindingFlags.Static);
                if (!(field?.GetValue(null) is Delegate handlers) || changed == null) continue;
                string method = name == "ParticipantsChanged" ? "OnParticipantsChanged" : "OnChatMessage";
                foreach (Delegate handler in handlers.GetInvocationList())
                    if (ReferenceEquals(handler.Target, owner) && handler.Method.Name == method)
                        changed.RemoveEventHandler(null, handler);
            }
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
