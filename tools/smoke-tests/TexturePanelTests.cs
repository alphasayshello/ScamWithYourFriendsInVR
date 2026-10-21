using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestTexturePanel()
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var settings = Own(ScriptableObject.CreateInstance<PanelSettings>());
            var texture = Own(new RenderTexture(400, 200, 0));
            texture.Create(); settings.targetTexture = texture;
            var doc = Own(new GameObject("SWYF texture-backed document probe")).AddComponent<UIDocument>();
            doc.panelSettings = settings;
            var root = doc.rootVisualElement;
            root.style.width = 400; root.style.height = 200;
            root.style.unityFont = Own(Font.CreateDynamicFontFromOSFont("Arial", 24));
            root.style.fontSize = 24;
            var button = new Button { text = "mesh uv" };
            button.style.position = Position.Absolute; button.style.left = 100; button.style.top = 35;
            button.style.width = 80; button.style.height = 50; root.Add(button);
            int clicks = 0; button.clicked += () => clicks++;
            var meshObject = Own(new GameObject("SWYF native texture mesh probe", typeof(MeshFilter), typeof(MeshRenderer)));
            meshObject.transform.SetPositionAndRotation(new Vector3(1000, 4, 1000), Quaternion.Euler(13, 29, 7));
            meshObject.transform.localScale = new Vector3(1.3f, 0.8f, 1.1f);
            var mesh = Own(new Mesh());
            mesh.vertices = new[] { new Vector3(-1, -.5f, 0), new Vector3(1, -.5f, 0), new Vector3(1, .5f, 0), new Vector3(-1, .5f, 0) };
            mesh.uv = new[] { new Vector2(.2f, .1f), new Vector2(.8f, .1f), new Vector2(.8f, .9f), new Vector2(.2f, .9f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds();
            meshObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            Shader shader = Shader.Find("Kolkata/Review Projector Surface");
            Check(shader != null, "Native review projector shader unavailable");
            var material = Own(new Material(shader));
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", new Vector2(1, -1));
            material.SetTextureOffset("_BaseMap", new Vector2(0, 1));
            var renderer = meshObject.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            yield return null; yield return null;
            var borrow = typeof(VRPanelSurface).GetMethod("Borrow", BindingFlags.Static | BindingFlags.NonPublic);
            var surface = Own((VRPanelSurface)borrow.Invoke(null, new object[] { renderer, mesh, 0, material, "_BaseMap", doc }));
            Rect layout = surface.Panel.visualTree.layout;
            Vector2 expected = new Vector2(button.worldBound.center.x / layout.width, 1 - button.worldBound.center.y / layout.height);
            Vector2 raw = new Vector2(expected.x, 1 - expected.y);
            Vector3 local = new Vector3((raw.x - .2f) / .6f * 2 - 1, (raw.y - .1f) / .8f - .5f, 0);
            Vector3 center = meshObject.transform.TransformPoint(local);
            Vector3 normal = meshObject.transform.forward;
            var ray = new Ray(center - normal, normal);
            Check(surface.NativePresenter, "Authored mesh presenter was converted into a floating screen");
            Check(surface.Intersect(ray, out var point, out var uv, out float distance), "Authored mesh presenter was not hit");
            Check(Vector2.Distance(uv, expected) < .001f && Vector3.Distance(point, center) < .001f && Mathf.Abs(distance - 1) < .001f,
                "Authored UV, vertical flip or transformed world distance is wrong: " + uv + "/" + expected);
            VisualElement picked = surface.Pick(uv);
            Check(picked == button || button.Contains(picked), "Mesh UV did not pick the native button");
            Check(surface.DocumentFor(picked) == doc, "Texture-backed keyboard routing lost its native document");
            var pointer = Own(new GameObject("texture pointer event probe")).AddComponent<VRPointer>(); pointer.enabled = false;
            typeof(VRPointer).GetField("hoverElement", instance).SetValue(pointer, picked);
            var send = typeof(VRPointer).GetMethod("Send", instance);
            send.Invoke(pointer, new object[] { picked, EventType.MouseDown, surface.PanelPosition(uv), 0, true });
            send.Invoke(pointer, new object[] { picked, EventType.MouseUp, surface.PanelPosition(uv), 0, false });
            Check(clicks == 1, "Texture-backed native button did not click");
            Ray outside = new Ray(center + meshObject.transform.right * 4 - normal, normal);
            Check(!surface.Intersect(outside, out _, out _, out _) && surface.Intersect(outside, out _, out var outsideUv, out _, false)
                && outsideUv.x > 1, "Captured texture drag did not extrapolate outside the mesh");
            renderer.enabled = false;
            Check(!surface.Intersect(ray, out _, out _, out _), "Disabled native renderer consumed input");
            renderer.enabled = true;
            bool pairTexture = Array.IndexOf(Environment.GetCommandLineArgs(), "--swyf-pair-client") >= 0;
            if (pairTexture || Array.IndexOf(Environment.GetCommandLineArgs(), "--swyf-vr-smoke-tests") >= 0)
            {
                float until = Time.realtimeSinceStartup + 45;
                VRPanelSurface discovered = null;
                do
                {
                    // Observe readiness after native camera/pose updates too.
                    yield return new WaitForEndOfFrame();
                    Check(Time.realtimeSinceStartup < until, "Texture presenter was not automatically discovered/ready");
                } while (pairTexture && (!Mirror.NetworkClient.ready || Mirror.NetworkClient.localPlayer == null)
                    || VRManager.Instance?.Interactive != true || VRManager.Instance.Rig.Head == null
                    || VRManager.Instance.Rig.HeadObstructed || !VRManager.Instance.Rig.Right.IsTracked
                    || (discovered = VRManager.Instance.UI.Panels.FirstOrDefault(p => p != null && p.NativePresenter && p.Document == doc)) == null);
                var manager = VRManager.Instance;
                bool originalOcclusion = Plugin.OccludeFloatingUI.Value;
                var tick = typeof(VRPointer).GetMethod("LateUpdate", instance);
                void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
                void Aim() => manager.Rig.Right.Aim.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction));
                try
                {
                    // Inject after the production LateUpdate pose/pointer ticks,
                    // so the mock pose writer cannot move this test ray mid-press.
                    Aim(); Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                    tick.Invoke(manager.Pointer, null);
                    Check(manager.Pointer.HasPanelHit, "Controller ray did not hit the discovered texture presenter");
                    Input("TriggerDown", false); yield return null;
                    Aim(); Input("Trigger", false); Input("TriggerUp", true);
                    tick.Invoke(manager.Pointer, null);
                    Check(clicks == 2, "Frame-separated controller click did not activate the mesh button");
                    Aim(); Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                    tick.Invoke(manager.Pointer, null);
                    renderer.enabled = false;
                    Aim(); Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", true);
                    tick.Invoke(manager.Pointer, null);
                    Check(clicks == 2, "Hiding a native presenter during a press activated its invisible button");
                    renderer.enabled = true;
                    Input("TriggerUp", false);
                    var wall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
                    wall.transform.SetPositionAndRotation(center - normal * .5f, Quaternion.LookRotation(normal));
                    wall.transform.localScale = new Vector3(.5f, .5f, .05f); Physics.SyncTransforms();
                    foreach (bool floatingOcclusion in new[] { true, false })
                    {
                        Plugin.OccludeFloatingUI.Value = floatingOcclusion;
                        Aim(); tick.Invoke(manager.Pointer, null);
                        Check(!manager.Pointer.HasPanelHit, "Native texture ray passed through a world obstacle; floating occlusion=" + floatingOcclusion);
                    }
                    wall.SetActive(false);
                    Logger.LogInfo("SMOKE PASS: discovered texture presenter controller click and world occlusion");
                }
                finally { Plugin.OccludeFloatingUI.Value = originalOcclusion; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false); manager.Pointer.Cancel(); }
            }
            surface.Restore();
            Check(meshObject != null && renderer.enabled && renderer.sharedMaterial == material && material.GetTexture("_BaseMap") == texture
                && settings.targetTexture == texture && texture.IsCreated() && meshObject.GetComponent<Collider>() == null,
                "Restoring a borrowed presenter changed its native renderer, collider or texture ownership");
            typeof(VRPanelSurface).GetMethod("Remove", instance).Invoke(surface, null);
            yield return null;
            Check(surface == null && meshObject != null && renderer != null && renderer.enabled && settings.targetTexture == texture,
                "Removing a borrowed panel destroyed its native game object or texture");
            var scanner = Own(new GameObject("SWYF incremental presenter scan probe")).AddComponent<VRUIPatcher>(); scanner.enabled = false;
            var pending = typeof(VRUIPatcher).GetField("presenterScan", instance);
            var cursor = typeof(VRUIPatcher).GetField("presenterScanCursor", instance);
            var documents = (System.Collections.Generic.List<UIDocument>)typeof(VRUIPatcher).GetField("textureDocuments", instance).GetValue(scanner);
            documents.Add(doc);
            var doomed = Own(new GameObject("SWYF destroyed scan entry", typeof(MeshRenderer)));
            var snapshot = new MeshRenderer[513]; snapshot[0] = doomed.GetComponent<MeshRenderer>(); snapshot[snapshot.Length - 1] = renderer;
            Destroy(doomed); yield return null;
            pending.SetValue(scanner, snapshot); cursor.SetValue(scanner, 0);
            var advance = typeof(VRUIPatcher).GetMethod("DiscoverTexturePresenters", instance);
            doc.enabled = false; advance.Invoke(scanner, null);
            Check((int)cursor.GetValue(scanner) > 0 && (int)cursor.GetValue(scanner) < snapshot.Length && scanner.Panels.Count == 0,
                "Presenter discovery consumed the entire snapshot in one call or bound an inactive document");
            doc.enabled = true; yield return null;
            for (int i = 0; i < 8 && pending.GetValue(scanner) != null; i++) advance.Invoke(scanner, null);
            Check(pending.GetValue(scanner) == null && scanner.Panels.Count == 1 && scanner.Panels[0].NativePresenter,
                "Incremental discovery did not resume and bind its late native presenter");
            pending.SetValue(scanner, snapshot); scanner.Restore();
            Check(pending.GetValue(scanner) == null && renderer.enabled && renderer.sharedMaterial == material && settings.targetTexture == texture,
                "Cancelling discovery retained a snapshot or changed native presenter ownership");
            doc.enabled = false; renderer.enabled = false;
        }
    }
}
