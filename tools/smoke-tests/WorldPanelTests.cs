using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestWorldPanel()
        {
            var settings = Own(ScriptableObject.CreateInstance<PanelSettings>());
            var mode = typeof(PanelSettings).GetProperty("renderMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Check(mode != null, "Native panel render mode unavailable");
            mode.SetValue(settings, Enum.Parse(mode.PropertyType, "WorldSpace"));
            var go = Own(new GameObject("SWYF native world panel probe"));
            go.transform.SetPositionAndRotation(new Vector3(1000, 2, 1000), Quaternion.Euler(8, 31, 0));
            go.transform.localScale = new Vector3(1.3f, 0.8f, 1);
            var doc = go.AddComponent<UIDocument>();
            doc.worldSpaceSize = new Vector2(400, 200);
            doc.panelSettings = settings;
            var root = doc.rootVisualElement;
            root.style.unityFont = Own(Font.CreateDynamicFontFromOSFont("Arial", 24));
            root.style.fontSize = 24;
            var button = new Button { text = "world button" };
            button.style.position = Position.Absolute; button.style.left = 30; button.style.top = 25;
            button.style.width = 160; button.style.height = 60;
            button.style.translate = new Translate(0, 0, 20);
            button.style.rotate = new Rotate(new Angle(18), Vector3.up);
            root.Add(button);
            yield return null;
            yield return null;
            Type picker = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRWorldPanelPicker");
            var pick = picker.GetMethod("TryPick", BindingFlags.Static | BindingFlags.NonPublic);
            var drag = picker.GetMethod("TryDrag", BindingFlags.Static | BindingFlags.NonPublic);
            Matrix4x4 elementMatrix = go.transform.localToWorldMatrix * button.worldTransform;
            Vector3 center = elementMatrix.MultiplyPoint3x4(button.contentRect.center);
            Vector3 normal = elementMatrix.inverse.transpose.MultiplyVector(Vector3.forward).normalized;
            var ray = new Ray(center - normal, normal);
            object[] values = { doc, ray, null, Vector3.zero, Vector3.zero, 0f };
            Check((bool)pick.Invoke(null, values), "Native world button was not picked");
            VisualElement picked = values[2] as VisualElement;
            Check(picked == button || button.Contains(picked), "World picker selected another element");
            Check(Mathf.Abs((float)values[5] - 1) < 0.02f, "World picking distance ignored document transform/scale");
            Vector3 position = (Vector3)values[4];
            Check(Vector3.Distance(position, go.transform.InverseTransformPoint(center)) < 0.001f, "World event coordinates differ from native document space");
            var pointer = Own(new GameObject("world pointer event probe")).AddComponent<VRPointer>();
            pointer.enabled = false;
            MethodInfo send = typeof(VRPointer).GetMethod("Send3D", BindingFlags.Instance | BindingFlags.NonPublic);
            typeof(VRPointer).GetField("hoverElement", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pointer, picked);
            int clicked = 0; button.clicked += () => clicked++;
            Vector3 localPress = new Vector3(float.NaN, 0, 0);
            button.RegisterCallback<PointerDownEvent>(evt => localPress = evt.localPosition, TrickleDown.TrickleDown);
            send.Invoke(pointer, new object[] { picked, EventType.MouseDown, position, 0, true });
            send.Invoke(pointer, new object[] { picked, EventType.MouseUp, position, 0, false });
            Check(clicked == 1, "Native world button did not click through pointer events");
            Check(Vector3.Distance(localPress, (Vector3)button.contentRect.center) < 0.02f, "3D pointer event lost depth or used the wrong local coordinates: " + localPress);
            send.Invoke(pointer, new object[] { picked, EventType.MouseDown, position, 0, true });
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(VRPointer).GetField("pressedElement", instance).SetValue(pointer, picked);
            typeof(VRPointer).GetField("pressedWorldDocument", instance).SetValue(pointer, doc);
            typeof(VRPointer).GetField("toolkitDown", instance).SetValue(pointer, true);
            typeof(VRPointer).GetField("lastPanelPosition", instance).SetValue(pointer, position);
            pointer.Cancel();
            Check(clicked == 1, "Cancellation activated the native world button");
            object[] captured = { doc, button, new Ray(center + elementMatrix.MultiplyVector(Vector3.right * 250) - normal, normal), Vector3.zero };
            Check((bool)drag.Invoke(null, captured), "Captured drag lost the world document outside the element");
            Check(Vector3.Distance((Vector3)captured[3], position) > 0.01f, "Captured world drag did not update coordinates");
            bool pairWorld = Array.IndexOf(Environment.GetCommandLineArgs(), "--swyf-pair-client") >= 0;
            if (pairWorld || Array.IndexOf(Environment.GetCommandLineArgs(), "--swyf-vr-smoke-tests") >= 0)
            {
                float until = Time.realtimeSinceStartup + 45;
                if (!pairWorld)
                {
                    while (VRManager.Instance?.Ready != true)
                    { Check(Time.realtimeSinceStartup < until, "World UI mock session did not become ready"); yield return null; }
                    var helper = FindFirstObjectByType<LocalPairHarness>();
                    Check(helper != null, "World UI mock pose helper missing");
                    typeof(LocalPairHarness).GetMethod("InitializeMockPose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(helper, null);
                }
                do
                {
                    yield return new WaitForEndOfFrame();
                    Check(Time.realtimeSinceStartup < until, "World UI controller integration not ready: interactive=" + VRManager.Instance?.Interactive
                        + "; blocked=" + VRManager.Instance?.Rig?.HeadObstructed + "; hand=" + VRManager.Instance?.Rig?.Right?.IsTracked
                        + "; discovered=" + VRManager.Instance?.UI?.WorldDocuments.Contains(doc));
                } while (pairWorld && (!Mirror.NetworkClient.ready || Mirror.NetworkClient.localPlayer == null)
                    || VRManager.Instance?.Interactive != true || VRManager.Instance.Rig.Head == null
                    || VRManager.Instance.Rig.HeadObstructed || !VRManager.Instance.Rig.Right.IsTracked || !VRManager.Instance.UI.WorldDocuments.Contains(doc));
                var manager = VRManager.Instance;
                bool originalOcclusion = Plugin.OccludeFloatingUI.Value;
                MethodInfo tick = typeof(VRPointer).GetMethod("LateUpdate", instance);
                void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
                void Aim() => manager.Rig.Right.Aim.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction));
                try
                {
                    Aim(); Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                    tick.Invoke(manager.Pointer, null);
                    Check(manager.Pointer.HasPanelHit, "Controller pointer did not select the native world document; interactive=" + manager.Interactive
                        + "; obstruction=" + manager.Rig.HeadObstructed + "; hand=" + manager.Rig.Right.IsTracked + "; docs=" + manager.UI.WorldDocuments.Count);
                    Input("TriggerDown", false);
                    yield return null;
                    Aim();
                    Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", true);
                    tick.Invoke(manager.Pointer, null);
                    Check(clicked == 2, "Controller ray did not activate the native world button");
                    var wall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
                    wall.name = "SWYF world UI occlusion probe";
                    wall.transform.SetPositionAndRotation(center - normal * 0.5f, Quaternion.LookRotation(normal));
                    wall.transform.localScale = new Vector3(0.5f, 0.5f, 0.05f);
                    Physics.SyncTransforms();
                    foreach (bool floatingOcclusion in new[] { true, false })
                    {
                        Plugin.OccludeFloatingUI.Value = floatingOcclusion;
                        Aim(); Input("TriggerUp", false); tick.Invoke(manager.Pointer, null);
                        Check(!manager.Pointer.HasPanelHit, "Native world UI ray passed through world geometry; floating occlusion=" + floatingOcclusion);
                    }
                    wall.SetActive(false);
                    Logger.LogInfo("SMOKE PASS: controller ray activates native world UI and respects world occlusion");
                }
                finally
                {
                    Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                    Plugin.OccludeFloatingUI.Value = originalOcclusion;
                    manager.Pointer.Cancel();
                }
            }
            button.SetEnabled(false);
            values = new object[] { doc, ray, null, Vector3.zero, Vector3.zero, 0f };
            Check(!(bool)pick.Invoke(null, values), "Disabled world button consumed the ray");
            button.SetEnabled(true); root.style.opacity = 0;
            yield return null;
            Check(!(bool)pick.Invoke(null, values), "Transparent world document consumed the ray");
            root.style.opacity = 1;
            doc.enabled = false;
            Check(!(bool)drag.Invoke(null, captured), "Disabled world document retained captured drag");
            Check(settings.targetTexture == null, "Native world panel was converted to a render texture");
        }
    }
}
