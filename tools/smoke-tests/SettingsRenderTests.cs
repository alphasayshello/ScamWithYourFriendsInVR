using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using TMPro;
using UnityEngine.UIElements;
using System.Linq;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestSettingsRender()
        {
            var manager = VRManager.Instance;
            Require(manager?.Ready == true && manager.Rig.Head != null, "Settings capture needs the VR session");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = adapter.GetMethod("TogglePause", BindingFlags.NonPublic | BindingFlags.Static);
            var settings = Instance("SettingsUi") as Component;
            Require(settings != null, "Native settings instance missing");
            bool wasOpen = (bool)Member(settings, "IsOpen");
            GameObject cameraObject = null;
            RenderTexture texture = null;
            try
            {
                foreach (string method in new[] { "OnEnable", "OnDisable" })
                    patches.Patch(AccessTools.Method(Find("MainMenuUI"), method), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(TraceSettingsClose)));
                patches.Patch(AccessTools.Method(Find("MetaCursorNetDebug"), "CloseEscapeMenu"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(TraceSettingsClose)));
                foreach (Component component in UnityEngine.Object.FindObjectsByType(Find("PauseMenuToolkitController"), FindObjectsInactive.Include, FindObjectsSortMode.None))
                    Logger.LogInfo("SETTINGS TOOLKIT: " + component.name + "; active=" + component.gameObject.activeInHierarchy + "; enabled=" + ((Behaviour)component).enabled
                        + "; parent=" + component.transform.parent?.name + "; root=" + Member(component, "root"));
                yield return new WaitForSecondsRealtime(1);
                wasOpen = (bool)Member(settings, "IsOpen");
                if (!wasOpen) toggle.Invoke(null, null);
                Logger.LogInfo("SETTINGS OPEN: IsOpen=" + Member(settings, "IsOpen") + "; escape=" + Member(Instance("MetaCursorNetDebug"), "escapeMenuOpen"));
                manager.UI.Place();
                yield return new WaitForSecondsRealtime(1);
                Logger.LogInfo("SETTINGS AFTER FRAME: IsOpen=" + Member(settings, "IsOpen") + "; escape=" + Member(Instance("MetaCursorNetDebug"), "escapeMenuOpen"));
                Require(Member(settings, "IsOpen") is bool open && open, "Settings did not remain open");
                foreach (Component component in UnityEngine.Object.FindObjectsByType(Find("PauseMenuToolkitController"), FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var doc = component.GetComponent<UIDocument>();
                    if (doc?.rootVisualElement == null || !component.gameObject.activeInHierarchy) continue;
                    Logger.LogInfo("SETTINGS LEGACY GRAPHICS: " + settings.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Count(g => g.enabled));
                    var panelSurface = manager.UI.Panels.FirstOrDefault(p => p != null && p.Settings == doc.panelSettings);
                    if (panelSurface != null)
                    {
                        foreach (var tab in new[] { ("general", "general"), ("graphics", "graphics"), ("audio", "audio"),
                            ("voice-chat", "voice-chat"), ("host", "host-settings"), ("change-log", "change-log"), ("credits", "credits"), ("general", "general") })
                        {
                            Button control = doc.rootVisualElement.Q<Button>(tab.Item1 + "-tab-button");
                            Require(control != null, "Missing settings tab: " + tab.Item1);
                            var click = ClickAppControl(panelSurface, control);
                            try { while (click.MoveNext()) yield return click.Current; }
                            finally { (click as IDisposable)?.Dispose(); }
                            Require(doc.rootVisualElement.Q(tab.Item2 + "-screen").ClassListContains("settings-screen--active"), "Controller did not open settings tab: " + tab.Item1);
                        }
                        Logger.LogInfo("SETTINGS PASS: controller clicks switch all native settings tabs and return to general");
                        yield return new WaitForSecondsRealtime(.6f);
                        var vrOptions = doc.rootVisualElement.Q("swyf-vr-options");
                        Require(vrOptions != null && doc.rootVisualElement.Query<VisualElement>("swyf-vr-options").ToList().Count == 1,
                            "VR options missing or duplicated in native General settings");
                        bool originalDesktop = Plugin.PhysicalDesktop.Value;
                        bool originalSprint = Plugin.AutoSprint.Value;
                        float originalDistance = Plugin.PanelDistance.Value;
                        float originalSteering = Plugin.FlightSteeringSpeed.Value;
                        try
                        {
                            var sprintChange = ClickAppControl(panelSurface, vrOptions.Q<Button>("swyf-vr-auto-sprint"));
                            try { while (sprintChange.MoveNext()) yield return sprintChange.Current; }
                            finally { (sprintChange as IDisposable)?.Dispose(); }
                            Require(Plugin.AutoSprint.Value != originalSprint, "Controller did not change auto sprint in VR options");
                            sprintChange = ClickAppControl(panelSurface, vrOptions.Q<Button>("swyf-vr-auto-sprint"));
                            try { while (sprintChange.MoveNext()) yield return sprintChange.Current; }
                            finally { (sprintChange as IDisposable)?.Dispose(); }
                            Require(Plugin.AutoSprint.Value == originalSprint, "Controller did not restore auto sprint option");
                            foreach (string id in new[] { "swyf-vr-desktop", "swyf-vr-desktop", "swyf-vr-ui-distance", "swyf-vr-flight-steering" })
                            {
                                var change = ClickAppControl(panelSurface, vrOptions.Q<Button>(id));
                                try { while (change.MoveNext()) yield return change.Current; }
                                finally { (change as IDisposable)?.Dispose(); }
                            }
                            Require(Plugin.PhysicalDesktop.Value == originalDesktop && Mathf.Abs(Plugin.PanelDistance.Value - originalDistance) > .01f,
                                "VR settings buttons did not change and restore native configuration");
                            Require(Mathf.Abs(Plugin.FlightSteeringSpeed.Value - originalSteering) > .01f, "VR steering setting did not change through the controller");
                            Logger.LogInfo("SETTINGS PASS: VR options controller clicks change desktop placement, menu distance, auto sprint and flight steering without config editing");
                        }
                        finally { Plugin.AutoSprint.Value = originalSprint; Plugin.PhysicalDesktop.Value = originalDesktop; Plugin.PanelDistance.Value = originalDistance; Plugin.FlightSteeringSpeed.Value = originalSteering; manager.UI.Place(); }
                        Transform head = manager.Rig.Head.transform;
                        Vector3 headPosition = head.position;
                        Quaternion headRotation = head.rotation;
                        head.SetPositionAndRotation(headPosition + new Vector3(.2f, .1f, -.15f), headRotation * Quaternion.Euler(12, 45, 0));
                        typeof(UI.VRUIPatcher).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager.UI, null);
                        Require(Vector3.Distance(manager.UI.Anchor.position, head.position + head.forward * Plugin.PanelDistance.Value) < .001f
                            && Quaternion.Angle(manager.UI.Anchor.rotation, head.rotation) < .01f, "Floating HUD/menu did not follow headset pose");
                        head.SetPositionAndRotation(headPosition, headRotation);
                        typeof(UI.VRUIPatcher).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager.UI, null);
                        Logger.LogInfo("SETTINGS PASS: floating HUD/menu follows head rotation and translation at configured distance=" + Plugin.PanelDistance.Value);
                        yield return new WaitForSecondsRealtime(.7f);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        RenderTexture previousTarget = RenderTexture.active;
                        var pixels = new Texture2D(panelSurface.Texture.width, panelSurface.Texture.height, TextureFormat.RGBA32, false);
                        try
                        {
                            RenderTexture.active = panelSurface.Texture;
                            pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply();
                            Button general = doc.rootVisualElement.Q<Button>("general-tab-button");
                            Rect layout = panelSurface.Panel.visualTree.layout;
                            Vector2 sample = general.worldBound.position + Vector2.one * 8;
                            Color background = pixels.GetPixel(Mathf.RoundToInt(sample.x / layout.width * pixels.width),
                                Mathf.RoundToInt((1 - sample.y / layout.height) * pixels.height));
                            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(readyFile), "settings-panel.png"), pixels.EncodeToPNG());
                            Logger.LogInfo("SETTINGS PIXEL: " + background + "; resolved=" + general.resolvedStyle.backgroundColor + "; radius=" + general.resolvedStyle.borderTopLeftRadius);
                            Require(background.r < .85f || background.g < .85f || background.b < .85f, "Settings button still renders as an opaque white block");
                            Logger.LogInfo("SETTINGS PASS: rendered button background retains native colour; sample=" + background);
                            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(readyFile), "settings-panel.png"), pixels.EncodeToPNG());
                        }
                        finally { RenderTexture.active = previousTarget; Destroy(pixels); }
                    }
                    foreach (var button in doc.rootVisualElement.Query<Button>().ToList().Take(12))
                        Logger.LogInfo("SETTINGS BUTTON: " + button.name + "; text=" + button.text + "; color=" + button.resolvedStyle.color
                            + "; bg=" + button.resolvedStyle.backgroundColor + "; font=" + button.resolvedStyle.unityFontDefinition + "; size=" + button.resolvedStyle.fontSize
                            + "; image=" + button.resolvedStyle.backgroundImage + "; bounds=" + button.worldBound);
                    foreach (var button in doc.rootVisualElement.Query<Button>().ToList().Take(3))
                        Logger.LogInfo("SETTINGS RENDER FLAGS: " + button.name + "; hints=" + button.usageHints + "; texture=" + Member(button, "useRenderTexture")
                            + "; filter=" + Member(button.resolvedStyle, "filter"));
                }
                foreach (TMP_Text label in settings.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (!label.gameObject.activeInHierarchy) continue;
                    Material material = label.fontSharedMaterial;
                    Logger.LogInfo("SETTINGS TEXT: " + label.name + "; text=" + label.text.Replace('\n', ' ') + "; color=" + label.color
                        + "; shader=" + material?.shader?.name + "; face=" + (material != null && material.HasProperty("_FaceColor") ? material.GetColor("_FaceColor").ToString() : "none")
                        + "; size=" + label.fontSize + "; rect=" + label.rectTransform.rect + "; culled=" + label.canvasRenderer.cull + "; scale=" + label.transform.lossyScale);
                    Logger.LogInfo("SETTINGS FONT: " + label.text.Replace('\n', ' ') + "; atlas=" + label.font?.atlasTexture?.name
                        + "; texture=" + material?.mainTexture?.name + "; keywords=" + (material != null ? string.Join(",", material.shaderKeywords) : "none")
                        + "; channels=" + label.canvas.additionalShaderChannels);
                }
                cameraObject = new GameObject("Single VR settings render check");
                var capture = cameraObject.AddComponent<UnityEngine.Camera>();
                capture.CopyFrom(manager.Rig.Head);
                var data = cameraObject.AddComponent(Find("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData"));
                AccessTools.Property(data.GetType(), "allowXRRendering").SetValue(data, false);
                AccessTools.Property(data.GetType(), "renderPostProcessing").SetValue(data, false);
                texture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                texture.Create(); capture.targetTexture = texture;
                capture.transform.SetPositionAndRotation(manager.UI.Anchor.position - manager.UI.Anchor.forward * Plugin.PanelDistance.Value, manager.UI.Anchor.rotation);
                capture.fieldOfView = 65; capture.enabled = true;
                yield return null;
                yield return new WaitForEndOfFrame();
                var previous = RenderTexture.active;
                Texture2D image = null;
                try
                {
                    RenderTexture.active = texture;
                    image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
                    string path = Path.Combine(Path.GetDirectoryName(readyFile), "settings-render.png");
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    Logger.LogInfo("SETTINGS CAPTURE: " + path);
                }
                finally { RenderTexture.active = previous; if (image != null) Destroy(image); }
            }
            finally
            {
                if (cameraObject != null) Destroy(cameraObject);
                if (texture != null) { texture.Release(); Destroy(texture); }
                if (!wasOpen && Member(settings, "IsOpen") is bool open && open) toggle.Invoke(null, null);
            }
        }
        private static void TraceSettingsClose(object __instance, MethodBase __originalMethod)
        {
            Debug.Log("SETTINGS TRACE: " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name
                + "; object=" + (__instance as Component)?.name + "; stack=" + Environment.StackTrace);
        }
    }
}
