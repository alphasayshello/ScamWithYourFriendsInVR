using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object submissionPlayer, submissionCall;
        private static Guid submissionGuid;
        private static uint submissionCallId;
        private static string submissionSource, submissionTag, submissionResponse;
        private static int submissionPayments, submissionReward;
        private static bool ObserveSubmissionPayment(object __instance, Guid __0, int __1)
        {
            if (!ReferenceEquals(__instance, submissionPlayer) || __0 != submissionGuid) return true;
            submissionPayments++; submissionReward += __1; return false;
        }
        private static bool SkipSubmissionReview(object __instance) => !ReferenceEquals(__instance, submissionCall);
        private static bool SkipSubmissionAnalytics(Guid __0) => __0 != submissionGuid;
        private static void ObserveSubmissionResponse(uint callId, string source, string tag, string status)
        { if (callId == submissionCallId && source == submissionSource && tag == submissionTag) submissionResponse = status; }

        private IEnumerator EnterScamText(VRPanelSurface surface, TextField input, string text)
        {
            var reveal = ScrollToScamControl(surface, input);
            try { while (reveal.MoveNext()) yield return reveal.Current; }
            finally { (reveal as IDisposable)?.Dispose(); }
            var click = ClickAppControl(surface, input);
            try { while (click.MoveNext()) yield return click.Current; }
            finally { (click as IDisposable)?.Dispose(); }
            var editor = input.focusController.focusedElement as VisualElement;
            Require(editor != null && (editor == input || input.Contains(editor)), "Controller did not focus the native scam editor");
            using (var select = KeyDownEvent.GetPooled('\0', KeyCode.A, EventModifiers.Control)) editor.SendEvent(select);
            using (var selectUp = KeyUpEvent.GetPooled('\0', KeyCode.A, EventModifiers.Control)) editor.SendEvent(selectUp);
            yield return null;
            using (var erase = KeyDownEvent.GetPooled('\b', KeyCode.Backspace, EventModifiers.FunctionKey)) editor.SendEvent(erase);
            using (var eraseUp = KeyUpEvent.GetPooled('\b', KeyCode.Backspace, EventModifiers.FunctionKey)) editor.SendEvent(eraseUp);
            yield return null;
            Require(input.value.Length == 0, "Native select-all/backspace did not clear the scam field");
            foreach (char character in text)
            {
                var target = input.focusController.focusedElement as VisualElement;
                Require(target != null && (target == input || input.Contains(target)), "Native scam input lost controller focus while typing");
                using (var down = KeyDownEvent.GetPooled(character, KeyCode.None, EventModifiers.None)) target.SendEvent(down);
                using (var up = KeyUpEvent.GetPooled(character, KeyCode.None, EventModifiers.None)) target.SendEvent(up);
                yield return null;
            }
            Require(input.value == text, "Native scam field did not process synthetic character events");
        }

        private IEnumerator TestNativeScamSubmission(object os, VRPanelSurface surface)
        {
            object call = callFixtureCaller;
            Require(call != null && ReferenceEquals(Member(os, "m_boundVictimCall"), call), "Submission fixture requires the owned talking caller");
            object player = AccessTools.Property(Find("KolkataOsPlayer"), "LocalPlayerInstance").GetValue(null);
            object app = (Member(os, "m_appsById") as IDictionary)?["car-registration"];
            Require(player != null && app != null, "Native car registration app/player missing");
            object session = Member(call, "conversationScams"), sentinel = Member(call, "sentinelCallState");
            object definition = AccessTools.Method(Find("ConversationScamCatalog"), "GetByAppId").Invoke(null, new object[] { "car-registration" });
            Require(session != null && sentinel != null && definition != null
                && Member(definition, "submissionRequiresObjectives") is bool required && !required
                && Convert.ToInt32(Member(session, "MoneyEarned")) == 0, "Native car registration preconditions changed");
            string tag = Member(definition, "sentinel") as string;
            int reward = Convert.ToInt32(Member(definition, "reward"));
            object builder = Member(os, "m_contentBuilder");
            string source = AccessTools.Method(builder.GetType(), "GetBindingCompletionSource").Invoke(null, new[] { app }) as string;
            Require(!string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(source) && reward > 0, "Native car registration binding missing");
            var responseEvent = player.GetType().GetEvent("OnScamFieldVerificationResponse", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Require(responseEvent != null, "Native verification response event missing");
            var callback = Delegate.CreateDelegate(responseEvent.EventHandlerType, typeof(LocalPairHarness).GetMethod(nameof(ObserveSubmissionResponse), BindingFlags.Static | BindingFlags.NonPublic));
            var probe = new Harmony("com.swyf.vr.validsubmissionfixture");
            try
            {
                submissionPlayer = player; submissionCall = call; submissionGuid = (Guid)Member(call, "CallGuid");
                submissionCallId = Convert.ToUInt32(Member(call, "netId")); submissionSource = source; submissionTag = tag;
                submissionResponse = null; submissionPayments = 0; submissionReward = 0;
                responseEvent.GetAddMethod(true).Invoke(null, new object[] { callback });
                // Keep native verification/reward calculation and response RPCs,
                // excluding save writes, medals and global review/analytics effects.
                probe.Patch(AccessTools.Method(player.GetType(), "ServerCreditScamPayout"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveSubmissionPayment)));
                probe.Patch(AccessTools.Method(call.GetType(), "RpcRecordReviewPayout"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipSubmissionReview)));
                probe.Patch(AccessTools.Method(Find("Kolkata.Analytics.AnalyticsHooks"), "Scam"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipSubmissionAnalytics)));
                AccessTools.Method(call.GetType(), "RefreshUnlockedScamConfiguration").Invoke(call, null);
                // This is the same fictional-data disclosure used for a caller's
                // reply. No real personal information or AI dialogue is supplied.
                AccessTools.Method(sentinel.GetType(), "EnsureSpokenValue").Invoke(sentinel, new object[] { tag, "en" });
                object info = AccessTools.Method(sentinel.GetType(), "GetInfo").Invoke(sentinel, new object[] { tag });
                string value = Member(info, "RawValue") as string;
                Require(!string.IsNullOrWhiteSpace(value) && Member(info, "IsRevealed") is bool revealed && revealed,
                    "Native fictional registration disclosure failed");
                AccessTools.Method(call.GetType(), "RefreshPublicUiState").Invoke(call, null);
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "car-registration" });
                yield return new WaitForSecondsRealtime(.5f);
                VisualElement Content() => Member(app, "WindowContent") as VisualElement;
                TextField Input() => Content()?.Q<TextField>("scam-entry-input");
                Button Submit() => Content()?.Q<Button>("scam-entry-submit");
                Label Status() => Content()?.Q<Label>("scam-entry-status");
                string wrong = (value[0] == 'A' ? "B" : char.IsDigit(value[0]) ? ((value[0] - '0' + 1) % 10).ToString() : "A") + value.Substring(1);
                var type = EnterScamText(surface, Input(), wrong);
                try { while (type.MoveNext()) yield return type.Current; }
                finally { (type as IDisposable)?.Dispose(); }
                var revealControl = ScrollToScamControl(surface, Submit());
                try { while (revealControl.MoveNext()) yield return revealControl.Current; }
                finally { (revealControl as IDisposable)?.Dispose(); }
                var click = ClickAppControl(surface, Submit());
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                float deadline = Time.realtimeSinceStartup + 8;
                while (submissionResponse == null || Status()?.ClassListContains("kolkata-field-status--error") != true)
                { Require(Time.realtimeSinceStartup < deadline, "Native wrong registration response/feedback did not arrive: " + submissionResponse); yield return null; }
                Require(submissionResponse == "mismatch" && submissionPayments == 0 && Convert.ToInt32(Member(session, "MoneyEarned")) == 0,
                    "Incorrect registration was not rejected by native server verification");
                Logger.LogInfo("SCAM SUBMISSION PASS: controller-focused field handles synthetic typing and the real server rejects a wrong fictional registration");
                submissionResponse = null;
                type = EnterScamText(surface, Input(), value);
                try { while (type.MoveNext()) yield return type.Current; }
                finally { (type as IDisposable)?.Dispose(); }
                revealControl = ScrollToScamControl(surface, Submit());
                try { while (revealControl.MoveNext()) yield return revealControl.Current; }
                finally { (revealControl as IDisposable)?.Dispose(); }
                click = ClickAppControl(surface, Submit());
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                deadline = Time.realtimeSinceStartup + 8;
                while (submissionResponse == null || Status()?.ClassListContains("kolkata-field-status--verified") != true)
                { Require(Time.realtimeSinceStartup < deadline, "Native valid registration response/feedback did not arrive: " + submissionResponse); yield return null; }
                Require(submissionResponse == "scam_completed" && submissionPayments == 1 && submissionReward == reward
                    && Convert.ToInt32(Member(session, "MoneyEarned")) == reward && Input().isReadOnly && !Submit().enabledInHierarchy,
                    "Native valid submission did not complete the app, calculate its reward once and lock the verified form");
                Logger.LogInfo("SCAM SUBMISSION PASS: controller submit completes native car registration, publishes verified UI and requests its calculated reward once; save credit/medals intercepted");
                submissionResponse = null;
                AccessTools.Method(player.GetType(), "RequestScamFieldVerification").Invoke(player, new object[] { submissionCallId, source, tag, value });
                deadline = Time.realtimeSinceStartup + 8;
                while (submissionResponse == null)
                { Require(Time.realtimeSinceStartup < deadline, "Native duplicate submission did not reply"); yield return null; }
                Require(submissionResponse == "already_verified" && submissionPayments == 1 && Convert.ToInt32(Member(session, "MoneyEarned")) == reward,
                    "Native duplicate verification changed the reward or failed to reject replay");
                Logger.LogInfo("SCAM SUBMISSION PASS: a duplicate native verification request returns already_verified without another reward request");
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel(); holdInjectedTrigger = false;
                responseEvent.GetRemoveMethod(true).Invoke(null, new object[] { callback }); probe.UnpatchSelf();
                submissionPlayer = null; submissionCall = null; submissionGuid = Guid.Empty; submissionResponse = null;
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
            }
        }
    }
}
