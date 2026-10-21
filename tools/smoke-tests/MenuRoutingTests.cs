using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestMenuRouting()
        {
            var manager = VRManager.Instance;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter", true);
            Type hudType = Find("ScammerGameHudController"), feedback = Find("FeedbackUi");
            var hud = UnityEngine.Object.FindAnyObjectByType(hudType) as Component;
            Require(manager?.Interactive == true && hud != null && feedback != null, "Menu routing needs a tracked private host and native HUD");
            bool Settings() => (bool)AccessTools.Property(adapter, "SettingsOpen").GetValue(null);
            bool Feedback() => (bool)AccessTools.Property(feedback, "IsOpen").GetValue(null);
            bool Review() => Member(hud, "runBreakdownVisible") is bool visible && visible;
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(typeof(UI.VRPointer), "LateUpdate");
            bool savedAlwaysPointer = Plugin.AlwaysShowPointer.Value;
            void CheckModalBeam(string screen)
            {
                var aim = manager.Rig.Right.Aim;
                Vector3 savedPosition = aim.position; Quaternion savedRotation = aim.rotation;
                object interactor = AccessTools.Property(adapter, "Interactor").GetValue(null);
                object target = Member(interactor, "currentInteractable"), pickup = Member(interactor, "currentScenePickup");
                var write = AccessTools.Method(adapter, "Write");
                try
                {
                    manager.Pointer.Cancel(); Plugin.AlwaysShowPointer.Value = false;
                    write.Invoke(null, new[] { interactor, "currentInteractable", null });
                    write.Invoke(null, new[] { interactor, "currentScenePickup", null });
                    aim.SetPositionAndRotation(new Vector3(10000, 10000, 10000), Quaternion.LookRotation(Vector3.up));
                    tick.Invoke(manager.Pointer, null);
                    var beam = AccessTools.Field(typeof(UI.VRPointer), "line").GetValue(manager.Pointer) as LineRenderer;
                    Require(!manager.Pointer.HasPanelHit && beam != null && beam.enabled,
                        "Native " + screen + " overlay lost its controller laser when aiming away from UI");
                }
                finally
                {
                    Plugin.AlwaysShowPointer.Value = savedAlwaysPointer;
                    aim.SetPositionAndRotation(savedPosition, savedRotation);
                    write.Invoke(null, new[] { interactor, "currentInteractable", target });
                    write.Invoke(null, new[] { interactor, "currentScenePickup", pickup });
                    manager.Pointer.Cancel();
                }
                Logger.LogInfo("MENU LASER PASS: " + screen + " keeps the controller beam visible without a UI or world target");
            }
            void PressMenu()
            {
                manager.Input.GetType().GetProperty("MenuDown").SetValue(manager.Input, true);
                try { tick.Invoke(manager.Pointer, null); }
                finally { manager.Input.GetType().GetProperty("MenuDown").SetValue(manager.Input, false); }
            }
            Require(!Settings() && !Feedback() && !Review(), "Menu fixture requires closed settings, feedback and results");
            try
            {
                AccessTools.Method(hudType, "ShowRunBreakdown").Invoke(hud, null);
                yield return null; yield return null;
                Require(Review(), "Native results panel did not open");
                CheckModalBeam("results");
                PressMenu();
                Require(!Review() && !Settings(), "Controller menu opened settings under results instead of dismissing results");
                PressMenu(); Require(!Settings(), "Consumed results/back input opened settings again in the same frame");
                Logger.LogInfo("MENU ROUTING PASS: controller menu dismisses native results and respects same-frame escape consumption");
                yield return null;
                AccessTools.Method(feedback, "Show").Invoke(null, null);
                yield return null; yield return null;
                Require(Feedback() && !Settings() && !Review(), "Standalone feedback overlay did not open");
                CheckModalBeam("feedback");
                PressMenu(); Require(!Feedback() && !Settings(), "Standalone feedback did not dismiss cleanly");
                yield return null;
                PressMenu(); yield return null;
                Require(Settings(), "Fresh controller menu did not open settings after results dismissal");
                AccessTools.Method(feedback, "Show").Invoke(null, null);
                yield return null; yield return null;
                Require(Feedback() && Settings(), "Native feedback overlay did not open over settings");
                PressMenu();
                Require(!Feedback() && Settings(), "Controller back changed underlying settings instead of closing feedback");
                PressMenu(); Require(Settings(), "Consumed feedback/back input also closed underlying settings");
                yield return null; PressMenu(); yield return null;
                Require(!Settings(), "Fresh back did not close settings after feedback dismissal");
                Logger.LogInfo("MENU ROUTING PASS: native feedback closes before settings, preserves the underlying menu and consumes only its dismissal frame");
                AccessTools.Method(hudType, "ShowRunBreakdown").Invoke(hud, null);
                AccessTools.Method(feedback, "Show").Invoke(null, null);
                yield return null; yield return null;
                Require(Feedback() && Review(), "Native nested feedback/results overlays missing");
                PressMenu(); Require(!Feedback() && Review() && !Settings(), "Back did not close the top feedback layer first");
                yield return null; PressMenu();
                Require(!Review() && !Settings(), "Second fresh back did not dismiss results without opening settings");
                Logger.LogInfo("MENU ROUTING PASS: nested native feedback/results dismiss one layer per fresh menu press; no feedback submission or run completion is invoked");
                yield return null;
                foreach (bool results in new[] { false, true })
                {
                    var capture = TestModalCapture(results);
                    try { while (capture.MoveNext()) yield return capture.Current; }
                    finally { (capture as IDisposable)?.Dispose(); }
                    yield return null;
                }
            }
            finally
            {
                manager.Input.GetType().GetProperty("MenuDown").SetValue(manager.Input, false);
                Plugin.AlwaysShowPointer.Value = savedAlwaysPointer;
                manager.Pointer.Cancel();
                if (Feedback()) AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null);
                if (Review()) AccessTools.Method(hudType, "HideRunBreakdown").Invoke(hud, null);
                if (Settings()) toggle.Invoke(null, null);
            }
        }
    }
}
