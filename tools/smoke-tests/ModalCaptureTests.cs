using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object modalCaptureFeedback;
        private static int modalCaptureReportAttempts;
        private static bool BlockModalCaptureReport(object __instance)
        {
            if (!ReferenceEquals(__instance, modalCaptureFeedback)) return true;
            modalCaptureReportAttempts++; return false;
        }
        private IEnumerator TestModalCapture(bool results)
        {
            var manager = VRManager.Instance;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter", true);
            Type feedback = Find("FeedbackUi"), hudType = Find("ScammerGameHudController");
            var hud = UnityEngine.Object.FindAnyObjectByType(hudType) as Component;
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(typeof(UI.VRPointer), "LateUpdate");
            bool Settings() => (bool)AccessTools.Property(adapter, "SettingsOpen").GetValue(null);
            bool Feedback() => (bool)AccessTools.Property(feedback, "IsOpen").GetValue(null);
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            VisualElement host = null, container = null;
            var guard = new Harmony("com.swyf.vr.modalcapturefeedbackguard");
            Require(!Settings() && !Feedback() && hud != null && !(bool)Member(hud, "runBreakdownVisible"), "Modal capture needs closed native overlays");
            try
            {
                Type formType = Find("FeedbackForm");
                modalCaptureFeedback = UnityEngine.Object.FindAnyObjectByType(formType);
                Require(modalCaptureFeedback != null && AccessTools.Method(formType, "SendReport") != null, "Owned feedback submission guard is unavailable");
                modalCaptureReportAttempts = 0;
                guard.Patch(AccessTools.Method(formType, "SendReport"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BlockModalCaptureReport)));
                if (results) AccessTools.Method(hudType, "ShowRunBreakdown").Invoke(hud, null);
                else toggle.Invoke(null, null);
                yield return new WaitForSecondsRealtime(.8f);
                UIDocument document = null;
                float deadline = Time.realtimeSinceStartup + 6;
                while (host == null || document == null)
                {
                    foreach (var candidate in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                    {
                        if (!candidate.isActiveAndEnabled || candidate.rootVisualElement?.panel == null) continue;
                        VisualElement root = results ? Member(hud, "runBreakdown") as VisualElement : candidate.rootVisualElement.Q("general-screen");
                        if (root == null || !candidate.rootVisualElement.Contains(root)) continue;
                        host = root; document = candidate; break;
                    }
                    if (host != null && document != null) break;
                    Require(Time.realtimeSinceStartup < deadline, "Native modal capture host did not appear"); yield return null;
                }
                var surface = manager.UI.Panels.FirstOrDefault(p => p != null && p.Settings == document.panelSettings);
                Require(surface != null, "Native modal capture host has no VR surface");
                container = new VisualElement { name = "swyf-modal-capture-fixture" };
                container.style.position = Position.Absolute; container.style.left = 30; container.style.top = 70;
                container.style.width = 360; container.style.height = 130;
                var button = new Button { text = "Modal capture fixture" }; button.style.height = 48;
                var slider = new Slider(0, 100); slider.style.height = 48;
                int clicks = 0, lastClickCount = 0; button.clicked += () => clicks++;
                button.RegisterCallback<ClickEvent>(evt => lastClickCount = evt.clickCount);
                container.Add(button); container.Add(slider); host.Add(container); container.BringToFront();
                yield return null; yield return null;
                foreach (VisualElement control in new VisualElement[] { button, slider })
                {
                    IEnumerator press = ClickAppControl(surface, control, false);
                    try
                    {
                        while (!(Member(manager.Pointer, "toolkitDown") is bool down && down))
                        {
                            Require(press.MoveNext(), "Native modal capture press finished before taking ownership");
                            if (Member(manager.Pointer, "toolkitDown") is bool captured && captured) break;
                            yield return press.Current;
                        }
                        float heldValue = slider.value;
                        Input("TriggerDown", true);
                        AccessTools.Method(feedback, "Show").Invoke(null, null);
                        if (control == slider) appFixturePoint += new Vector2(slider.worldBound.width * .3f, 0);
                        tick.Invoke(manager.Pointer, null);
                        Input("TriggerDown", false);
                        bool retained = Member(manager.Pointer, "toolkitDown") is bool owned && owned;
                        holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", true);
                        tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                        Require(!retained && clicks == 0 && (control != slider || Mathf.Approximately(slider.value, heldValue)),
                            "Opening feedback retained or activated the underlying " + (control == slider ? "slider" : "button") + " press in " + (results ? "results" : "settings"));
                        holdInjectedTrigger = true; Input("Trigger", true); tick.Invoke(manager.Pointer, null);
                        Require(!(bool)Member(manager.Pointer, "toolkitDown"), "Held trigger reacquired an overlay press without a fresh edge");
                        holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerUp", true); tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                        float beforeFreshAttempt = slider.value;
                        Input("Trigger", true); Input("TriggerDown", true); tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                        Input("Trigger", false); Input("TriggerUp", true); tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                        Require(clicks == 0 && (control != slider || Mathf.Approximately(slider.value, beforeFreshAttempt)) && modalCaptureReportAttempts == 0,
                            "A fresh press reached an underlying control while feedback was still open");
                    }
                    finally { (press as IDisposable)?.Dispose(); }
                    AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null);
                    yield return null; yield return new WaitForEndOfFrame();
                    if (control == slider) slider.value = 0;
                    var fresh = ClickAppControl(surface, control);
                    try { while (fresh.MoveNext()) yield return fresh.Current; }
                    finally { (fresh as IDisposable)?.Dispose(); }
                    Require(control == button ? clicks == 1 && lastClickCount == 1 : slider.value > 0, "Fresh UI input did not recover as a single click after feedback closed");
                    clicks = 0;
                }
                Logger.LogInfo("MODAL CAPTURE PASS: feedback cancels held button/slider input from " + (results ? "results" : "settings") + "; no underlying activation, drag continuation or held-input reacquisition; fresh presses recover");
            }
            finally
            {
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                manager.Pointer.Cancel(); container?.RemoveFromHierarchy();
                if (Feedback()) AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null);
                if (results) AccessTools.Method(hudType, "HideRunBreakdown").Invoke(hud, null);
                if (Settings()) toggle.Invoke(null, null);
                guard.UnpatchSelf(); modalCaptureFeedback = null;
            }
        }
    }
}
