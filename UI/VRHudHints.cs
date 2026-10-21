using System.Collections.Generic;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.Input;

namespace ScamWithYourFriendsInVR.UI
{
    internal static class VRHudHints
    {
        private sealed class Caption
        {
            internal string Original, Applied;
        }
        private static readonly Dictionary<Label, Caption> captions = new Dictionary<Label, Caption>();
        private static VisualElement owner;
        private static VisualElement bagMoveHint, bagExitHint;

        internal static void ApplyBag(VisualElement hints, VRInputManager input, bool occupied)
        {
            if (hints == null || input == null) return;
            if (!ReferenceEquals(owner, hints)) { Restore(); owner = hints; }
            if (!occupied) { RemoveBagHints(); return; }
            BagHint(hints, ref bagMoveHint, "swyf-vr-bag-move", input.MoveHint, "Move bag");
            BagHint(hints, ref bagExitHint, "swyf-vr-bag-exit", input.JumpHint, "Exit bag");
        }

        private static void BagHint(VisualElement hints, ref VisualElement panel, string name, string key, string description)
        {
            if (panel == null || panel.parent != hints)
            {
                panel?.RemoveFromHierarchy();
                panel = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                panel.AddToClassList("player-menu-hint__panel");
                var keyLabel = new Label { name = name + "-key", pickingMode = PickingMode.Ignore };
                keyLabel.AddToClassList("player-menu-hint__key"); panel.Add(keyLabel);
                var label = new Label(description) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("player-menu-hint__label"); panel.Add(label); hints.Add(panel);
            }
            Label caption = panel.Q<Label>(name + "-key");
            if (caption.text != key) caption.text = key;
        }

        private static void RemoveBagHints()
        {
            bagMoveHint?.RemoveFromHierarchy(); bagExitHint?.RemoveFromHierarchy(); bagMoveHint = bagExitHint = null;
        }

        internal static void Apply(VisualElement hints, VRInputManager input)
        {
            if (hints == null || input == null) return;
            if (!ReferenceEquals(owner, hints)) { Restore(); owner = hints; }
            Set(hints.Q<Label>("player-menu-hint-label")?.parent?.Q<Label>(className: "player-menu-hint__key"), input.PlayersHint);
            Set(hints.Q<Label>("settings-menu-hint-label")?.parent?.Q<Label>(className: "player-menu-hint__key"), input.SettingsHint);
            Set(hints.Q("recovery-hint")?.Q<Label>(className: "player-menu-hint__key"), input.GiveUpHint);
        }

        private static void Set(Label label, string value)
        {
            if (label == null) return;
            if (!captions.TryGetValue(label, out var saved))
                captions.Add(label, saved = new Caption { Original = label.text });
            else if (label.text != saved.Applied) saved.Original = label.text;
            saved.Applied = value;
            if (label.text != value) label.text = value;
        }

        internal static void Restore()
        {
            foreach (var entry in captions)
                if (entry.Key.text == entry.Value.Applied) entry.Key.text = entry.Value.Original;
            captions.Clear(); owner = null;
            RemoveBagHints();
        }
    }
}
