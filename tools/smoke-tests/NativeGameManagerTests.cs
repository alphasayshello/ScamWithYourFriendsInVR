using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object managerFixtureGame;
        private static int managerFixtureClearCalls;
        private static bool managerFixtureForeignClear;
        private static bool ManagerFixtureNoClear(object __instance, ref string __0, ref bool __result)
        {
            if (managerFixtureGame == null) return true;
            if (!ReferenceEquals(__instance, managerFixtureGame)) managerFixtureForeignClear = true;
            managerFixtureClearCalls++; __0 = "fixture clear dispatch intercepted"; __result = false; return false;
        }

        private IEnumerator ClickManagerControl(VRPanelSurface surface, VisualElement control)
        {
            var reveal = ScrollToScamControl(surface, control);
            try { while (reveal.MoveNext()) yield return reveal.Current; }
            finally { (reveal as IDisposable)?.Dispose(); }
            var click = ClickAppControl(surface, control);
            try { while (click.MoveNext()) yield return click.Current; }
            finally { (click as IDisposable)?.Dispose(); }
        }

        private IEnumerator TestNativeGameManager(object os, VRPanelSurface surface)
        {
            bool originalLaptop = (bool)Member(os, "m_useLaptopDesktop");
            var setLaptop = AccessTools.Method(os.GetType(), "SetLaptopDesktop");
            try
            {
                // The host manager belongs to the native laptop desktop, not the office desktop.
                setLaptop.Invoke(os, new object[] { true });
                yield return null; yield return null;
                var checks = TestAvailableNativeGameManager(os, surface);
                try { while (checks.MoveNext()) yield return checks.Current; }
                finally { (checks as IDisposable)?.Dispose(); }
            }
            finally { setLaptop.Invoke(os, new object[] { originalLaptop }); }
        }

        private IEnumerator TestAvailableNativeGameManager(object os, VRPanelSurface surface)
        {
            Require(NetworkServer.connections.Count == 1, "Game manager fixture requires one private host connection");
            var apps = Member(os, "m_appsById") as IDictionary;
            object app = apps.Values.Cast<object>().Single(a => Member(Member(a, "Definition"), "contentType") as string == "gameManager");
            Require((bool)Member(app, "IsAvailable") && Member(app, "State").ToString() == "Closed", "Game manager fixture requires an available closed app");
            var field = AccessTools.Field(os.GetType(), "m_gameManagerApp");
            object original = field.GetValue(os), fixture = null;
            Require(original != null && Member(original, "pendingAction") == null, "Game manager already owns a pending action");
            var type = original.GetType();
            object game = Instance("ScammerGameManager");
            Require(game != null && Member(game, "CanManageDaySaves") is true,
                "Game manager fixture requires the native local host's save-management authority");
            object originalDay = Member(game, "CurrentDay"), originalSave = Member(game, "CurrentDaySaveId");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter", true);
            var manager = VRManager.Instance;
            var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
            var probe = new Harmony("com.swyf.vr.gamemanagerfixture");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            try
            {
                managerFixtureGame = game; managerFixtureClearCalls = 0; managerFixtureForeignClear = false;
                probe.Patch(AccessTools.Method(game.GetType(), "ClearSavedProgress"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ManagerFixtureNoClear)));
                fixture = Activator.CreateInstance(type, true); field.SetValue(os, fixture);
                AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new[] { Member(Member(app, "Definition"), "id") });
                yield return null; yield return null;
                var root = Member(fixture, "root") as VisualElement;
                var confirmation = root.Q<VisualElement>("manager-confirmation");
                var clear = root.Q<Button>("manager-clear-save");
                var click = ClickManagerControl(surface, clear);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(fixture, "pendingAction") is Action && confirmation.resolvedStyle.display == DisplayStyle.Flex && managerFixtureClearCalls == 0,
                    "Native destructive confirmation did not open without dispatching a clear action");
                click = ClickManagerControl(surface, root.Q<Button>("manager-cancel"));
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(fixture, "pendingAction") == null && confirmation.resolvedStyle.display == DisplayStyle.None && managerFixtureClearCalls == 0,
                    "Controller cancel did not clear the native pending action");
                Logger.LogInfo("GAME MANAGER CANCEL PASS: controller opens native clear-progress confirmation and cancels it without dispatching a clear");
                click = ClickManagerControl(surface, clear);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Input("MenuDown", true);
                try { tick.Invoke(manager.Pointer, null); }
                finally { Input("MenuDown", false); }
                yield return null;
                Require(Member(fixture, "pendingAction") == null && confirmation.resolvedStyle.display == DisplayStyle.None
                    && (bool)AccessTools.Property(adapter, "MonitorCaptured").GetValue(null),
                    "Controller back failed to dismiss the confirmation while keeping the computer captured");
                Logger.LogInfo("GAME MANAGER BACK PASS: controller menu/back dismisses the confirmation before leaving the computer");
                yield return null;
                bool priorAim = appFixtureAim; var priorSurface = appFixtureSurface; var priorPoint = appFixturePoint;
                try
                {
                    var refreshButton = root.Q<Button>("manager-refresh");
                    var reveal = ScrollToScamControl(surface, refreshButton);
                    try { while (reveal.MoveNext()) yield return reveal.Current; }
                    finally { (reveal as IDisposable)?.Dispose(); }
                    appFixtureSurface = surface; appFixturePoint = refreshButton.worldBound.center; appFixtureAim = true;
                    holdInjectedTrigger = true; Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                    tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                    Require((bool)Member(manager.Pointer, "toolkitDown"), "Underlying manager control did not acquire the held press");
                    AccessTools.Method(type, "ConfirmClearSave").Invoke(fixture, null);
                    tick.Invoke(manager.Pointer, null);
                    Require(!(bool)Member(manager.Pointer, "toolkitDown"), "New computer confirmation retained the underlying UI press");
                    appFixturePoint = root.Q<Button>("manager-confirm").worldBound.center;
                    yield return null;
                    holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerUp", true);
                    tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                    Require(managerFixtureClearCalls == 0 && Member(fixture, "pendingAction") is Action,
                        "Releasing a background press activated the new confirmation");
                }
                finally
                {
                    holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                    manager.Pointer.Cancel(); appFixtureAim = priorAim; appFixtureSurface = priorSurface; appFixturePoint = priorPoint;
                }
                click = ClickManagerControl(surface, root.Q<Button>("manager-cancel"));
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Logger.LogInfo("GAME MANAGER OWNERSHIP PASS: opening a confirmation cancels underlying pointer ownership; held-trigger release cannot confirm it");
                click = ClickManagerControl(surface, clear);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                click = ClickManagerControl(surface, root.Q<Button>("manager-confirm"));
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(managerFixtureClearCalls == 1 && !managerFixtureForeignClear && Member(fixture, "pendingAction") == null
                    && confirmation.resolvedStyle.display == DisplayStyle.None && Equals(Member(game, "CurrentDay"), originalDay)
                    && Equals(Member(game, "CurrentDaySaveId"), originalSave),
                    "Native controller confirm did not dispatch exactly once and release its modal state");
                Logger.LogInfo("GAME MANAGER CONFIRM PASS: controller confirm dispatches once and closes its modal; actual clear/save writes intercepted");
                click = ClickManagerControl(surface, clear);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                yield return null; yield return null;
                Require(Member(fixture, "pendingAction") == null && (Member(fixture, "confirmation") as VisualElement).resolvedStyle.display == DisplayStyle.None,
                    "Game manager rebuild retained an obsolete confirmation action");
                Logger.LogInfo("GAME MANAGER REBUILD PASS: native content rebuild cancels obsolete confirmation ownership");
            }
            finally
            {
                Input("MenuDown", false); manager.Pointer.Cancel();
                try { AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app }); }
                finally
                {
                    try { (fixture as IDisposable)?.Dispose(); }
                    finally
                    {
                        field.SetValue(os, original);
                        try { AccessTools.Method(type, "BuildContent").Invoke(original, new[] { app }); }
                        finally { probe.UnpatchSelf(); managerFixtureGame = null; }
                    }
                }
            }
            Logger.LogInfo("GAME MANAGER COMPLETE PASS: original app restored; save clearing, loading/restarts and headset acceptance are not established");
        }
    }
}
