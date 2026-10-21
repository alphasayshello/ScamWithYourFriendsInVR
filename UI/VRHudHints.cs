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
        }
    }
}
