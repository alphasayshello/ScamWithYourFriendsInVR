using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool holdInjectedTrigger;
        private static bool holdInjectedSecondary;
        private static VRPanelSurface appFixtureSurface;
        private static Vector2 appFixturePoint;
        private static bool appFixtureAim;
        private static float appFixtureScroll;
        private static void AimAppFixture()
        {
            if (!appFixtureAim || appFixtureSurface?.Panel == null) return;
            Rect bounds = appFixtureSurface.Panel.visualTree.layout;
            Vector2 uv = new Vector2(appFixturePoint.x / bounds.width, 1 - appFixturePoint.y / bounds.height);
            Transform surface = appFixtureSurface.transform;
            Vector3 point = surface.TransformPoint(new Vector3(uv.x - .5f, uv.y - .5f, 0));
            VRManager.Instance.Rig.Right.Aim.SetPositionAndRotation(point - surface.forward * .35f, surface.rotation);
            AccessTools.Method(typeof(VRPointer), "ResetAim").Invoke(VRManager.Instance.Pointer, null);
        }
        private static void KeepTestTriggerHeld(object __instance)
        {
            KeepFlightInput(__instance);
            KeepLadderInput(__instance);
            KeepSprintInput(__instance);
            KeepJumpInput(__instance);
            KeepPeeInput(__instance);
            KeepStorageInput(__instance);
            KeepTackleInput(__instance);
            KeepVoiceInput(__instance);
            if (injectBagGrip) __instance.GetType().GetProperty("LeftGrip").SetValue(__instance, true);
            if (bagFixture != null) __instance.GetType().GetProperty("Move").SetValue(__instance, bagMove);
            if (holdInjectedSecondary)
            {
                __instance.GetType().GetProperty("Secondary").SetValue(__instance, true);
                __instance.GetType().GetProperty("SecondaryDown").SetValue(__instance, false);
            }
            if (appFixtureScroll != 0) __instance.GetType().GetProperty("Move").SetValue(__instance, new Vector2(0, appFixtureScroll));
            if (injectFoodJump) __instance.GetType().GetProperty("Jump").SetValue(__instance, true);
            if (injectGiveUp)
            {
                __instance.GetType().GetProperty("GiveUp").SetValue(__instance, true);
                __instance.GetType().GetProperty("GiveUpDown").SetValue(__instance, false);
            }
            if (!holdInjectedTrigger) return;
            var type = __instance.GetType();
            type.GetProperty("Trigger").SetValue(__instance, true);
            type.GetProperty("TriggerDown").SetValue(__instance, false);
            type.GetProperty("TriggerUp").SetValue(__instance, false);
        }

        private IEnumerator ClickAppControl(VRPanelSurface surface, VisualElement control, bool cancelAfter = true)
        {
            bool previousAim = appFixtureAim;
            VRPanelSurface previousSurface = appFixtureSurface;
            Vector2 previousPoint = appFixturePoint;
            float layoutDeadline = Time.realtimeSinceStartup + 5;
            while (control != null && (control.panel == null || !(control.worldBound.width > 1)) && Time.realtimeSinceStartup < layoutDeadline) yield return null;
            Require(control?.panel != null && control.enabledInHierarchy && control.worldBound.width > 1, "App control is not visible and enabled: " + control + "; panel=" + (control?.panel != null) + "; enabled=" + control?.enabledInHierarchy);
            var manager = VRManager.Instance;
            var tick = typeof(VRPointer).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            void Set(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Aim()
            {
                Rect bounds = surface.Panel.visualTree.layout;
                Rect visible = control.worldBound;
                for (VisualElement parent = control.parent; parent != null; parent = parent.parent)
                    if (parent is ScrollView scroll)
                    {
                        Rect clip = scroll.contentViewport.worldBound;
                        visible = Rect.MinMaxRect(Mathf.Max(visible.xMin, clip.xMin + 4), Mathf.Max(visible.yMin, clip.yMin + 4),
                            Mathf.Min(visible.xMax, clip.xMax - 4), Mathf.Min(visible.yMax, clip.yMax - 4));
                    }
                Require(visible.width > 4 && visible.height > 4, "App control is clipped outside its native viewport: " + control.name);
                Vector2 center = visible.center;
                appFixtureSurface = surface; appFixturePoint = center; appFixtureAim = true;
                Vector2 uv = new Vector2(center.x / bounds.width, 1 - center.y / bounds.height);
                Require(uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1, "App control lies outside the desktop texture");
                Vector3 point = surface.transform.TransformPoint(new Vector3(uv.x - .5f, uv.y - .5f, 0));
                manager.Rig.Right.Aim.SetPositionAndRotation(point - surface.transform.forward * .35f, surface.transform.rotation);
                // The fixture teleports the aim between controls rather than moving a real hand.
                typeof(VRPointer).GetMethod("ResetAim", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager.Pointer, null);
            }
            try
            {
                yield return new WaitForEndOfFrame();
                holdInjectedTrigger = true;
                Aim(); Set("Trigger", true); Set("TriggerDown", true); Set("TriggerUp", false); tick.Invoke(manager.Pointer, null);
                Require(manager.Pointer.HasPanelHit, "App control ray missed the desktop");
                var recipient = Member(manager.Pointer, "pressedElement") as VisualElement;
                if (recipient != null && recipient != control && !control.Contains(recipient))
                {
                    Logger.LogInfo("APP MISS expected: " + control.name + "; rect=" + control.worldBound);
                    for (var parent = recipient; parent != null; parent = parent.parent)
                        Logger.LogInfo("APP MISS recipient: " + parent.GetType().Name + "/" + parent.name + "; classes=" + string.Join(",", parent.GetClasses())
                            + "; rect=" + parent.worldBound);
                    RenderTexture previous = RenderTexture.active; Texture2D capture = null;
                    try
                    {
                        RenderTexture.active = surface.Texture;
                        capture = new Texture2D(surface.Texture.width, surface.Texture.height, TextureFormat.RGBA32, false);
                        capture.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0); capture.Apply();
                        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(readyFile), "app-control-miss.png"), capture.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; if (capture != null) Destroy(capture); }
                }
                Require(ReferenceEquals(Member(manager.Pointer, "pressedPanel"), surface) && recipient != null && (recipient == control || control.Contains(recipient)),
                    "App control ray selected another element: " + recipient + "; surface=" + (Member(manager.Pointer, "pressedPanel") as VRPanelSurface)?.name);
                yield return null;
                yield return new WaitForEndOfFrame();
                holdInjectedTrigger = false;
                Aim(); Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", true); tick.Invoke(manager.Pointer, null);
                Set("TriggerUp", false);
                yield return null;
            }
            finally
            {
                holdInjectedTrigger = false;
                Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false);
                if (cancelAfter) manager.Pointer.Cancel();
                appFixtureAim = previousAim; appFixtureSurface = previousSurface; appFixturePoint = previousPoint;
            }
        }

        private IEnumerator TestAppWindow(object os, object app, string id, VRPanelSurface surface)
        {
            string original = Member(app, "State")?.ToString();
            bool originalMaximized = Member(app, "IsMaximized") is bool maxBefore && maxBefore;
            try
            {
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                yield return null;
                yield return null;
                Require(Member(app, "State")?.ToString() == "OpenVisible", "Native app did not open");
                var window = Member(app, "Window") as VisualElement;
                Require(window?.panel != null && window.worldBound.width > 1, "App window did not lay out");
                var maximize = Member(app, "MaximizeButton") as Button;
                if (maximize != null && maximize.enabledInHierarchy)
                {
                    bool before = Member(app, "IsMaximized") is bool maximized && maximized;
                    var click = ClickAppControl(surface, maximize);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    Require(Member(app, "IsMaximized") is bool max && max != before, "Controller maximize/restore did not toggle");
                    click = ClickAppControl(surface, maximize);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    Require(Member(app, "IsMaximized") is bool restored && restored == before, "Controller maximize/restore did not restore its starting state");
                }
                var minimize = window.Query<Button>(className: "kolkata-window-button").ToList()
                    .FirstOrDefault(b => b != maximize && !b.ClassListContains("kolkata-window-button--close"));
                if (minimize != null && minimize.enabledInHierarchy)
                {
                    var click = ClickAppControl(surface, minimize);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    Require(Member(app, "State")?.ToString() != "OpenVisible", "Controller minimize failed");
                    AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                    yield return null;
                }
                var close = window.Q<Button>(className: "kolkata-window-button--close");
                Require(close != null && close.enabledInHierarchy, "Native app has no enabled close control");
                var closing = ClickAppControl(surface, close);
                try { while (closing.MoveNext()) yield return closing.Current; }
                finally { (closing as IDisposable)?.Dispose(); }
                Require(Member(app, "State")?.ToString() == "Closed", "Controller close failed");
            }
            finally
            {
                if (Member(app, "IsMaximized") is bool currentMax && currentMax != originalMaximized)
                    AccessTools.Method(os.GetType(), "ToggleMaximizeApp").Invoke(os, new[] { app });
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                if (original != "Closed") AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                if (original == "OpenMinimized") AccessTools.Method(os.GetType(), "MinimizeApp").Invoke(os, new[] { app });
            }
        }

        private IEnumerator TestAllAppWindows(object os, VRPanelSurface surface)
        {
            var apps = Member(os, "m_appsById") as IDictionary;
            Require(apps != null, "App registry missing");
            var report = new List<string> { "# native application window checks", "", "Single simulated VR client. Window controls are tested; internal app workflows, external services and headset usability remain unverified.", "", "| app | result |", "| --- | --- |" };
            foreach (string id in apps.Keys.Cast<string>().OrderBy(v => v).ToArray())
            {
                object app = apps[id];
                string contentType = Member(Member(app, "Definition"), "contentType") as string;
                if (contentType == "discordInvite" || contentType == "promptEditor")
                {
                    report.Add("| " + id + " | " + (contentType == "discordInvite" ? "external invitation link; no in-game window" : "disabled by the native game") + " |");
                    continue;
                }
                if (!(Member(app, "IsAvailable") is bool available && available))
                {
                    report.Add("| " + id + " | unavailable in this native lobby; not tested |");
                    continue;
                }
                bool passed = true;
                string failure = "";
                IEnumerator test = TestAppWindow(os, app, id, surface);
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = test.MoveNext(); }
                        catch (Exception ex) { passed = false; failure = ex.GetBaseException().Message.Replace('|', '/'); break; }
                        if (!more) break;
                        yield return test.Current;
                    }
                }
                finally { (test as IDisposable)?.Dispose(); }
                report.Add("| " + id + " | " + (passed ? "window controls passed; workflows unverified" : "FAILED: " + failure) + " |");
                Logger.LogInfo("APP WINDOW " + (passed ? "PASS: " : "FAIL: ") + id + (passed ? "" : ": " + failure));
            }
            File.WriteAllLines(Path.Combine(Path.GetDirectoryName(readyFile), "app-window-checks.md"), report);
        }
    }
}
