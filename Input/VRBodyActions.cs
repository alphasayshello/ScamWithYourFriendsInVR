using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    [DefaultExecutionOrder(-30000)]
    public sealed class VRBodyActions : MonoBehaviour
    {
        private Component givingUp;
        private Component storing;

        private void Update()
        {
            var manager = VRManager.Instance;
            if (manager != null) VRVoiceControls.Update(manager.Input); else VRVoiceControls.Cancel();
            bool allowed = manager != null && manager.Interactive && !GameAdapter.InMainMenu && !GameAdapter.SettingsOpen
                && !GameAdapter.MonitorCaptured && !GameAdapter.ModalOverlayOpen && !manager.Keyboard.Visible
                && !(GameAdapter.Read(GameAdapter.Static("TabMenu", "Instance"), "IsOpen") is bool players && players);
            Component target = GameAdapter.Read(GameAdapter.LocalPlayer, "RagdollManager") as Component;
            bool giveUp = allowed && manager.Input.GiveUp && target != null;
            if (givingUp != null && (!giveUp || givingUp != target)) CancelGiveUp();
            if (giveUp && manager.Input.GiveUpDown && givingUp == null)
            {
                givingUp = target;
                NativeItemInput.SendNamed(target, "OnGiveUp", true);
            }
            if (allowed && manager.Input.JumpDown && target != null)
                NativeItemInput.SendNamed(target, "OnJump", true);
            bool requestStorage = allowed && manager.Input.Storage;
            Component storage = requestStorage ? GameAdapter.Static("ButtStorage", "LocalPlayerInstance") as Component : null;
            bool storageHold = requestStorage && storage != null
                && !GameAdapter.AvatarUnavailable(GameAdapter.LocalPlayer) && GameAdapter.Allowed(GameAdapter.Interactor)
                && !manager.Pointer.HasPanelHit;
            if (storing != null && (!storageHold || storing != storage)) CancelStorage();
            if (storageHold && manager.Input.StorageDown && storing == null)
            {
                storing = storage;
                NativeItemInput.SendNamed(storing, "OnStoreInButt", true);
            }
        }

        public void Cancel()
        {
            CancelGiveUp();
            CancelStorage();
            VRVoiceControls.Cancel();
        }
        private void CancelGiveUp()
        {
            if (givingUp != null) NativeItemInput.SendNamed(givingUp, "OnGiveUp", false);
            givingUp = null;
        }
        private void CancelStorage()
        {
            if (storing != null) NativeItemInput.SendNamed(storing, "OnStoreInButt", false);
            storing = null;
        }
        private void OnDisable() => Cancel();
    }
}
