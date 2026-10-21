using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    [BepInPlugin("com.swyf.vr.smoketests", "SWYF VR optional pointer tests", "0.1.0")]
    [BepInDependency("com.swyf.vr")]
    public sealed partial class PointerSmokeTests : BaseUnityPlugin
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private IEnumerator Start()
        {
            if (!Environment.GetCommandLineArgs().Contains("--swyf-vr-smoke-tests")) yield break;
            yield return null;
            try { TestInputRecovery(); Logger.LogInfo("SMOKE PASS: focus/head/controller recovery requires released buttons and centred sticks, preserves grip modifiers and connected menu access"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: input recovery: " + ex.GetBaseException()); }
            try { TestBodyButton(); Logger.LogInfo("SMOKE PASS: jump/give-up press ownership, modifier changes and cancellation"); }
            catch (System.Exception ex) { Logger.LogError("SMOKE FAIL: body button: " + ex.GetBaseException()); }
            try { TestPoseContext(); Logger.LogInfo("SMOKE PASS: spectator subject selection, ragdoll/hiding ownership and valid cleared pose packets"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: pose context: " + ex.GetBaseException()); }
            try { TestGameAdapterCache(); Logger.LogInfo("SMOKE PASS: cached game fields, properties, owner types, inherited methods and optional arguments"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: game adapter cache: " + ex.GetBaseException()); }
            try { TestEyeAnchor(); Logger.LogInfo("SMOKE PASS: valid eye averaging, stale world-bounds rejection and restored renderer anchoring"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: eye anchor: " + ex.GetBaseException()); }
            try { TestPointerAim(); Logger.LogInfo("SMOKE PASS: adaptive aim stability, direct fast turns, reset and contextual pointer visibility"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: pointer aim: " + ex.GetBaseException()); }
            try { TestRoomScale(); Logger.LogInfo("SMOKE PASS: room-scale body follows tracking without double movement and respects walls/disabled controllers"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: room-scale body: " + ex.GetBaseException()); }
            try { TestPoseProtocol(); Logger.LogInfo("SMOKE PASS: pose serialization, invalid data rejection, sequence wrap and remote arm solve/restore"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: pose protocol: " + ex.GetBaseException()); }
            try { TestUGUI(); Logger.LogInfo("SMOKE PASS: uGUI click, double click, drag/drop, off-target release, scroll and cancellation"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: " + ex.GetBaseException()); }
            try { TestCanvasOwnership(); Logger.LogInfo("SMOKE PASS: canvas native-parent visibility, world placement and restoration"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: canvas ownership: " + ex.GetBaseException()); }
            var documentObject = Own(new GameObject("SWYF temporary test document"));
            var settings = Own(ScriptableObject.CreateInstance<PanelSettings>());
            settings.targetTexture = Own(new RenderTexture(256, 256, 0));
            var document = documentObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.visualTreeAsset = null;
            // A bare PanelSettings has no theme/font. Without a real font the
            // editor reads shared fallback geometry from unrelated native UI.
            document.rootVisualElement.style.unityFont = Own(Font.CreateDynamicFontFromOSFont("Arial", 24));
            document.rootVisualElement.style.fontSize = 24;
            yield return null;
            try { TestToolkit(document); Logger.LogInfo("SMOKE PASS: UI Toolkit pointer down/up/move and pressed-button state"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: " + ex.GetBaseException()); }
            document.rootVisualElement.Add(new TextField { name = "keyboard-test-field", value = "abcde" });
            document.rootVisualElement.Add(new TextField { name = "keyboard-test-delayed", value = "old", isDelayed = true });
            var realButton = new UnityEngine.UIElements.Button { name = "vr-real-button", text = "click test" };
            realButton.style.width = 160; realButton.style.height = 45;
            document.rootVisualElement.Add(realButton);
            yield return null;
            try { TestToolkitButton(document); Logger.LogInfo("SMOKE PASS: real UI Toolkit button click through VR pointer events"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: real button: " + ex.GetBaseException()); }
            var otherDocument = Own(new GameObject("SWYF shared panel second document")).AddComponent<UIDocument>();
            otherDocument.panelSettings = settings;
            yield return null;
            try { TestSharedPanel(document, otherDocument); Logger.LogInfo("SMOKE PASS: shared-panel keyboard document routing"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: shared panel: " + ex.GetBaseException()); }
            IEnumerator worldTest = TestWorldPanel();
            IEnumerator browserTest = TestBrowserScroll(document);
            bool browserPassed = true;
            try
            {
                while (true)
                {
                    bool more;
                    try { more = browserTest.MoveNext(); }
                    catch (Exception ex) { browserPassed = false; Logger.LogError("SMOKE FAIL: browser scroll: " + ex.GetBaseException()); break; }
                    if (!more) break;
                    yield return browserTest.Current;
                }
            }
            finally { (browserTest as IDisposable)?.Dispose(); }
            if (browserPassed) Logger.LogInfo("SMOKE PASS: browser pixel scroll mapping, native direction, readiness, fine input, rebind and detached/chrome guards");
            bool worldPassed = true;
            while (true)
            {
                bool more;
                try { more = worldTest.MoveNext(); }
                catch (Exception ex) { worldPassed = false; Logger.LogError("SMOKE FAIL: native world UI: " + ex.GetBaseException()); break; }
                if (!more) break;
                yield return worldTest.Current;
            }
            if (worldPassed) Logger.LogInfo("SMOKE PASS: native world UI picking, transformed coordinates, click, captured drag and visibility guards");
            IEnumerator textureTest = TestTexturePanel();
            bool texturePassed = true;
            try
            {
                while (true)
                {
                    bool more;
                    try { more = textureTest.MoveNext(); }
                    catch (Exception ex) { texturePassed = false; Logger.LogError("SMOKE FAIL: texture-backed UI: " + ex.GetBaseException()); break; }
                    if (!more) break;
                    yield return textureTest.Current;
                }
            }
            finally { (textureTest as IDisposable)?.Dispose(); }
            if (texturePassed) Logger.LogInfo("SMOKE PASS: authored mesh UV/flip mapping, native button, off-mesh drag and renderer/texture ownership");
            IEnumerator cornerTest = TestCornerLifecycle(document);
            bool cornersPassed = true;
            try
            {
                while (true)
                {
                    bool more;
                    try { more = cornerTest.MoveNext(); }
                    catch (Exception ex) { cornersPassed = false; Logger.LogError("SMOKE FAIL: UI corner lifecycle: " + ex.GetBaseException()); break; }
                    if (!more) break;
                    yield return cornerTest.Current;
                }
            }
            finally { (cornerTest as IDisposable)?.Dispose(); }
            if (cornersPassed) Logger.LogInfo("SMOKE PASS: repeated UI tree detach releases corner snapshots, reattach repairs and shutdown restores native styles");
            IEnumerator motionTest = TestGripMotion();
            bool motionPassed = true;
            try
            {
                while (true)
                {
                    bool more;
                    try { more = motionTest.MoveNext(); }
                    catch (Exception ex) { motionPassed = false; Logger.LogError("SMOKE FAIL: grip motion: " + ex.GetBaseException()); break; }
                    if (!more) break;
                    yield return motionTest.Current;
                }
            }
            finally { (motionTest as IDisposable)?.Dispose(); }
            if (motionPassed) Logger.LogInfo("SMOKE PASS: grip release ignores rig translation/snap motion, maps physical controller velocity and resets after recenter");
            try { TestSelectedInput(); Logger.LogInfo("SMOKE PASS: selected uGUI controls receive per-frame keyboard/edit updates, respect XR focus and follow selection changes"); }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: selected input: " + ex.GetBaseException()); }
            IEnumerator keyboardTest = TestKeyboard(document);
            bool keyboardPassed = true;
            bool savedKeyboard = Plugin.VirtualKeyboard.Value;
            Plugin.VirtualKeyboard.Value = true;
            try
            {
                while (true)
                {
                    bool more = false;
                    try { more = keyboardTest.MoveNext(); }
                    catch (Exception ex) { keyboardPassed = false; Logger.LogError("SMOKE FAIL: keyboard: " + ex.GetBaseException()); }
                    if (!more) break;
                    yield return keyboardTest.Current;
                }
            }
            finally { (keyboardTest as IDisposable)?.Dispose(); Plugin.VirtualKeyboard.Value = savedKeyboard; }
            if (keyboardPassed) Logger.LogInfo("SMOKE PASS: keyboard UI Toolkit/uGUI/TMP editing, navigation, validation, browser dispatch and stale-target cleanup");
            if (!Environment.GetCommandLineArgs().Contains("--swyf-pair-client") && !Environment.GetCommandLineArgs().Contains("--swyf-pair-host"))
            {
                var menuTest = TestMenuRig();
                bool menuPassed = true;
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = menuTest.MoveNext(); }
                        catch (Exception ex) { menuPassed = false; Logger.LogError("SMOKE FAIL: menu rig: " + ex.GetBaseException()); break; }
                        if (!more) break;
                        yield return menuTest.Current;
                    }
                }
                finally { (menuTest as IDisposable)?.Dispose(); }
                if (menuPassed) Logger.LogInfo("SMOKE PASS: native menu demo cannot own the headset origin; beam remains visible and attached without a UI hit");
            }
            if (Environment.GetCommandLineArgs().Contains("--swyf-vr-pose-host-tests"))
                yield return TestPoseHost();
            foreach (var item in owned) if (item != null) Destroy(item);
            owned.Clear();
            if (Environment.GetCommandLineArgs().Contains("--swyf-vr-mock-lifecycle-tests"))
                yield return TestMockLifecycle();
        }

        private T Own<T>(T item) where T : UnityEngine.Object
        {
            owned.Add(item);
            if (item is GameObject go) go.transform.SetParent(transform, false);
            return item;
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Same(PointerProbe probe, params string[] expected)
        {
            string actual = string.Join(",", probe.Events);
            Check(actual == string.Join(",", expected), "Expected " + string.Join(",", expected) + "; got " + actual);
            probe.Events.Clear();
        }

        private void TestUGUI()
        {
            var root = Own(new GameObject("SWYF temporary pointer test"));
            var system = root.AddComponent<EventSystem>();
            system.enabled = false;
            var module = root.AddComponent<VRPointerModule>();
            var a = Own(new GameObject("test source")).AddComponent<PointerProbe>();
            var b = Own(new GameObject("test drop target")).AddComponent<PointerProbe>();
            var tick = typeof(VRPointerModule).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
            var cancel = typeof(VRPointerModule).GetMethod("Cancel", BindingFlags.Instance | BindingFlags.NonPublic);
            void Tick(PointerProbe target, float x, bool down = false, bool up = false, float scroll = 0)
                => tick.Invoke(module, new object[] { new RaycastResult { gameObject = target?.gameObject }, new Vector2(x, 0), down, up, scroll });
            Tick(a, 0, down: true); Tick(a, 0, up: true);
            Same(a, "down", "up", "click:1");
            Tick(a, 0, down: true); Tick(a, 0, up: true);
            Same(a, "down", "up", "click:2");
            Tick(a, 0, down: true); Tick(a, system.pixelDragThreshold + 10); Tick(b, 100, up: true);
            Same(a, "down", "begin", "drag", "up", "end"); Same(b, "drop");
            Tick(a, 0, down: true); Tick(b, 0, up: true);
            Same(a, "down", "up"); Same(b);
            Tick(a, 0, scroll: 1); Same(a, "scroll");
            Tick(a, 0, down: true); cancel.Invoke(module, null); Tick(a, 0, up: true);
            Same(a, "down", "up");
            Tick(a, 0, down: true); Tick(a, system.pixelDragThreshold + 10); cancel.Invoke(module, null);
            Same(a, "down", "begin", "drag", "up", "end");
            module.enabled = false;
        }

        private void TestToolkit(UIDocument document)
        {
            Check(document != null && document.rootVisualElement != null && document.rootVisualElement.panel != null, "Test document has no live panel");
            var element = new VisualElement();
            document.rootVisualElement.Add(element);
            var events = new List<string>();
            element.RegisterCallback<PointerDownEvent>(evt => events.Add("down:" + evt.pressedButtons));
            element.RegisterCallback<PointerMoveEvent>(evt => events.Add("move:" + evt.pressedButtons));
            element.RegisterCallback<PointerUpEvent>(evt => events.Add("up:" + evt.pressedButtons));
            var pointer = Own(new GameObject("SWYF temporary toolkit pointer")).AddComponent<VRPointer>();
            pointer.enabled = false;
            var send = typeof(VRPointer).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic);
            void Send(EventType type, bool held) => send.Invoke(pointer, new object[] { element, type, new Vector2(10, 10), 0, held });
            Send(EventType.MouseDown, true); Send(EventType.MouseMove, true); Send(EventType.MouseUp, false); Send(EventType.MouseMove, false);
            Check(string.Join(",", events) == "down:1,move:1,up:0,move:0", "Toolkit state: " + string.Join(",", events));
            events.Clear();
            var buttonTarget = new VisualElement(); document.rootVisualElement.Add(buttonTarget);
            var counts = new List<int>();
            buttonTarget.RegisterCallback<PointerDownEvent>(evt => counts.Add(evt.clickCount));
            foreach (int button in new[] { 0, 0, 1, 0 })
            {
                send.Invoke(pointer, new object[] { buttonTarget, EventType.MouseDown, new Vector2(10, 10), button, true });
                send.Invoke(pointer, new object[] { buttonTarget, EventType.MouseUp, new Vector2(10, 10), button, false });
            }
            Check(string.Join(",", counts) == "1,2,1,1", "Double click count crossed mouse buttons: " + string.Join(",", counts));
            buttonTarget.RemoveFromHierarchy();
            Send(EventType.MouseDown, true);
            typeof(VRPointer).GetField("toolkitDown", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pointer, true);
            typeof(VRPointer).GetField("pressedElement", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pointer, element);
            element.RemoveFromHierarchy();
            typeof(VRPointer).GetMethod("CancelToolkit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pointer, null);
            Type pointerState = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.PointerDeviceState");
            Check((int)pointerState.GetMethod("GetPressedButtons", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { VRPointer.ToolkitPointerId }) == 0,
                "Removing a pressed panel left the controller's global button state held");
            document.rootVisualElement.Add(element);
            Send(EventType.MouseMove, false);
            Check(string.Join(",", events) == "down:1,move:0", "Detached target left a pressed pointer button: " + string.Join(",", events));
        }

        private void TestSharedPanel(UIDocument first, UIDocument second)
        {
            var root = Own(new GameObject("SWYF shared panel surface"));
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
            var surface = (VRPanelSurface)typeof(VRPanelSurface).GetMethod("Create", flags).Invoke(null, new object[] { root.transform, first.panelSettings, 1.6f });
            var add = typeof(VRPanelSurface).GetMethod("AddDocument", flags);
            add.Invoke(surface, new object[] { first }); add.Invoke(surface, new object[] { second });
            var a = new VisualElement(); var b = new VisualElement();
            first.rootVisualElement.Add(a); second.rootVisualElement.Add(b);
            Check(surface.DocumentFor(a) == first && surface.DocumentFor(b) == second, "Shared panel chose the wrong document");
            second.enabled = false;
            Check(surface.DocumentFor(b) == null, "Disabled document remained a keyboard target");
            var settings = Own(ScriptableObject.CreateInstance<PanelSettings>());
            settings.clearColor = false; settings.colorClearValue = Color.red;
            var ownedSurface = (VRPanelSurface)typeof(VRPanelSurface).GetMethod("Create", flags).Invoke(null, new object[] { root.transform, settings, 1.6f });
            Check(settings.clearColor && settings.colorClearValue == Color.clear, "Owned UI texture did not enable transparent clearing");
            ownedSurface.Restore();
            Check(settings.targetTexture == null && !settings.clearColor && settings.colorClearValue == Color.red, "Owned UI texture did not restore its clear settings");
        }

        private void TestCanvasOwnership()
        {
            var patcher = Own(new GameObject("SWYF canvas ownership patcher")).AddComponent<VRUIPatcher>();
            patcher.enabled = false;
            var parent = Own(new GameObject("native menu visibility owner"));
            parent.transform.localScale = Vector3.one * 2;
            var canvasObject = Own(new GameObject("native hidden menu", typeof(RectTransform), typeof(Canvas)));
            canvasObject.transform.SetParent(parent.transform, false);
            var rect = (RectTransform)canvasObject.transform;
            rect.sizeDelta = new Vector2(800, 450);
            rect.localPosition = new Vector3(2, 3, 4);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            parent.SetActive(false);
            var convert = typeof(VRUIPatcher).GetMethod("Convert", BindingFlags.NonPublic | BindingFlags.Instance);
            convert.Invoke(patcher, new object[] { canvas });
            Check(patcher.Canvases.Count == 0 && !canvas.isActiveAndEnabled, "Hidden canvas was converted or activated");
            parent.SetActive(true);
            Vector3 position = rect.localPosition, scale = rect.localScale;
            RenderMode originalMode = canvas.renderMode;
            float pixelsWide = Mathf.Max(rect.rect.width, 640);
            patcher.Anchor.SetPositionAndRotation(new Vector3(9, 8, 7), Quaternion.Euler(0, 35, 0));
            convert.Invoke(patcher, new object[] { canvas });
            Check(canvas.transform.parent == parent.transform, "VR canvas escaped its native parent");
            Check(Vector3.Distance(rect.position, patcher.Anchor.position) < 0.001f, "VR canvas did not follow its anchor");
            Check(Mathf.Abs(rect.lossyScale.x * pixelsWide - Plugin.PanelWidth.Value) < 0.005f, "Native parent scale changed VR canvas width");
            var image = new GameObject("temporary menu graphic", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
            image.transform.SetParent(canvas.transform, false);
            var survivingImage = new GameObject("surviving menu graphic", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
            survivingImage.transform.SetParent(canvas.transform, false);
            Material nativeMaterial = survivingImage.material;
            typeof(VRUIPatcher).GetMethod("PrepareFloatingGraphics", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(patcher, new object[] { canvas });
            var materials = (System.Collections.IDictionary)typeof(VRUIPatcher).GetField("floatingMaterials", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(patcher);
            Check(materials.Count == 2 && survivingImage.material != nativeMaterial, "Floating graphics ownership was not recorded");
            DestroyImmediate(image.gameObject);
            typeof(VRUIPatcher).GetMethod("PruneDestroyedGraphics", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(patcher, null);
            Check(materials.Count == 1, "Destroyed UI retained its owned material cache entry");
            parent.SetActive(false);
            Check(!canvas.isActiveAndEnabled, "Native parent no longer hides converted canvas");
            patcher.Restore();
            Check(survivingImage.material == nativeMaterial && materials.Count == 0, "Live UI material was not restored after cache pruning");
            Check(rect.parent == parent.transform && Vector3.Distance(rect.localPosition, position) < 0.001f && Vector3.Distance(rect.localScale, scale) < 0.001f
                && canvas.renderMode == originalMode && !canvas.isActiveAndEnabled, "Canvas restoration changed native visibility or transform: " + rect.localPosition + "/" + position + "; mode=" + canvas.renderMode + "/" + originalMode);
            var settings = Own(ScriptableObject.CreateInstance<PanelSettings>());
            var requires = typeof(VRUIPatcher).GetMethod("RequiresFloatingPanel", BindingFlags.Static | BindingFlags.NonPublic);
            Check((bool)requires.Invoke(null, new object[] { settings }), "Screen overlay was not eligible for VR conversion");
            settings.targetTexture = Own(new RenderTexture(16, 16, 0));
            Check(!(bool)requires.Invoke(null, new object[] { settings }), "Native texture presenter was duplicated into the floating UI");
            settings.targetTexture = null;
            FieldInfo mode = typeof(PanelSettings).GetField("m_RenderMode", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(mode != null, "Native world panel mode field missing");
            mode.SetValue(settings, System.Enum.Parse(mode.FieldType, "WorldSpace"));
            Check(!(bool)requires.Invoke(null, new object[] { settings }), "Native world-space panel was duplicated into the floating UI");
        }

        private IEnumerator TestMockLifecycle()
        {
            float until = Time.realtimeSinceStartup + 30;
            while ((VRManager.Instance == null || !VRManager.Instance.Ready || VRManager.Instance.Rig.Head == null) && Time.realtimeSinceStartup < until)
                yield return null;
            UnityEngine.Camera camera = null;
            float near = 0;
            StereoTargetEyeMask stereo = StereoTargetEyeMask.None;
            bool builtInStereo = false;
            int cullingMask = 0;
            FieldInfo visibilityField = null;
            bool visibilityValue = false;
            int savedVsync = 0, savedFrameLimit = -1;
            GameObject nativePresenter = null;
            Material nativeMaterial = null;
            PanelSettings nativeSettings = null;
            RenderTexture nativeTexture = null;
            VRPanelSurface borrowed = null;
            VRManager manager = VRManager.Instance;
            try
            {
                var runtime = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.XR.OpenXR.OpenXRRuntime")).FirstOrDefault(t => t != null);
                Check(runtime?.GetProperty("name")?.GetValue(null) as string == "Unity Mock Runtime", "Lifecycle stop requires Unity Mock Runtime");
                if (manager == null || !manager.Ready || manager.Rig.Head == null)
                {
                    Logger.LogInfo("SMOKE INFO: manager=" + (manager != null) + "; ready=" + (manager != null && manager.Ready)
                        + "; rig active=" + (manager != null && manager.Rig != null && manager.Rig.isActiveAndEnabled));
                    foreach (var available in UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        Logger.LogInfo("SMOKE CAMERA: " + available.name + "; enabled=" + available.isActiveAndEnabled + "; tag=" + available.tag);
                }
                Check(manager != null && manager.Ready && manager.Rig.Head != null, "Mock XR session did not bind a camera");
                var performance = manager.GetComponentInChildren<VRPerformance>();
                Check(performance != null, "No VR performance component");
                savedVsync = (int)typeof(VRPerformance).GetField("originalVsync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(performance);
                savedFrameLimit = (int)typeof(VRPerformance).GetField("originalFrameLimit", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(performance);
                visibilityField = typeof(VRPerformance).GetField("visibilityFlag", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(performance) as FieldInfo;
                if (visibilityField != null)
                    visibilityValue = (bool)typeof(VRPerformance).GetField("savedVisibility", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(performance);
                Check(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == -1, "Desktop frame pacing is still active during VR");
                camera = manager.Rig.Head;
                near = (float)typeof(ScamWithYourFriendsInVR.Camera.VRCameraController).GetField("savedNear", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager.Rig);
                cullingMask = (int)typeof(ScamWithYourFriendsInVR.Camera.VRCameraController).GetField("savedCullingMask", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager.Rig);
                stereo = (StereoTargetEyeMask)typeof(ScamWithYourFriendsInVR.Camera.VRCameraController).GetField("savedStereo", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager.Rig);
                builtInStereo = (bool)typeof(ScamWithYourFriendsInVR.Camera.VRCameraController).GetField("builtInStereo", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager.Rig);
                var xrManager = typeof(VRManager).GetField("ownedManager", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                Check(xrManager != null, "No owned XR manager");
                nativePresenter = Own(GameObject.CreatePrimitive(PrimitiveType.Quad));
                nativePresenter.name = "SWYF borrowed presenter shutdown probe";
                nativePresenter.transform.position = Vector3.one * 1000;
                nativePresenter.GetComponent<Collider>().enabled = false;
                nativeTexture = Own(new RenderTexture(64, 64, 0)); nativeTexture.Create();
                nativeSettings = Own(ScriptableObject.CreateInstance<PanelSettings>()); nativeSettings.targetTexture = nativeTexture;
                var nativeDocument = Own(new GameObject("SWYF borrowed document shutdown probe")).AddComponent<UIDocument>();
                nativeDocument.panelSettings = nativeSettings;
                nativeMaterial = Own(new Material(Shader.Find("UI/Default"))); nativeMaterial.mainTexture = nativeTexture;
                var nativeRenderer = nativePresenter.GetComponent<MeshRenderer>(); nativeRenderer.sharedMaterial = nativeMaterial;
                borrowed = (VRPanelSurface)typeof(VRPanelSurface).GetMethod("Borrow", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { nativeRenderer, nativePresenter.GetComponent<MeshFilter>().sharedMesh, 0, nativeMaterial, "_MainTex", nativeDocument });
                manager.UI.Panels.Add(borrowed);
                Logger.LogInfo("SMOKE INFO: mock session active; canvases=" + manager.UI.Canvases.Count + "; panels=" + manager.UI.Panels.Count);
                xrManager.GetType().GetMethod("StopSubsystems").Invoke(xrManager, null);
            }
            catch (Exception ex)
            {
                Logger.LogError("SMOKE FAIL: mock lifecycle setup: " + ex.GetBaseException());
                foreach (var item in owned) if (item != null) Destroy(item);
                owned.Clear(); yield break;
            }
            yield return new WaitForSecondsRealtime(2);
            try
            {
                Check(!manager.Ready, "Runtime stop left the session ready");
                Check(manager.Rig == null || manager.Rig.Head == null, "Runtime stop retained the camera binding");
                Check(camera == null || Mathf.Approximately(camera.nearClipPlane, near) && (!builtInStereo || camera.stereoTargetEye == stereo), "Camera settings were not restored");
                Check(camera == null || camera.cullingMask == cullingMask, "VR UI layer retained in camera culling mask after shutdown");
                Check(visibilityField == null || (bool)visibilityField.GetValue(null) == visibilityValue, "XR visibility-mesh setting not restored");
                Check(manager.UI == null || manager.UI.Canvases.Count == 0 && manager.UI.Panels.Count == 0 && manager.UI.WorldDocuments.Count == 0, "Runtime stop retained converted UI");
                Check(typeof(VRManager).GetField("harmony", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager) == null, "Runtime stop retained patches");
                Check(QualitySettings.vSyncCount == savedVsync && Application.targetFrameRate == savedFrameLimit, "Runtime stop did not restore desktop frame pacing");
                Check(borrowed == null && nativePresenter != null && nativePresenter.GetComponent<MeshRenderer>().enabled
                    && nativePresenter.GetComponent<MeshRenderer>().sharedMaterial == nativeMaterial && nativeMaterial.mainTexture == nativeTexture
                    && nativeSettings.targetTexture == nativeTexture && nativeTexture.IsCreated(),
                    "Runtime stop destroyed or altered the borrowed native presenter/texture");
                Logger.LogInfo("SMOKE PASS: mock runtime startup and stop restore camera/UI and remove gameplay patches");
            }
            catch (Exception ex) { Logger.LogError("SMOKE FAIL: mock lifecycle cleanup: " + ex.GetBaseException()); }
            finally
            {
                foreach (var item in owned) if (item != null) Destroy(item);
                owned.Clear();
            }
        }
    }

    public sealed class PointerProbe : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IScrollHandler, IUpdateSelectedHandler
    {
        public readonly List<string> Events = new List<string>();
        public void OnPointerDown(PointerEventData e) => Events.Add("down");
        public void OnPointerUp(PointerEventData e) => Events.Add("up");
        public void OnPointerClick(PointerEventData e) => Events.Add("click:" + e.clickCount);
        public void OnBeginDrag(PointerEventData e) => Events.Add("begin");
        public void OnDrag(PointerEventData e) => Events.Add("drag");
        public void OnEndDrag(PointerEventData e) => Events.Add("end");
        public void OnDrop(PointerEventData e) => Events.Add("drop");
        public void OnScroll(PointerEventData e) => Events.Add("scroll");
        public void OnUpdateSelected(BaseEventData e) { Events.Add("update"); e.Use(); }
    }
}
