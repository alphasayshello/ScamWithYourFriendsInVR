using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestGameplayControls()
        {
            var sprint = TestNativeSprint();
            try { while (sprint.MoveNext()) yield return sprint.Current; }
            finally { (sprint as IDisposable)?.Dispose(); }
            var jumps = TestNativeJump();
            try { while (jumps.MoveNext()) yield return jumps.Current; }
            finally { (jumps as IDisposable)?.Dispose(); }
            var peeChecks = TestNativePee();
            try { while (peeChecks.MoveNext()) yield return peeChecks.Current; }
            finally { (peeChecks as IDisposable)?.Dispose(); }
            var manager = VRManager.Instance;
            var player = Mirror.NetworkClient.localPlayer;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            Type playerPatches = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches");
            object controller = adapter.GetProperty("Controller", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var beforeController = AccessTools.Method(playerPatches, "BeforeController");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var inputType = Find("GameInputBindings");
            var held = AccessTools.Method(inputType, "Held", new[] { typeof(string), typeof(string) });
            var pressed = AccessTools.Method(inputType, "Pressed", new[] { typeof(string), typeof(string) });
            void Set(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            bool Bound(MethodInfo method, string name) => (bool)method.Invoke(null, new object[] { "Shortcuts", name });
            var handleP = AccessTools.Method(player.GetComponent(Find("Metater.MetaPlayer")).GetType(), "HandlePInput");
            object nativePlayer = player.GetComponent(Find("Metater.MetaPlayer"));
            bool paused = false;
            try
            {
                yield return null;
                yield return new WaitForEndOfFrame();
                manager.Pointer.Cancel();
                Require(playerPatches.GetProperty("GameplayInput", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) is bool allowed && allowed,
                    "Native gameplay input is blocked after leaving the computer");
                Set("Pee", true); Set("PeeDown", true); Set("FartDown", true);
                Require(Bound(held, "HoldP") && Bound(pressed, "ManualFart"), "Controller pee/fart did not reach native bindings");
                handleP.Invoke(nativePlayer, null);
                Require(Member(nativePlayer, "wasPPressed") is bool peeing && peeing, "Native pee transition did not receive controller input");
                Set("Pee", false); Set("PeeDown", false); Set("FartDown", false); handleP.Invoke(nativePlayer, null);
                Require(Member(nativePlayer, "wasPPressed") is bool released && !released, "Native pee input remained held");
                Logger.LogInfo("GAMEPLAY PASS: controller pee/fart bindings and native pee transition/release");

                Set("Jump", true); Set("JumpDown", true); beforeController.Invoke(null, new[] { controller }); Set("JumpDown", false);
                Require(Member(controller, "isJumping") is bool jumping && jumping, "Controller jump did not reach native controller");
                toggle.Invoke(null, null); paused = true;
                beforeController.Invoke(null, new[] { controller });
                Require(Member(controller, "isJumping") is bool stopped && !stopped, "Opening settings left jump held");
                Set("Pee", true); Set("PeeDown", true); Set("FartDown", true);
                Require(!Bound(held, "HoldP") && !Bound(pressed, "ManualFart"), "Settings allowed controller pee/fart gameplay input");
                toggle.Invoke(null, null); paused = false;
                Set("Jump", false); Set("JumpDown", false); Set("Pee", false); Set("PeeDown", false); Set("FartDown", false);
                Logger.LogInfo("GAMEPLAY PASS: native jump input and settings gameplay suppression");

                var hudType = Find("ScammerGameHudController");
                var hud = FindAnyObjectByType(hudType) as Component;
                Require(hud != null, "Native gameplay HUD missing");
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                var hint = Member(hud, "playerMenuHint") as VisualElement;
                Require(hint?.Q<Label>("player-menu-hint-label")?.parent.Q<Label>(className: "player-menu-hint__key")?.text == "LEFT X"
                    && hint.Q<Label>("settings-menu-hint-label")?.parent.Q<Label>(className: "player-menu-hint__key")?.text == "LEFT Y", "HUD kept desktop shortcut labels");
                var layoutProperty = manager.Input.GetType().GetProperty("LeftLayout");
                var rightLayoutProperty = manager.Input.GetType().GetProperty("RightLayout");
                object savedLayout = layoutProperty.GetValue(manager.Input);
                object savedRightLayout = rightLayoutProperty.GetValue(manager.Input);
                var recoveryKey = hint.Q("recovery-hint")?.Q<Label>(className: "player-menu-hint__key");
                Require(recoveryKey?.text == "LEFT GRIP + RIGHT A", "Native recovery hint kept its desktop key");
                try
                {
                    foreach (var layout in new[] { Plugin.ControllerLayoutKind.ViveWand, Plugin.ControllerLayoutKind.Wmr })
                    {
                        layoutProperty.SetValue(manager.Input, layout);
                        rightLayoutProperty.SetValue(manager.Input, layout);
                        AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                        Require(hint.Q<Label>("player-menu-hint-label").parent.Q<Label>(className: "player-menu-hint__key").text == "LEFT PAD"
                            && hint.Q<Label>("settings-menu-hint-label").parent.Q<Label>(className: "player-menu-hint__key").text == "MENU", "Native HUD kept face buttons for " + layout);
                        Require(recoveryKey.text == "LEFT GRIP + RIGHT PAD UP", "Native recovery hint kept a missing face button for " + layout);
                    }
                    AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRHudHints"), "Restore").Invoke(null, null);
                    Require(hint.Q<Label>("player-menu-hint-label").parent.Q<Label>(className: "player-menu-hint__key").text == "TAB"
                        && hint.Q<Label>("settings-menu-hint-label").parent.Q<Label>(className: "player-menu-hint__key").text == "ESC"
                        && recoveryKey.text != "LEFT GRIP + RIGHT PAD UP", "Native HUD did not restore desktop captions");
                }
                finally
                {
                    layoutProperty.SetValue(manager.Input, savedLayout);
                    rightLayoutProperty.SetValue(manager.Input, savedRightLayout);
                    AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                }
                Logger.LogInfo("GAMEPLAY PASS: native HUD shows layout-specific players/settings/recovery shortcuts, restores desktop captions and resumes VR hints");
                var menus = TestMenuRouting();
                try { while (menus.MoveNext()) yield return menus.Current; }
                finally { (menus as IDisposable)?.Dispose(); }
            }
            finally
            {
                Set("Jump", false); Set("JumpDown", false); Set("Pee", false); Set("PeeDown", false); Set("FartDown", false);
                if (paused) toggle.Invoke(null, null);
                handleP.Invoke(nativePlayer, null);
                beforeController.Invoke(null, new[] { controller });
                manager.Pointer.Cancel();
            }
        }
    }
}
