using System.Collections.Generic;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    [DefaultExecutionOrder(30600)]
    public sealed class VRAvatarArms : MonoBehaviour
    {
        private Component player;
        private readonly List<Arm> arms = new List<Arm>();
        private readonly Dictionary<Behaviour, bool> disabled = new Dictionary<Behaviour, bool>();
        private float nextScan;
        private readonly Dictionary<Transform, Quaternion> headAlignment = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Quaternion> headPose = new Dictionary<Transform, Quaternion>();
        private sealed class Arm
        {
            internal Transform Upper, Lower, Hand;
            internal bool Left, Applied;
            internal Quaternion UpperPose, LowerPose, HandPose, Alignment;
            internal Vector3 GripOffset, LowerPalm;
            internal void Restore()
            {
                if (!Applied) return;
                if (Upper != null) Upper.localRotation = UpperPose;
                if (Lower != null) Lower.localRotation = LowerPose;
                if (Hand != null) Hand.localRotation = HandPose;
                Applied = false;
            }
        }

        private void RestoreHead()
        {
            foreach (var pose in headPose) if (pose.Key != null) pose.Key.localRotation = pose.Value;
            headPose.Clear();
        }
        private void Update() { foreach (Arm arm in arms) arm.Restore(); RestoreHead(); }

        private void LateUpdate()
        {
            foreach (Arm arm in arms) arm.Restore();
            RestoreHead();
            var manager = VRManager.Instance;
            if (manager == null || !manager.Ready) return;
            if (GameAdapter.InMainMenu) { if (player != null) Restore(); return; }
            if (GameAdapter.AvatarUnavailable(GameAdapter.LocalPlayer))
            {
                Multiplayer.PoseNetwork.ClearLocalPose(); if (player != null) Restore(); return;
            }
            if (player != GameAdapter.LocalPlayer) { Restore(); player = GameAdapter.LocalPlayer; nextScan = 0; }
            if (player == null) return;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1;
                Scan();
            }
            if (!manager.Interactive || manager.Rig.Head == null) return;
            foreach (var head in headAlignment)
            {
                if (head.Key == null || !head.Key.gameObject.activeInHierarchy) continue;
                headPose[head.Key] = head.Key.localRotation;
                head.Key.rotation = manager.Rig.Head.transform.rotation * head.Value;
            }
            byte tracked = (byte)(1 | (manager.Rig.Left.IsTracked ? 2 : 0) | (manager.Rig.Right.IsTracked ? 4 : 0));
            Multiplayer.PoseNetwork.SetLocalPose(player.transform, manager.Rig.Head.transform, manager.Rig.Left.transform, manager.Rig.Right.transform, tracked);
            foreach (Arm arm in arms)
            {
                if (arm.Upper == null || arm.Lower == null || arm.Hand == null || !arm.Hand.gameObject.activeInHierarchy) continue;
                var controller = arm.Left ? manager.Rig.Left : manager.Rig.Right;
                if (!controller.IsTracked) continue;
                arm.UpperPose = arm.Upper.localRotation; arm.LowerPose = arm.Lower.localRotation; arm.HandPose = arm.Hand.localRotation;
                arm.Applied = true;
                Vector3 shoulder = arm.Upper.position;
                float upperLength = Vector3.Distance(shoulder, arm.Lower.position);
                float lowerLength = Vector3.Distance(arm.Lower.position, arm.Hand.position);
                if (upperLength < 0.001f || lowerLength < 0.001f) continue;
                Vector3 gripWrist = controller.transform.position - controller.transform.rotation * arm.GripOffset;
                Vector3 delta = gripWrist - shoulder;
                float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);
                Vector3 direction = delta.sqrMagnitude > 0.000001f ? delta.normalized : -manager.Rig.Head.transform.up;
                Vector3 pole = manager.Rig.Head.transform.right * (arm.Left ? -1 : 1) - manager.Rig.Head.transform.up * 0.65f;
                Vector3 bend = Vector3.ProjectOnPlane(pole, direction).normalized;
                if (bend.sqrMagnitude < 0.01f) bend = Vector3.ProjectOnPlane(manager.Rig.Head.transform.forward, direction).normalized;
                float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2 * distance);
                Vector3 elbow = shoulder + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
                arm.Upper.rotation = Quaternion.FromToRotation(arm.Lower.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
                Vector3 wrist = shoulder + direction * distance;
                arm.Lower.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Lower.position, wrist - arm.Lower.position) * arm.Lower.rotation;
                Multiplayer.AvatarArmCalibration.ForearmTwist(arm.Lower, arm.Hand, arm.LowerPalm, controller.transform.up);
                arm.Hand.rotation = controller.transform.rotation * arm.Alignment;
            }
        }

        private void Scan()
        {
            foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string name = behaviour.GetType().Name;
                if (name != "MonitorArmIkDriver" && name != "ProceduralArmAnimator" && name != "SmugglerStepArmAnimator") continue;
                if (disabled.ContainsKey(behaviour)) continue;
                disabled[behaviour] = behaviour.enabled;
                behaviour.enabled = false;
            }
            foreach (Transform bone in player.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "mixamorig:Head" && !headAlignment.ContainsKey(bone))
                    headAlignment[bone] = Quaternion.Inverse(Multiplayer.AvatarArmCalibration.FacingFrame(player.transform).rotation) * bone.rotation;
                bool left = bone.name == "mixamorig:LeftHand";
                if (!left && bone.name != "mixamorig:RightHand") continue;
                if (arms.Exists(a => a.Hand == bone)) continue;
                if (!Multiplayer.AvatarArmCalibration.TryArmBones(player.transform, bone, out Transform upper, out Transform lower)) continue;
                var arm = new Arm { Hand = bone, Lower = lower, Upper = upper, Left = left };
                bool palm = Multiplayer.AvatarArmCalibration.GripCalibration(player.transform, bone, lower,
                    out arm.Alignment, out arm.GripOffset, out arm.LowerPalm);
                arms.Add(arm);
                Plugin.Log.LogInfo("Bound tracked avatar arm: " + bone.name + " under " + bone.root.name + "; palm calibration=" + palm + "; wrist offset=" + arm.GripOffset);
            }
        }

        private void Restore()
        {
            RestoreHead(); headAlignment.Clear();
            foreach (Arm arm in arms) arm.Restore();
            arms.Clear();
            foreach (var pair in disabled) if (pair.Key != null) pair.Key.enabled = pair.Value;
            disabled.Clear();
            player = null;
        }
        private void OnEnable() => Application.onBeforeRender += LateUpdate;
        private void OnDisable() { Application.onBeforeRender -= LateUpdate; Restore(); }
        private void OnDestroy() => Restore();
    }
}
