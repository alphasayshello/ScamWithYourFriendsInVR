using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object messengerFixtureApp, messengerFixturePlayer;
        private static int messengerFixtureSendAttempts;
        private static bool MessengerFixtureActive(object app) => ReferenceEquals(app, messengerFixtureApp);
        private static bool MessengerFixtureNoSend(object __instance)
        {
            if (!ReferenceEquals(__instance, messengerFixturePlayer)) return true;
            messengerFixtureSendAttempts++; return false;
        }

        private static string MessengerScrollState(object fixture, ScrollView scroll)
        {
            object item = Member(fixture, "m_pendingConversationScroll");
            string detail = "; currentGeneration=" + Member(fixture, "m_conversationRenderGeneration");
            object[] targetArgs = { scroll, null };
            detail += "; nativeTargetFound=" + AccessTools.Method(fixture.GetType(), "TryGetCurrentScrollTarget").Invoke(null, targetArgs);
            if (targetArgs[1] is VisualElement target) detail += "; target=" + target.worldBound;
            var content = scroll.contentContainer;
            if (content.childCount > 0)
            {
                var last = content[content.childCount - 1];
                detail += "; logicalParentIsView=" + ReferenceEquals(last.parent, scroll)
                    + "; hierarchyParentIsContent=" + ReferenceEquals(last.hierarchy.parent, content);
            }
            for (Type type = item?.GetType(); type != null; type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (field.GetValue(item) is Delegate action)
                        foreach (var callback in action.GetInvocationList())
                            if (callback.Target != null)
                            {
                                var closureType = callback.Target.GetType();
                                detail += "; callback=" + callback.Method.Name;
                                var generation = AccessTools.Field(closureType, "renderGeneration");
                                var view = AccessTools.Field(closureType, "scheduledScrollView");
                                if (generation != null) detail += "; capturedGeneration=" + generation.GetValue(callback.Target);
                                if (view != null) detail += "; currentView=" + ReferenceEquals(view.GetValue(callback.Target), scroll);
                                var owner = AccessTools.Field(closureType, "<>4__this");
                                if (owner != null) detail += "; currentOwner=" + ReferenceEquals(owner.GetValue(callback.Target), fixture);
                            }
            return detail;
        }

        private IEnumerator TestNativeMessenger(object os, VRPanelSurface surface)
        {
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
                var test = TestAvailableNativeMessenger(os, surface);
                try { while (test.MoveNext()) yield return test.Current; }
                finally { (test as IDisposable)?.Dispose(); }
            }
            finally { if (changed) { unlock.Invoke(game, new object[] { true, originalUnlock }); refresh.Invoke(os, null); } }
        }

        private IEnumerator TestAvailableNativeMessenger(object os, VRPanelSurface surface)
        {
            Require(NetworkServer.connections.Count == 1, "Messenger fixture requires one private host connection");
            var apps = Member(os, "m_appsById") as IDictionary;
            object app = apps.Values.Cast<object>().Single(a => Member(Member(a, "Definition"), "contentType") as string == "messenger");
            Require((bool)Member(app, "IsAvailable") && Member(app, "State").ToString() == "Closed", "Messenger fixture requires an available closed app");
            var field = AccessTools.Field(os.GetType(), "m_messengerApp");
            object original = field.GetValue(os);
            Require(original != null, "Native messenger owner unavailable");
            Type type = original.GetType(), inbox = Find("Kolkata.UI.KolkataOsMessengerInbox");
            var history = (IList)AccessTools.Field(inbox, "s_messages").GetValue(null);
            object[] previousMessages = history.Cast<object>().ToArray();
            var ownerField = AccessTools.Field(inbox, "s_ownerNetId");
            object previousOwner = ownerField.GetValue(null), previousContact = Member(original, "m_selectedContactNetId");
            string oldSearch = (Member(original, "m_searchField") as TextField)?.value;
            string oldDraft = (Member(original, "m_messageInput") as TextField)?.value;
            uint local = NetworkClient.localPlayer.netId, alpha = uint.MaxValue - 10, beta = uint.MaxValue - 11;
            Require(!NetworkClient.spawned.ContainsKey(alpha) && !NetworkClient.spawned.ContainsKey(beta), "Fictional contacts collide with real network objects");
            object fixture = null;
            var probe = new Harmony("com.swyf.vr.messengerfixture");
            Type messageType = Find("Kolkata.UI.KolkataOsMessengerMessage");
            var changed = inbox.GetEvent("Changed");
            int notifications = 0;
            Action independent = () => notifications++;
            object Message(uint sender, string name, string body, int index, bool attachment = false)
            {
                object value = Activator.CreateInstance(messageType, true);
                void Set(string member, object data) => AccessTools.Field(messageType, member).SetValue(value, data);
                Set("Id", "vr-messenger-fixture-" + index); Set("SenderNetId", sender); Set("SenderName", name);
                Set("RecipientNetId", local); Set("RecipientName", "local fixture"); Set("Body", body);
                Set("Kind", Enum.ToObject(Find("Kolkata.UI.KolkataOsMessengerMessageKind"), attachment ? 1 : 0));
                Set("SentAtUnixMilliseconds", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (50 - index) * 1000);
                return value;
            }
            Button Contact(string name) => (Member(fixture, "m_contactsScroll") as ScrollView).Query<Button>(className: "kolkata-messenger__contact")
                .ToList().Single(b => b.Q<Label>(className: "kolkata-messenger__contact-name")?.text == name);
            try
            {
                messengerFixturePlayer = NetworkClient.localPlayer.GetComponent(Find("KolkataOsPlayer"));
                Require(messengerFixturePlayer != null, "Native messenger player unavailable");
                foreach (string method in new[] { "SendMessengerMessage", "SendMessengerSevereVirusLink" })
                    probe.Patch(AccessTools.Method(messengerFixturePlayer.GetType(), method), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(MessengerFixtureNoSend)));
                messengerFixtureSendAttempts = 0;
                history.Clear(); ownerField.SetValue(null, local);
                for (int i = 0; i < 25; i++) history.Add(Message(alpha, "alpha fixture", "fictional history line " + i, i));
                history.Add(Message(beta, "beta fixture", "fictional second conversation", 25));
                object attachment = Message(beta, "beta fixture", "fictional attachment", 26, true); history.Add(attachment);
                var ctor = type.GetConstructors().Single();
                var active = Delegate.CreateDelegate(ctor.GetParameters()[3].ParameterType,
                    typeof(LocalPairHarness).GetMethod(nameof(MessengerFixtureActive), BindingFlags.Static | BindingFlags.NonPublic));
                fixture = ctor.Invoke(new object[] { Member(os, "m_assets"), null, new Action(() => { }), active, null });
                messengerFixtureApp = app; field.SetValue(os, fixture); changed.AddEventHandler(null, independent);
                AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new[] { Member(Member(app, "Definition"), "id") });
                yield return null; yield return null;
                Require((Member(fixture, "m_contactsScroll") as ScrollView).Query<Button>(className: "kolkata-messenger__contact").ToList().Count == 2,
                    "Native messenger did not project the two fictional offline contacts");
                var click = ClickAppControl(surface, Contact("beta fixture"));
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require((uint)Member(fixture, "m_selectedContactNetId") == beta
                    && (int)AccessTools.Method(inbox, "GetUnreadCount").Invoke(null, new object[] { beta }) == 0
                    && !(Member(fixture, "m_sendButton") as Button).enabledInHierarchy
                    && !(Member(fixture, "m_virusButton") as Button).enabledInHierarchy,
                    "Controller conversation selection failed to read history or retain native offline send guards");
                Logger.LogInfo("MESSENGER CONTACT PASS: controller selects native offline history, marks its messages read and leaves outgoing controls disabled");
                click = ClickAppControl(surface, (Member(fixture, "m_messagesScroll") as ScrollView).Q<Button>(className: "kolkata-messenger__attachment"));
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(!(bool)Member(attachment, "AttachmentOpened") && (Member(fixture, "m_statusLabel") as Label).ClassListContains("kolkata-messenger__status--error"),
                    "Native unavailable attachment activation did not report failure without opening/reporting it");
                Logger.LogInfo("MESSENGER ATTACHMENT PASS: controller reaches the native unavailable-activation response without game-virus activation or infection reporting");
                var typing = EnterScamText(surface, Member(fixture, "m_searchField") as TextField, "alpha");
                try { while (typing.MoveNext()) yield return typing.Current; }
                finally { (typing as IDisposable)?.Dispose(); }
                Require((Member(fixture, "m_contactsScroll") as ScrollView).Query<Button>(className: "kolkata-messenger__contact").ToList().Count == 1
                    && Contact("alpha fixture") != null, "Controller-focused native search did not filter contacts");
                Logger.LogInfo("MESSENGER SEARCH PASS: controller focuses native search and synthetic keyboard characters filter the real contact list");
                const string draft = "fictional unsent offline draft";
                (Member(fixture, "m_messageInput") as TextField).SetValueWithoutNotify(draft);
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                    yield return null; yield return null;
                    Require((Member(fixture, "m_searchField") as TextField).value == "alpha"
                        && (Member(fixture, "m_messageInput") as TextField).value == draft
                        && (uint)Member(fixture, "m_selectedContactNetId") == beta,
                        "Native messenger content rebuild discarded search, existing offline draft or selected conversation");
                    Require((Member(fixture, "m_contactsScroll") as ScrollView).Query<Button>(className: "kolkata-messenger__contact").ToList().Count == 1,
                        "Rebuilt search text did not filter replacement contacts");
                    foreach (string eventName in new[] { "MessageReceived", "Changed", "DeliveryFailed" })
                    {
                        var handlers = (Delegate)AccessTools.Field(inbox, eventName).GetValue(null);
                        Require(handlers.GetInvocationList().Count(d => ReferenceEquals(d.Target, fixture)) == 1, "Messenger rebuild changed event ownership: " + eventName);
                    }
                }
                Require(((Delegate)AccessTools.Field(inbox, "Changed").GetValue(null)).GetInvocationList().Contains(independent) && notifications > 0,
                    "Messenger rebuild removed another owner's inbox observer");
                Logger.LogInfo("MESSENGER REBUILD PASS: native rebuilds retain search/filter, existing offline draft, selected contact and one listener per event while preserving other observers");
                typing = EnterScamText(surface, Member(fixture, "m_searchField") as TextField, "");
                try { while (typing.MoveNext()) yield return typing.Current; }
                finally { (typing as IDisposable)?.Dispose(); }
                click = ClickAppControl(surface, Contact("alpha fixture"));
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                yield return null; yield return new WaitForEndOfFrame();
                var scroll = Member(fixture, "m_messagesScroll") as ScrollView;
                object[] scrollTarget = { scroll, null };
                Require((bool)AccessTools.Method(type, "TryGetCurrentScrollTarget").Invoke(null, scrollTarget)
                    && ReferenceEquals(scrollTarget[1], scroll.contentContainer[scroll.contentContainer.childCount - 1]),
                    "Native history target did not resolve its actual final row");
                float deadline = Time.realtimeSinceStartup + 4;
                while (scroll.verticalScroller.highValue < 20 || scroll.scrollOffset.y < scroll.verticalScroller.highValue * .8f)
                { Require(Time.realtimeSinceStartup < deadline, "Native history did not finish its deferred scroll to the latest message; max="
                    + scroll.verticalScroller.highValue + "; y=" + scroll.scrollOffset.y + "; rows=" + scroll.contentContainer.childCount
                    + "; viewport=" + scroll.contentViewport.worldBound + "; content=" + scroll.contentContainer.worldBound
                    + "; pending=" + (Member(fixture, "m_pendingConversationScroll") != null)
                    + "; scheduled=" + (Member(fixture, "m_pendingConversationScroll") as IVisualElementScheduledItem)?.isActive
                    + "; panel=" + (scroll.panel != null) + "; selected=" + Member(fixture, "m_selectedContactNetId")
                    + "; disposed=" + Member(fixture, "m_disposed") + MessengerScrollState(fixture, scroll)); yield return null; }
                float start = scroll.scrollOffset.y;
                appFixtureSurface = surface; appFixturePoint = scroll.contentViewport.worldBound.center; appFixtureAim = true; appFixtureScroll = 1;
                deadline = Time.realtimeSinceStartup + 3;
                while (scroll.scrollOffset.y > start - 20)
                { Require(Time.realtimeSinceStartup < deadline, "Controller stick did not scroll native message history"); yield return null; }
                appFixtureScroll = 0;
                Require(messengerFixtureSendAttempts == 0 && NetworkServer.connections.Count == 1, "Messenger fixture attempted outgoing communication or another client joined");
                Logger.LogInfo("MESSENGER SCROLL PASS: deferred latest-message positioning completes and controller stick reveals older native history without outgoing messages");
            }
            finally
            {
                appFixtureScroll = 0; appFixtureAim = false; appFixtureSurface = null;
                VRManager.Instance.Input.GetType().GetProperty("Move").SetValue(VRManager.Instance.Input, Vector2.zero);
                VRManager.Instance.Pointer.Cancel(); AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                (fixture as IDisposable)?.Dispose(); changed.RemoveEventHandler(null, independent);
                history.Clear(); foreach (object message in previousMessages) history.Add(message); ownerField.SetValue(null, previousOwner);
                field.SetValue(os, original); AccessTools.Field(type, "m_selectedContactNetId").SetValue(original, previousContact);
                try
                {
                    AccessTools.Method(type, "BuildContent").Invoke(original, new[] { app });
                    if (oldSearch != null) (Member(original, "m_searchField") as TextField).value = oldSearch;
                    if (oldDraft != null) (Member(original, "m_messageInput") as TextField).SetValueWithoutNotify(oldDraft);
                }
                finally { probe.UnpatchSelf(); messengerFixtureApp = null; messengerFixturePlayer = null; }
            }
            Require(history.Cast<object>().SequenceEqual(previousMessages) && Equals(ownerField.GetValue(null), previousOwner), "Messenger fixture failed to restore native history ownership");
            Logger.LogInfo("MESSENGER COMPLETE PASS: original inbox objects/owner and native app restored; live recipients, positive attachment effects, networking and headset acceptance pending");
        }
    }
}
