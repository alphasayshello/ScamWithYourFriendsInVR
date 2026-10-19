using System;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed class PoseContextProbe : MonoBehaviour
    {
        public bool isSpectating;
        public Component LocalSpectatedPlayer;
        public PoseContextRagdoll RagdollManager = new PoseContextRagdoll();
    }
    public sealed class PoseContextRagdoll { public bool IsRagdollActive, IsHiding; }
    public sealed partial class PointerSmokeTests
    {
        private void TestPoseContext()
        {
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var resolve = adapter.GetMethod("ResolveViewPlayer", flags);
            var unavailable = adapter.GetMethod("AvatarUnavailable", flags);
            var local = Own(new GameObject("local pose context")).AddComponent<PoseContextProbe>();
            var remote = Own(new GameObject("spectated pose context")).AddComponent<PoseContextProbe>();
            Check(ReferenceEquals(resolve.Invoke(null, new object[] { local }), local), "Normal camera selected another player");
            local.isSpectating = true; local.LocalSpectatedPlayer = remote;
            Check(ReferenceEquals(resolve.Invoke(null, new object[] { local }), remote) && (bool)unavailable.Invoke(null, new object[] { local }),
                "Spectating kept local camera ownership or tracked-avatar ownership");
            local.LocalSpectatedPlayer = null;
            Check(ReferenceEquals(resolve.Invoke(null, new object[] { local }), local), "Missing spectate target lost the camera fallback");
            local.isSpectating = false; local.RagdollManager.IsRagdollActive = true;
            Check((bool)unavailable.Invoke(null, new object[] { local }), "Ragdoll retained tracked bone ownership");
            local.RagdollManager.IsRagdollActive = false; local.RagdollManager.IsHiding = true;
            Check((bool)unavailable.Invoke(null, new object[] { local }), "Hiding retained tracked bone ownership");
            local.RagdollManager.IsHiding = false;
            Check(!(bool)unavailable.Invoke(null, new object[] { local }), "Recovered avatar remained blocked");
            Multiplayer.PoseNetwork.ClearLocalPose();
            var network = Multiplayer.PoseNetwork.Instance;
            Check(network != null, "Pose sender missing");
            var frame = (Multiplayer.PoseFrame)network.GetType().GetField("local", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(network);
            Check(frame.Tracked == 0 && Multiplayer.PoseProtocol.Valid(frame) && frame.Head == Vector3.zero,
                "Cleared tracking cannot send a valid zero-tracking packet");
        }
    }
}
