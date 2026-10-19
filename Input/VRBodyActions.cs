using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    [DefaultExecutionOrder(-30000)]
    public sealed class VRBodyActions : MonoBehaviour
    {
        private Component givingUp;

        private void Update()
        {
            var manager = VRManager.Instance;
            bool allowed = manager != null && manager.Interactive && !GameAdapter.InMainMenu && !GameAdapter.SettingsOpen
                && !GameAdapter.MonitorCaptured && !manager.Keyboard.Visible
                && !(GameAdapter.Read(GameAdapter.Static("TabMenu", "Instance"), "IsOpen") is bool players && players);
            Component target = GameAdapter.Read(GameAdapter.LocalPlayer, "RagdollManager") as Component;
            bool giveUp = allowed && manager.Input.GiveUp && target != null;
            if (givingUp != null && (!giveUp || givingUp != target)) Cancel();
            if (giveUp && manager.Input.GiveUpDown && givingUp == null)
            {
                givingUp = target;
                NativeItemInput.SendNamed(target, "OnGiveUp", true);
            }
            if (allowed && manager.Input.JumpDown && target != null)
                NativeItemInput.SendNamed(target, "OnJump", true);
        }

        public void Cancel()
        {
            if (givingUp != null) NativeItemInput.SendNamed(givingUp, "OnGiveUp", false);
            givingUp = null;
        }
        private void OnDisable() => Cancel();
    }
}
