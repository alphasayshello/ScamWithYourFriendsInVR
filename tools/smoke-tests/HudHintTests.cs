using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private static (VisualElement Root, Label Players, Label Settings, Label Recovery) HudFixture()
        {
            var root = new VisualElement();
            Label Panel(string name, string text)
            {
                var panel = new VisualElement();
                var key = new Label(text); key.AddToClassList("player-menu-hint__key"); panel.Add(key);
                panel.Add(new Label { name = name }); root.Add(panel); return key;
            }
            Label players = Panel("player-menu-hint-label", "TAB"), settings = Panel("settings-menu-hint-label", "ESC");
            var recovery = new VisualElement { name = "recovery-hint" };
            var recoveryKey = new Label("G"); recoveryKey.AddToClassList("player-menu-hint__key"); recovery.Add(recoveryKey); root.Add(recovery);
            return (root, players, settings, recoveryKey);
        }
        private static void ApplyHudHints(VisualElement root, Input.VRInputManager input) => typeof(VRManager).Assembly
            .GetType("ScamWithYourFriendsInVR.UI.VRHudHints", true).GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root, input });
        private static void RestoreHudHints() => typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRHudHints", true)
            .GetMethod("Restore", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        private void TestHudHints()
        {
            var input = Own(new GameObject("HUD captions fixture")).AddComponent<Input.VRInputManager>(); input.enabled = false;
            var first = HudFixture(); var second = HudFixture();
            void Layout(string side, Plugin.ControllerLayoutKind layout) => input.GetType().GetProperty(side + "Layout").SetValue(input, layout);
            try
            {
                Layout("Left", Plugin.ControllerLayoutKind.FaceButtons); Layout("Right", Plugin.ControllerLayoutKind.FaceButtons);
                ApplyHudHints(first.Root, input);
                Check(first.Players.text == "LEFT X" && first.Settings.text == "LEFT Y" && first.Recovery.text == "LEFT GRIP + RIGHT A", "Face-controller HUD captions are incorrect");
                Layout("Right", Plugin.ControllerLayoutKind.ViveWand); ApplyHudHints(first.Root, input);
                Check(first.Players.text == "LEFT X" && first.Recovery.text == "LEFT GRIP + RIGHT PAD UP", "Recovery caption ignored a mixed right controller");
                Layout("Left", Plugin.ControllerLayoutKind.Wmr); Layout("Right", Plugin.ControllerLayoutKind.Wmr); ApplyHudHints(first.Root, input);
                Check(first.Players.text == "LEFT PAD" && first.Settings.text == "MENU" && first.Recovery.text == "LEFT GRIP + RIGHT PAD UP", "WMR HUD captions are incorrect");
                first.Recovery.text = "H"; ApplyHudHints(first.Root, input); RestoreHudHints();
                Check(first.Players.text == "TAB" && first.Settings.text == "ESC" && first.Recovery.text == "H", "HUD restore lost original/rebound keyboard captions");
                ApplyHudHints(first.Root, input); ApplyHudHints(second.Root, input);
                Check(first.Players.text == "TAB" && first.Settings.text == "ESC" && first.Recovery.text == "H", "Replacing a HUD retained its old VR captions");
                second.Settings.text = "external caption"; RestoreHudHints();
                Check(second.Players.text == "TAB" && second.Settings.text == "external caption" && second.Recovery.text == "G", "HUD restore overwrote another owner's caption");
                ApplyHudHints(new VisualElement(), input); RestoreHudHints();
            }
            finally { RestoreHudHints(); }
        }
    }
}
