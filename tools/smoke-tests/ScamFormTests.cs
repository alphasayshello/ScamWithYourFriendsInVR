using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object scamFormPlayer;
        private static int scamFormSubmissions;
        private static bool ObserveEmptyFormRequest(object __instance)
        {
            if (!ReferenceEquals(__instance, scamFormPlayer)) return true;
            scamFormSubmissions++; return false;
        }

        private static bool ScamEntryDisplayed(VisualElement control)
        {
            for (var element = control; element != null; element = element.parent)
                if (!element.visible || element.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        private IEnumerator TestNotificationDismiss(object os, VRPanelSurface surface)
        {
            Type type = Find("Kolkata.UI.KolkataOsNotificationCenter");
            object center = Activator.CreateInstance(type, new[] { Member(os, "m_root"), Member(os, "m_assets"), Member(os, "m_uiAudio") });
            int opened = 0;
            try
            {
                AccessTools.Method(type, "ShowSvg").Invoke(center, new object[] { "VR notification fixture", "KolkataOS/SVG/camera", "#0078D7", new Action(() => opened++), false });
                yield return new WaitForSecondsRealtime(.6f);
                var entries = Member(center, "m_entries") as IList;
                Require(entries?.Count == 1, "Native notification fixture did not appear");
                var element = Member(entries[0], "Element") as VisualElement;
                var close = element?.Q<Button>(className: "kolkata-notification__close");
                var click = ClickAppControl(surface, close);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                float deadline = Time.realtimeSinceStartup + 3;
                while (entries.Count != 0 || element.panel != null)
                { Require(Time.realtimeSinceStartup < deadline, "Controller did not dismiss the native notification"); yield return null; }
                Require(opened == 0, "Closing a notification also activated its app action");
                Logger.LogInfo("NOTIFICATION PASS: controller dismisses a native clickable toast without activating its body action");
            }
            finally { (center as IDisposable)?.Dispose(); }
        }

        private IEnumerator ScrollToScamControl(VRPanelSurface surface, VisualElement control)
        {
            var scroll = control.GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) yield break;
            bool moved = false;
            bool Fits()
            {
                Rect view = scroll.contentViewport.worldBound, bounds = control.worldBound;
                return bounds.yMin >= view.yMin + 8 && bounds.yMax <= view.yMax - 8;
            }
            try
            {
                appFixtureSurface = surface; appFixtureAim = true;
                float deadline = Time.realtimeSinceStartup + 8;
                while (!Fits())
                {
                    Rect viewport = scroll.contentViewport.worldBound;
                    Require(viewport.width > 5 && viewport.height > 5 && Time.realtimeSinceStartup < deadline,
                        "Controller scrolling did not reveal the native scam control: " + control.name);
                    appFixturePoint = viewport.center;
                    appFixtureScroll = control.worldBound.yMax > viewport.yMax - 8 ? -1 : 1;
                    moved = true; yield return null;
                }
                appFixtureScroll = 0;
                VRManager.Instance.Input.GetType().GetProperty("Move").SetValue(VRManager.Instance.Input, Vector2.zero);
                if (moved)
                {
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(Fits(), "Native scam control left the viewport after scrolling stopped");
                    Logger.LogInfo("SCAM SCROLL PASS: controller stick reveals a clipped native form control; control=" + control.name);
                }
            }
            finally
            {
                appFixtureScroll = 0; appFixtureAim = false; appFixtureSurface = null;
                VRManager.Instance.Input.GetType().GetProperty("Move").SetValue(VRManager.Instance.Input, Vector2.zero);
            }
        }

        private IEnumerator TestScamForms(object os, VRPanelSurface surface)
        {
            object game = Instance("ScammerGameManager");
            bool originalUnlock = (bool)Member(game, "DeveloperAllAppsUnlocked");
            var unlock = AccessTools.Method(game.GetType(), "ServerSetDeveloperLobbySettings");
            var refresh = AccessTools.Method(os.GetType(), "RefreshApplicationAccessRuntimes");
            Require(unlock != null && refresh != null && !originalUnlock, "Scam form fixture requires the owned lobby's normal app access state");
            var originalStates = new Dictionary<string, string>();
            object originalLayout = AccessTools.Method(os.GetType(), "CaptureRuntimeSnapshots").Invoke(os, null);
            var originalApps = Member(os, "m_appsById") as IDictionary;
            foreach (DictionaryEntry entry in originalApps) originalStates[(string)entry.Key] = Member(entry.Value, "State")?.ToString();
            var claims = Member(os, "m_claimFieldValues") as IDictionary;
            Require(claims != null, "Native claim field cache missing");
            var originalClaims = new Dictionary<object, object>();
            foreach (DictionaryEntry entry in claims) originalClaims[entry.Key] = entry.Value;
            var probe = new Harmony("com.swyf.vr.scamformfixture");
            int windows = 0, fields = 0, locked = 0, noEntry = 0;
            try
            {
                scamFormPlayer = AccessTools.Property(Find("KolkataOsPlayer"), "LocalPlayerInstance").GetValue(null);
                Require(scamFormPlayer != null, "Native scam form player missing");
                scamFormSubmissions = 0;
                // A blank form must stop in local validation. Keep any unexpected
                // request from mutating gameplay, then fail the assertion below.
                probe.Patch(AccessTools.Method(scamFormPlayer.GetType(), "RequestScamFieldVerification"),
                    prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveEmptyFormRequest)));
                var notification = TestNotificationDismiss(os, surface);
                try { while (notification.MoveNext()) yield return notification.Current; }
                finally { (notification as IDisposable)?.Dispose(); }
                unlock.Invoke(game, new object[] { true, true }); refresh.Invoke(os, null);
                yield return null; yield return null;
                var ticTacToe = TestNativeTicTacToe(os, surface);
                try { while (ticTacToe.MoveNext()) yield return ticTacToe.Current; }
                finally { (ticTacToe as IDisposable)?.Dispose(); }
                var apps = Member(os, "m_appsById") as IDictionary;
                string[] ids = apps.Keys.Cast<string>().Where(id => Member(Member(apps[id], "Definition"), "contentType") as string == "conversationScam")
                    .OrderBy(id => id).ToArray();
                Require(ids.Length > 0, "Native developer access did not expose any conversation scam apps");
                foreach (string id in ids)
                {
                    object app = apps[id];
                    Require(Member(app, "IsAvailable") is bool available && available, "Unlocked native scam app is unavailable: " + id);
                    AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                    yield return null; yield return null;
                    var content = Member(app, "WindowContent") as VisualElement;
                    Require(content?.panel != null && content.worldBound.width > 1 && content.childCount > 0,
                        "Native scam content did not render: " + id);
                    Require(content.Q<VisualElement>("scam-objectives") != null && content.Q<VisualElement>("scam-checklist")?.childCount > 0
                        && content.Q<Label>("scam-progress")?.text?.Contains("/") == true, "Native scam objectives did not populate: " + id);
                    var input = content.Q<TextField>("scam-entry-input");
                    var submit = content.Q<Button>("scam-entry-submit");
                    var status = content.Q<Label>("scam-entry-status");
                    if (input == null || submit == null || !ScamEntryDisplayed(input) || !ScamEntryDisplayed(submit)) noEntry++;
                    else if (!input.enabledInHierarchy || !submit.enabledInHierarchy || input.resolvedStyle.display == DisplayStyle.None) locked++;
                    else
                    {
                        var reveal = ScrollToScamControl(surface, input);
                        try { while (reveal.MoveNext()) yield return reveal.Current; }
                        finally { (reveal as IDisposable)?.Dispose(); }
                        var click = ClickAppControl(surface, input);
                        try { while (click.MoveNext()) yield return click.Current; }
                        finally { (click as IDisposable)?.Dispose(); }
                        var focused = input.focusController.focusedElement as VisualElement;
                        Require(focused != null && (focused == input || input.Contains(focused)), "Controller did not focus the native scam input: " + id);
                        input.value = string.Empty;
                        reveal = ScrollToScamControl(surface, submit);
                        try { while (reveal.MoveNext()) yield return reveal.Current; }
                        finally { (reveal as IDisposable)?.Dispose(); }
                        click = ClickAppControl(surface, submit);
                        try { while (click.MoveNext()) yield return click.Current; }
                        finally { (click as IDisposable)?.Dispose(); }
                        Require(status != null && !string.IsNullOrWhiteSpace(status.text) && status.ClassListContains("kolkata-field-status--error")
                            && submit.enabledInHierarchy && scamFormSubmissions == 0, "Native scam empty-entry validation failed: " + id);
                        fields++;
                        Logger.LogInfo("SCAM FORM PASS: controller focuses native input and empty submit reports a local validation error without a request; app=" + id);
                    }
                    var window = TestAppWindow(os, app, id, surface);
                    try { while (window.MoveNext()) yield return window.Current; }
                    finally { (window as IDisposable)?.Dispose(); }
                    AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                    windows++;
                    Logger.LogInfo("SCAM WINDOW PASS: native controller window controls; app=" + id);
                }
                Require(windows == ids.Length && fields > 0 && scamFormSubmissions == 0,
                    "Scam form fixture did not cover every exposed window and at least one enabled native entry");
                Logger.LogInfo("SCAM FORM PASS: windows=" + windows + "; focused/validated entries=" + fields + "; native locked entries=" + locked
                    + "; dialogue/special panels=" + noEntry + "; window/empty-validation coverage only");
                probe.UnpatchSelf();
                var validSubmission = TestNativeScamSubmission(os, surface);
                try { while (validSubmission.MoveNext()) yield return validSubmission.Current; }
                finally { (validSubmission as IDisposable)?.Dispose(); }
                var remoteDesktop = TestNativeRemoteDesktop(os, surface);
                try { while (remoteDesktop.MoveNext()) yield return remoteDesktop.Current; }
                finally { (remoteDesktop as IDisposable)?.Dispose(); }
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel(); holdInjectedTrigger = false;
                unlock.Invoke(game, new object[] { true, originalUnlock }); refresh.Invoke(os, null);
                claims.Clear(); foreach (var entry in originalClaims) claims.Add(entry.Key, entry.Value);
                var apps = Member(os, "m_appsById") as IDictionary;
                foreach (var state in originalStates)
                {
                    if (!apps.Contains(state.Key)) continue;
                    object app = apps[state.Key];
                    if (Member(app, "State")?.ToString() != state.Value)
                    {
                        AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                        if (state.Value != "Closed") AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { state.Key });
                        if (state.Value == "OpenMinimized") AccessTools.Method(os.GetType(), "MinimizeApp").Invoke(os, new[] { app });
                    }
                    AccessTools.Method(os.GetType(), "ApplyRuntimeSnapshot").Invoke(os, new[] { app, originalLayout });
                    AccessTools.Method(os.GetType(), "RefreshApp").Invoke(os, new[] { app });
                }
                probe.UnpatchSelf(); scamFormPlayer = null;
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "phone" });
            }
        }
    }
}
