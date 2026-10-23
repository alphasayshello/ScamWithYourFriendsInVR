using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestNativeLaptop()
        {
            Require(NetworkServer.connections.Count == 1, "Laptop fixture requires one private host");
            var type = Find("MonitorInteractable");
            var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            bool physical = Plugin.PhysicalDesktop.Value;
            object desktop = Instance("KolkataOsSingleton");
            testingComputer = true;
            try
            {
                Plugin.PhysicalDesktop.Value = true;
                float deadline = Time.realtimeSinceStartup + 10;
                while (MonitorCaptured() || Member(desktop, "activeLocalMonitor") != null || Member(desktop, "hideRoutine") != null
                    || Member(interactor, "isSitting") is true)
                {
                    Require(Time.realtimeSinceStartup < deadline, "Previous native monitor/seat exit did not finish before laptop interaction");
                    yield return null;
                }
                var monitor = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None).Cast<Component>()
                    .Where(m => Member(m, "UsesLaptopDesktop") is true && AccessTools.Method(type, "CanInteract").Invoke(m, new object[] { interactor }) is true)
                    .OrderBy(m => (m.transform.position - interactor.transform.position).sqrMagnitude).FirstOrDefault();
                Require(monitor != null, "No usable native laptop monitor loaded in this room");
                var controller = Instance("Metater.MetaPlayerController");
                AccessTools.Method(controller.GetType(), "Teleport").Invoke(controller, new object[] { monitor.transform.position - monitor.transform.forward, true });
                AccessTools.Method(type, "CmdInteract").Invoke(monitor, new object[] { interactor });
                deadline = Time.realtimeSinceStartup + 30;
                while (!MonitorCaptured())
                {
                    Require(Time.realtimeSinceStartup < deadline, "Native laptop did not capture monitor input");
                    yield return null;
                }
                desktop = Instance("KolkataOsSingleton");
                object os = Member(desktop, "Controller");
                deadline = Time.realtimeSinceStartup + 20;
                while (!(Member(desktop, "IsOpenAnimationComplete") is true) || !(Member(os, "m_useLaptopDesktop") is true))
                {
                    Require(Time.realtimeSinceStartup < deadline, "Actual laptop did not select its native desktop");
                    yield return null;
                }
                Logger.LogInfo("LAPTOP CAPTURE PASS: actual native laptop interaction selects laptop desktop and captures input");
                var root = monitor.transform.parent ?? monitor.transform;
                Logger.LogInfo("LAPTOP BINDING: monitor=" + monitor.name + "; parent=" + root.name + "; workspace=" + Member(desktop, "boundWorkspace"));
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    Logger.LogInfo("LAPTOP MESH: name=" + renderer.name + "; parent=" + renderer.transform.parent?.name
                        + "; position=" + renderer.transform.position + "; rotation=" + renderer.transform.eulerAngles
                        + "; scale=" + renderer.transform.lossyScale + "; bounds=" + renderer.GetComponent<MeshFilter>()?.sharedMesh?.bounds);
                var document = ((Component)os).GetComponent<UIDocument>();
                var manager = VRManager.Instance;
                deadline = Time.realtimeSinceStartup + 10;
                var surface = manager.UI.Panels.FirstOrDefault(p => p != null && p.Settings == document.panelSettings && p.Panel != null);
                while (surface == null || !surface.MonitorAnchored)
                {
                    Require(Time.realtimeSinceStartup < deadline, "Actual laptop desktop did not anchor to its authored screen");
                    yield return null;
                    surface = manager.UI.Panels.FirstOrDefault(p => p != null && p.Settings == document.panelSettings && p.Panel != null);
                }
                var label = Member(monitor, "idleScreenLabel") as GameObject;
                var backing = label.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Black backing");
                object state = Member(Member(desktop, "boundWorkspace"), "callStateScreen");
                var display = Member(state, "loadingScreenRenderer") as MeshRenderer;
                Require(display != null, "Native laptop loading display missing");
                Vector3 center = display.bounds.center;
                Require(Vector3.Distance(surface.transform.position, center) < .012f,
                    "Laptop desktop is anchored away from the model's actual screen: distance=" + Vector3.Distance(surface.transform.position, center));
                float displayWidth = Vector3.Dot(display.bounds.size, new Vector3(Mathf.Abs(backing.transform.right.x), Mathf.Abs(backing.transform.right.y), Mathf.Abs(backing.transform.right.z)));
                Require(surface.transform.lossyScale.x > displayWidth * .95f && surface.transform.lossyScale.x < displayWidth * 1.01f,
                    "Laptop desktop did not fill the actual display width: surface=" + surface.transform.lossyScale.x + "; display=" + displayWidth);
                Logger.LogInfo("LAPTOP SURFACE PASS: desktop lies within 12mm of actual display and fills its width instead of the smaller text backing");
                var placement = AccessTools.Method(surface.GetType(), "UpdatePlacement");
                Vector3 savedPosition = root.localPosition;
                Quaternion savedRotation = root.localRotation;
                var labelField = AccessTools.Field(type, "idleScreenLabel");
                var displayField = AccessTools.Field(state.GetType(), "loadingScreenRenderer");
                try
                {
                    root.position += new Vector3(.2f, .1f, -.15f);
                    root.rotation = Quaternion.AngleAxis(10, Vector3.up) * root.rotation;
                    placement.Invoke(surface, null);
                    Require(surface.MonitorAnchored && Vector3.Distance(surface.transform.position, display.bounds.center) < .012f
                        && Vector3.Dot(surface.transform.up, backing.transform.up) > .999f,
                        "Laptop surface did not follow its moved authored backing");
                    Logger.LogInfo("LAPTOP MOTION PASS: surface follows moved and rotated laptop backing");
                    labelField.SetValue(monitor, null); placement.Invoke(surface, null);
                    Require(!surface.MonitorAnchored && !surface.GetComponent<MeshRenderer>().enabled
                        && !surface.Intersect(new Ray(manager.Rig.Head.transform.position, surface.transform.position - manager.Rig.Head.transform.position), out _, out _, out _),
                        "Missing laptop screen reference retained a visible/clickable stale surface");
                    labelField.SetValue(monitor, label); placement.Invoke(surface, null);
                    Require(surface.MonitorAnchored && surface.GetComponent<MeshRenderer>().enabled, "Restored laptop screen did not recover its surface");
                    displayField.SetValue(state, null); placement.Invoke(surface, null);
                    Require(!surface.MonitorAnchored && !surface.GetComponent<MeshRenderer>().enabled,
                        "Missing full display reference retained a stale laptop surface");
                    displayField.SetValue(state, display); placement.Invoke(surface, null);
                    Require(surface.MonitorAnchored && surface.GetComponent<MeshRenderer>().enabled, "Restored full display did not recover its surface");
                    Logger.LogInfo("LAPTOP RECOVERY PASS: missing orientation or display reference disables the surface and restored bindings recover");
                }
                finally
                {
                    labelField.SetValue(monitor, label);
                    displayField.SetValue(state, display);
                    root.localPosition = savedPosition; root.localRotation = savedRotation;
                    placement.Invoke(surface, null);
                }
                var apps = Member(os, "m_appsById") as IDictionary;
                object app = apps["daily-callers"];
                Require(Member(app, "IsAvailable") is true, "Actual laptop caller manager unavailable");
                var checks = TestAppWindow(os, app, "daily-callers", surface);
                try { while (checks.MoveNext()) yield return checks.Current; }
                finally { (checks as IDisposable)?.Dispose(); }
                Logger.LogInfo("LAPTOP POINTER PASS: controller maximize/restore, minimize/reopen and close reach the native caller-manager window on the actual laptop");
                manager.Input.GetType().GetProperty("MenuDown").SetValue(manager.Input, true);
                try { AccessTools.Method(manager.Pointer.GetType(), "LateUpdate").Invoke(manager.Pointer, null); }
                finally { manager.Input.GetType().GetProperty("MenuDown").SetValue(manager.Input, false); }
                deadline = Time.realtimeSinceStartup + 5;
                while (MonitorCaptured())
                {
                    Require(Time.realtimeSinceStartup < deadline, "Controller back did not close the actual laptop");
                    yield return null;
                }
                Logger.LogInfo("LAPTOP EXIT PASS: controller back releases actual laptop capture");
            }
            finally
            {
                Plugin.PhysicalDesktop.Value = physical;
                if (desktop != null) AccessTools.Method(desktop.GetType(), "RequestActiveMonitorClose").Invoke(desktop, new object[] { true });
                VRManager.Instance?.Pointer?.Cancel(); testingComputer = false; placed = false;
            }
            Logger.LogInfo("LAPTOP COMPLETE PASS: native laptop capture, anchored caller-manager pointer controls and exit; real headset acceptance remains pending");
        }
    }
}
