using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Camera
{
    [DefaultExecutionOrder(30500)]
    public sealed class VRAudioController : MonoBehaviour
    {
        private Transform mouth;
        private Vector3 position;
        private Quaternion rotation;
        private Component player;

        private void LateUpdate()
        {
            VRManager manager = VRManager.Instance;
            if (manager == null || !manager.Ready || manager.Rig.Head == null) return;
            Component local = GameAdapter.LocalPlayer;
            if (GameAdapter.Read(local, "isSpectating") is bool spectating && spectating) { Restore(); return; }
            if (local != player || mouth == null)
            {
                Restore();
                player = local;
                var modeType = ReflectionTypes.Find("PlayerModeManager");
                Component mode = player != null && modeType != null ? player.GetComponent(modeType) : null;
                mouth = GameAdapter.Read(mode, "voiceChatMouthAudioSource") as Transform;
                if (mouth != null) { position = mouth.localPosition; rotation = mouth.localRotation; }
            }
            if (mouth != null && manager.Input.HeadTracked)
                mouth.SetPositionAndRotation(manager.Rig.Head.transform.TransformPoint(new Vector3(0, -0.1f, 0.05f)), manager.Rig.Head.transform.rotation);
        }

        private void Restore()
        {
            if (mouth != null) { mouth.localPosition = position; mouth.localRotation = rotation; }
            mouth = null; player = null;
        }
        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
