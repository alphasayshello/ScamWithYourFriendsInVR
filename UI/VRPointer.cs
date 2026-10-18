using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.Patches;
using ScamWithYourFriendsInVR.Input;

namespace ScamWithYourFriendsInVR.UI
{
    [DefaultExecutionOrder(31000)]
    public sealed class VRPointer : MonoBehaviour
    {
        public static readonly int ToolkitPointerId = PointerId.penPointerIdBase + PointerId.penPointerCount - 1;
        public bool HasPanelHit { get; private set; }
        public Vector2 NormalizedPanelPosition { get; private set; }
        private LineRenderer line;
        private GameObject dot;
        private Material material;
        private VRPointerModule module;
        private readonly Dictionary<BaseInputModule, bool> modules = new Dictionary<BaseInputModule, bool>();
        private readonly List<RaycastResult> results = new List<RaycastResult>();
        private readonly RaycastHit[] worldHits = new RaycastHit[64];
        private PointerEventData rayData;
        private EventSystem raySystem;
        private VRPanelSurface pressedPanel;
        private UIDocument pressedWorldDocument;
        private VisualElement pressedElement, hoverElement;
        private Vector3 lastPanelPosition;
        private bool toolkitDown, worldDown, attacking;
        private VisualElement clickRoot, clickTarget;
        private UnityEngine.UIElements.Button clickButton;
        private bool observedClick, observedButton;
        private Vector3 pressPosition;
        private Vector3 previousEventPosition;
        private VisualElement lastClicked;
        private float lastClickTime;
        private int clickCount;
        private readonly VRGripInteractor grab = new VRGripInteractor();



        private EventSystem createdSystem;
        private static MethodInfo setWorldHover;
        private static readonly System.Type pointerState = ReflectionTypes.Find("UnityEngine.UIElements.PointerDeviceState");
        private static readonly MethodInfo pressPointer = pointerState?.GetMethod("PressButton", BindingFlags.Static | BindingFlags.Public);
        private static readonly MethodInfo releasePointer = pointerState?.GetMethod("ReleaseButton", BindingFlags.Static | BindingFlags.Public);
        private static readonly MethodInfo releasePointers = pointerState?.GetMethod("ReleaseAllButtons", BindingFlags.Static | BindingFlags.Public);
        private static readonly MethodInfo savePointer = pointerState?.GetMethod("SavePointerPosition", BindingFlags.Static | BindingFlags.Public);
        private float visualDistance = 2;
        private bool visualHit;

        private void PrepareWorldEvent(EventBase evt, VisualElement target, Vector3 position, EventType type)
        {
            IPanel panel = target?.panel;
            if (panel == null) return;
            savePointer?.Invoke(null, new object[] { ToolkitPointerId, position, panel, ContextType.Player });
            if (GameAdapter.Read(panel, "isFlat") is bool flat && !flat) GameAdapter.Write(evt, "position", position);
            if (setWorldHover == null)
                setWorldHover = panel.GetType().GetMethod("SetTopElementUnderPointer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(int), typeof(VisualElement), typeof(EventBase) }, null);
            VisualElement under = type == EventType.MouseLeaveWindow ? null : hoverElement;
            if (under?.panel != panel) under = null;
            setWorldHover?.Invoke(panel, new object[] { ToolkitPointerId, under, evt });
        }

        private void Start()
        {
            gameObject.layer = 5;
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.002f;
            line.useWorldSpace = true;
            Shader shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                material = new Material(shader) { color = Color.cyan, renderQueue = 4500 };
                material.SetInt("unity_GUIZTestMode", 8);
                line.sharedMaterial = material;
            }
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 2;
            dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "VR pointer reticle";
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.012f;
            dot.layer = 5;
            Destroy(dot.GetComponent<Collider>());
            if (material != null) dot.GetComponent<Renderer>().sharedMaterial = material;
            Hide();
        }

        private void EnsureModule()
        {
            EventSystem system = EventSystem.current;
            if (system == null)
            {
                GameObject go = new GameObject("VR EventSystem");
                go.transform.SetParent(transform, false);
                createdSystem = go.AddComponent<EventSystem>();
                system = createdSystem;
            }
            if (module == null || module.gameObject != system.gameObject)
            {
                if (module != null) { module.Cancel(); Destroy(module); }
                RestoreModules();
                module = system.gameObject.AddComponent<VRPointerModule>();
            }
            foreach (BaseInputModule input in system.GetComponents<BaseInputModule>())
            {
                if (input == module || modules.ContainsKey(input)) continue;
                modules[input] = input.enabled;
                input.enabled = false;
            }
        }

        private void LateUpdate()
        {
            VRManager manager = VRManager.Instance;
            if (manager != null && manager.Interactive && manager.Input.PlayersDown)
            {
                Cancel(); GameAdapter.TogglePlayers(); manager.UI.Place(); Hide(); return;
            }
            if (manager != null && manager.Interactive && manager.Input.MenuDown)
            {
                Cancel(); GameAdapter.Back(); manager.UI.Place(); Hide(); return;
            }
            if (manager == null || !manager.Interactive || manager.Rig.Head == null || !manager.Rig.Right.IsTracked
                || manager.Rig.HeadObstructed && !GameAdapter.MonitorCaptured && !GameAdapter.SettingsOpen)
            {
                Cancel(); Hide(); return;
            }
            EnsureModule();
            if (toolkitDown && pressedWorldDocument == null && (pressedPanel == null || !pressedPanel.CanInteract)) CancelToolkit();
            Ray ray = new Ray(manager.Rig.Right.Aim.position, manager.Rig.Right.Aim.forward);
            float nearest = 10f;
            Transform player = GameAdapter.LocalPlayer?.transform;
            int hitCount = Plugin.OccludeFloatingUI.Value ? Physics.RaycastNonAlloc(ray, worldHits, 10f, ~(1 << 2), QueryTriggerInteraction.Ignore) : 0;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit wall = worldHits[i];
                if (player != null && wall.collider.transform.IsChildOf(player)) continue;
                nearest = Mathf.Min(nearest, wall.distance + 0.01f);
            }
            VRPanelSurface panelHit = null;
            UIDocument worldDocumentHit = null;
            VisualElement elementHit = null;
            Vector2 uvHit = default;
            Vector3 endpoint = ray.GetPoint(nearest);
            RaycastResult graphic = default;
            Vector2 graphicPosition = default;
            foreach (Canvas canvas in manager.UI.Canvases)
            {

                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null || !raycaster.isActiveAndEnabled) continue;
                Plane plane = new Plane(canvas.transform.forward, canvas.transform.position);
                if (!plane.Raycast(ray, out float distance) || distance > nearest) continue;
                Vector3 world = ray.GetPoint(distance);
                UnityEngine.Camera eventCamera = raycaster.eventCamera;
                if (eventCamera == null) continue;
                Vector3 screen = eventCamera.WorldToScreenPoint(world);
                if (screen.z <= 0) continue;
                results.Clear();
                if (rayData == null || raySystem != EventSystem.current) { raySystem = EventSystem.current; rayData = new PointerEventData(raySystem); }
                rayData.position = screen;
                raycaster.Raycast(rayData, results);
                if (results.Count == 0) continue;
                results.Sort((a, b) => b.depth.CompareTo(a.depth));
                graphic = results[0]; graphicPosition = screen;
                nearest = distance; endpoint = world;
            }
            foreach (VRPanelSurface surface in manager.UI.Panels)
            {

                if (surface == null || surface.PhysicalPresenter || !surface.Intersect(ray, out Vector3 world, out Vector2 uv, out float distance) || distance > nearest) continue;
                VisualElement element = surface.Pick(uv);
                if (element == null) continue;
                panelHit = surface; elementHit = element; uvHit = uv;
                graphic = default; nearest = distance; endpoint = world;
            }
            float nativeLimit = nearest;
            bool nativeSurfaces = manager.UI.Panels.Exists(p => p != null && p.PhysicalPresenter);
            int nativeHits = manager.UI.WorldDocuments.Count == 0 && !nativeSurfaces ? 0
                : Physics.RaycastNonAlloc(ray, worldHits, 10f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            foreach (VRPanelSurface surface in manager.UI.Panels)
            {

                if (surface == null || !surface.PhysicalPresenter
                    || !surface.Intersect(ray, out Vector3 world, out Vector2 uv, out float distance) || distance > nearest) continue;
                bool blocked = false;
                for (int i = 0; i < nativeHits; i++)
                {
                    Collider obstacle = worldHits[i].collider;
                    if (player != null && obstacle.transform.IsChildOf(player)) continue;
                    if (surface.OwnsCollider(obstacle)) continue;
                    if (worldHits[i].distance + 0.02f < distance) { blocked = true; break; }
                }
                if (blocked) continue;
                VisualElement element = surface.Pick(uv);
                if (element == null) continue;
                panelHit = surface; elementHit = element; uvHit = uv;
                graphic = default; nativeLimit = nearest = distance; endpoint = world;
            }
            for (int i = 0; i < nativeHits; i++)
            {
                Collider obstacle = worldHits[i].collider;
                if (player != null && obstacle.transform.IsChildOf(player)) continue;
                if (obstacle.GetComponentInParent<UIDocument>() != null) continue;
                nativeLimit = Mathf.Min(nativeLimit, worldHits[i].distance + 0.02f);
            }
            Vector3 worldPanelPosition = default;
            foreach (UIDocument document in manager.UI.WorldDocuments)
            {

                if (!VRWorldPanelPicker.TryPick(document, ray, out VisualElement element, out Vector3 point, out Vector3 position, out float distance)
                    || distance > nativeLimit) continue;
                panelHit = null; worldDocumentHit = document; elementHit = element; worldPanelPosition = position;
                graphic = default; nativeLimit = nearest = distance; endpoint = point;
            }
            HasPanelHit = panelHit != null || graphic.gameObject != null;
            HasPanelHit |= worldDocumentHit != null;
            if (panelHit != null) NormalizedPanelPosition = uvHit;
            else if (worldDocumentHit != null)
            {
                VisualElement root = worldDocumentHit.rootVisualElement;
                Vector3 local = root.worldTransform.inverse.MultiplyPoint3x4(worldPanelPosition);
                Rect bounds = root.layout;
                NormalizedPanelPosition = new Vector2(Mathf.Clamp01(local.x / Mathf.Max(1, bounds.width)),
                    Mathf.Clamp01(1 - local.y / Mathf.Max(1, bounds.height)));
            }
            else if (graphic.gameObject != null) NormalizedPanelPosition = new Vector2(graphicPosition.x / Screen.width, graphicPosition.y / Screen.height);
            if (hoverElement != elementHit)
            {
                Send3D(hoverElement, EventType.MouseLeaveWindow, lastPanelPosition, 0, toolkitDown);
                hoverElement = elementHit;
                if (panelHit != null) Send3D(hoverElement, EventType.MouseEnterWindow, panelHit.PanelPosition(uvHit), 0, toolkitDown);
                else if (worldDocumentHit != null) Send3D(hoverElement, EventType.MouseEnterWindow, worldPanelPosition, 0, toolkitDown);
            }
            if (panelHit != null) lastPanelPosition = panelHit.PanelPosition(uvHit);
            else if (worldDocumentHit != null) lastPanelPosition = worldPanelPosition;
            if (toolkitDown && pressedPanel != null && pressedPanel.Intersect(ray, out _, out Vector2 dragUv, out _, false))
                lastPanelPosition = pressedPanel.PanelPosition(dragUv);
            if (toolkitDown && pressedWorldDocument != null && VRWorldPanelPicker.TryDrag(pressedWorldDocument, CapturedOrPressed(), ray, out Vector3 nativeDrag))
                lastPanelPosition = nativeDrag;
            VisualElement recipient = toolkitDown ? CapturedOrPressed() : elementHit;
            Send3D(recipient, EventType.MouseMove, lastPanelPosition, 0, toolkitDown);
            float scrollAxis = Mathf.Abs(manager.Input.Move.y) > .01f ? manager.Input.Move.y : manager.Input.Turn.y;
            float scroll = -scrollAxis * 18f * Time.unscaledDeltaTime;
            bool toolkitHit = panelHit != null || worldDocumentHit != null;
            if (toolkitHit && Mathf.Abs(scroll) > 0.01f) Scroll(elementHit, lastPanelPosition, scroll);
            if (module.TryDragPosition(ray, out Vector2 dragPosition)) graphicPosition = dragPosition;
            module.Tick(graphic, graphicPosition, manager.Input.TriggerDown && !toolkitHit,
                manager.Input.TriggerUp || toolkitDown, !toolkitHit ? scroll : 0);
            if (manager.Input.TriggerDown && toolkitHit)
            {
                pressedPanel = panelHit; pressedElement = elementHit; toolkitDown = true;
                pressedWorldDocument = worldDocumentHit;
                BeginClickWatch(elementHit);
                Send3D(elementHit, EventType.MouseDown, lastPanelPosition, 0, true);
                manager.Keyboard.TryOpen(elementHit, worldDocumentHit ?? panelHit.DocumentFor(elementHit));
            }

            if (manager.Input.TriggerDown && !HasPanelHit)
            {
                if (manager.Input.Grip) { GameAdapter.Attack(true); attacking = true; }
                else { GameAdapter.Interact(true); worldDown = true; }
            }
            if (manager.Input.TriggerUp)
            {
                ReleaseToolkit();
                if (attacking) { GameAdapter.Attack(false); attacking = false; }
                if (worldDown) { GameAdapter.Interact(false); worldDown = false; }
            }
            grab.Tick(HasPanelHit);
            if (manager.Input.SecondaryDown && elementHit != null)
            {
                Send3D(elementHit, EventType.MouseDown, lastPanelPosition, 1, true);
                Send3D(elementHit, EventType.MouseUp, lastPanelPosition, 1, false);
            }
            if (manager.Input.TriggerUp && graphic.gameObject != null) manager.Keyboard.TryOpen(graphic.gameObject);
            visualHit = HasPanelHit || worldDown;
            visualDistance = visualHit ? Vector3.Distance(ray.origin, endpoint) : Mathf.Min(nearest, 2);
            line.enabled = true; dot.SetActive(visualHit);
            line.SetPosition(0, ray.origin); line.SetPosition(1, ray.GetPoint(visualDistance)); dot.transform.position = endpoint;
        }

        private void OnEnable() => Application.onBeforeRender += BeforeRender;
        private void OnDisable() { Application.onBeforeRender -= BeforeRender; Cancel(); Hide(); }

        [BeforeRenderOrder(-100)]
        private void BeforeRender()
        {
            var manager = VRManager.Instance;
            if (line == null || !line.enabled || manager == null || !manager.Interactive || manager.Rig.HeadObstructed) return;
            if (!VRInputManager.Tracked(UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand))) return;
            manager.Rig.Left.Sync(); manager.Rig.Right.Sync();
            if (!manager.Rig.Right.IsTracked) { Hide(); return; }
            Transform aim = manager.Rig.Right.Aim;
            Vector3 endpoint = aim.position + aim.forward * visualDistance;
            line.SetPosition(0, aim.position); line.SetPosition(1, endpoint);
            if (visualHit && dot != null) dot.transform.position = endpoint;
        }

        private VisualElement CapturedOrPressed()
        {
            if (pressedWorldDocument?.rootVisualElement?.panel != null)
                return pressedWorldDocument.rootVisualElement.panel.GetCapturingElement(ToolkitPointerId) as VisualElement ?? pressedElement;
            if (pressedPanel?.Panel == null) return pressedElement;
            return pressedPanel.Panel.GetCapturingElement(ToolkitPointerId) as VisualElement ?? pressedElement;
        }

        private void Send(VisualElement target, EventType type, Vector2 position, int button, bool held) => Send3D(target, type, position, button, held);

        private void Send3D(VisualElement target, EventType type, Vector3 position, int button, bool held)
        {
            // Pooling an up event releases UI Toolkit's global button state even
            // when the pressed document was detached before cancellation.
            if (target?.panel == null && type != EventType.MouseUp) return;
            if (type == EventType.MouseDown)
            {
                clickCount = lastClicked == target && Time.unscaledTime - lastClickTime < 0.3f ? clickCount + 1 : 1;
                lastClicked = target; lastClickTime = Time.unscaledTime;
            }
            Event source = new Event { type = type, mousePosition = position, button = button, clickCount = Mathf.Max(1, clickCount),
                delta = position - previousEventPosition };
            previousEventPosition = position;
            switch (type)
            {
                case EventType.MouseDown:
                    using (PointerDownEvent evt = PointerDownEvent.GetPooled(source)) { PreparePointer(evt, button, held); PrepareWorldEvent(evt, target, position, type); evt.target = target; target.SendEvent(evt); }
                    break;
                case EventType.MouseUp:
                    using (PointerUpEvent evt = PointerUpEvent.GetPooled(source))
                        if (target?.panel != null) { PreparePointer(evt, button, held); PrepareWorldEvent(evt, target, position, type); evt.target = target; target.SendEvent(evt); }
                    break;
                case EventType.MouseMove:
                    using (PointerMoveEvent evt = PointerMoveEvent.GetPooled(source)) { PreparePointer(evt, button, held); PrepareWorldEvent(evt, target, position, type); evt.target = target; target.SendEvent(evt); }
                    break;
                case EventType.MouseEnterWindow:
                    using (PointerEnterEvent evt = PointerEnterEvent.GetPooled(source)) { PreparePointer(evt, button, held); PrepareWorldEvent(evt, target, position, type); evt.target = target; target.SendEvent(evt); }
                    break;
                case EventType.MouseLeaveWindow:
                    using (PointerLeaveEvent evt = PointerLeaveEvent.GetPooled(source)) { PreparePointer(evt, button, held); PrepareWorldEvent(evt, target, position, type); evt.target = target; target.SendEvent(evt); }
                    break;
            }
        }

        private static void PreparePointer(EventBase evt, int button, bool held)
        {
            // The native runtime continues to pump the desktop mouse between
            // Unity frames. Keep controller capture independent of that cursor.
            GameAdapter.Write(evt, "pointerId", ToolkitPointerId);
            GameAdapter.Write(evt, "pointerType", UnityEngine.UIElements.PointerType.pen);
            GameAdapter.Write(evt, "isPrimary", true);
            GameAdapter.Write(evt, "pressedButtons", held ? 1 << button : 0);
            if (evt is PointerDownEvent) pressPointer?.Invoke(null, new object[] { ToolkitPointerId, button });
            else if (evt is PointerUpEvent) releasePointer?.Invoke(null, new object[] { ToolkitPointerId, button });
        }

        private static void Scroll(VisualElement target, Vector2 position, float amount)
        {
            if (target?.panel == null) return;
            Event source = new Event { type = EventType.ScrollWheel, mousePosition = position, delta = new Vector2(0, amount) };
            using (WheelEvent evt = WheelEvent.GetPooled(source)) { evt.target = target; target.SendEvent(evt); }
        }

        private void ReleaseToolkit()
        {
            if (!toolkitDown) return;
            VisualElement recipient = CapturedOrPressed();
            bool releaseInside = clickTarget != null && hoverElement != null && (clickTarget == hoverElement || clickTarget.Contains(hoverElement));
            float threshold = pressedWorldDocument != null ? .02f : 10;
            bool eligible = releaseInside && Vector3.Distance(lastPanelPosition, pressPosition) <= threshold;
            Send3D(recipient, EventType.MouseUp, lastPanelPosition, 0, false);
            if (eligible && clickTarget?.panel != null)
            {
                Event source = new Event { type = EventType.MouseUp, mousePosition = lastPanelPosition, button = 0, clickCount = Mathf.Max(1, clickCount) };
                using (var up = PointerUpEvent.GetPooled(source))
                {
                    PreparePointer(up, 0, false); PrepareWorldEvent(up, clickTarget, lastPanelPosition, EventType.MouseUp);
                    if (!observedButton && clickButton != null) GameAdapter.Call(clickButton.clickable, "SimulateSingleClick", up, 100);
                    if (!observedClick)
                        using (ClickEvent click = ClickEvent.GetPooled(up)) { click.target = clickTarget; clickTarget.SendEvent(click); }
                }
            }
            EndClickWatch();
            if (recipient?.panel != null && recipient.HasPointerCapture(ToolkitPointerId)) recipient.ReleasePointer(ToolkitPointerId);
            toolkitDown = false; pressedPanel = null; pressedWorldDocument = null; pressedElement = null;
        }

        private void BeginClickWatch(VisualElement element)
        {
            EndClickWatch(); observedClick = observedButton = false; pressPosition = lastPanelPosition;
            clickButton = element as UnityEngine.UIElements.Button ?? element?.GetFirstAncestorOfType<UnityEngine.UIElements.Button>();
            clickTarget = clickButton ?? element;
            clickRoot = element?.panel?.visualTree;
            clickRoot?.RegisterCallback<ClickEvent>(ObserveClick, TrickleDown.TrickleDown);
            if (clickButton != null) clickButton.clicked += ObserveButton;
        }
        private void ObserveClick(ClickEvent evt) { if (evt.pointerId == ToolkitPointerId) observedClick = true; }
        private void ObserveButton() => observedButton = true;
        private void EndClickWatch()
        {
            clickRoot?.UnregisterCallback<ClickEvent>(ObserveClick, TrickleDown.TrickleDown);
            if (clickButton != null) clickButton.clicked -= ObserveButton;
            clickRoot = null; clickTarget = null; clickButton = null;
        }

        private void CancelToolkit()
        {
            EndClickWatch();
            if (toolkitDown)
            {
                VisualElement target = CapturedOrPressed();
                if (target?.panel != null)
                    using (PointerCancelEvent evt = PointerCancelEvent.GetPooled())
                    {
                        GameAdapter.Write(evt, "pointerId", ToolkitPointerId);
                        GameAdapter.Write(evt, "position", lastPanelPosition);
                        evt.target = target; target.SendEvent(evt);
                    }
            }
            ReleaseToolkit();
        }

        public void Cancel()
        {
            releasePointers?.Invoke(null, new object[] { ToolkitPointerId });

            if (attacking) { GameAdapter.Attack(false); attacking = false; }
            CancelToolkit();
            grab.Cancel();
            module?.Cancel();
            if (worldDown) GameAdapter.Interact(false);
            worldDown = false;
            Send3D(hoverElement, EventType.MouseLeaveWindow, lastPanelPosition, 0, false);
            hoverElement = null;
            HasPanelHit = false;
        }

        private void Hide() { if (line != null) line.enabled = false; if (dot != null) dot.SetActive(false); }
        private void RestoreModules() { foreach (var pair in modules) if (pair.Key != null) pair.Key.enabled = pair.Value; modules.Clear(); }
        private void OnDestroy()
        {
            Cancel(); RestoreModules();
            if (module != null) Destroy(module);
            if (material != null) Destroy(material);
        }
    }

    public sealed class VRPointerModule : PointerInputModule
    {
        private PointerEventData data;
        public override void Process() { }
        public override bool ShouldActivateModule() => isActiveAndEnabled && VRManager.Instance != null && VRManager.Instance.Ready;
        public override bool IsPointerOverGameObject(int pointerId) => data?.pointerEnter != null;

        internal bool TryDragPosition(Ray ray, out Vector2 position)
        {
            position = default;
            if (data?.pointerPress == null || data.pressEventCamera == null) return false;
            Transform target = data.pointerPress.transform;
            if (!new Plane(target.forward, target.position).Raycast(ray, out float distance)) return false;
            position = data.pressEventCamera.WorldToScreenPoint(ray.GetPoint(distance));
            return true;
        }

        internal void Tick(RaycastResult hit, Vector2 position, bool down, bool up, float scroll)
        {
            if (data == null) data = new PointerEventData(eventSystem) { pointerId = -1001, button = PointerEventData.InputButton.Left };
            data.delta = position - data.position;
            data.position = position;
            data.pointerCurrentRaycast = hit;
            HandlePointerExitAndEnter(data, hit.gameObject);
            if (down && hit.gameObject != null)
            {
                data.eligibleForClick = true; data.dragging = false;
                data.pressPosition = position; data.pointerPressRaycast = hit;
                data.clickCount = data.lastPress == hit.gameObject && Time.unscaledTime - data.clickTime < 0.3f ? data.clickCount + 1 : 1;
                data.clickTime = Time.unscaledTime;
                data.pointerPress = ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.pointerDownHandler)
                    ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
                data.rawPointerPress = hit.gameObject;
                data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(hit.gameObject);
                if (data.pointerDrag != null) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
                if (hit.gameObject.GetComponentInParent<VRKeyboardMarker>() == null)
                    eventSystem.SetSelectedGameObject(ExecuteEvents.GetEventHandler<ISelectHandler>(hit.gameObject), data);
            }
            if (data.pointerDrag != null && !up)
            {
                if (!data.dragging && (position - data.pressPosition).sqrMagnitude >= eventSystem.pixelDragThreshold * eventSystem.pixelDragThreshold)
                {
                    ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
                    data.dragging = true; data.eligibleForClick = false;
                }
                if (data.dragging) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
            }
            if (Mathf.Abs(scroll) > 0.01f && hit.gameObject != null)
            {
                data.scrollDelta = new Vector2(0, -scroll * 20);
                ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.scrollHandler);
            }
            if (up) Release(hit.gameObject, true);
        }

        private void Release(GameObject target, bool click)
        {
            if (data == null) return;
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
            if (click && data.eligibleForClick && data.pointerPress == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target))
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
            if (data.dragging)
            {
                if (click) ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.dropHandler);
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
            }
            data.eligibleForClick = false; data.dragging = false;
            data.pointerPress = null; data.rawPointerPress = null; data.pointerDrag = null;
        }

        internal void Cancel() { Release(null, false); if (data != null) HandlePointerExitAndEnter(data, null); }
        protected override void OnDisable() { Cancel(); base.OnDisable(); }
    }
}
