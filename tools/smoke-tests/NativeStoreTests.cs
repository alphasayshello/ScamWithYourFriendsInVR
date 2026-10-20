using System;
using System.Collections;
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
        private static object storeFixtureOwner;
        private static int storeFixturePurchases;
        private static bool BlockFixturePurchase(object __instance, ref bool __result)
        {
            if (!ReferenceEquals(__instance, storeFixtureOwner)) return true;
            storeFixturePurchases++; __result = false; return false;
        }

        private IEnumerator TestNativeStore(object os, VRPanelSurface surface)
        {
            object app = (Member(os, "m_appsById") as IDictionary)?["scamazon"];
            Require(app != null, "Native store runtime missing");
            string originalState = Member(app, "State")?.ToString();
            AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
            var content = Member(app, "WindowContent") as VisualElement;
            Require(content != null, "Native store content missing");
            var originalChildren = content.Children().ToArray();
            var probe = new Harmony("com.swyf.vr.storefixture");
            var manager = VRManager.Instance;
            var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
            void Set(string key, bool value) => manager.Input.GetType().GetProperty(key).SetValue(manager.Input, value);
            void Trigger(bool pressed)
            {
                holdInjectedTrigger = pressed;
                Set("Trigger", pressed); Set("TriggerDown", pressed); Set("TriggerUp", !pressed);
                tick.Invoke(manager.Pointer, null); Set("TriggerDown", false); Set("TriggerUp", false);
            }
            try
            {
                Type type = Find("Kolkata.UI.KolkataOsScamazonApp");
                var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 5);
                object store = ctor.Invoke(new object[] { Member(os, "m_assets"), Member(os, "m_applicationAccess"), Member(os, "m_uiAudio"), Member(os, "m_notifications"),
                    new Action<string>(id => AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id })) });
                storeFixtureOwner = store; storeFixturePurchases = 0;
                // Observe completion at the native UI boundary. Do not spend money,
                // buy saved entitlements or create deliveries during this fixture.
                probe.Patch(AccessTools.Method(type, "BuyProduct"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BlockFixturePurchase)));
                AccessTools.Method(type, "BuildContent").Invoke(store, new[] { app });
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "scamazon" });
                yield return null; yield return null;
                var weeks = Member(store, "m_weekButtons") as IList;
                Require(weeks?.Count == 3, "Native store week navigation missing");
                foreach (int week in new[] { 3, 2, 1 })
                {
                    var click = ClickAppControl(surface, weeks[week - 1] as VisualElement);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    Require(Convert.ToInt32(Member(store, "m_activeWeek")) == week, "Controller store week selection failed");
                    var pages = Member(store, "m_weekPages") as IList;
                    Require(pages?.Count == 3, "Native store week pages missing");
                    for (int i = 0; i < pages.Count; i++)
                        Require(((VisualElement)pages[i]).resolvedStyle.display == (i + 1 == week ? DisplayStyle.Flex : DisplayStyle.None),
                            "Store week visibility does not match controller selection");
                }
                Logger.LogInfo("STORE PASS: controller week navigation selects all three native catalog pages without changing unlock rules");

                var action = new Button { name = "swyf-store-hold-fixture" };
                action.style.position = Position.Absolute; action.style.left = 10; action.style.top = 10;
                action.style.width = 220; action.style.height = 64;
                content.Add(action);
                Type productType = Find("Kolkata.UI.ScamazonProductDefinition");
                var productCtor = productType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Single(c => c.GetParameters().Length == 10);
                Type actionType = Find("Kolkata.UI.ScamazonProductAction");
                object product = productCtor.Invoke(new object[] { "VR fixture", "Isolated UI hold", "0", "", "", "", "Hold", Enum.Parse(actionType, "PhysicalProduct"), "swyf-vr-fixture", 1 });
                AccessTools.Method(type, "ConfigureHoldToBuy").Invoke(store, new[] { action, product, (object)0 });
                yield return null; yield return new WaitForEndOfFrame();

                appFixtureSurface = surface; appFixturePoint = action.worldBound.center; appFixtureAim = true;
                Trigger(true);
                Require(action.ClassListContains("scamazon__buy-button--charging") && action.HasPointerCapture(VRPointer.ToolkitPointerId),
                    "Controller did not begin the native purchase hold");
                yield return new WaitForSecondsRealtime(.15f); Trigger(false);
                Require(storeFixturePurchases == 0 && !action.ClassListContains("scamazon__buy-button--charging") && !action.HasPointerCapture(VRPointer.ToolkitPointerId),
                    "Short controller release did not cancel the native purchase hold");
                Trigger(true); yield return new WaitForSecondsRealtime(.15f);
                Set("HasInputFocus", false); tick.Invoke(manager.Pointer, null); Set("HasInputFocus", true);
                holdInjectedTrigger = false; Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false);
                Require(storeFixturePurchases == 0 && !action.ClassListContains("scamazon__buy-button--charging") && !action.HasPointerCapture(VRPointer.ToolkitPointerId),
                    "Controller focus cancellation left the native purchase hold active");
                Logger.LogInfo("STORE PASS: native controller purchase hold captures correctly and cancels on release or focus loss");
                Trigger(true);
                float deadline = Time.realtimeSinceStartup + 4;
                while (storeFixturePurchases == 0)
                { Require(Time.realtimeSinceStartup < deadline, "Controller hold did not reach native purchase dispatch"); yield return null; }
                Trigger(false);
                Require(storeFixturePurchases == 1 && !action.HasPointerCapture(VRPointer.ToolkitPointerId), "Controller hold dispatched more than once or retained capture");
                Logger.LogInfo("STORE PASS: completed controller hold reaches native purchase UI dispatch once; actual spending/delivery deliberately intercepted");
            }
            finally
            {
                appFixtureAim = false; appFixtureSurface = null; holdInjectedTrigger = false;
                Set("HasInputFocus", true); Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false); manager.Pointer.Cancel();
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                content.Clear(); foreach (VisualElement child in originalChildren) content.Add(child);
                probe.UnpatchSelf(); storeFixtureOwner = null;
                if (originalState != "Closed") AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "scamazon" });
                if (originalState == "OpenMinimized") AccessTools.Method(os.GetType(), "MinimizeApp").Invoke(os, new[] { app });
            }
        }
    }
}
