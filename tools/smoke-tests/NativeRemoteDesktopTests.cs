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
        private static object remoteFixtureOwner, remoteFixtureResponse;
        private static void ObserveRemoteConnection(object __instance, object __0)
        {
            if (ReferenceEquals(__instance, remoteFixtureOwner) && __0 != null
                && Convert.ToUInt32(Member(__0, "callNetId")) == submissionCallId)
                remoteFixtureResponse = __0;
        }

        private IEnumerator RemoteClick(VRPanelSurface surface, VisualElement control, bool twice = false)
        {
            Require(control != null, "Native remote desktop control missing");
            yield return new WaitForSecondsRealtime(.2f);
            var scroll = control.GetFirstAncestorOfType<ScrollView>();
            if (scroll != null)
            {
                bool Visible()
                {
                    Rect view = scroll.contentViewport.worldBound, bounds = control.worldBound;
                    return Mathf.Min(view.yMax, bounds.yMax) - Mathf.Max(view.yMin, bounds.yMin) >= Mathf.Min(24, bounds.height - 2);
                }
                try
                {
                    appFixtureSurface = surface; appFixtureAim = true;
                    float deadline = Time.realtimeSinceStartup + 8;
                    while (!Visible())
                    {
                        Rect view = scroll.contentViewport.worldBound;
                        Require(view.width > 5 && view.height > 5 && Time.realtimeSinceStartup < deadline,
                            "Controller did not reveal native remote file control: " + string.Join(",", control.GetClasses()) + "; bounds=" + control.worldBound + "; viewport=" + view);
                        appFixturePoint = view.center;
                        appFixtureScroll = control.worldBound.yMax > view.yMax ? -1 : 1;
                        yield return null;
                    }
                }
                finally
                {
                    appFixtureScroll = 0; appFixtureAim = false; appFixtureSurface = null;
                    VRManager.Instance.Input.GetType().GetProperty("Move").SetValue(VRManager.Instance.Input, Vector2.zero);
                }
            }
            for (int index = 0; index < (twice ? 2 : 1); index++)
            {
                var click = ClickAppControl(surface, control, !twice || index == 1);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
            }
            yield return new WaitForSecondsRealtime(.2f);
        }

        private IEnumerator TestNativeRemoteDesktop(object os, VRPanelSurface surface)
        {
            object call = callFixtureCaller;
            object owner = Member(os, "m_anyViewerApp");
            object player = AccessTools.Property(Find("KolkataOsPlayer"), "LocalPlayerInstance").GetValue(null);
            var apps = Member(os, "m_appsById") as IDictionary;
            object app = apps?.Values.Cast<object>().FirstOrDefault(a => Member(Member(a, "Definition"), "contentType") as string == "anyViewer");
            Require(owner != null && app != null && player != null && ReferenceEquals(Member(owner, "m_boundCall"), call)
                && !(bool)Member(owner, "IsConnectionActive"), "Remote desktop fixture requires an unconnected owned caller");
            string id = Member(Member(app, "Definition"), "id") as string;
            object sentinel = Member(call, "sentinelCallState");
            const string tag = "FICTIONAL_REMOTE_DESKTOP_CODE";
            var probe = new Harmony("com.swyf.vr.remotedesktopfixture");
            try
            {
                submissionPlayer = player; submissionCall = call; submissionGuid = (Guid)Member(call, "CallGuid");
                submissionCallId = Convert.ToUInt32(Member(call, "netId")); submissionPayments = 0; submissionReward = 0;
                remoteFixtureOwner = owner; remoteFixtureResponse = null;
                // Preserve native verification and RPCs while excluding persistent
                // wallet, medal, review and analytics changes for this owned caller.
                probe.Patch(AccessTools.Method(player.GetType(), "ServerCreditScamPayout"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveSubmissionPayment)));
                probe.Patch(AccessTools.Method(call.GetType(), "RpcRecordReviewPayout"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipSubmissionReview)));
                probe.Patch(AccessTools.Method(Find("Kolkata.Analytics.AnalyticsHooks"), "Scam"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipSubmissionAnalytics)));
                probe.Patch(AccessTools.Method(owner.GetType(), "OnConnectionResult"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveRemoteConnection)));
                AccessTools.Method(call.GetType(), "RefreshUnlockedScamConfiguration").Invoke(call, null);
                AccessTools.Method(sentinel.GetType(), "EnsureSpokenValue").Invoke(sentinel, new object[] { tag, "en" });
                object info = AccessTools.Method(sentinel.GetType(), "GetInfo").Invoke(sentinel, new object[] { tag });
                string code = Member(info, "RawValue") as string;
                Require(!string.IsNullOrWhiteSpace(code) && (bool)Member(info, "IsRevealed"), "Native fictional remote code was not disclosed");
                AccessTools.Method(call.GetType(), "RefreshPublicUiState").Invoke(call, null);
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "phone" });
                yield return new WaitForSecondsRealtime(.5f);
                object phone = apps["phone"];
                if (Member(phone, "State")?.ToString() == "OpenVisible")
                {
                    var minimize = RemoteClick(surface, (Member(phone, "Window") as VisualElement)?.Q<Button>(className: "kolkata-window-button--minimize"));
                    try { while (minimize.MoveNext()) yield return minimize.Current; }
                    finally { (minimize as IDisposable)?.Dispose(); }
                    Require(Member(phone, "State")?.ToString() != "OpenVisible", "Controller did not minimize the overlapping native phone");
                }
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                yield return new WaitForSecondsRealtime(.5f);
                VisualElement Content() => Member(app, "WindowContent") as VisualElement;
                TextField CodeField() => Content()?.Q<TextField>(className: "kolkata-anyviewer__code-field");
                Button Connect() => Content()?.Q<Button>(className: "kolkata-anyviewer__connect");
                string wrong = (code[0] == '9' ? "8" : "9") + code.Substring(1);
                var action = EnterScamText(surface, CodeField(), wrong);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                action = RemoteClick(surface, Connect());
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                float deadline = Time.realtimeSinceStartup + 8;
                while (remoteFixtureResponse == null)
                { Require(Time.realtimeSinceStartup < deadline, "Native wrong remote code did not reply"); yield return null; }
                Require(Member(remoteFixtureResponse, "status") as string == "mismatch" && !(bool)Member(owner, "IsConnectionActive")
                    && !(bool)Member(call, "serverRdpSessionOpen") && submissionPayments == 0,
                    "Native remote desktop did not reject a wrong fictional code");
                Logger.LogInfo("REMOTE DESKTOP PASS: controller-focused synthetic typing and connect receive a real native wrong-code rejection");
                remoteFixtureResponse = null;
                action = EnterScamText(surface, CodeField(), code);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                action = RemoteClick(surface, Connect());
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                deadline = Time.realtimeSinceStartup + 12;
                while (remoteFixtureResponse == null || !(bool)Member(owner, "IsConnectionActive"))
                { Require(Time.realtimeSinceStartup < deadline, "Native remote desktop connection did not open"); yield return null; }
                Require((bool)Member(remoteFixtureResponse, "connected") && (bool)Member(call, "serverRdpSessionOpen")
                    && submissionPayments == 1 && submissionReward > 0, "Native remote connection did not calculate its single payout request");
                object profile = AccessTools.Method(owner.GetType(), "GetCurrentProfile").Invoke(owner, new object[] { false });
                object[] entries = ((IEnumerable)Member(profile, "Entries")).Cast<object>().ToArray();
                object file = entries.FirstOrDefault(e => Member(e, "entryType") as string == "text" && !string.IsNullOrEmpty(Member(e, "parentId") as string));
                Require(file != null, "Native fictional remote desktop generated no text file");
                var nestedApps = Member(owner, "m_appsById") as IDictionary;
                object explorer = nestedApps?["file-explorer"];
                Require(explorer != null, "Native remote file explorer missing");
                var path = new List<object>();
                string folder = Member(file, "parentId") as string;
                while (!string.IsNullOrEmpty(folder))
                {
                    object parent = entries.FirstOrDefault(e => Member(e, "id") as string == folder);
                    Require(parent != null && !path.Contains(parent) && path.Count < 20, "Native remote file ancestry is invalid");
                    path.Insert(0, parent); folder = Member(parent, "parentId") as string;
                }
                Button Entry(object entry) => (Member(explorer, "WindowContent") as VisualElement)?.Query<Button>(className: "kolkata-rdp-explorer__entry")
                    .ToList().FirstOrDefault(b => b.tooltip == Member(entry, "name") as string);
                Require(path.Count > 0, "Native remote text file has no desktop folder");
                var folderIcon = Content().Query<Button>(className: "kolkata-desktop-icon").ToList()
                    .FirstOrDefault(b => b.tooltip == Member(path[0], "name") as string);
                action = RemoteClick(surface, folderIcon, true);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                Require(Member(owner, "m_currentFolderId") as string == Member(path[0], "id") as string, "Controller desktop folder double-click did not open explorer");
                foreach (object parent in path.Skip(1))
                {
                    action = RemoteClick(surface, Entry(parent), true);
                    try { while (action.MoveNext()) yield return action.Current; }
                    finally { (action as IDisposable)?.Dispose(); }
                    Require(Member(owner, "m_currentFolderId") as string == Member(parent, "id") as string, "Controller double-click did not open remote folder");
                }
                if (path.Count > 0)
                {
                    for (int direction = 0; direction < 2; direction++)
                    {
                        var toolbar = (Member(explorer, "WindowContent") as VisualElement).Query<Button>(className: "kolkata-rdp-explorer__toolbar-button").ToList();
                        action = RemoteClick(surface, toolbar[direction]);
                        try { while (action.MoveNext()) yield return action.Current; }
                        finally { (action as IDisposable)?.Dispose(); }
                    }
                    Require(Member(owner, "m_currentFolderId") as string == Member(file, "parentId") as string, "Remote back/forward failed to restore the folder");
                }
                action = RemoteClick(surface, Entry(file), true);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                object notepad = nestedApps["notepad"];
                Require(ReferenceEquals(Member(owner, "m_currentTextEntry"), file), "Controller double-click did not open native remote text file");
                TextField Editor() => (Member(notepad, "WindowContent") as VisualElement)?.Q<TextField>(className: "kolkata-notes-editor");
                const string edited = "VR remote desktop fixture";
                action = EnterScamText(surface, Editor(), edited);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                Require(Member(file, "contents") as string == edited, "Native remote notepad did not update its fictional file");
                action = RemoteClick(surface, (Member(notepad, "Window") as VisualElement)?.Q<Button>(className: "kolkata-window-button--close"));
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                if (Member(explorer, "State")?.ToString() != "OpenVisible")
                {
                    action = RemoteClick(surface, Member(explorer, "TaskbarButton") as VisualElement);
                    try { while (action.MoveNext()) yield return action.Current; }
                    finally { (action as IDisposable)?.Dispose(); }
                }
                action = RemoteClick(surface, Entry(file), true);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                Require(Editor()?.value == edited, "Native remote text edit did not survive closing/reopening its window");
                Logger.LogInfo("REMOTE DESKTOP PASS: real connection opens generated fictional files; controller double-click, folders, back/forward and notepad edit/reopen work; save payout intercepted");
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    action = RemoteClick(surface, Content()?.Q<Button>(className: "kolkata-taskbar-power"));
                    try { while (action.MoveNext()) yield return action.Current; }
                    finally { (action as IDisposable)?.Dispose(); }
                    deadline = Time.realtimeSinceStartup + 8;
                    while ((bool)Member(call, "serverRdpSessionOpen"))
                    { Require(Time.realtimeSinceStartup < deadline, "Native remote disconnect did not reach server"); yield return null; }
                    Require(!(bool)Member(owner, "IsConnectionActive") && CodeField() != null, "Native remote disconnect did not restore connection form");
                    if (cycle == 1) break;
                    remoteFixtureResponse = null;
                    action = EnterScamText(surface, CodeField(), code);
                    try { while (action.MoveNext()) yield return action.Current; }
                    finally { (action as IDisposable)?.Dispose(); }
                    action = RemoteClick(surface, Connect());
                    try { while (action.MoveNext()) yield return action.Current; }
                    finally { (action as IDisposable)?.Dispose(); }
                    deadline = Time.realtimeSinceStartup + 8;
                    while (remoteFixtureResponse == null || !(bool)Member(owner, "IsConnectionActive"))
                    { Require(Time.realtimeSinceStartup < deadline, "Native remote reconnect did not finish"); yield return null; }
                    Require(Member(remoteFixtureResponse, "status") as string == "already_verified" && submissionPayments == 1,
                        "Native remote reconnect repeated its payout or returned an unexpected status");
                }
                Logger.LogInfo("REMOTE DESKTOP PASS: controller disconnect closes native server session; reconnect returns already_verified without another reward request");
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel(); holdInjectedTrigger = false;
                if ((bool)Member(owner, "IsConnectionActive")) AccessTools.Method(owner.GetType(), "Disconnect").Invoke(owner, null);
                probe.UnpatchSelf(); remoteFixtureOwner = null; remoteFixtureResponse = null;
                submissionPlayer = null; submissionCall = null; submissionGuid = Guid.Empty;
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
            }
        }
    }
}
