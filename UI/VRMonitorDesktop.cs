using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.UI
{
    internal sealed class VRMonitorDesktop
    {
        private Component monitor;
        private MeshRenderer screen;
        private float nextResolve;
        internal Component Monitor => monitor;

        internal bool TryPose(UnityEngine.UIElements.UIDocument document, float aspect,
            out Vector3 position, out Quaternion rotation, out Vector2 size)
        {
            position = default; rotation = Quaternion.identity; size = default;
            if (!Plugin.PhysicalDesktop.Value || !GameAdapter.MonitorCaptured || document == null) return false;
            object os = GameAdapter.Static("KolkataOsSingleton", "Instance");
            if (!ReferenceEquals(GameAdapter.Read(os, "kolkataOsDocument"), document)) return false;
            Component active = GameAdapter.Read(os, "activeLocalMonitor") as Component;
            if (active == null || !active.gameObject.activeInHierarchy) return false;
            if (active != monitor) { monitor = active; screen = null; nextResolve = 0; }
            if (screen == null && Time.unscaledTime >= nextResolve)
            {
                nextResolve = Time.unscaledTime + .5f;
                Component workspace = GameAdapter.Read(os, "boundWorkspace") as Component;
                object state = GameAdapter.Read(workspace, "callStateScreen");
                if (!ReferenceEquals(GameAdapter.Read(state, "MonitorInteractable"), monitor)) return false;
                var type = ReflectionTypes.Find("ScammerWorkspaceWebRtc");
                if (workspace != null && type != null)
                    screen = GameAdapter.Read(workspace.GetComponent(type), "monitorRenderer") as MeshRenderer;
            }
            if (screen == null) return false;
            Mesh mesh = screen.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return false;
            Bounds bounds = mesh.bounds;
            // The current native presenter is an XY quad. Unknown geometry keeps
            // the floating fallback instead of guessing a monitor calibration.
            if (bounds.size.z > .001f || bounds.size.x <= 0 || bounds.size.y <= 0) return false;
            Vector3 right = screen.transform.TransformVector(Vector3.right * bounds.size.x);
            Vector3 up = screen.transform.TransformVector(Vector3.up * bounds.size.y);
            if (right.magnitude < .15f || right.magnitude > 5 || up.magnitude < .1f || up.magnitude > 3
                || Mathf.Abs(Vector3.Dot(right.normalized, up.normalized)) > .01f) return false;
            Vector3 normal = Vector3.Cross(right, up).normalized;
            rotation = Quaternion.LookRotation(normal, up.normalized);
            position = screen.transform.TransformPoint(bounds.center) - normal * .006f;
            float width = Mathf.Min(right.magnitude, up.magnitude * aspect);
            size = new Vector2(width, width / aspect);
            return true;
        }
    }
}
