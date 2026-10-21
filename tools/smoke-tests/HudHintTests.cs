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
            void Bag(VisualElement root, bool occupied) => typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRHudHints", true)
                .GetMethod("ApplyBag", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root, input, occupied });
            void Flight(VisualElement root, bool flying) => typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRHudHints", true)
                .GetMethod("ApplyFlight", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root, input, flying });
            void Release(VisualElement root, bool waiting) => typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRHudHints", true)
                .GetMethod("ApplyFlightRelease", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root, input, waiting });
            void Ladder(VisualElement root, bool climbing, bool waiting = false) => typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRHudHints", true)
                .GetMethod("ApplyLadder", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root, input, climbing, waiting });
            try
            {
                Layout("Left", Plugin.ControllerLayoutKind.FaceButtons); Layout("Right", Plugin.ControllerLayoutKind.FaceButtons);
                Ladder(first.Root, true); Ladder(first.Root, true);
                Check(first.Root.Query<VisualElement>("swyf-vr-ladder-move").ToList().Count == 1
                    && first.Root.Q<Label>("swyf-vr-ladder-move-key").text == "LEFT STICK"
                    && first.Root.Q<Label>("swyf-vr-ladder-exit-key").text == "RIGHT A"
                    && first.Root.Q("swyf-vr-ladder-exit").pickingMode == PickingMode.Ignore, "Ladder HUD keys duplicate or steal pointer input");
                Flight(first.Root, true); Flight(first.Root, true);
                Check(first.Root.Query<VisualElement>("swyf-vr-flight-climb").ToList().Count == 1
                    && first.Root.Q<Label>("swyf-vr-flight-move-key").text == "LEFT STICK"
                    && first.Root.Q<Label>("swyf-vr-flight-turn-key").text == "RIGHT STICK"
                    && first.Root.Q<Label>("swyf-vr-flight-climb-key").text == "LEFT TRIGGER"
                    && first.Root.Q<Label>("swyf-vr-flight-turn-key").pickingMode == PickingMode.Ignore, "Flight HUD keys duplicate or steal pointer input");
                ApplyHudHints(first.Root, input);
                Check(first.Players.text == "LEFT X" && first.Settings.text == "LEFT Y" && first.Recovery.text == "LEFT GRIP + RIGHT A", "Face-controller HUD captions are incorrect");
                Bag(first.Root, true); Bag(first.Root, true);
                Check(first.Root.Query<VisualElement>("swyf-vr-bag-exit").ToList().Count == 1
                    && first.Root.Q<Label>("swyf-vr-bag-exit-key").text == "RIGHT A"
                    && first.Root.Q<Label>("swyf-vr-bag-move-key").text == "LEFT STICK"
                    && first.Root.Q<Label>("swyf-vr-bag-exit-key").pickingMode == PickingMode.Ignore, "Bag HUD captions duplicate, pick rays or show desktop keys");
                Layout("Right", Plugin.ControllerLayoutKind.ViveWand); ApplyHudHints(first.Root, input);
                Layout("Left", Plugin.ControllerLayoutKind.ViveWand); Bag(first.Root, true);
                Flight(first.Root, true);
                Check(first.Root.Q<Label>("swyf-vr-flight-turn-key").text == "RIGHT PAD" && first.Root.Q<Label>("swyf-vr-flight-climb-key").text == "LEFT TRIGGER", "Vive flight HUD keys incorrect");
                Check(first.Root.Q<Label>("swyf-vr-bag-exit-key").text == "RIGHT PAD UP" && first.Root.Q<Label>("swyf-vr-bag-move-key").text == "LEFT PAD", "Vive bag hint ignored controller layout");
                Layout("Left", Plugin.ControllerLayoutKind.FaceButtons);
                Check(first.Players.text == "LEFT X" && first.Recovery.text == "LEFT GRIP + RIGHT PAD UP", "Recovery caption ignored a mixed right controller");
                Layout("Left", Plugin.ControllerLayoutKind.Wmr); Layout("Right", Plugin.ControllerLayoutKind.Wmr); ApplyHudHints(first.Root, input);
                Flight(first.Root, true);
                Check(first.Root.Q<Label>("swyf-vr-flight-turn-key").text == "RIGHT STICK", "WMR flight steering hint lost its independent stick");
                Ladder(first.Root, false, true);
                Check(first.Root.Q("swyf-vr-ladder-move") == null && first.Root.Q<Label>("swyf-vr-ladder-release-key").text == "RIGHT PAD UP",
                    "Alternate ladder release HUD key incorrect");
                Ladder(first.Root, true);
                Flight(first.Root, false); Check(first.Root.Q("swyf-vr-flight-turn") == null, "Flight end retained its HUD keys"); Flight(first.Root, true);
                Release(first.Root, true); Release(first.Root, true);
                Check(first.Root.Query<VisualElement>("swyf-vr-flight-release").ToList().Count == 1
                    && first.Root.Q<Label>("swyf-vr-flight-release-key").text == "TRIGGER / PAD UP", "Flight-release HUD key incorrect or duplicated");
                Bag(first.Root, true); Check(first.Root.Q<Label>("swyf-vr-bag-move-key").text == "LEFT STICK", "WMR bag hint replaced its independent stick with the pad");
                Bag(first.Root, false); Check(first.Root.Q("swyf-vr-bag-exit") == null, "Bag exit left its VR HUD panels behind");
                Bag(first.Root, true);
                Check(first.Players.text == "LEFT PAD" && first.Settings.text == "MENU" && first.Recovery.text == "LEFT GRIP + RIGHT PAD UP", "WMR HUD captions are incorrect");
                first.Recovery.text = "H"; ApplyHudHints(first.Root, input); RestoreHudHints();
                Check(first.Players.text == "TAB" && first.Settings.text == "ESC" && first.Recovery.text == "H", "HUD restore lost original/rebound keyboard captions");
                Check(first.Root.Q("swyf-vr-bag-exit") == null && first.Root.Q("swyf-vr-bag-move") == null, "HUD restore retained VR-only bag panels");
                Check(first.Root.Q("swyf-vr-flight-move") == null && first.Root.Q("swyf-vr-flight-climb") == null, "HUD restore retained VR-only flight panels");
                Check(first.Root.Q("swyf-vr-ladder-move") == null && first.Root.Q("swyf-vr-ladder-exit") == null
                    && first.Root.Q("swyf-vr-ladder-release") == null, "HUD restore retained ladder controls");
                Check(first.Root.Q("swyf-vr-flight-release") == null, "HUD restore retained the flight-release prompt");
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
