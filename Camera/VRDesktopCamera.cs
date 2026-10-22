using System.Collections.Generic;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Camera
{
    // Native camera previews must not redirect the headset into a desktop texture.
    [DefaultExecutionOrder(30800)]
    public sealed class VRDesktopCamera : MonoBehaviour
    {
        private UnityEngine.Camera preview;
        private float nextPreviewTime;
        private int scheduledFrame = -1;
        internal UnityEngine.Camera Source { get; private set; }
        private readonly Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> headVisibility = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
        private readonly HashSet<UnityEngine.Camera> photoCameras = new HashSet<UnityEngine.Camera>();
        private readonly List<Renderer> captureRenderers = new List<Renderer>();
        internal void RegisterPhoto(UnityEngine.Camera camera)
        {
            photoCameras.RemoveWhere(candidate => candidate == null);
            if (camera != null && camera != VRManager.Instance?.Rig?.Head) photoCameras.Add(camera);
        }
        private readonly List<System.WeakReference> owners = new List<System.WeakReference>();

        internal UnityEngine.Camera Assign(object owner, RenderTexture texture)
        {
            if (preview == null)
            {
                var cameraObject = new GameObject("VR desktop camera preview");
                cameraObject.transform.SetParent(transform, false);
                preview = cameraObject.AddComponent<UnityEngine.Camera>();
                var dataType = ReflectionTypes.Find("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
                if (dataType != null)
                {
                    Component data = cameraObject.AddComponent(dataType);
                    GameAdapter.Write(data, "allowXRRendering", false);
                    GameAdapter.Write(data, "renderPostProcessing", false);
                }
            }
            owners.RemoveAll(reference => !reference.IsAlive);
            if (!owners.Exists(reference => ReferenceEquals(reference.Target, owner))) owners.Add(new System.WeakReference(owner));
            if (preview.targetTexture != texture) nextPreviewTime = 0;
            preview.targetTexture = texture;
            Synchronize();
            return preview;
        }

        private void LateUpdate() => Synchronize();

        private void OnEnable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += BeginPreview;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += EndPreview;
        }

        private void BeginPreview(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            RestoreVisibility();
            bool photo = photoCameras.Contains(camera) && camera.targetTexture != null;
            if (!photo && (camera != preview || Source == null || Source == VRManager.Instance?.Rig?.Head)) return;
            Component player = GameAdapter.Controller;
            if (player == null) return;
            player.GetComponentsInChildren(true, captureRenderers);
            foreach (Renderer renderer in captureRenderers)
                if (renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                {
                    headVisibility[renderer] = renderer.shadowCastingMode;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
        }

        private void EndPreview(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (camera == preview || photoCameras.Contains(camera)) RestoreVisibility();
        }

        private void RestoreVisibility()
        {
            foreach (var state in headVisibility) if (state.Key != null) state.Key.shadowCastingMode = state.Value;
            headVisibility.Clear();
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= BeginPreview;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= EndPreview;
            RestoreVisibility();
            photoCameras.Clear(); captureRenderers.Clear();
            if (preview != null) preview.enabled = false;
        }

        private void Synchronize()
        {
            if (preview == null) return;
            object os = GameAdapter.Static("KolkataOsSingleton", "Instance");
            object workspace = GameAdapter.Read(os, "boundWorkspace");
            UnityEngine.Camera source = GameAdapter.Read(workspace, "zoomyWebcamCamera") as UnityEngine.Camera;
            if (source == null) source = VRManager.Instance?.Rig?.Head;
            Source = source;
            RenderTexture texture = preview.targetTexture;
            preview.enabled = source != null && texture != null
                && (Time.unscaledTime >= nextPreviewTime || scheduledFrame == Time.frameCount);
            if (!preview.enabled) return;
            if (scheduledFrame != Time.frameCount)
            {
                scheduledFrame = Time.frameCount;
                nextPreviewTime = Time.unscaledTime + 1f / Mathf.Clamp(Plugin.DesktopCameraFPS.Value, 5, 60);
            }
            preview.CopyFrom(source);
            preview.targetTexture = texture;
            preview.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            preview.cullingMask &= ~(1 << 5);
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                preview.stereoTargetEye = StereoTargetEyeMask.None;
            preview.enabled = true;
        }

        private void OnDestroy()
        {
            foreach (System.WeakReference reference in owners)
                if (reference.Target is object owner && ReferenceEquals(GameAdapter.Read(owner, "m_camera"), preview))
                {
                    GameAdapter.Write(owner, "m_camera", null);
                    if (VRManager.Instance != null && !VRManager.Instance.Ready)
                        GameAdapter.Call(owner, "AssignRenderTexture");
                }
            if (preview != null) Destroy(preview.gameObject);
        }
    }
}
