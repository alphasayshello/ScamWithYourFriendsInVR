using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.UI
{
    internal sealed class VRMonitorDesktop
    {
        private Component monitor;
        private Component workspace, presenter;
        private MeshRenderer screen;
        private GameObject laptopLabel;
        private float nextResolve;
        internal Component Monitor => monitor;
        internal bool AwaitingScreen { get; private set; }

        internal bool TryPose(UnityEngine.UIElements.UIDocument document, float aspect,
            out Vector3 position, out Quaternion rotation, out Vector2 size)
        {
            position = default; rotation = Quaternion.identity; size = default;
            AwaitingScreen = false;
            if (!Plugin.PhysicalDesktop.Value || !GameAdapter.MonitorCaptured || document == null) return false;
            object os = GameAdapter.Static("KolkataOsSingleton", "Instance");
            if (!ReferenceEquals(GameAdapter.Read(os, "kolkataOsDocument"), document)) return false;
            // A desk desktop must not jump into the player's face while native
            // capture/stream bindings are being replaced. Only the explicit
            // floating option permits a head-relative computer surface.
            AwaitingScreen = true;
            Component active = GameAdapter.Read(os, "activeLocalMonitor") as Component;
            if (active == null || !active.gameObject.activeInHierarchy) return false;
            Component bound = GameAdapter.Read(os, "boundWorkspace") as Component;
            if (active != monitor || bound != workspace)
            {
                monitor = active; workspace = bound; presenter = null; screen = null; laptopLabel = null; nextResolve = 0;
            }
            object state = GameAdapter.Read(workspace, "callStateScreen");
            if (!ReferenceEquals(GameAdapter.Read(state, "MonitorInteractable"), monitor)) return false;
            if (GameAdapter.Read(monitor, "UsesLaptopDesktop") is true)
            {
                GameObject label = GameAdapter.Read(monitor, "idleScreenLabel") as GameObject;
                if (label != laptopLabel) { laptopLabel = label; screen = null; nextResolve = 0; }
                if (screen == null && laptopLabel != null && Time.unscaledTime >= nextResolve)
                {
                    nextResolve = Time.unscaledTime + .5f;
                    // The laptop's stream presenter can sit outside its model. Its authored
                    // idle label owns the screen backing; accept only one planar quad.
                    MeshRenderer candidate = null;
                    foreach (var renderer in laptopLabel.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Mesh quad = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (quad == null || quad.vertexCount != 4 || quad.bounds.size.z > .001f
                            || quad.bounds.size.x <= 0 || quad.bounds.size.y <= 0) continue;
                        if (candidate != null) { candidate = null; break; }
                        candidate = renderer;
                    }
                    screen = candidate;
                }
            }
            else
            {
                if (presenter == null && Time.unscaledTime >= nextResolve)
                {
                    nextResolve = Time.unscaledTime + .5f;
                    var type = ReflectionTypes.Find("ScammerWorkspaceWebRtc");
                    if (workspace != null && type != null)
                        presenter = workspace.GetComponent(type);
                }
                // Native streaming can replace its renderer without changing the binding.
                screen = GameAdapter.Read(presenter, "monitorRenderer") as MeshRenderer;
            }
            // The game hides its streamed presenter for the local computer;
            // its transform is still the authored screen geometry.
            if (screen == null) return false;
            Mesh mesh = screen.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return false;
            Bounds bounds = mesh.bounds;
            // Both authored screen sources use XY quads. Unknown geometry keeps
            // desk mode hidden instead of guessing a screen calibration.
            if (bounds.size.z > .001f || bounds.size.x <= 0 || bounds.size.y <= 0) return false;
            Vector3 right = screen.transform.TransformVector(Vector3.right * bounds.size.x);
            Vector3 up = screen.transform.TransformVector(Vector3.up * bounds.size.y);
            if (right.magnitude < .15f || right.magnitude > 5 || up.magnitude < .1f || up.magnitude > 3
                || Mathf.Abs(Vector3.Dot(right.normalized, up.normalized)) > .01f) return false;
            Vector3 normal = Vector3.Cross(right, up).normalized;
            rotation = Quaternion.LookRotation(normal, up.normalized);
            Vector3 center = screen.transform.TransformPoint(bounds.center);
            if (GameAdapter.Read(monitor, "UsesLaptopDesktop") is true)
            {
                // Use the full authored loading display for extent/position. The smaller
                // idle-label backing supplies upright orientation, not the desktop's size.
                var display = GameAdapter.Read(state, "loadingScreenRenderer") as MeshRenderer;
                Mesh displayMesh = display == null ? null : display.GetComponent<MeshFilter>()?.sharedMesh;
                if (displayMesh == null) return false;
                Bounds displayBounds = displayMesh.bounds;
                Vector3 displayCenter = display.transform.TransformPoint(displayBounds.center);
                Vector3 x = display.transform.TransformVector(Vector3.right * displayBounds.extents.x);
                Vector3 y = display.transform.TransformVector(Vector3.up * displayBounds.extents.y);
                Vector3 z = display.transform.TransformVector(Vector3.forward * displayBounds.extents.z);
                float Extent(Vector3 axis) => Mathf.Abs(Vector3.Dot(x, axis)) + Mathf.Abs(Vector3.Dot(y, axis)) + Mathf.Abs(Vector3.Dot(z, axis));
                float displayWidth = 2 * Extent(right.normalized), displayHeight = 2 * Extent(up.normalized);
                if (Vector3.Distance(displayCenter, center) > .15f || 2 * Extent(normal) > .02f
                    || displayWidth < right.magnitude * .9f || displayHeight < up.magnitude * .9f
                    || displayWidth > 5 || displayHeight > 3) return false;
                right = right.normalized * displayWidth; up = up.normalized * displayHeight; center = displayCenter;
            }
            position = center - normal * .006f;
            float width = Mathf.Min(right.magnitude, up.magnitude * aspect);
            size = new Vector2(width, width / aspect);
            AwaitingScreen = false;
            return true;
        }
    }
}
