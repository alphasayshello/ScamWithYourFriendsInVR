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
        private static VisualElement flightMoveHint, flightTurnHint, flightClimbHint;
        private static VisualElement flightReleaseHint;
        private static VisualElement ladderMoveHint, ladderExitHint, ladderReleaseHint;

        internal static void ApplyLadder(VisualElement hints, VRInputManager input, bool climbing, bool waiting)
        {
            if (hints == null || input == null) return;
            if (!ReferenceEquals(owner, hints)) { Restore(); owner = hints; }
            if (climbing)
            {
                ActionHint(hints, ref ladderMoveHint, "swyf-vr-ladder-move", input.MoveHint, "Up / down ladder");
                ActionHint(hints, ref ladderExitHint, "swyf-vr-ladder-exit", input.JumpHint, "Dismount");
            }
            else
            {
                ladderMoveHint?.RemoveFromHierarchy(); ladderExitHint?.RemoveFromHierarchy(); ladderMoveHint = ladderExitHint = null;
            }
            if (waiting) ActionHint(hints, ref ladderReleaseHint, "swyf-vr-ladder-release", input.JumpHint, "Release + centre sticks");
            else { ladderReleaseHint?.RemoveFromHierarchy(); ladderReleaseHint = null; }
        }

        internal static void ApplyFlightRelease(VisualElement hints, VRInputManager input, bool waiting)
        {
            if (hints == null || input == null) return;
            if (!ReferenceEquals(owner, hints)) { Restore(); owner = hints; }
            if (waiting) ActionHint(hints, ref flightReleaseHint, "swyf-vr-flight-release", input.FlightReleaseHint, "Release + centre sticks");
            else { flightReleaseHint?.RemoveFromHierarchy(); flightReleaseHint = null; }
        }

        internal static void ApplyFlight(VisualElement hints, VRInputManager input, bool flying)
        {
            if (hints == null || input == null) return;
            if (!ReferenceEquals(owner, hints)) { Restore(); owner = hints; }
            if (!flying) { RemoveFlightHints(); return; }
            ActionHint(hints, ref flightMoveHint, "swyf-vr-flight-move", input.MoveHint, "Move FPV");
            ActionHint(hints, ref flightTurnHint, "swyf-vr-flight-turn", input.TurnHint, "Steer FPV");
            ActionHint(hints, ref flightClimbHint, "swyf-vr-flight-climb", input.FlightClimbHint, "Hold to climb");
        }

        internal static void ApplyBag(VisualElement hints, VRInputManager input, bool occupied)
        {
            if (hints == null || input == null) return;
            if (!ReferenceEquals(owner, hints)) { Restore(); owner = hints; }
            if (!occupied) { RemoveBagHints(); return; }
            ActionHint(hints, ref bagMoveHint, "swyf-vr-bag-move", input.MoveHint, "Move bag");
            ActionHint(hints, ref bagExitHint, "swyf-vr-bag-exit", input.JumpHint, "Exit bag");
        }

        private static void ActionHint(VisualElement hints, ref VisualElement panel, string name, string key, string description)
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

        private static void RemoveFlightHints()
        {
            flightMoveHint?.RemoveFromHierarchy(); flightTurnHint?.RemoveFromHierarchy(); flightClimbHint?.RemoveFromHierarchy();
            flightMoveHint = flightTurnHint = flightClimbHint = null;
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
            RemoveFlightHints();
            flightReleaseHint?.RemoveFromHierarchy(); flightReleaseHint = null;
            ladderMoveHint?.RemoveFromHierarchy(); ladderExitHint?.RemoveFromHierarchy(); ladderReleaseHint?.RemoveFromHierarchy();
            ladderMoveHint = ladderExitHint = ladderReleaseHint = null;
        }
    }
}
