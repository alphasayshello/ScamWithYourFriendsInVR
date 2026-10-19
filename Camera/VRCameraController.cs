using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.XR;
using ScamWithYourFriendsInVR.Input;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Camera
{
    [DefaultExecutionOrder(30000)]
    public sealed class VRCameraController : MonoBehaviour
    {
        public UnityEngine.Camera Head { get; private set; }
        public VRHandController Right { get; private set; }
        public VRHandController Left { get; private set; }
        public Transform Origin { get; private set; }
        public bool HeadObstructed { get; private set; }
        private Transform savedParent;
        private Vector3 savedPosition, savedScale, center, menuAnchor;
        private Quaternion savedRotation;
        private float savedNear;
        private int savedCullingMask;
        private StereoTargetEyeMask savedStereo;
        private bool builtInStereo;
        private readonly Dictionary<Behaviour, bool> behaviours = new Dictionary<Behaviour, bool>();
        private readonly Dictionary<Behaviour, bool> menuBehaviours = new Dictionary<Behaviour, bool>();
        private UnityEngine.Camera menuCamera;
        private float nextMenuScan;
        private readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        private AudioListener addedListener;
        private Transform player;
        private bool calibrated;
        private bool floorOrigin;
        private bool wasSitting;
        private float menuYaw;
        private float centerYaw;
        private float trackingYaw, lastBodyYaw;
        private Vector3 eyeOffset, lastTrackedPosition;
        private bool hasEyeOffset;
        private float anchorSettlesUntil;
        private bool hadPoseContext, lastAgent, lastRagdolled;
        public Vector3 AuthoredEye { get; private set; }
        private readonly VRAvatarEyeAnchor avatarEyes = new VRAvatarEyeAnchor();
        private readonly RaycastHit[] obstructionHits = new RaycastHit[64];
        private readonly Collider[] overlaps = new Collider[64];
        private GameObject collisionCurtain;
        private Material collisionMaterial;

        private void Awake()
        {
            Origin = new GameObject("Tracking origin").transform;
            Origin.SetParent(transform, false);
            Left = CreateHand(XRNode.LeftHand);
            Right = CreateHand(XRNode.RightHand);
        }

        private VRHandController CreateHand(XRNode node)
        {
            GameObject hand = new GameObject(node.ToString());
            hand.transform.SetParent(Origin, false);
            VRHandController result = hand.AddComponent<VRHandController>();
            result.Node = node;
            return result;
        }

        private void BindCamera(UnityEngine.Camera camera)
        {
            Restore();
            Head = camera;
            savedParent = camera.transform.parent;
            savedPosition = camera.transform.localPosition;
            savedRotation = camera.transform.localRotation;
            savedScale = camera.transform.localScale;
            savedNear = camera.nearClipPlane;
            savedCullingMask = camera.cullingMask;
            builtInStereo = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null;
            if (builtInStereo) savedStereo = camera.stereoTargetEye;
            menuAnchor = camera.transform.position;
            menuYaw = camera.transform.eulerAngles.y;
            foreach (Behaviour behaviour in camera.GetComponents<Behaviour>())
                if (behaviour.GetType().FullName == "Unity.Cinemachine.CinemachineBrain") Disable(behaviour);
            foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.gameObject != camera.gameObject) Disable(listener);
            AudioListener own = camera.GetComponent<AudioListener>();
            if (own == null) addedListener = camera.gameObject.AddComponent<AudioListener>();
            else { behaviours[own] = own.enabled; own.enabled = true; }
            // Keep the camera in its native scene hierarchy so scene unload destroys it.
            // Tracking is applied in world space below; parenting to the persistent rig
            // would otherwise retain an old scene's camera across scene transitions.
            camera.nearClipPlane = 0.03f;
            camera.cullingMask |= 1 << 5;
            if (builtInStereo) camera.stereoTargetEye = StereoTargetEyeMask.Both;
            collisionCurtain = GameObject.CreatePrimitive(PrimitiveType.Quad);
            collisionCurtain.name = "VR head collision curtain";
            Destroy(collisionCurtain.GetComponent<Collider>());
            collisionCurtain.transform.SetParent(camera.transform, false);
            collisionCurtain.transform.localPosition = new Vector3(0, 0, 0.05f);
            Shader shader = Shader.Find("UI/Default");
            if (shader != null)
            {
                collisionMaterial = new Material(shader) { color = Color.black, renderQueue = 5000 };
                collisionCurtain.GetComponent<Renderer>().sharedMaterial = collisionMaterial;
            }
            collisionCurtain.SetActive(false);
            Recenter();
        }

        private void Disable(Behaviour value) { behaviours[value] = value.enabled; value.enabled = false; }

        public void Recenter()
        {
            VRManager.Instance?.Pointer?.ResetAim();
            inputs.Clear();
            SubsystemManager.GetSubsystems(inputs);
            floorOrigin = false;
            foreach (XRInputSubsystem input in inputs)
            {
                if (!input.running) continue;
                input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                floorOrigin |= input.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor;
            }
            calibrated = false;
            hasEyeOffset = false;
            anchorSettlesUntil = Time.unscaledTime + .75f;
        }

        private void LateUpdate()
        {
            VRManager manager = VRManager.Instance;
            if (manager == null || !manager.Ready) return;
            bool inMenu = GameAdapter.InMainMenu;
            UpdateMenuCamera(inMenu);
            UnityEngine.Camera candidate = inMenu ? menuCamera : GameAdapter.Read(GameAdapter.Interactor, "playerCamera") as UnityEngine.Camera;
            if (!IsSceneCamera(candidate)) candidate = UnityEngine.Camera.main;
            if (!IsSceneCamera(candidate) && IsSceneCamera(Head)) candidate = Head;
            if (!IsSceneCamera(candidate))
            {
                // The menu demo scene disables MainCamera and renders through an
                // untagged scene camera. Exclude webcam/portrait render textures.
                candidate = null;
                foreach (UnityEngine.Camera camera in UnityEngine.Camera.allCameras)
                    if (IsSceneCamera(camera) && (candidate == null || camera.depth > candidate.depth)) candidate = camera;
            }
            if (candidate != null && candidate.isActiveAndEnabled && candidate != Head) BindCamera(candidate);
            if (Head == null || !Head.isActiveAndEnabled)
            {
                Restore();
                return;
            }
            if (Head.transform.parent != savedParent)
            {
                savedParent = Head.transform.parent; savedPosition = Head.transform.localPosition; savedRotation = Head.transform.localRotation;
                Recenter();
            }
            Component localPlayer = GameAdapter.LocalPlayer;
            Component subject = GameAdapter.ResolveViewPlayer(localPlayer);
            Component viewController = GameAdapter.Read(subject, "controller") as Component ?? GameAdapter.Controller;
            Transform nextPlayer = inMenu ? null : subject?.transform;
            if (nextPlayer != player) { player = nextPlayer; Recenter(); }
            object ragdoll = GameAdapter.Read(subject, "RagdollManager");
            bool ragdolled = GameAdapter.Read(ragdoll, "IsRagdollActive") is bool down && down
                || GameAdapter.Read(ragdoll, "IsHiding") is bool hiding && hiding;
            Type modeType = ReflectionTypes.Find("PlayerModeManager");
            Component mode = subject != null && modeType != null ? subject.GetComponent(modeType) : null;
            bool agent = GameAdapter.Read(mode, "isAgent") is bool police && police;
            if (!hadPoseContext || lastAgent != agent || lastRagdolled != ragdolled)
            {
                hadPoseContext = true; lastAgent = agent; lastRagdolled = ragdolled; Recenter();
            }
            bool sitting = GameAdapter.Read(GameAdapter.Interactor, "isSitting") is bool seated && seated;
            if (sitting != wasSitting) { wasSitting = sitting; Recenter(); }
            if (manager.Input.RecenterDown) Recenter();
            InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!VRInputManager.Tracked(device) || !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position)
                || !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation)) return;
            // OpenXR can report a zero pose while tracking is being acquired.
            // Do not latch that as the user's floor-relative standing height.
            if (!calibrated && floorOrigin && position.y < .1f) return;
            if (calibrated && Vector3.Distance(position, lastTrackedPosition) > 1f) { Recenter(); return; }
            lastTrackedPosition = position;
            bool recentered = !calibrated;
            if (recentered) { center = position; centerYaw = rotation.eulerAngles.y; calibrated = true; }
            Vector3 anchor = menuAnchor;
            float yaw = menuYaw;
            CharacterController body = null;
            if (player != null)
            {
                body = player.GetComponent<CharacterController>();
                if (body == null && viewController != null) body = viewController.GetComponent<CharacterController>();
                anchor = player.position;
                if (body != null && body.enabled) anchor.y = body.bounds.min.y;
                yaw = (GameAdapter.Read(viewController, "polarTransform") as Transform ?? player).eulerAngles.y;
            }
            else anchor.y -= !Plugin.Seated.Value && floorOrigin ? center.y : Plugin.SeatedHeight.Value;
            float heightOffset = Plugin.Seated.Value || !floorOrigin ? Plugin.SeatedHeight.Value - center.y : 0;
            // A native chair moves the authored eye marker independently of the
            // standing capsule. Keep room-scale offsets relative to that marker.
            Transform nativeEye = player != null ? (ragdolled ? GameAdapter.Call(ragdoll, "GetCurrentRagdollCameraTransform") as Transform
                : GameAdapter.Read(viewController, "defaultCamTransform") as Transform ?? GameAdapter.Read(viewController, "cameraTransform") as Transform) : null;
            if (nativeEye != null)
            {
                // The game's authored first-person eye can sit above a literal
                // metre-scale capsule. Use it for standing as well as chairs.
                // Avoid feeding an already tracked camera back into its origin.
                anchor = nativeEye == Head.transform || nativeEye.IsChildOf(Head.transform)
                    ? (savedParent != null ? savedParent.TransformPoint(savedPosition) : savedPosition) : nativeEye.position;
                if (!ragdolled && avatarEyes.TryPosition(player, out Vector3 eyePosition)) anchor = eyePosition;
                if (!hasEyeOffset || ragdolled || Time.unscaledTime < anchorSettlesUntil) { eyeOffset = anchor - player.position; hasEyeOffset = true; }
                // Native eye meshes follow animation and the tracked head bone.
                // Reusing their new position each frame feeds tracking back into itself.
                anchor = player.position + eyeOffset;
                AuthoredEye = anchor;
                anchor.y -= center.y + heightOffset;
            }
            if (recentered) trackingYaw = yaw - centerYaw;
            else if (player != null) trackingYaw += Mathf.DeltaAngle(lastBodyYaw, yaw);
            lastBodyYaw = yaw;
            Quaternion yawRotation = Quaternion.Euler(0, trackingYaw, 0);
            bool bodyInput = player != null && subject == localPlayer && !GameAdapter.AvatarUnavailable(localPlayer)
                && !sitting && manager.Interactive && GameAdapter.Allowed(GameAdapter.Interactor);
            if (bodyInput)
            {
                Vector3 moved = VRRoomScaleBody.Follow(body, yawRotation, position - center);
                center = VRRoomScaleBody.Consume(center, yawRotation, moved);
                anchor += moved;
            }
            Origin.SetPositionAndRotation(anchor + yawRotation * new Vector3(-center.x, heightOffset, -center.z), yawRotation);
            Vector3 desired = Origin.TransformPoint(position);
            Vector3 start = new Vector3(anchor.x, desired.y, anchor.z);
            Vector3 delta = desired - start;
            HeadObstructed = false;
            if (player != null && delta.sqrMagnitude > 0.0001f)
            {
                float allowed = delta.magnitude;
                int count = Physics.SphereCastNonAlloc(start, 0.12f, delta.normalized, obstructionHits, allowed, Plugin.CollisionMask.Value, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = obstructionHits[i];
                    if (player != null && hit.collider.transform.IsChildOf(player)) continue;
                    allowed = Mathf.Min(allowed, Mathf.Max(0, hit.distance - 0.02f));
                    HeadObstructed = true;
                }
                desired = start + delta.normalized * allowed;
            }
            int overlapCount = player == null ? 0 : Physics.OverlapSphereNonAlloc(desired, 0.1f, overlaps, Plugin.CollisionMask.Value, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlapCount; i++)
            {
                Collider overlap = overlaps[i];
                if (player != null && overlap.transform.IsChildOf(player)) continue;
                HeadObstructed = true;
                break;
            }
            Head.transform.SetPositionAndRotation(desired, Origin.rotation * rotation);
            if (bodyInput && GameAdapter.Polar != null)
            {
                Transform polar = GameAdapter.Polar;
                Vector3 facing = Vector3.ProjectOnPlane(Head.transform.forward, Vector3.up);
                if (facing.sqrMagnitude > .01f) polar.rotation = Quaternion.LookRotation(facing, Vector3.up);
                lastBodyYaw = polar.eulerAngles.y;
                Head.transform.SetPositionAndRotation(desired, Origin.rotation * rotation);
            }
            Vector3 parentScale = Head.transform.parent != null ? Head.transform.parent.lossyScale : Vector3.one;
            Head.transform.localScale = new Vector3(Reciprocal(parentScale.x), Reciprocal(parentScale.y), Reciprocal(parentScale.z));
            if (collisionCurtain != null) collisionCurtain.SetActive(HeadObstructed && !GameAdapter.MonitorCaptured && !GameAdapter.SettingsOpen);
            Left.Sync();
            Right.Sync();
            if (recentered) Plugin.Log.LogInfo("VR calibration: rawHead=" + position + "; floor=" + floorOrigin
                + "; nativeEye=" + (nativeEye != null ? AuthoredEye.ToString() : "menu/fallback")
                + "; placedHead=" + desired + "; player=" + player?.name + "; sitting=" + sitting);
        }

        private void UpdateMenuCamera(bool active)
        {
            if (!active)
            {
                foreach (var state in menuBehaviours) if (state.Key != null) state.Key.enabled = state.Value;
                menuBehaviours.Clear(); menuCamera = null; nextMenuScan = 0;
                return;
            }
            if (Time.unscaledTime < nextMenuScan) return;
            nextMenuScan = Time.unscaledTime + 1;
            var type = ReflectionTypes.Find("MainMenuCameraHandler");
            if (type == null) return;
            foreach (Component component in FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!(component is Behaviour handler) || !handler.gameObject.activeInHierarchy) continue;
                if (!menuBehaviours.ContainsKey(handler))
                {
                    menuBehaviours[handler] = handler.enabled;
                    handler.enabled = false;
                }
                if (GameAdapter.Read(handler, "normalCam") is GameObject normal)
                {
                    var camera = normal.GetComponentInChildren<UnityEngine.Camera>(true);
                    if (IsSceneCamera(camera)) menuCamera = camera;
                }
            }
        }

        private static bool IsSceneCamera(UnityEngine.Camera camera)
            => camera != null && camera.isActiveAndEnabled && camera.targetTexture == null && camera.targetDisplay == 0;
        private static float Reciprocal(float value) => Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;

        public void Restore()
        {
            if (Head != null)
            {
                Head.transform.SetParent(savedParent, false);
                Head.transform.localPosition = savedPosition;
                Head.transform.localRotation = savedRotation;
                Head.transform.localScale = savedScale;
                Head.nearClipPlane = savedNear;
                Head.cullingMask = savedCullingMask;
                if (builtInStereo) Head.stereoTargetEye = savedStereo;
            }
            foreach (KeyValuePair<Behaviour, bool> item in behaviours)
                if (item.Key != null) item.Key.enabled = item.Value;
            behaviours.Clear();
            if (addedListener != null) Destroy(addedListener);
            if (collisionCurtain != null) Destroy(collisionCurtain);
            if (collisionMaterial != null) Destroy(collisionMaterial);
            collisionCurtain = null; collisionMaterial = null;
            addedListener = null;
            Head = null;
        }

        private void OnDestroy() { Restore(); UpdateMenuCamera(false); }
    }
}
