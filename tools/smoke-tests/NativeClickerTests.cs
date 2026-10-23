using System;
using System.Collections;
using System.Linq;
using System.Numerics;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool creatingClickerFixture;
        private static object clickerFixtureSession, clickerFixtureApp;
        private static int clickerFixtureEvents;
        private static bool ClickerFixtureLoad() => !creatingClickerFixture;
        private static bool ClickerFixtureSave(object __instance) => !ReferenceEquals(__instance, clickerFixtureSession);
        private static bool ClickerFixtureLeaderboard(object __instance) => !ReferenceEquals(__instance, clickerFixtureApp);
        private static void ClickerFixtureEvent(object __instance) { if (ReferenceEquals(__instance, clickerFixtureSession)) clickerFixtureEvents++; }

        private IEnumerator TestNativeClicker(object os, VRPanelSurface surface)
        {
            Require(NetworkServer.connections.Count == 1, "Clicker fixture requires one private host connection");
            object game = Instance("ScammerGameManager");
            bool originalUnlock = (bool)Member(game, "DeveloperAllAppsUnlocked");
            var unlock = AccessTools.Method(game.GetType(), "ServerSetDeveloperLobbySettings");
            var refresh = AccessTools.Method(os.GetType(), "RefreshApplicationAccessRuntimes");
            bool changed = false;
            try
            {
                if (!originalUnlock)
                {
                    unlock.Invoke(game, new object[] { true, true }); changed = true; refresh.Invoke(os, null);
                    yield return null; yield return null;
                }
                var test = TestAvailableNativeClicker(os, surface);
                try { while (test.MoveNext()) yield return test.Current; }
                finally { (test as IDisposable)?.Dispose(); }
            }
            finally { if (changed) { unlock.Invoke(game, new object[] { true, originalUnlock }); refresh.Invoke(os, null); } }
        }

        private IEnumerator TestAvailableNativeClicker(object os, VRPanelSurface surface)
        {
            var apps = Member(os, "m_appsById") as IDictionary;
            object app = apps.Values.Cast<object>().Single(a => Member(Member(a, "Definition"), "contentType") as string == "meteorCookie");
            Require((bool)Member(app, "IsAvailable") && Member(app, "State").ToString() == "Closed", "Clicker fixture requires an available closed app");
            var field = AccessTools.Field(os.GetType(), "m_clickerApp");
            object original = field.GetValue(os);
            var type = original.GetType();
            Type sessionType = Find("Kolkata.UI.KolkataOsClickerSession");
            var probe = new Harmony("com.swyf.vr.clickerfixture");
            object fixture = null, session = null, events = null;
            Action independent = () => { };
            var sessionEvent = sessionType.GetEvent("Changed");
            string key = "KolkataOS.Clicker.SaveV2";
            bool hadSave = PlayerPrefs.HasKey(key); string saved = PlayerPrefs.GetString(key, "");
            BigInteger Number(string member) => BigInteger.Parse(Member(session, member).ToString());
            int ListCount(string member) => ((IList)Member(fixture, member)).Count;
            try
            {
                probe.Patch(AccessTools.Method(sessionType, "Load"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ClickerFixtureLoad)));
                probe.Patch(AccessTools.Method(sessionType, "Save"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ClickerFixtureSave)));
                probe.Patch(AccessTools.Method(type, "SubmitLeaderboard"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ClickerFixtureLeaderboard)));
                probe.Patch(AccessTools.Method(sessionType, "ApplyHelpfulEvent"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ClickerFixtureEvent)));
                creatingClickerFixture = true;
                try { session = Activator.CreateInstance(sessionType, true); }
                finally { creatingClickerFixture = false; }
                clickerFixtureSession = session;
                sessionEvent.AddEventHandler(session, independent);
                events = Activator.CreateInstance(Find("Kolkata.UI.KolkataOsEventRegistry"), true);
                fixture = type.GetConstructors().Single().Invoke(new[] { Member(os, "m_assets"), events, Member(os, "m_uiAudio"), session, (object)false });
                clickerFixtureApp = fixture; field.SetValue(os, fixture);
                AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                string id = Member(Member(app, "Definition"), "id") as string;
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                yield return null; yield return null;
                BigInteger before = Number("TotalCookies");
                BigInteger gain = (BigInteger)AccessTools.Method(sessionType, "GetCurrentManualGain").Invoke(session, null);
                var click = ClickAppControl(surface, Member(fixture, "m_cookieButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Number("TotalCookies") == before + gain && Number("SpendableCookies") == gain,
                    "Controller cookie click did not award exactly one native gain to the isolated session");
                Logger.LogInfo("CLICKER INPUT PASS: controller cookie click awards exactly one native gain without saved progress or leaderboard submission");
                AccessTools.Field(type, "m_nextEventAt").SetValue(fixture, Time.unscaledTime - 1);
                AccessTools.Method(type, "TickHelpfulEvent").Invoke(fixture, new object[] { Time.unscaledTime });
                string eventId = Member(fixture, "m_activeEventId") as string;
                float eventExpiry = (float)Member(fixture, "m_eventExpiresAt");
                Require(eventId != null, "Native helpful event did not spawn");
                int crumbs = ListCount("m_crumbs"), icons = ListCount("m_iconParticles"), labels = ListCount("m_floatingLabels");
                before = Number("TotalCookies");
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                    yield return null; yield return null;
                    var listeners = (Delegate)AccessTools.Field(sessionType, "Changed").GetValue(session);
                    int own = listeners.GetInvocationList().Count(d => ReferenceEquals(d.Target, fixture));
                    Require(own == 1 && listeners.GetInvocationList().Contains(independent), "Native clicker rebuild duplicated session listeners; own=" + own);
                    Require(ListCount("m_crumbs") == crumbs && ListCount("m_iconParticles") == icons && ListCount("m_floatingLabels") == labels
                        && ListCount("m_categoryTabs") == 4, "Native clicker rebuild retained detached particle pools or category tabs");
                    var root = Member(fixture, "m_root") as VisualElement;
                    foreach (string pool in new[] { "m_crumbs", "m_iconParticles", "m_floatingLabels" })
                        foreach (object state in (IList)Member(fixture, pool))
                            Require(root.Contains(Member(state, "Element") as VisualElement), "Clicker retained a detached particle element");
                    Require(ReferenceEquals(Member(fixture, "m_session"), session) && Number("TotalCookies") == before, "Clicker rebuild changed session ownership or progress");
                    Require(!string.IsNullOrEmpty((Member(fixture, "m_totalValue") as Label)?.text)
                        && (Member(fixture, "m_cpmLeaderboard") as VisualElement)?.childCount > 0
                        && (Member(fixture, "m_totalLeaderboard") as VisualElement)?.childCount > 0,
                        "Native clicker rebuild retained a cache signature but lost visible totals or leaderboard rows");
                    Require(Member(fixture, "m_activeEventId") as string == eventId && (float)Member(fixture, "m_eventExpiresAt") == eventExpiry
                        && (Member(fixture, "m_eventElement") as VisualElement)?.resolvedStyle.display == DisplayStyle.Flex,
                        "Native clicker rebuild hid or restarted the live helpful event; id=" + Member(fixture, "m_activeEventId")
                        + "; expiry=" + Member(fixture, "m_eventExpiresAt") + "; expectedExpiry=" + eventExpiry
                        + "; display=" + (Member(fixture, "m_eventElement") as VisualElement)?.resolvedStyle.display);
                }
                Logger.LogInfo("CLICKER REBUILD PASS: repeated native builds retain one session listener, independent observers, bounded current-panel pools and session progress");
                clickerFixtureEvents = 0;
                click = ClickAppControl(surface, Member(fixture, "m_eventElement") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(clickerFixtureEvents == 1 && Member(fixture, "m_activeEventId") == null
                    && (Member(fixture, "m_eventElement") as VisualElement).resolvedStyle.display == DisplayStyle.None,
                    "Controller did not collect the preserved native helpful event exactly once");
                float nextEvent = (float)Member(fixture, "m_nextEventAt");
                object rootBeforeNull = Member(fixture, "m_root");
                AccessTools.Method(type, "BuildContent").Invoke(fixture, new object[] { null });
                Require(ReferenceEquals(Member(fixture, "m_root"), rootBeforeNull) && ListCount("m_crumbs") == crumbs,
                    "A native null-app build cleared a live clicker view");
                AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                yield return null; yield return null;
                Require((float)Member(fixture, "m_nextEventAt") == nextEvent, "Clicker rebuild delayed the next native helpful event");
                Logger.LogInfo("CLICKER EVENT PASS: live event expiry and next-event schedule survive rebuilds, null builds preserve the view and controller collection applies one native effect");
                foreach (string category in new[] { "Click Power", "Boosts", "Synergies", "Automation" })
                {
                    var tabs = ((IList)Member(fixture, "m_categoryTabs")).Cast<Button>();
                    click = ClickAppControl(surface, tabs.Single(b => b.userData as string == category));
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    Require(Member(fixture, "m_selectedCategory") as string == category
                        && ((IList)Member(fixture, "m_categoryTabs")).Cast<Button>().Count(b => b.ClassListContains("meteor-cookie__tab--active")) == 1,
                        "Controller category selection failed: " + category);
                }
                Logger.LogInfo("CLICKER TABS PASS: controller opens all four native upgrade categories and selects exactly one current tab");
                var manager = VRManager.Instance;
                var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
                bool priorAim = appFixtureAim; var priorSurface = appFixtureSurface; var priorPoint = appFixturePoint;
                void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
                before = Number("TotalCookies");
                try
                {
                    var button = Member(fixture, "m_cookieButton") as Button;
                    appFixtureSurface = surface; appFixturePoint = button.worldBound.center; appFixtureAim = true;
                    holdInjectedTrigger = true; Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                    tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                    var recipient = Member(manager.Pointer, "pressedElement") as VisualElement;
                    Require(recipient != null && (recipient == button || button.Contains(recipient)), "Held cookie press did not acquire native control ownership");
                    AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                    yield return null; yield return null;
                    appFixturePoint = (Member(fixture, "m_cookieButton") as Button).worldBound.center;
                    holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerUp", true);
                    tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                    Require(Number("TotalCookies") == before && !(bool)Member(manager.Pointer, "toolkitDown"),
                        "Rebuilding a pressed cookie control activated the old/new view or retained pointer ownership");
                }
                finally
                {
                    holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                    manager.Pointer.Cancel(); appFixtureAim = priorAim; appFixtureSurface = priorSurface; appFixturePoint = priorPoint;
                }
                Logger.LogInfo("CLICKER OWNERSHIP PASS: rebuilding during a held cookie press cancels old input without granting cookies or activating the replacement");
                before = Number("TotalCookies"); gain = (BigInteger)AccessTools.Method(sessionType, "GetCurrentManualGain").Invoke(session, null);
                click = ClickAppControl(surface, Member(fixture, "m_cookieButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Number("TotalCookies") == before + gain, "Rebuilt cookie button duplicated or lost native input");
                Logger.LogInfo("CLICKER RECOVERY PASS: rebuilt cookie control retains one native gain per controller click");
                var catalog = (IEnumerable)AccessTools.Property(Find("Kolkata.UI.KolkataOsClickerCatalog"), "Items").GetValue(null);
                object item = catalog.Cast<object>().First(i => (bool)Member(i, "IsAutomation"));
                var costMethod = AccessTools.Method(sessionType, "GetBulkCost");
                BigInteger cost = (BigInteger)costMethod.Invoke(session, new object[] { item, 1 });
                // Provision only the unsaved fixture economy; real native prices,
                // affordability, spending and purchase callbacks remain intact.
                AccessTools.Field(sessionType, "m_totalCookies").SetValue(session, BigInteger.Max(Number("TotalCookies"), cost * 2));
                AccessTools.Field(sessionType, "m_spendableCookies").SetValue(session, cost * 2);
                AccessTools.Method(type, "RefreshUi").Invoke(fixture, new object[] { true });
                yield return null;
                var rows = Member(fixture, "m_shopRows") as VisualElement;
                var buy = rows.Query<Button>().ToList().First(b => b.text.StartsWith("BUY 1\n", StringComparison.Ordinal));
                before = Number("SpendableCookies");
                click = ClickAppControl(surface, buy);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require((BigInteger)AccessTools.Method(sessionType, "GetCount").Invoke(session, new[] { Member(item, "id") }) == BigInteger.One
                    && Number("SpendableCookies") == before - cost, "Controller upgrade did not apply one native purchase and exact isolated cookie cost");
                Logger.LogInfo("CLICKER PURCHASE PASS: controller buys one native automation upgrade at its exact cost using only a provisioned unsaved fixture balance");
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel();
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                (fixture as IDisposable)?.Dispose();
                if (session != null) sessionEvent.RemoveEventHandler(session, independent);
                (session as IDisposable)?.Dispose(); (events as IDisposable)?.Dispose();
                field.SetValue(os, original);
                AccessTools.Method(type, "BuildContent").Invoke(original, new[] { app });
                probe.UnpatchSelf(); creatingClickerFixture = false; clickerFixtureSession = null; clickerFixtureApp = null;
                Require(PlayerPrefs.HasKey(key) == hadSave && PlayerPrefs.GetString(key, "") == saved, "Clicker fixture changed real saved progress");
            }
            Logger.LogInfo("CLICKER COMPLETE PASS: isolated native session disposed and original app/save restored; cross-account leaderboard and headset acceptance pending");
        }
    }
}
