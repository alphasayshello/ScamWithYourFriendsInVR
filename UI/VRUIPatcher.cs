using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace ScamWithYourFriendsInVR.UI
{
    [DefaultExecutionOrder(30900)]
    public sealed class VRUIPatcher : MonoBehaviour
    {
        public readonly List<Canvas> Canvases = new List<Canvas>();
        public readonly List<VRPanelSurface> Panels = new List<VRPanelSurface>();
        public readonly List<UIDocument> WorldDocuments = new List<UIDocument>();
        public Transform Anchor { get; private set; }
        private readonly List<CanvasState> canvases = new List<CanvasState>();
        private readonly HashSet<PanelSettings> panelSettings = new HashSet<PanelSettings>();
        private UnityEngine.Camera eventCamera;
        private readonly HashSet<UIDocument> knownDocuments = new HashSet<UIDocument>();
        private readonly List<UIDocument> documentScan = new List<UIDocument>();
        private bool documentEvents, documentsSeeded;
        private readonly List<Canvas> maintenanceCanvases = new List<Canvas>();
        private readonly List<VRPanelSurface> maintenancePanels = new List<VRPanelSurface>();
        private int maintenanceCanvasCursor, maintenancePanelCursor;
        private bool maintenancePending;
        private float nextScan;
        private float nextTextureScan;
        private MeshRenderer[] presenterScan;
        private int presenterScanCursor;
        private readonly List<UIDocument> textureDocuments = new List<UIDocument>();
        private readonly List<Material> presenterMaterials = new List<Material>();
        private readonly List<PresenterDocument> presenterDocuments = new List<PresenterDocument>();
        private struct PresenterDocument { internal UIDocument Document; internal PanelSettings Settings; internal RenderTexture Texture; }
        private static readonly string[] textureProperties = { "_BaseMap", "_MainTex" };
        private bool placed;
        private bool monitorCaptured;
        private UnityEngine.Camera lastHead;
        private bool lastMenu;
        private bool lastSettings;
        private readonly Dictionary<Graphic, GraphicMaterial> floatingMaterials = new Dictionary<Graphic, GraphicMaterial>();
        private readonly Dictionary<Canvas, AdditionalCanvasShaderChannels> canvasChannels = new Dictionary<Canvas, AdditionalCanvasShaderChannels>();
        private readonly List<Canvas> nestedCanvasBuffer = new List<Canvas>(), deadCanvases = new List<Canvas>();
        private readonly List<Graphic> graphicBuffer = new List<Graphic>(), deadGraphics = new List<Graphic>();
        private sealed class GraphicMaterial { internal Material Original, Overlay; }

        private sealed class CanvasState
        {
            public Canvas Canvas;
            public RenderMode Mode;
            public UnityEngine.Camera Camera;
            public Transform Parent;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public Vector2 Size, Min, Max, Pivot, Anchored;
            public Scene Scene;
            public float PixelsWide;
        }

        private void Awake()
        {
            Anchor = new GameObject("World UI anchor").transform;
            Anchor.SetParent(transform, false);
            GameObject camera = new GameObject("UI event camera");
            camera.transform.SetParent(Anchor, false);
            camera.transform.localPosition = new Vector3(0, 0, -2);
            eventCamera = camera.AddComponent<UnityEngine.Camera>();
            eventCamera.enabled = false;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                eventCamera.stereoTargetEye = StereoTargetEyeMask.None;
            eventCamera.orthographic = true;
            eventCamera.orthographicSize = Plugin.PanelWidth.Value;
            eventCamera.nearClipPlane = 0.01f;
            eventCamera.farClipPlane = 20;
            SceneManager.sceneUnloaded += SceneUnloaded;
            SceneManager.activeSceneChanged += SceneChanged;
        }

        internal void UseDocumentEvents()
        {
            documentEvents = true; documentsSeeded = false; nextScan = 0;
        }
        internal void RememberDocument(UIDocument document)
        {
            if (document == null) return;
            knownDocuments.Add(document); nextScan = 0;
        }
        private List<UIDocument> GetDocuments()
        {
            if (!documentsSeeded || !documentEvents)
            {
                foreach (UIDocument document in FindDocuments()) knownDocuments.Add(document);
                documentsSeeded = true;
            }
            knownDocuments.RemoveWhere(document => document == null);
            // Enable callbacks may add documents while a surface is prepared.
            // Enumerate a reusable snapshot instead of the live registry.
            documentScan.Clear(); documentScan.AddRange(knownDocuments);
            return documentScan;
        }

        public void Place()
        {
            UnityEngine.Camera head = VRManager.Instance.Rig.Head;
            if (head == null) return;
            Quaternion facing = head.transform.rotation;
            Anchor.SetPositionAndRotation(head.transform.position + facing * Vector3.forward * Plugin.PanelDistance.Value, facing);
            placed = true;
        }

        private void Update()
        {
            VRManager manager = VRManager.Instance;
            if (manager == null || !manager.Ready) return;
            bool captured = Patches.GameAdapter.MonitorCaptured;
            bool menu = Patches.GameAdapter.InMainMenu;
            bool settingsOpen = Patches.GameAdapter.SettingsOpen;
            if (settingsOpen && !lastSettings) { placed = false; nextScan = 0; }
            lastSettings = settingsOpen;
            if (lastHead != manager.Rig.Head || menu != lastMenu) { lastHead = manager.Rig.Head; lastMenu = menu; placed = false; }
            if (captured != monitorCaptured) { monitorCaptured = captured; placed = false; }
            if (!placed || manager.Input.RecenterDown) Place();
            if (presenterScan != null) DiscoverTexturePresenters();
            if (!placed || Time.unscaledTime < nextScan)
            {
                AdvanceMaintenance();
                return;
            }
            nextScan = Time.unscaledTime + 0.5f;
            PruneDestroyedGraphics();
            canvases.RemoveAll(state => state.Canvas == null);
            Canvases.RemoveAll(c => c == null);
            Panels.RemoveAll(p =>
            {
                if (p == null) return true;
                if (!p.NativePresenter || p.BindingValid) return false;
                p.Remove(); return true;
            });
            textureDocuments.Clear();
            WorldDocuments.RemoveAll(d => d == null || !d.isActiveAndEnabled);
            foreach (Canvas canvas in FindCanvases())
            {
                if (!canvas.isActiveAndEnabled || !canvas.isRootCanvas || Canvases.Contains(canvas)) continue;
                if (canvas.renderMode != RenderMode.WorldSpace) Convert(canvas);
                else Canvases.Add(canvas);
            }
            foreach (UIDocument document in GetDocuments())
            {
                if (!document.isActiveAndEnabled || document.rootVisualElement?.panel == null) continue;
                manager.Settings?.Track(document);
                PanelSettings settings = document.panelSettings;
                if (settings == null) continue;
                if (Patches.GameAdapter.Read(settings, "renderMode")?.ToString() == "WorldSpace")
                {
                    if (Patches.GameAdapter.Read(document, "parentUI") == null && !WorldDocuments.Contains(document)) WorldDocuments.Add(document);
                    continue;
                }
                // Authored texture-backed panels already have a world presenter
                // (for example the review projector). Do not duplicate it in the HUD.
                if (!panelSettings.Contains(settings) && !RequiresFloatingPanel(settings))
                {
                    textureDocuments.Add(document);
                    continue;
                }
                if (!panelSettings.Add(settings))
                {
                    VRPanelSurface existing = Panels.Find(p => p != null && p.Settings == settings);
                    if (existing != null) existing.AddDocument(document);
                    continue;
                }
                VRPanelSurface surface = VRPanelSurface.Create(Anchor, settings, Plugin.PanelWidth.Value);
                surface.Document = document;
                surface.AddDocument(document);
                Panels.Add(surface);
                surface.RefreshDocument();
            }
            if (textureDocuments.Count > 0 && presenterScan == null && Time.unscaledTime >= nextTextureScan)
            {
                nextTextureScan = Time.unscaledTime + 2;
                presenterScan = FindPresenters();
                presenterScanCursor = 0;
            }
            if (!maintenancePending)
            {
                maintenanceCanvases.Clear(); maintenancePanels.Clear();
                foreach (CanvasState state in canvases) if (state.Canvas != null) maintenanceCanvases.Add(state.Canvas);
                maintenancePanels.AddRange(Panels);
                maintenanceCanvasCursor = maintenancePanelCursor = 0;
                maintenancePending = true;
            }
        }

        private void AdvanceMaintenance()
        {
            if (!maintenancePending) return;
            long started = Stopwatch.GetTimestamp();
            long budget = Stopwatch.Frequency * 35 / 100000;
            while (maintenanceCanvasCursor < maintenanceCanvases.Count)
            {
                Canvas canvas = maintenanceCanvases[maintenanceCanvasCursor++];
                if (canvas != null && canvas.isActiveAndEnabled) PrepareFloatingGraphics(canvas);
                if (Stopwatch.GetTimestamp() - started >= budget) return;
            }
            while (maintenancePanelCursor < maintenancePanels.Count)
            {
                VRPanelSurface panel = maintenancePanels[maintenancePanelCursor++];
                if (panel != null) panel.RefreshDocument();
                if (Stopwatch.GetTimestamp() - started >= budget) return;
            }
            ResetMaintenance();
        }

        private void ResetMaintenance()
        {
            maintenancePending = false;
            maintenanceCanvasCursor = maintenancePanelCursor = 0;
            maintenanceCanvases.Clear(); maintenancePanels.Clear();
        }

        private static Canvas[] FindCanvases() => FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        private static UIDocument[] FindDocuments() => FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        private static MeshRenderer[] FindPresenters() => FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

        private void DiscoverTexturePresenters()
        {
            if (presenterScan == null) return;
            presenterDocuments.Clear();
            foreach (UIDocument document in textureDocuments)
            {
                if (document == null || !document.isActiveAndEnabled) continue;
                PanelSettings settings = document.panelSettings;
                if (settings != null && settings.targetTexture != null)
                    presenterDocuments.Add(new PresenterDocument { Document = document, Settings = settings, Texture = settings.targetTexture });
            }
            int end = Mathf.Min(presenterScan.Length, presenterScanCursor + 128);
            long started = Stopwatch.GetTimestamp();
            long budget = Stopwatch.Frequency * 35 / 100000; // 0.35 ms, checked in small batches.
            int processed = 0;
            while (presenterScanCursor < end)
            {
                if (processed > 0 && (processed & 7) == 0 && Stopwatch.GetTimestamp() - started >= budget) break;
                processed++;
                MeshRenderer renderer = presenterScan[presenterScanCursor++];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || !mesh.isReadable || mesh.vertexCount > 10000) continue;
                renderer.GetSharedMaterials(presenterMaterials);
                for (int slot = 0; slot < presenterMaterials.Count && slot < mesh.subMeshCount; slot++)
                {
                    Material material = presenterMaterials[slot];
                    if (material == null || mesh.GetTopology(slot) != MeshTopology.Triangles) continue;
                    foreach (string property in textureProperties)
                    {
                        if (!material.HasProperty(property) || !(material.GetTexture(property) is RenderTexture texture)) continue;
                        bool matched = false;
                        foreach (PresenterDocument target in presenterDocuments)
                        {
                            if (target.Texture != texture) continue;
                            matched = true;
                            VRPanelSurface surface = Panels.Find(p => p != null && p.MatchesPresenter(renderer, slot, property)
                                && p.Settings == target.Settings && p.Texture == texture);
                            if (surface == null)
                            {
                                try
                                {
                                    surface = VRPanelSurface.Borrow(renderer, mesh, slot, material, property, target.Document);
                                    Panels.Add(surface);
                                }
                                catch (System.Exception ex) { Patches.GameAdapter.Report("Texture panel " + renderer.name, ex); continue; }
                            }
                            surface.AddDocument(target.Document);
                        }
                        if (matched) break;
                    }
                }
            }
            if (presenterScanCursor >= presenterScan.Length) { presenterScan = null; presenterScanCursor = 0; }
        }

        private void Convert(Canvas canvas)
        {
            if (canvas == null || !canvas.isActiveAndEnabled) return;
            RectTransform rect = (RectTransform)canvas.transform;
            float width = Mathf.Max(rect.rect.width, 640);
            float height = Mathf.Max(rect.rect.height, 360);
            canvases.Add(new CanvasState { Canvas = canvas, Mode = canvas.renderMode, Camera = canvas.worldCamera,
                Parent = rect.parent, Position = rect.localPosition, Rotation = rect.localRotation,
                Scale = rect.localScale, Size = rect.sizeDelta, Min = rect.anchorMin, Max = rect.anchorMax,
                Pivot = rect.pivot, Anchored = rect.anchoredPosition, Scene = canvas.gameObject.scene, PixelsWide = width });
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = new Vector2(width, height);
            Canvases.Add(canvas);
            PlaceCanvas(canvases[canvases.Count - 1]);
            PrepareFloatingGraphics(canvas);
        }

        private void LateUpdate()
        {
            // Floating HUD and menus remain in the headset's view. Physical
            // monitor presenters keep their authored desk placement below.
            if (VRManager.Instance?.Ready == true) Place();
            foreach (CanvasState state in canvases)
                if (state.Canvas != null && state.Canvas.isActiveAndEnabled) PlaceCanvas(state);
            foreach (VRPanelSurface panel in Panels) if (panel != null) panel.UpdatePlacement();
        }

        private void PlaceCanvas(CanvasState state)
        {
            state.Canvas.renderMode = RenderMode.WorldSpace;
            state.Canvas.worldCamera = eventCamera;
            Transform rect = state.Canvas.transform;
            rect.SetPositionAndRotation(Anchor.TransformPoint(new Vector3(0, 0, -Mathf.Clamp(state.Canvas.sortingOrder, -1000, 1000) * 0.0001f)), Anchor.rotation);
            Vector3 scale = rect.parent != null ? rect.parent.lossyScale : Vector3.one;
            float worldScale = Plugin.PanelWidth.Value / state.PixelsWide;
            rect.localScale = new Vector3(Divide(worldScale, scale.x), Divide(worldScale, scale.y), Divide(worldScale, scale.z));
        }

        private static float Divide(float value, float scale) => Mathf.Abs(scale) > 0.0001f ? value / scale : value;

        private void PrepareFloatingGraphics(Canvas canvas)
        {
            nestedCanvasBuffer.Clear(); canvas.GetComponentsInChildren(true, nestedCanvasBuffer);
            foreach (Canvas nested in nestedCanvasBuffer)
            {
                if (!canvasChannels.ContainsKey(nested)) canvasChannels[nested] = nested.additionalShaderChannels;
                nested.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            }
            graphicBuffer.Clear(); canvas.GetComponentsInChildren(true, graphicBuffer);
            foreach (Graphic graphic in graphicBuffer)
            {
                if (graphic == null || !graphic.gameObject.activeInHierarchy) continue;
                if (floatingMaterials.TryGetValue(graphic, out var saved) && graphic.material == saved.Overlay) continue;
                Material original = graphic.material;
                if (original == null) continue;
                if (saved != null && saved.Overlay != null) Destroy(saved.Overlay);
                var overlay = new Material(original) { name = original.name + " (VR floating UI)" };
                overlay.SetInt("unity_GUIZTestMode", Plugin.OccludeFloatingUI.Value ? 4 : 8);
                if (overlay.HasProperty("_ZTestMode")) overlay.SetInt("_ZTestMode", Plugin.OccludeFloatingUI.Value ? 4 : 8);
                floatingMaterials[graphic] = new GraphicMaterial { Original = original, Overlay = overlay };
                graphic.material = overlay;
                graphic.SetVerticesDirty();
            }
            nestedCanvasBuffer.Clear(); graphicBuffer.Clear();
        }

        private void PruneDestroyedGraphics()
        {
            deadGraphics.Clear(); deadCanvases.Clear();
            foreach (var state in floatingMaterials)
                if (state.Key == null)
                {
                    if (state.Value.Overlay != null) Destroy(state.Value.Overlay);
                    deadGraphics.Add(state.Key);
                }
            foreach (Graphic graphic in deadGraphics) floatingMaterials.Remove(graphic);
            foreach (var state in canvasChannels) if (state.Key == null) deadCanvases.Add(state.Key);
            foreach (Canvas canvas in deadCanvases) canvasChannels.Remove(canvas);
            deadGraphics.Clear(); deadCanvases.Clear();
        }

        internal static bool RequiresFloatingPanel(PanelSettings settings)
            => settings != null && settings.targetTexture == null
                && Patches.GameAdapter.Read(settings, "renderMode")?.ToString() != "WorldSpace";

        public void Restore()
        {
            foreach (var entry in floatingMaterials)
            {
                if (entry.Key != null && entry.Key.material == entry.Value.Overlay) entry.Key.material = entry.Value.Original;
                if (entry.Value.Overlay != null) Destroy(entry.Value.Overlay);
            }
            floatingMaterials.Clear();
            foreach (var state in canvasChannels) if (state.Key != null) state.Key.additionalShaderChannels = state.Value;
            canvasChannels.Clear();
            foreach (CanvasState state in canvases)
            {
                if (state.Canvas == null) continue;
                RectTransform rect = (RectTransform)state.Canvas.transform;
                rect.SetParent(state.Parent, false);
                rect.anchorMin = state.Min; rect.anchorMax = state.Max; rect.pivot = state.Pivot;
                rect.sizeDelta = state.Size; rect.anchoredPosition = state.Anchored;
                rect.localPosition = state.Position; rect.localRotation = state.Rotation; rect.localScale = state.Scale;
                state.Canvas.worldCamera = state.Camera; state.Canvas.renderMode = state.Mode;
            }
            canvases.Clear(); Canvases.Clear();
            WorldDocuments.Clear();
            foreach (VRPanelSurface panel in Panels) if (panel != null) panel.Remove();
            Panels.Clear(); panelSettings.Clear();
            textureDocuments.Clear(); knownDocuments.Clear(); documentScan.Clear(); documentsSeeded = false;
            ResetPresenterScan(); ResetMaintenance();
        }

        private void ResetPresenterScan()
        {
            presenterScan = null; presenterScanCursor = 0; nextTextureScan = 0;
            presenterDocuments.Clear(); presenterMaterials.Clear();
        }
        private void SceneChanged(Scene oldScene, Scene newScene) { placed = false; documentsSeeded = false; nextScan = 0; ResetPresenterScan(); ResetMaintenance(); }
        private void SceneUnloaded(Scene scene)
        {
            documentsSeeded = false; nextScan = 0;
            ResetPresenterScan(); ResetMaintenance();
            for (int i = canvases.Count - 1; i >= 0; i--)
            {
                CanvasState state = canvases[i];
                if (state.Scene != scene) continue;
                Canvases.Remove(state.Canvas);
                if (state.Canvas != null) Destroy(state.Canvas.gameObject);
                canvases.RemoveAt(i);
            }
            Panels.RemoveAll(panel =>
            {
                if (panel == null) return true;
                if (panel.Settings != null) return false;
                panel.Remove(); return true;
            });
            placed = false;
        }
        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= SceneUnloaded;
            SceneManager.activeSceneChanged -= SceneChanged;
            Restore();
        }
    }

    public sealed class VRPanelSurface : MonoBehaviour
    {
        public UIDocument Document;
        public PanelSettings Settings { get; private set; }
        public RenderTexture Texture { get; private set; }
        private RenderTexture previous;
        private Material material;
        private Renderer mesh;
        private Mesh quadMesh;
        private bool ownsTexture;
        private bool previousClearColor;
        private Color previousClearValue;
        private readonly List<UIDocument> documents = new List<UIDocument>();
        private VRMeshPanelMapping nativeMapping;
        private int nativeSlot;
        private string nativeProperty;
        private readonly VRMonitorDesktop desktop = new VRMonitorDesktop();
        private readonly Dictionary<VisualElement, Corners> texturedCorners = new Dictionary<VisualElement, Corners>();
        private sealed class Corners { internal StyleLength TopLeft, TopRight, BottomLeft, BottomRight; }
        private Vector3 floatingPosition, floatingScale;
        private Quaternion floatingRotation;
        public bool MonitorAnchored { get; private set; }
        public bool NativePresenter { get; private set; }
        internal bool PhysicalPresenter => NativePresenter || MonitorAnchored;
        internal bool BindingValid => nativeMapping != null && Settings != null && Settings.targetTexture == Texture && nativeMapping.SourceMatches(Texture);
        public IPanel Panel => Document != null && Document.isActiveAndEnabled ? Document.rootVisualElement?.panel : null;
        internal bool CanInteract => isActiveAndEnabled && Panel != null && (!NativePresenter
            || nativeMapping != null && Settings != null && Settings.targetTexture == Texture && nativeMapping.Active(Texture));

        internal bool MatchesPresenter(MeshRenderer renderer, int slot, string property)
            => NativePresenter && mesh == renderer && nativeSlot == slot && nativeProperty == property;

        internal static VRPanelSurface Borrow(MeshRenderer renderer, Mesh mesh, int slot, Material material, string property, UIDocument document)
        {
            var mapping = new VRMeshPanelMapping(renderer, mesh, material, property, slot);
            VRPanelSurface surface = renderer.gameObject.AddComponent<VRPanelSurface>();
            surface.NativePresenter = true; surface.nativeMapping = mapping;
            surface.mesh = renderer; surface.nativeSlot = slot; surface.nativeProperty = property;
            surface.Settings = document.panelSettings; surface.Texture = surface.Settings.targetTexture;
            surface.Document = document; surface.AddDocument(document);
            return surface;
        }

        internal void Remove()
        {
            Restore();
            if (NativePresenter) Destroy(this); else Destroy(gameObject);
        }

        internal static VRPanelSurface Create(Transform anchor, PanelSettings settings, float width)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "VR UI Toolkit " + settings.name;
            quad.layer = 5;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(anchor, false);
            quad.transform.localPosition = new Vector3(0, 0, -Mathf.Clamp(settings.sortingOrder, -1000, 1000) * 0.0001f);
            VRPanelSurface surface = quad.AddComponent<VRPanelSurface>();
            surface.quadMesh = Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
            var colors = new Color[surface.quadMesh.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            surface.quadMesh.colors = colors;
            quad.GetComponent<MeshFilter>().sharedMesh = surface.quadMesh;
            surface.Settings = settings;
            surface.previous = settings.targetTexture;
            surface.previousClearColor = settings.clearColor;
            surface.previousClearValue = settings.colorClearValue;
            surface.Texture = surface.previous;
            if (surface.Texture == null)
            {
                int pixels = Plugin.PanelPixels.Value;
                surface.Texture = new RenderTexture(pixels, pixels * 9 / 16, 0, RenderTextureFormat.ARGB32) { name = "SWYF VR UI " + settings.name, filterMode = FilterMode.Bilinear };
                surface.Texture.Create();
                surface.ownsTexture = true;
                settings.targetTexture = surface.Texture;
                settings.clearColor = true;
                settings.colorClearValue = Color.clear;
            }
            quad.transform.localScale = new Vector3(width, width * surface.Texture.height / surface.Texture.width, 1);
            Shader shader = Shader.Find("UI/Default") ?? Shader.Find("Unlit/Transparent");
            if (shader == null) throw new System.InvalidOperationException("No transparent UI shader in player.");
            surface.material = new Material(shader) { mainTexture = surface.Texture, color = Color.white, renderQueue = 4000 };
            surface.material.SetInt("unity_GUIZTestMode", Plugin.OccludeFloatingUI.Value ? 4 : 8);
            surface.mesh = quad.GetComponent<Renderer>();
            surface.mesh.sharedMaterial = surface.material;
            surface.mesh.sortingOrder = Mathf.RoundToInt(settings.sortingOrder);
            surface.floatingPosition = quad.transform.localPosition;
            surface.floatingRotation = quad.transform.localRotation;
            surface.floatingScale = quad.transform.localScale;
            return surface;
        }

        internal void UpdatePlacement()
        {
            if (NativePresenter || Texture == null) return;
            bool anchored = desktop.TryPose(Document, (float)Texture.width / Texture.height, out var position, out var rotation, out var size);
            if (anchored)
            {
                transform.SetPositionAndRotation(position, rotation);
                Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
                transform.localScale = new Vector3(size.x / Mathf.Max(.0001f, Mathf.Abs(parentScale.x)),
                    size.y / Mathf.Max(.0001f, Mathf.Abs(parentScale.y)), 1 / Mathf.Max(.0001f, Mathf.Abs(parentScale.z)));
            }
            else if (MonitorAnchored)
            {
                transform.localPosition = floatingPosition; transform.localRotation = floatingRotation; transform.localScale = floatingScale;
            }
            MonitorAnchored = anchored;
            if (material != null) material.SetInt("unity_GUIZTestMode", anchored || Plugin.OccludeFloatingUI.Value ? 4 : 8);
        }

        internal bool OwnsCollider(Collider collider) => collider != null && (collider.transform == transform
            || collider.transform.IsChildOf(transform) || MonitorAnchored && desktop.Monitor != null && collider.transform.IsChildOf(desktop.Monitor.transform));

        internal void RefreshDocument()
        {
            if (material != null) material.SetInt("unity_GUIZTestMode", MonitorAnchored || Plugin.OccludeFloatingUI.Value ? 4 : 8);
            documents.RemoveAll(d => d == null);
            if (Document == null || !Document.isActiveAndEnabled || Document.panelSettings != Settings || Document.rootVisualElement?.panel == null)
            {
                Document = null;
                foreach (UIDocument document in documents)
                    if (document.isActiveAndEnabled && document.panelSettings == Settings && document.rootVisualElement?.panel != null) { Document = document; break; }
            }
            if (!NativePresenter && mesh != null) mesh.enabled = Panel != null;
            if (ownsTexture)
                foreach (UIDocument document in documents)
                    if (document != null && document.isActiveAndEnabled) RepairTexturedCorners(document.rootVisualElement);
        }

        private void RepairTexturedCorners(VisualElement element)
        {
            if (element == null) return;
            var resolved = element.resolvedStyle;
            if (!texturedCorners.ContainsKey(element) && (resolved.borderTopLeftRadius > 0 || resolved.borderTopRightRadius > 0
                || resolved.borderBottomLeftRadius > 0 || resolved.borderBottomRightRadius > 0))
            {
                // This player's rounded-rectangle render path turns textured
                // controls into opaque white blocks. Square geometry retains
                // native colours, labels, focus states and event handlers.
                texturedCorners[element] = new Corners { TopLeft = element.style.borderTopLeftRadius,
                    TopRight = element.style.borderTopRightRadius, BottomLeft = element.style.borderBottomLeftRadius,
                    BottomRight = element.style.borderBottomRightRadius };
                element.RegisterCallback<DetachFromPanelEvent>(RestoreDetachedCorners);
                element.style.borderTopLeftRadius = 0; element.style.borderTopRightRadius = 0;
                element.style.borderBottomLeftRadius = 0; element.style.borderBottomRightRadius = 0;
            }
            foreach (VisualElement child in element.Children()) RepairTexturedCorners(child);
        }

        private void RestoreDetachedCorners(DetachFromPanelEvent evt)
        {
            if (!(evt.target is VisualElement element) || !texturedCorners.TryGetValue(element, out Corners corners)) return;
            RestoreCorners(element, corners);
            texturedCorners.Remove(element);
        }

        private void RestoreCorners(VisualElement element, Corners corners)
        {
            element.UnregisterCallback<DetachFromPanelEvent>(RestoreDetachedCorners);
            element.style.borderTopLeftRadius = corners.TopLeft; element.style.borderTopRightRadius = corners.TopRight;
            element.style.borderBottomLeftRadius = corners.BottomLeft; element.style.borderBottomRightRadius = corners.BottomRight;
        }

        public VisualElement Pick(Vector2 uv)
        {
            IPanel panel = Panel;
            if (panel == null) return null;
            VisualElement picked = panel.Pick(PanelPosition(uv));
            if (picked == null || picked == panel.visualTree || !picked.enabledInHierarchy || !picked.visible) return null;
            // UIDocument containers can span the whole render texture while showing
            // nothing. An empty transparent document must not eat a lower panel's ray.
            foreach (UIDocument document in documents)
                if (picked == document.rootVisualElement) return null;
            float opacity = 1;
            for (VisualElement ancestor = picked; ancestor != null; ancestor = ancestor.parent)
            {
                opacity *= ancestor.resolvedStyle.opacity;
                if (opacity <= 0.001f) return null;
            }
            return picked;
        }

        internal void AddDocument(UIDocument document) { if (!documents.Contains(document)) documents.Add(document); }

        public UIDocument DocumentFor(VisualElement element)
        {
            if (element == null) return null;
            foreach (UIDocument document in documents)
            {
                if (document == null || !document.isActiveAndEnabled || document.panelSettings != Settings) continue;
                VisualElement root = document.rootVisualElement;
                if (root != null && (root == element || root.Contains(element))) return document;
            }
            return null;
        }

        public bool Intersect(Ray ray, out Vector3 world, out Vector2 uv, out float distance, bool bounded = true)
        {
            world = default; uv = default; distance = 0;
            if (!CanInteract) return false;
            if (NativePresenter) return nativeMapping.Intersect(ray, out world, out uv, out distance, bounded);
            if (MonitorAnchored && Vector3.Dot(ray.direction, transform.forward) <= 0) return false;
            Plane plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out distance) || distance > 10f) return false;
            world = ray.GetPoint(distance);
            Vector3 local = transform.InverseTransformPoint(world);
            uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
            return !bounded || (uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1);
        }

        public Vector2 PanelPosition(Vector2 uv)
        {
            Rect layout = Panel.visualTree.layout;
            return new Vector2(uv.x * layout.width, (1 - uv.y) * layout.height);
        }

        public void Restore()
        {
            foreach (var entry in texturedCorners)
            {
                RestoreCorners(entry.Key, entry.Value);
            }
            texturedCorners.Clear();
            if (NativePresenter) { nativeMapping = null; Texture = null; Settings = null; return; }
            if (Settings != null && Settings.targetTexture == Texture)
            {
                Settings.targetTexture = previous;
                if (ownsTexture) { Settings.clearColor = previousClearColor; Settings.colorClearValue = previousClearValue; }
            }
            if (ownsTexture && Texture != null) { Texture.Release(); Destroy(Texture); }
            Texture = null;
            if (material != null) Destroy(material);
            if (quadMesh != null) Destroy(quadMesh);
            quadMesh = null;
            material = null;
        }
        private void OnDestroy() { Restore(); }
    }
}
