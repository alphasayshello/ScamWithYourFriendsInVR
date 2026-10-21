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
            var pee = player.GetComponent(Find("PeeNetworking"));
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
                Set("Pee", true); Set("FartDown", true);
                Require(Bound(held, "HoldP") && Bound(pressed, "ManualFart"), "Controller pee/fart did not reach native bindings");
                handleP.Invoke(nativePlayer, null);
                Require(Member(nativePlayer, "wasPPressed") is bool peeing && peeing, "Native pee transition did not receive controller input");
                if (pee != null) Require(Member(pee, "NetworkisPeeing") is bool streamed && streamed, "Native host did not synchronize pee state");
                Set("Pee", false); Set("FartDown", false); handleP.Invoke(nativePlayer, null);
                Require(Member(nativePlayer, "wasPPressed") is bool released && !released, "Native pee input remained held");
                Logger.LogInfo("GAMEPLAY PASS: controller pee/fart bindings, native pee transition and host synchronization/release");

                Set("Jump", true); beforeController.Invoke(null, new[] { controller });
                Require(Member(controller, "isJumping") is bool jumping && jumping, "Controller jump did not reach native controller");
                toggle.Invoke(null, null); paused = true;
                beforeController.Invoke(null, new[] { controller });
                Require(Member(controller, "isJumping") is bool stopped && !stopped, "Opening settings left jump held");
                Set("Pee", true); Set("FartDown", true);
                Require(!Bound(held, "HoldP") && !Bound(pressed, "ManualFart"), "Settings allowed controller pee/fart gameplay input");
                toggle.Invoke(null, null); paused = false;
                Set("Jump", false); Set("Pee", false); Set("FartDown", false);
                Logger.LogInfo("GAMEPLAY PASS: native jump input and settings gameplay suppression");

                var hudType = Find("ScammerGameHudController");
                var hud = FindAnyObjectByType(hudType) as Component;
                Require(hud != null, "Native gameplay HUD missing");
                AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                var hint = Member(hud, "playerMenuHint") as VisualElement;
                Require(hint?.Q<Label>("player-menu-hint-label")?.parent.Q<Label>(className: "player-menu-hint__key")?.text == "LEFT X"
                    && hint.Q<Label>("settings-menu-hint-label")?.parent.Q<Label>(className: "player-menu-hint__key")?.text == "LEFT Y", "HUD kept desktop shortcut labels");
                var layoutProperty = manager.Input.GetType().GetProperty("LeftLayout");
                object savedLayout = layoutProperty.GetValue(manager.Input);
                try
                {
                    foreach (var layout in new[] { Plugin.ControllerLayoutKind.ViveWand, Plugin.ControllerLayoutKind.Wmr })
                    {
                        layoutProperty.SetValue(manager.Input, layout);
                        AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                        Require(hint.Q<Label>("player-menu-hint-label").parent.Q<Label>(className: "player-menu-hint__key").text == "LEFT PAD"
                            && hint.Q<Label>("settings-menu-hint-label").parent.Q<Label>(className: "player-menu-hint__key").text == "MENU", "Native HUD kept face buttons for " + layout);
                    }
                }
                finally
                {
                    layoutProperty.SetValue(manager.Input, savedLayout);
                    AccessTools.Method(hudType, "RefreshPlayerMenuHint").Invoke(hud, null);
                }
                Logger.LogInfo("GAMEPLAY PASS: native HUD shows Quest and Vive/WMR players/settings shortcuts and restores the current layout");
            }
            finally
            {
                Set("Jump", false); Set("Pee", false); Set("FartDown", false);
                if (paused) toggle.Invoke(null, null);
                handleP.Invoke(nativePlayer, null);
                beforeController.Invoke(null, new[] { controller });
                manager.Pointer.Cancel();
            }
        }
    }
}
