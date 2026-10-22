using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRVoiceControls
    {
        internal static bool Available;
        private static Component owner;
        private static bool held;

        private static bool ContextAllowed => VRManager.Instance?.Interactive == true && !GameAdapter.InMainMenu
            && !GameAdapter.SettingsOpen && !(GameAdapter.Static("GameInputBindings", "IsRebinding") is bool rebinding && rebinding)
            && !(GameAdapter.Static("SteamBlockInputIfOverlayEnabled", "IsOverlayActive") is bool overlay && overlay);

        internal static void Update(VRInputManager input)
        {
            if (!Available || input == null || !input.Voice || !ContextAllowed) { Cancel(); return; }
            Component player = GameAdapter.LocalPlayer;
            var type = ReflectionTypes.Find("PlayerPushToTalk");
            Component target = player != null && type != null ? player.GetComponent(type) : null;
            if (target == null || target is Behaviour behaviour && !behaviour.isActiveAndEnabled
                || !(GameAdapter.Read(target, "isLocalPlayer") is bool local && local)
                || GameAdapter.Read(target, "IsInputMuted") is bool muted && muted) { Cancel(); return; }
            if (owner != target) Cancel();
            if (input.VoiceDown) { owner = target; held = true; }
        }

        internal static bool HeldFor(object target)
            => Available && held && owner != null && (!(owner is Behaviour behaviour) || behaviour.isActiveAndEnabled)
                && ReferenceEquals(owner, target) && ContextAllowed
                && !(GameAdapter.Read(owner, "IsInputMuted") is bool muted && muted);

        internal static void Cancel() { owner = null; held = false; }
        internal static void Clear() { Cancel(); Available = false; }
    }
}
