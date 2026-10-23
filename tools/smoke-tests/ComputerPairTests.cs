using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object Member(object owner, string name)
        {
            if (owner == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(owner);
                var field = type.GetField(name, flags);
                if (field != null) return field.GetValue(owner);
            }
            return null;
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        private static bool MonitorCaptured() => Find("KolkataMonitorInputState")?.GetProperty("IsMonitorInputCaptured")?.GetValue(null) is bool captured && captured;

        private IEnumerator TestPreviewRate(UnityEngine.Camera preview, UnityEngine.Camera head)
        {
            int savedRate = Plugin.DesktopCameraFPS.Value;
            int previewFrames = 0, headFrames = 0;
            void Rendered(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Camera camera)
            {
                if (camera == preview) previewFrames++;
                if (camera == head) headFrames++;
            }
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += Rendered;
            try
            {
                Plugin.DesktopCameraFPS.Value = 5;
                float started = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - started < 2)
                    yield return null;
                float elapsed = Time.realtimeSinceStartup - started;
                Require(previewFrames >= 2 && previewFrames <= Mathf.CeilToInt(elapsed * 5) + 1,
                    "Preview render callback count did not respect the configured cap: " + previewFrames + " in " + elapsed);
                Require(headFrames > previewFrames, "Headset rendering did not continue independently of the preview cap");
                Require(head.targetTexture == null && preview.targetTexture != null, "Preview throttling changed headset/preview outputs");
                Logger.LogInfo("COMPUTER PASS: actual preview renders capped independently of headset; preview=" + previewFrames
                    + "; headset=" + headFrames + "; seconds=" + elapsed + "; configuredFPS=5");
            }
            finally
            {
                UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= Rendered;
                Plugin.DesktopCameraFPS.Value = savedRate;
            }
        }

        private IEnumerator TestComputer()
        {
            float deadline = Time.realtimeSinceStartup + 90;
            while (!NetworkClient.ready || NetworkClient.localPlayer == null || VRManager.Instance == null || !VRManager.Instance.Ready
                || Instance("MainMenuSingleton") is Behaviour menu && menu.isActiveAndEnabled
                || Find("NetworkingInitializer")?.GetProperty("IsNetworkTransitionInProgress")?.GetValue(null) is bool transition && transition)
            {
                Require(Time.realtimeSinceStartup < deadline, "Client/VR session did not become ready");
                yield return null;
            }
            yield return new WaitForSecondsRealtime(3);
            Require(NetworkManager.singleton.networkAddress == "127.0.0.1", "Computer test requires the local pair transport");
            Require(Find("UnityEngine.XR.OpenXR.OpenXRRuntime")?.GetProperty("name")?.GetValue(null) as string == "Unity Mock Runtime", "Computer simulation requires Unity Mock Runtime");
            testingComputer = true;
            object desktop = null;
            bool savedPhysicalDesktop = Plugin.PhysicalDesktop.Value;
            try
            {
                Plugin.PhysicalDesktop.Value = true;
                var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
                var type = Find("MonitorInteractable");
                var workspaceType = Find("ScammerWorkspace");
                var sameFloor = AccessTools.Method(workspaceType, "IsOnSameFloor");
                var floorMonitors = UnityEngine.Object.FindObjectsByType(workspaceType, FindObjectsSortMode.None).Cast<Component>()
                    .Where(workspace => sameFloor.Invoke(workspace, new object[] { interactor.transform.position }) is bool same && same)
                    .Select(workspace => Member(Member(workspace, "callStateScreen"), "MonitorInteractable") as Component).Where(m => m != null).ToArray();
                Component monitor = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None).Cast<Component>()
                    .Where(candidate => Member(candidate, "UsesLaptopDesktop") is bool laptop && !laptop
                        && Member(candidate, "chairInteractable") is object chair
                        && Member(chair, "Networksitter") == null
                        && floorMonitors.Contains(candidate)
                        && AccessTools.Method(type, "CanInteract").Invoke(candidate, new object[] { interactor }) is bool allowed && allowed)
                    .OrderBy(candidate => (candidate.transform.position - interactor.transform.position).sqrMagnitude).FirstOrDefault();
                Require(monitor != null, "No free, usable monitor found");
                var controller = Instance("Metater.MetaPlayerController");
                AccessTools.Method(controller.GetType(), "Teleport").Invoke(controller, new object[] { monitor.transform.position - monitor.transform.forward, true });
                AccessTools.Method(type, "CmdInteract").Invoke(monitor, new object[] { interactor });
                deadline = Time.realtimeSinceStartup + 30;
                while (!MonitorCaptured())
                {
                    Require(Time.realtimeSinceStartup < deadline, "Native seated monitor did not open");
                    yield return null;
                }
                desktop = Instance("KolkataOsSingleton");
                var geometryRoot = monitor.transform.parent ?? monitor.transform;
                Logger.LogInfo("MONITOR geometry: interactable=" + monitor.name + "; parent=" + geometryRoot.name
                    + "; idleLabel=" + Member(monitor, "idleScreenLabel") + "; active=" + Member(desktop, "activeLocalMonitor"));
                foreach (var renderer in geometryRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    Logger.LogInfo("MONITOR mesh: name=" + renderer.name + "; parent=" + renderer.transform.parent?.name
                        + "; position=" + renderer.transform.position + "; rotation=" + renderer.transform.eulerAngles
                        + "; scale=" + renderer.transform.lossyScale + "; bounds=" + renderer.localBounds
                        + "; mesh=" + mesh?.name + "; readable=" + mesh?.isReadable + "; vertices=" + mesh?.vertexCount
                        + "; materials=" + string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "null" : m.name + ":" + m.shader?.name)));
                }
                object os = Member(desktop, "Controller");
                Require(os is Component, "Native desktop controller missing");
                deadline = Time.realtimeSinceStartup + 15;
                while (!(Member(desktop, "IsOpenAnimationComplete") is bool open && open)
                    || !(Member(os, "m_useLaptopDesktop") is bool laptopMode && !laptopMode))
                {
                    Require(Time.realtimeSinceStartup < deadline, "Native workstation opening/binding did not complete");
                    yield return null;
                }
                var document = ((Component)os).GetComponent<UIDocument>();
                var manager = VRManager.Instance;
                VRPanelSurface surface = null;
                deadline = Time.realtimeSinceStartup + 5;
                while (surface == null)
                {
                    surface = manager.UI.Panels.FirstOrDefault(p => p != null && p.Settings == document.panelSettings && p.Panel != null);
                    Require(Time.realtimeSinceStartup < deadline, "Native desktop was not converted to a VR surface");
                    yield return null;
                }
                IDictionary apps;
                deadline = Time.realtimeSinceStartup + 25;
                while ((apps = Member(os, "m_appsById") as IDictionary) == null || !(Member(os, "m_desktopInitialized") is bool initialized && initialized))
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        Logger.LogInfo("COMPUTER diagnostics: initialized=" + Member(os, "m_desktopInitialized")
                            + "; controller=" + os.GetType().FullName + "; apps=" + (apps?.Count ?? -1)
                            + "; keys=" + (apps == null ? "missing" : string.Join(",", apps.Keys.Cast<object>())));
                        Require(false, "Native desktop apps did not finish loading");
                    }
                    yield return null;
                }
                deadline = Time.realtimeSinceStartup + 8;
                while (!surface.MonitorAnchored)
                {
                    Require(Time.realtimeSinceStartup < deadline, "Active computer desktop did not anchor to its authored screen");
                    yield return null;
                }
                var workspace = Member(desktop, "boundWorkspace") as Component;
                Transform keyboardModel = AccessTools.Method(type, "GetKeyboardTarget").Invoke(monitor, null) as Transform;
                Logger.LogInfo("DESK keyboard model: " + keyboardModel?.name + "; position=" + keyboardModel?.position + "; rotation=" + keyboardModel?.eulerAngles);
                if (keyboardModel != null)
                    foreach (Transform part in keyboardModel.GetComponentsInChildren<Transform>(true))
                        Logger.LogInfo("DESK keyboard part: " + part.name + "; local=" + part.localPosition + "; scale=" + part.lossyScale);
                foreach (Transform part in monitor.transform.parent.GetComponentsInChildren<Transform>(true))
                    if (part.name.ToLowerInvariant().Contains("key"))
                        Logger.LogInfo("DESK named part: " + part.name + "; active=" + part.gameObject.activeInHierarchy + "; components="
                            + string.Join(",", part.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name))
                            + "; position=" + part.position + "; rotation=" + part.eulerAngles + "; scale=" + part.lossyScale
                            + "; meshBounds=" + part.GetComponent<MeshFilter>()?.sharedMesh?.bounds + "; collider=" + part.GetComponent<BoxCollider>()?.size);
                foreach (Renderer renderer in monitor.transform.parent.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.ToLowerInvariant().Contains("key") || renderer.name.ToLowerInvariant().Contains("mouse"))
                        Logger.LogInfo("DESK renderer: " + renderer.name + "; position=" + renderer.transform.position + "; rotation=" + renderer.transform.eulerAngles
                            + "; bounds=" + renderer.bounds + "; mesh=" + renderer.GetComponent<MeshFilter>()?.sharedMesh?.bounds);
                var stream = workspace.GetComponent(Find("ScammerWorkspaceWebRtc"));
                var nativeScreen = Member(stream, "monitorRenderer") as MeshRenderer;
                Require(nativeScreen != null, "Native monitor display geometry missing");
                Mesh screenMesh = nativeScreen.GetComponent<MeshFilter>().sharedMesh;
                Vector3 nativeCenter = nativeScreen.transform.TransformPoint(screenMesh.bounds.center);
                Require(Vector3.Distance(surface.transform.position, nativeCenter) < .015f,
                    "Desktop placement does not match the authored monitor center");
                Require(Mathf.Abs(surface.transform.lossyScale.x / surface.transform.lossyScale.y - (float)surface.Texture.width / surface.Texture.height) < .01f,
                    "Physical desktop stretched its render texture");
                Require(surface.transform.lossyScale.x <= nativeScreen.transform.TransformVector(Vector3.right * screenMesh.bounds.size.x).magnitude + .005f
                    && surface.transform.lossyScale.y <= nativeScreen.transform.TransformVector(Vector3.up * screenMesh.bounds.size.y).magnitude + .005f,
                    "Physical desktop extends beyond the native monitor screen");
                Require(surface.GetComponent<MeshRenderer>().sharedMaterial.GetInt("unity_GUIZTestMode") == 4,
                    "Physical desktop bypasses world depth testing");
                Require(!surface.Intersect(new Ray(surface.transform.position + surface.transform.forward * .3f, -surface.transform.forward), out _, out _, out _),
                    "Physical desktop accepts pointer input through the back of the monitor");
                var placement = typeof(VRPanelSurface).GetMethod("UpdatePlacement", BindingFlags.Instance | BindingFlags.NonPublic);
                bool sourceActive = nativeScreen.gameObject.activeSelf;
                Material sourceMaterial = nativeScreen.sharedMaterial;
                try
                {
                    Plugin.PhysicalDesktop.Value = false; placement.Invoke(surface, null);
                    Require(!surface.MonitorAnchored, "Floating desktop option did not release monitor placement");
                    Plugin.PhysicalDesktop.Value = true; placement.Invoke(surface, null);
                    Require(surface.MonitorAnchored && nativeScreen.gameObject.activeSelf == sourceActive && nativeScreen.sharedMaterial == sourceMaterial,
                        "Desktop placement changed native stream renderer visibility/material ownership");
                    manager.UI.Place(); placement.Invoke(surface, null);
                    Require(Vector3.Distance(surface.transform.position, nativeCenter) < .015f, "Recenter moved the desktop away from the physical monitor");
                    try
                    {
                        SetMockHeadPose(new Vector3(.3f, 1.5f, .1f), Quaternion.Euler(0, 40, 0));
                        for (int frame = 0; frame < 3; frame++) yield return new WaitForEndOfFrame();
                        Require(surface.MonitorAnchored && Vector3.Distance(surface.transform.position, nativeCenter) < .015f,
                            "Physical head movement or rotation detached the desktop from its desk");
                    }
                    finally { SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity); }
                }
                finally { Plugin.PhysicalDesktop.Value = true; }
                Vector3 savedPosition = nativeScreen.transform.position;
                Quaternion savedRotation = nativeScreen.transform.rotation;
                GameObject replacement = null;
                var screenField = AccessTools.Field(stream.GetType(), "monitorRenderer");
                try
                {
                    nativeScreen.transform.SetPositionAndRotation(savedPosition + new Vector3(.12f, .08f, -.04f),
                        Quaternion.AngleAxis(18, Vector3.up) * savedRotation);
                    placement.Invoke(surface, null);
                    Vector3 movedCenter = nativeScreen.transform.TransformPoint(screenMesh.bounds.center);
                    Require(Vector3.Distance(surface.transform.position, movedCenter) < .015f,
                        "Desktop did not follow the moved native screen");
                    Vector3 movedNormal = Vector3.Cross(nativeScreen.transform.TransformVector(Vector3.right),
                        nativeScreen.transform.TransformVector(Vector3.up)).normalized;
                    Require(Vector3.Dot(surface.transform.forward, movedNormal) > .999f,
                        "Desktop did not follow the rotated native screen");
                    replacement = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    replacement.name = "SWYF temporary screen rebind fixture";
                    replacement.transform.SetPositionAndRotation(savedPosition + Vector3.up * .15f, savedRotation);
                    replacement.transform.localScale = nativeScreen.transform.lossyScale;
                    var replacementRenderer = replacement.GetComponent<MeshRenderer>();
                    replacementRenderer.enabled = false;
                    screenField.SetValue(stream, replacementRenderer); placement.Invoke(surface, null);
                    Require(surface.MonitorAnchored && Vector3.Distance(surface.transform.position, replacement.transform.position) < .015f,
                        "Desktop retained a stale screen after the native presenter rebound");
                    replacement.SetActive(false); placement.Invoke(surface, null);
                    Require(surface.MonitorAnchored && Vector3.Distance(surface.transform.position, replacement.transform.position) < .015f,
                        "Hiding the streamed presenter detached the local desktop from its screen geometry");
                    screenField.SetValue(stream, null); placement.Invoke(surface, null);
                    Require(!surface.MonitorAnchored, "Missing screen retained a stale monitor attachment");
                    Require(!surface.GetComponent<MeshRenderer>().enabled && !surface.Intersect(new Ray(manager.Rig.Head.transform.position,
                        surface.transform.position - manager.Rig.Head.transform.position), out _, out _, out _),
                        "Unavailable native desk geometry left a floating/clickable computer overlay");
                    Plugin.PhysicalDesktop.Value = false; placement.Invoke(surface, null);
                    Require(surface.GetComponent<MeshRenderer>().enabled && !surface.MonitorAnchored,
                        "Explicit floating desktop could not recover while native screen geometry was unavailable");
                    Plugin.PhysicalDesktop.Value = true; placement.Invoke(surface, null);
                    Require(!surface.GetComponent<MeshRenderer>().enabled, "Returning to desk mode retained the floating fallback");
                    screenField.SetValue(stream, nativeScreen); placement.Invoke(surface, null);
                    Require(surface.MonitorAnchored && surface.GetComponent<MeshRenderer>().enabled && Vector3.Distance(surface.transform.position, movedCenter) < .015f,
                        "Desktop did not reacquire the native monitor after rebinding");
                }
                finally
                {
                    screenField.SetValue(stream, nativeScreen);
                    nativeScreen.transform.SetPositionAndRotation(savedPosition, savedRotation);
                    placement.Invoke(surface, null);
                    if (replacement != null) Destroy(replacement);
                }
                Logger.LogInfo("COMPUTER PASS: desktop fits authored monitor, preserves aspect, survives recenter and supports floating fallback without changing native stream renderer");
                Logger.LogInfo("COMPUTER PASS: desktop stays at the desk during physical head movement, follows screen movement/rebinding, and waits invisibly without clicks when native geometry is missing");
                Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule", false)?.GetMethod("CaptureScreenshot", new[] { typeof(string), typeof(int) })?
                    .Invoke(null, new object[] { System.IO.Path.Combine(System.IO.Path.GetDirectoryName(readyFile), "pair-computer-view.png"), 1 });
                yield return new WaitForEndOfFrame();
                RenderTexture savedTarget = RenderTexture.active;
                Texture2D image = null;
                try
                {
                    RenderTexture.active = surface.Texture;
                    image = new Texture2D(surface.Texture.width, surface.Texture.height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(readyFile), "pair-desktop-texture.png"), image.EncodeToPNG());
                }
                finally { RenderTexture.active = savedTarget; if (image != null) Destroy(image); }
                string appId = "checklist";
                object paint = null;
                deadline = Time.realtimeSinceStartup + 10;
                while (true)
                {
                    // Binding the workstation can rebuild the native app table
                    // after initialization; do not hold an old laptop entry.
                    apps = Member(os, "m_appsById") as IDictionary;
                    appId = new[] { "jw-paint", "phone", "checklist", "dump-dial", "browser" }.FirstOrDefault(id => apps != null && apps.Contains(id)
                        && Member(apps[id], "IsAvailable") is bool availableApp && availableApp
                        && Member(apps[id], "DesktopButton") is Button icon && icon.panel != null && icon.worldBound.width > 1 && icon.worldBound.height > 1) ?? "phone";
                    paint = apps != null && apps.Contains(appId) ? apps[appId] : null;
                    if (paint != null && Member(paint, "IsAvailable") is bool available && available
                        && Member(paint, "DesktopButton") is Button readyIcon && readyIcon.panel != null && readyIcon.worldBound.width > 1 && readyIcon.worldBound.height > 1) break;
                    Require(Time.realtimeSinceStartup < deadline, "Native " + appId + " app unavailable after workstation binding; available="
                        + (apps == null ? "missing" : string.Join(",", apps.Keys.Cast<object>().Where(key => Member(apps[key], "IsAvailable") is bool value && value))));
                    yield return null;
                }
                if (appId != "jw-paint") Logger.LogInfo("COMPUTER INFO: paint is gated by the game's application license; testing the available native "
                    + appId + " window. The separate paint fixture checks drawing without changing saved licences.");
                var button = Member(paint, "DesktopButton") as Button;
                Require(button != null && button.panel != null, "Native " + appId + " desktop button missing");
                deadline = Time.realtimeSinceStartup + 15;
                while (button.worldBound.width < 1 || button.worldBound.height < 1)
                {
                    Require(Time.realtimeSinceStartup < deadline, "Native desktop icon never became visible: " + button.name);
                    yield return null;
                }
                manager.UI.Place();
                yield return null;
                Logger.LogInfo("COMPUTER view: head=" + manager.Rig.Head.transform.position + "; authoredEye=" + ((Member(controller, "cameraTransform") as Transform)?.position.ToString() ?? "missing")
                    + "; firstPersonEye=" + ((Member(controller, "defaultCamTransform") as Transform)?.position.ToString() ?? "missing")
                    + "; obstructed=" + manager.Rig.HeadObstructed + "; cullingMask=" + manager.Rig.Head.cullingMask
                    + "; panel=" + surface.transform.position + "; rootOpacity=" + document.rootVisualElement.resolvedStyle.opacity);
                Vector2 center = button.worldBound.center;
                Rect layout = surface.Panel.visualTree.layout;
                Vector2 uv = new Vector2(center.x / layout.width, 1 - center.y / layout.height);
                Vector3 point = surface.transform.TransformPoint(new Vector3(uv.x - 0.5f, uv.y - 0.5f, 0));
                manager.Rig.Right.Aim.SetPositionAndRotation(point - surface.transform.forward * 0.4f, surface.transform.rotation);
                var inputType = manager.Input.GetType();
                var pointerTick = typeof(VRPointer).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                void Set(string name, bool value) => inputType.GetProperty(name).SetValue(manager.Input, value);
                // Native desktop icons open on a double-click, unlike taskbar buttons.
                yield return new WaitForEndOfFrame();
                manager.Rig.Right.Aim.SetPositionAndRotation(point - surface.transform.forward * .4f, surface.transform.rotation);
                typeof(VRPointer).GetMethod("ResetAim", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager.Pointer, null);
                Logger.LogInfo("COMPUTER click probe: expected=" + button.name + "; bounds=" + button.worldBound + "; uv=" + uv
                    + "; directPick=" + surface.Pick(uv)?.name + "; interactive=" + manager.Interactive + "; right=" + manager.Rig.Right.IsTracked);
                for (int click = 0; click < 2; click++)
                {
                    Set("Trigger", true); Set("TriggerDown", true); Set("TriggerUp", false);
                    pointerTick.Invoke(manager.Pointer, null);
                    Logger.LogInfo("COMPUTER click recipient: " + Member(manager.Pointer, "pressedElement") + "; panel="
                        + (Member(manager.Pointer, "pressedPanel") as VRPanelSurface)?.name + "; world="
                        + (Member(manager.Pointer, "pressedWorldDocument") as UIDocument)?.name);
                    Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", true);
                    pointerTick.Invoke(manager.Pointer, null);
                    Set("TriggerUp", false);
                }
                Require(manager.Pointer.HasPanelHit, "Controller ray did not hit the native desktop");
                yield return null;
                Require(Member(paint, "State")?.ToString() == "OpenVisible", "Controller ray did not open the " + appId + " app");
                Logger.LogInfo("COMPUTER PASS: native monitor seating/capture, VR surface and controller-ray " + appId + " app click");
                Require(!manager.Keyboard.Visible, "Pointer desktop unexpectedly opened a VR keyboard");
                foreach (var panel in manager.UI.Panels)
                    if (panel != null) Logger.LogInfo("COMPUTER panel: " + panel.name + "; document=" + panel.Document?.name
                        + "; active=" + (panel.Panel != null) + "; display=" + panel.Document?.rootVisualElement?.resolvedStyle.display
                        + "; order=" + panel.Settings.sortingOrder + "; originalTexture=" + typeof(VRPanelSurface).GetField("previous", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel));
                var surfaceRenderer = surface.GetComponent<MeshRenderer>();
                Require(manager.Rig.Head.targetTexture == null, "Native camera app redirected the headset to its preview texture");
                var preview = UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(FindObjectsSortMode.None)
                    .FirstOrDefault(c => c.name == "VR desktop camera preview");
                Require(preview != null && preview.targetTexture != null && preview != manager.Rig.Head, "Native camera app lacks an isolated preview camera");
                var cameraData = preview.GetComponent(Find("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData"));
                Require(Member(cameraData, "allowXRRendering") is bool allowXR && !allowXR, "Preview camera still participates in XR rendering");
                Require((preview.cullingMask & (1 << 5)) == 0, "Preview camera includes the floating desktop and creates recursive feedback");
                Logger.LogInfo("COMPUTER PASS: native camera preview isolated from headset output and floating UI");
                var rateTest = TestPreviewRate(preview, manager.Rig.Head);
                try
                {
                    while (rateTest.MoveNext()) yield return rateTest.Current;
                }
                finally { (rateTest as IDisposable)?.Dispose(); }
                foreach (var camera in UnityEngine.Camera.allCameras)
                    Logger.LogInfo("COMPUTER camera: " + camera.name + "; tracked=" + (camera == manager.Rig.Head)
                        + "; depth=" + camera.depth + "; target=" + camera.targetTexture?.name + "; mask=" + camera.cullingMask
                        + "; position=" + camera.transform.position + "; forward=" + camera.transform.forward + "; fov=" + camera.fieldOfView
                        + "; rect=" + camera.rect + "; type=" + camera.cameraType);
                Logger.LogInfo("COMPUTER surface: enabled=" + surfaceRenderer.enabled + "; visible=" + surfaceRenderer.isVisible
                    + "; bounds=" + surfaceRenderer.bounds + "; shader=" + surfaceRenderer.sharedMaterial.shader.name
                    + "; supported=" + surfaceRenderer.sharedMaterial.shader.isSupported + "; passes=" + surfaceRenderer.sharedMaterial.passCount
                    + "; keywords=" + string.Join(",", surfaceRenderer.sharedMaterial.shaderKeywords)
                    + "; scale=" + surface.transform.lossyScale + "; rt=" + surface.Texture.width + "x" + surface.Texture.height);
                foreach (var canvas in manager.UI.Canvases)
                    if (canvas != null) Logger.LogInfo("COMPUTER canvas: " + canvas.name + "; active=" + canvas.isActiveAndEnabled + "; parent=" + canvas.transform.parent?.name
                        + "; groups=" + string.Join(",", canvas.GetComponentsInParent<CanvasGroup>(true).Select(g => g.name + ":" + g.alpha)));
                yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForEndOfFrame();
                Type mirrorType = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.VRMirrorView");
                int adjusted = (int)mirrorType.GetProperty("AdjustedFrames", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Require(adjusted > 0, "Full-view mirror presentation was not exercised");
                Logger.LogInfo("COMPUTER PASS: full-view mirror presentation exercised; frames=" + adjusted);
                foreach (var display in GetDisplays())
                {
                    if (!display.running || display.GetRenderPassCount() == 0) continue;
                    display.GetRenderPass(0, out var pass);
                    pass.GetRenderParameter(manager.Rig.Head, 0, out var parameter);
                    Vector3 renderedEye = parameter.view.inverse.MultiplyPoint3x4(Vector3.zero);
                    Vector3 stereoEye = manager.Rig.Head.GetStereoViewMatrix(UnityEngine.Camera.StereoscopicEye.Left).inverse.MultiplyPoint3x4(Vector3.zero);
                    Vector3 rightEye = manager.Rig.Head.GetStereoViewMatrix(UnityEngine.Camera.StereoscopicEye.Right).inverse.MultiplyPoint3x4(Vector3.zero);
                    float ipd = Vector3.Distance(stereoEye, rightEye);
                    Require(Mathf.Abs(ipd - 0.064f) < 0.005f, "Mock eye separation was not applied: " + ipd);
                    Require(Mathf.Abs(parameter.projection.m02) < 0.001f && Mathf.Abs(parameter.projection.m12) < 0.001f,
                        "Mock symmetric eye projection was not applied");
                    Logger.LogInfo("COMPUTER PASS: mock stereo uses 64mm eye separation and symmetric projection; ipd=" + ipd);
                    Vector3 eyeForward = parameter.view.inverse.MultiplyVector(Vector3.back).normalized;
                    Logger.LogInfo("COMPUTER XR view: head=" + manager.Rig.Head.transform.position + "; eye=" + renderedEye + "; stereoEye=" + stereoEye
                        + "; headLocal=" + manager.Rig.Head.transform.localPosition + "; parent=" + manager.Rig.Head.transform.parent?.name
                        + "; origin=" + manager.Rig.Origin.position + "; headForward=" + manager.Rig.Head.transform.forward + "; eyeForward=" + eyeForward
                        + "; towardPanel=" + (surface.transform.position - renderedEye).normalized + "; headRotation=" + manager.Rig.Head.transform.eulerAngles);
                    foreach (int mode in new[] { UnityEngine.XR.XRMirrorViewBlitMode.LeftEye, UnityEngine.XR.XRMirrorViewBlitMode.RightEye })
                    {
                        if (!display.GetMirrorViewBlitDesc(null, out var mirror, mode) || mirror.blitParamsCount == 0)
                        {
                            Logger.LogInfo("COMPUTER XR capture unavailable: mode=" + mode);
                            continue;
                        }
                        mirror.GetBlitParameter(0, out var blit);
                        RenderTexture source = blit.srcTex;
                        if (source == null || source.dimension != UnityEngine.Rendering.TextureDimension.Tex2D)
                        {
                            Logger.LogInfo("COMPUTER XR capture unsupported source: mode=" + mode + "; dimension=" + source?.dimension);
                            continue;
                        }
                        RenderTexture oldTarget = RenderTexture.active;
                        Texture2D eyeImage = null;
                        try
                        {
                            RenderTexture.active = source;
                            eyeImage = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                            eyeImage.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); eyeImage.Apply();
                            string eye = mode == UnityEngine.XR.XRMirrorViewBlitMode.LeftEye ? "left" : "right";
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(readyFile), "pair-computer-eye-" + eye + ".png"), eyeImage.EncodeToPNG());
                            Logger.LogInfo("COMPUTER XR texture: eye=" + eye + "; size=" + source.width + "x" + source.height + "; srcRect=" + blit.srcRect + "; destRect=" + blit.destRect + "; nativeBlit=" + mirror.nativeBlitAvailable);
                        }
                        finally { RenderTexture.active = oldTarget; if (eyeImage != null) Destroy(eyeImage); }
                    }
                    break;
                }
                string openView = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(readyFile), "pair-computer-open-view.png");
                if (System.IO.File.Exists(openView)) System.IO.File.Delete(openView);
                Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule", false)?.GetMethod("CaptureScreenshot", new[] { typeof(string), typeof(int) })?
                    .Invoke(null, new object[] { openView, 1 });
                deadline = Time.realtimeSinceStartup + 5;
                while (!System.IO.File.Exists(openView))
                {
                    Require(Time.realtimeSinceStartup < deadline, "Post-click camera screenshot was not saved");
                    yield return null;
                }
                // CaptureScreenshot is deferred. Keep the computer open until the
                // image is saved, instead of closing it before the capture executes.
                yield return new WaitForSecondsRealtime(0.5f);
                if (singleComputer)
                {
                    if (Environment.GetCommandLineArgs().Contains("--swyf-only-messenger-tests"))
                    {
                        var messenger = TestNativeMessenger(os, surface);
                        try { while (messenger.MoveNext()) yield return messenger.Current; }
                        finally { (messenger as IDisposable)?.Dispose(); }
                        yield break;
                    }
                    if (Environment.GetCommandLineArgs().Contains("--swyf-only-clicker-tests"))
                    {
                        var clicker = TestNativeClicker(os, surface);
                        try { while (clicker.MoveNext()) yield return clicker.Current; }
                        finally { (clicker as IDisposable)?.Dispose(); }
                        yield break;
                    }
                    if (Environment.GetCommandLineArgs().Contains("--swyf-only-zoomy-tests"))
                    {
                        var zoomy = TestNativeZoomy(os, surface);
                        try { while (zoomy.MoveNext()) yield return zoomy.Current; }
                        finally { (zoomy as IDisposable)?.Dispose(); }
                        yield break;
                    }
                    if (Environment.GetCommandLineArgs().Contains("--swyf-only-paint-tests"))
                    {
                        var focusedPaint = TestNativePaint(os, surface);
                        try { while (focusedPaint.MoveNext()) yield return focusedPaint.Current; }
                        finally { (focusedPaint as IDisposable)?.Dispose(); }
                        yield break;
                    }
                    if (singleScam)
                    {
                        var focusedScam = TestNativeCall(os, surface);
                        try { while (focusedScam.MoveNext()) yield return focusedScam.Current; }
                        finally { (focusedScam as IDisposable)?.Dispose(); }
                        yield break;
                    }
                    if (singleRecording)
                    {
                        var focusedRecording = TestNativeRecording(os, surface);
                        try { while (focusedRecording.MoveNext()) yield return focusedRecording.Current; }
                        finally { (focusedRecording as IDisposable)?.Dispose(); }
                        yield break;
                    }
                    var browserChecks = TestNativeBrowserScroll(os);
                    try { while (browserChecks.MoveNext()) yield return browserChecks.Current; }
                    finally { (browserChecks as IDisposable)?.Dispose(); }
                    var renderedBrowser = TestRenderedBrowser(os, surface);
                    try { while (renderedBrowser.MoveNext()) yield return renderedBrowser.Current; }
                    finally { (renderedBrowser as IDisposable)?.Dispose(); }
                    var paintChecks = TestNativePaint(os, surface);
                    try { while (paintChecks.MoveNext()) yield return paintChecks.Current; }
                    finally { (paintChecks as IDisposable)?.Dispose(); }
                    var cameraChecks = TestNativeCamera(os, surface);
                    try { while (cameraChecks.MoveNext()) yield return cameraChecks.Current; }
                    finally { (cameraChecks as IDisposable)?.Dispose(); }
                    var zoomyChecks = TestNativeZoomy(os, surface);
                    try { while (zoomyChecks.MoveNext()) yield return zoomyChecks.Current; }
                    finally { (zoomyChecks as IDisposable)?.Dispose(); }
                    var clickerChecks = TestNativeClicker(os, surface);
                    try { while (clickerChecks.MoveNext()) yield return clickerChecks.Current; }
                    finally { (clickerChecks as IDisposable)?.Dispose(); }
                    var messengerChecks = TestNativeMessenger(os, surface);
                    try { while (messengerChecks.MoveNext()) yield return messengerChecks.Current; }
                    finally { (messengerChecks as IDisposable)?.Dispose(); }
                    var storeChecks = TestNativeStore(os, surface);
                    try { while (storeChecks.MoveNext()) yield return storeChecks.Current; }
                    finally { (storeChecks as IDisposable)?.Dispose(); }
                    var phoneChecks = TestNativePhone(os, surface);
                    try { while (phoneChecks.MoveNext()) yield return phoneChecks.Current; }
                    finally { (phoneChecks as IDisposable)?.Dispose(); }
                    var recordingChecks = TestNativeRecording(os, surface);
                    try { while (recordingChecks.MoveNext()) yield return recordingChecks.Current; }
                    finally { (recordingChecks as IDisposable)?.Dispose(); }
                    var callChecks = TestNativeCall(os, surface);
                    try { while (callChecks.MoveNext()) yield return callChecks.Current; }
                    finally { (callChecks as IDisposable)?.Dispose(); }
                    var appChecks = TestAllAppWindows(os, surface);
                    try { while (appChecks.MoveNext()) yield return appChecks.Current; }
                    finally { (appChecks as IDisposable)?.Dispose(); }
                }
                Set("MenuDown", true);
                pointerTick.Invoke(manager.Pointer, null);
                Set("MenuDown", false);
                deadline = Time.realtimeSinceStartup + 5;
                while (MonitorCaptured())
                {
                    Require(Time.realtimeSinceStartup < deadline, "Controller back did not release the native computer");
                    yield return null;
                }
                Logger.LogInfo("COMPUTER PASS: controller back closes the native computer and releases monitor input");
                yield return null;
                placement.Invoke(surface, null);
                Require(!surface.MonitorAnchored, "Closed computer retained physical monitor placement");
            }
            finally
            {
                Plugin.PhysicalDesktop.Value = savedPhysicalDesktop;
                if (desktop != null) AccessTools.Method(desktop.GetType(), "RequestActiveMonitorClose")?.Invoke(desktop, new object[] { true });
                VRManager.Instance?.Pointer?.Cancel();
                testingComputer = false;
                placed = false;
            }
        }

        private static System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem> GetDisplays()
        {
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays;
        }
    }
}
