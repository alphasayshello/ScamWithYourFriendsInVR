using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object callerQueueFixture;
        private static int callerQueueEditAttempts;
        private static bool BlockCallerQueueFixtureEdit(object __instance)
        {
            if (!ReferenceEquals(__instance, callerQueueFixture)) return true;
            callerQueueEditAttempts++; return false;
        }

        private IEnumerator TestNativeCallerQueue(object os, VRPanelSurface surface)
        {
            Require(NetworkServer.connections.Count == 1, "Caller queue fixture requires one private host");
            bool laptop = (bool)Member(os, "m_useLaptopDesktop");
            var mode = AccessTools.Method(os.GetType(), "SetLaptopDesktop");
            var field = AccessTools.Field(os.GetType(), "m_callerQueueApp");
            object original = field.GetValue(os), fixture = null, app = null;
            var probe = new Harmony("com.swyf.vr.callerqueuefixture");
            string QueueIds(object state) => string.Join("|", ((IEnumerable)Member(state, "callerIds")).Cast<object>());
            try
            {
                mode.Invoke(os, new object[] { true });
                yield return null; yield return null;
                var apps = Member(os, "m_appsById") as IDictionary;
                app = apps.Values.Cast<object>().Single(a => Member(Member(a, "Definition"), "contentType") as string == "dailyCallers");
                Require(Member(app, "IsAvailable") is true && Member(app, "State").ToString() == "Closed", "Native laptop caller manager unavailable or already open");
                fixture = Activator.CreateInstance(original.GetType(), true);
                callerQueueFixture = fixture; callerQueueEditAttempts = 0;
                probe.Patch(AccessTools.Method(fixture.GetType(), "Edit"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BlockCallerQueueFixtureEdit)));
                field.SetValue(os, fixture);
                AccessTools.Method(fixture.GetType(), "BuildContent").Invoke(fixture, new[] { app });
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new[] { Member(Member(app, "Definition"), "id") });
                yield return null; yield return null;
                string queuedIds = QueueIds(Member(fixture, "state"));
                var available = Member(fixture, "availableList") as ScrollView;
                var count = Member(fixture, "availableCount") as Label;
                int initial = int.Parse(count.text);
                Require(initial > 0 && available.contentContainer.childCount >= initial, "Native caller manager has no searchable available callers");
                Logger.LogInfo("CALLER QUEUE CONTENT PASS: native laptop app renders available callers and the host's queue");
                const string query = "swyf no caller matches 926813";
                var search = Member(fixture, "search") as TextField;
                var typing = EnterScamText(surface, search, query);
                try { while (typing.MoveNext()) yield return typing.Current; }
                finally { (typing as IDisposable)?.Dispose(); }
                Require(search.value == query && count.text == "00" && available.contentContainer.childCount == 1,
                    "Controller-focused search did not reach the native empty result state");
                Logger.LogInfo("CALLER QUEUE SEARCH PASS: controller-focused text filters the actual available list without editing the queue");
                var type = fixture.GetType();
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                    yield return null; yield return null;
                    Require((Member(fixture, "search") as TextField).value == query && (Member(fixture, "availableCount") as Label).text == "00",
                        "Native caller manager rebuild lost its search/filter");
                    foreach (var source in new[] { Tuple.Create(Find("CallSwitchboard"), "DailyQueueChanged"), Tuple.Create(Find("LanguageManager"), "OnLanguageChanged"), Tuple.Create(Find("CallerDailyQueueStore"), "Changed") })
                    {
                        var handlers = AccessTools.Field(source.Item1, source.Item2)?.GetValue(null) as Delegate;
                        Require(handlers != null && handlers.GetInvocationList().Count(h => ReferenceEquals(h.Target, fixture)) == 1,
                            "Caller manager rebuild changed event ownership: " + source.Item2);
                    }
                }
                Logger.LogInfo("CALLER QUEUE REBUILD PASS: two rebuilds preserve native search/filter and one listener per observed event");
                (Member(fixture, "search") as TextField).value = "";
                Require(int.Parse((Member(fixture, "availableCount") as Label).text) == initial
                    && QueueIds(Member(fixture, "state")) == queuedIds && callerQueueEditAttempts == 0,
                    "Clearing search did not restore callers without queue mutation");
                var window = TestAppWindow(os, app, "daily-callers", surface);
                try { while (window.MoveNext()) yield return window.Current; }
                finally { (window as IDisposable)?.Dispose(); }
                Logger.LogInfo("CALLER QUEUE WINDOW PASS: controller maximize/restore, minimize/reopen and close work on the native laptop app");
            }
            finally
            {
                try { if (app != null) AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app }); }
                finally
                {
                    try { (fixture as IDisposable)?.Dispose(); }
                    finally
                    {
                        field.SetValue(os, original);
                        try { if (app != null) AccessTools.Method(original.GetType(), "BuildContent").Invoke(original, new[] { app }); }
                        finally
                        {
                            probe.UnpatchSelf(); callerQueueFixture = null;
                            mode.Invoke(os, new object[] { laptop });
                        }
                    }
                }
            }
            Logger.LogInfo("CALLER QUEUE COMPLETE PASS: original app/desktop restored; saved edits, community services, voice preview and headset acceptance remain unverified");
        }
    }
}
