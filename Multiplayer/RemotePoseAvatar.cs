using System.Collections.Generic;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Multiplayer
{
    [DefaultExecutionOrder(30700)]
    public sealed class RemotePoseAvatar : MonoBehaviour
    {
        private sealed class Chain
        {
            internal Transform Upper, Lower, Hand;
            internal bool Left, Applied;
            internal Quaternion UpperPose, LowerPose, HandPose;
            internal Quaternion Alignment;
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
        private readonly List<Chain> chains = new List<Chain>();
        private readonly Dictionary<Transform, Quaternion> heads = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Quaternion> savedHeads = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Behaviour, bool> nativeAnimators = new Dictionary<Behaviour, bool>();
        private PoseFrame target, current;
        private bool received;
        private float lastReceived, nextScan;
        private Transform voiceMouth;
        private Vector3 voicePosition;
        private Quaternion voiceRotation;
        private bool voiceApplied;

        public void Receive(PoseFrame frame)
        {
            if (received && !PoseProtocol.Newer(frame.Sequence, target.Sequence)) return;
            target = frame;
            if (!received || Time.unscaledTime - lastReceived > 0.75f) current = frame;
            received = true; lastReceived = Time.unscaledTime;
        }
        private bool HasHandTracking => received && Time.unscaledTime - lastReceived <= 0.75f && (target.Tracked & 6) != 0;
        private void Update()
        {
            RestoreBones();
            if (!HasHandTracking) RestoreAnimators();
        }
        private void LateUpdate()
        {
            if (!received || Time.unscaledTime - lastReceived > 0.75f || target.Tracked == 0) return;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1;
                foreach (MonoBehaviour component in GetComponents<MonoBehaviour>())
                    if (component != null && component.GetType().Name == "PlayerModeManager")
                    {
                        Transform mouth = component.GetType().GetField("voiceChatMouthAudioSource",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(component) as Transform;
                        if (mouth != voiceMouth) { RestoreVoice(); voiceMouth = mouth; }
                        break;
                    }
                if (HasHandTracking)
                    foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (component == null) continue;
                        string name = component.GetType().Name;
                        if (name != "MonitorArmIkDriver" && name != "ProceduralArmAnimator" && name != "SmugglerStepArmAnimator") continue;
                        if (!nativeAnimators.ContainsKey(component)) nativeAnimators.Add(component, component.enabled);
                        component.enabled = false;
                    }
                foreach (Transform bone in GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == "mixamorig:Head" && !heads.ContainsKey(bone))
                        heads[bone] = Quaternion.Inverse(AvatarArmCalibration.FacingFrame(transform).rotation) * bone.rotation;
                    bool left = bone.name == "mixamorig:LeftHand";
                    if (!left && bone.name != "mixamorig:RightHand") continue;
                    if (chains.Exists(c => c.Hand == bone)) continue;
                    if (!AvatarArmCalibration.TryArmBones(transform, bone, out Transform upper, out Transform lower)) continue;
                    var chain = new Chain { Upper = upper, Lower = lower, Hand = bone, Left = left };
                    AvatarArmCalibration.GripCalibration(transform, bone, lower, out chain.Alignment, out chain.GripOffset, out chain.LowerPalm);
                    chains.Add(chain);
                }
            }
            float alpha = 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
            current.Head = Vector3.Lerp(current.Head, target.Head, alpha);
            current.Left = Vector3.Lerp(current.Left, target.Left, alpha);
            current.Right = Vector3.Lerp(current.Right, target.Right, alpha);
            current.HeadRotation = Quaternion.Slerp(current.HeadRotation, target.HeadRotation, alpha);
            current.LeftRotation = Quaternion.Slerp(current.LeftRotation, target.LeftRotation, alpha);
            current.RightRotation = Quaternion.Slerp(current.RightRotation, target.RightRotation, alpha);
            if ((target.Tracked & 1) != 0)
            {
                foreach (var pair in heads)
                {
                    if (pair.Key == null || !pair.Key.gameObject.activeInHierarchy) continue;
                    savedHeads[pair.Key] = pair.Key.localRotation;
                    pair.Key.rotation = transform.rotation * current.HeadRotation * pair.Value;
                }
            }
            foreach (Chain chain in chains)
            {
                if ((target.Tracked & (chain.Left ? 2 : 4)) == 0 || chain.Hand == null || !chain.Hand.gameObject.activeInHierarchy) continue;
                chain.UpperPose = chain.Upper.localRotation; chain.LowerPose = chain.Lower.localRotation; chain.HandPose = chain.Hand.localRotation; chain.Applied = true;
                Vector3 shoulder = chain.Upper.position;
                float a = Vector3.Distance(shoulder, chain.Lower.position), b = Vector3.Distance(chain.Lower.position, chain.Hand.position);
                if (a < 0.001f || b < 0.001f) continue;
                Quaternion gripRotation = transform.rotation * (chain.Left ? current.LeftRotation : current.RightRotation);
                Vector3 delta = transform.TransformPoint(chain.Left ? current.Left : current.Right) - gripRotation * chain.GripOffset - shoulder;
                float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
                Vector3 direction = delta.sqrMagnitude > 0.000001f ? delta.normalized : -transform.up;
                Quaternion facing = transform.rotation * current.HeadRotation;
                Vector3 bend = Vector3.ProjectOnPlane(facing * Vector3.right * (chain.Left ? -1 : 1) - Vector3.up * 0.65f, direction).normalized;
                if (bend.sqrMagnitude < 0.01f) bend = Vector3.ProjectOnPlane(facing * Vector3.forward, direction).normalized;
                float along = (a * a - b * b + distance * distance) / (2 * distance);
                Vector3 elbow = shoulder + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
                chain.Upper.rotation = Quaternion.FromToRotation(chain.Lower.position - shoulder, elbow - shoulder) * chain.Upper.rotation;
                chain.Lower.rotation = Quaternion.FromToRotation(chain.Hand.position - chain.Lower.position, shoulder + direction * distance - chain.Lower.position) * chain.Lower.rotation;
                AvatarArmCalibration.ForearmTwist(chain.Lower, chain.Hand, chain.LowerPalm, gripRotation * Vector3.up);
                chain.Hand.rotation = gripRotation * chain.Alignment;
            }
            if ((target.Tracked & 1) != 0 && voiceMouth != null)
            {
                if (!voiceApplied) { voicePosition = voiceMouth.localPosition; voiceRotation = voiceMouth.localRotation; voiceApplied = true; }
                Quaternion headRotation = transform.rotation * current.HeadRotation;
                voiceMouth.SetPositionAndRotation(transform.TransformPoint(current.Head) + headRotation * new Vector3(0, -.1f, .05f), headRotation);
            }
        }
        private void RestoreBones()
        {
            RestoreVoice();
            foreach (Chain chain in chains) chain.Restore();
            foreach (var pair in savedHeads) if (pair.Key != null) pair.Key.localRotation = pair.Value;
            savedHeads.Clear();
        }
        private void RestoreVoice()
        {
            if (voiceApplied && voiceMouth != null) { voiceMouth.localPosition = voicePosition; voiceMouth.localRotation = voiceRotation; }
            voiceApplied = false;
        }
        private void RestoreAnimators()
        {
            foreach (var pair in nativeAnimators) if (pair.Key != null) pair.Key.enabled = pair.Value;
            if (nativeAnimators.Count > 0) nextScan = 0;
            nativeAnimators.Clear();
        }
        private void OnDisable() { RestoreBones(); RestoreAnimators(); }
        private void OnDestroy() { RestoreBones(); RestoreAnimators(); }
    }
}
