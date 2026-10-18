using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Multiplayer
{
    public static class AvatarArmCalibration
    {
        public static bool GripCalibration(Transform root, Transform hand, Transform lower,
            out Quaternion alignment, out Vector3 gripOffset, out Vector3 lowerPalm)
        {
            alignment = WristAlignment(root, hand); gripOffset = Vector3.zero; lowerPalm = Vector3.zero;
            string prefix = hand.name;
            Transform middle = hand.Find(prefix + "Middle1"), index = hand.Find(prefix + "Index1"), pinky = hand.Find(prefix + "Pinky1");
            if (index == null) return false;
            bool left = hand.name.Contains("Left");
            Vector3 forward = (middle != null ? middle : index).position - hand.position;
            Vector3 normal;
            if (middle != null && pinky != null)
            {
                normal = Vector3.Cross(forward, index.position - pinky.position).normalized;
                if (!left) normal = -normal;
            }
            else
            {
                // The shipped scammer mitten rig only has an index chain. Its
                // finger axis is local Y and the back of the hand is mirrored X.
                // A tabletop mouse alignment is not a controller grip frame.
                Transform thumb = hand.Find(prefix + "Thumb1");
                normal = thumb != null ? Vector3.Cross(forward, thumb.position - hand.position).normalized
                    : hand.right;
                if (!left) normal = -normal;
            }
            if (forward.magnitude < .005f || forward.magnitude > .3f || normal.sqrMagnitude < .5f) return false;
            normal = Vector3.ProjectOnPlane(normal, forward).normalized;
            if (normal.sqrMagnitude < .5f) return false;
            Quaternion palm = Quaternion.LookRotation(forward.normalized, normal);
            alignment = Quaternion.Inverse(palm) * hand.rotation;
            Vector3 knuckles = middle != null && pinky != null ? (middle.position + index.position + pinky.position) / 3 : index.position;
            gripOffset = Quaternion.Inverse(palm) * (knuckles - hand.position) * .5f;
            lowerPalm = Quaternion.Inverse(lower.rotation) * normal;
            return true;
        }

        public static void ForearmTwist(Transform lower, Transform hand, Vector3 localPalm, Vector3 targetPalm)
        {
            if (localPalm.sqrMagnitude < .5f) return;
            Vector3 axis = (hand.position - lower.position).normalized;
            Vector3 current = Vector3.ProjectOnPlane(lower.rotation * localPalm, axis);
            Vector3 desired = Vector3.ProjectOnPlane(targetPalm, axis);
            if (current.sqrMagnitude < .001f || desired.sqrMagnitude < .001f) return;
            float twist = Mathf.Clamp(Vector3.SignedAngle(current, desired, axis), -100, 100) * .65f;
            lower.rotation = Quaternion.AngleAxis(twist, axis) * lower.rotation;
        }

        public static bool TryArmBones(Transform playerRoot, Transform hand, out Transform upper, out Transform lower)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MonoBehaviour component in playerRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().Name != "MonitorArmIkDriver") continue;
                foreach (string name in new[] { "agentRightArm", "agentLeftArm", "smugglerRightArm", "smugglerLeftArm" })
                {
                    object chain = component.GetType().GetField(name, flags)?.GetValue(component);
                    if (chain == null || chain.GetType().GetField("tip", flags)?.GetValue(chain) as Transform != hand) continue;
                    upper = chain.GetType().GetField("root", flags)?.GetValue(chain) as Transform;
                    lower = chain.GetType().GetField("mid", flags)?.GetValue(chain) as Transform;
                    if (upper != null && lower != null) return true;
                }
            }
            lower = hand.parent;
            upper = lower?.parent;
            string side = hand.name == "mixamorig:LeftHand" ? "Left" : "Right";
            return lower != null && upper != null && lower.name == "mixamorig:" + side + "ForeArm"
                && upper.name == "mixamorig:" + side + "Arm";
        }

        public static Transform FacingFrame(Transform playerRoot)
        {
            foreach (MonoBehaviour component in playerRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().FullName != "Metater.MetaPlayer") continue;
                var field = component.GetType().GetField("polarTransform", BindingFlags.Instance | BindingFlags.Public);
                if (field?.GetValue(component) is Transform facing) return facing;
            }
            return playerRoot;
        }

        public static Quaternion WristAlignment(Transform playerRoot, Transform hand)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MonoBehaviour component in playerRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().Name != "MonitorArmIkDriver") continue;
                foreach (string name in new[] { "agentRightArm", "agentLeftArm", "smugglerRightArm", "smugglerLeftArm" })
                {
                    object chain = component.GetType().GetField(name, flags)?.GetValue(component);
                    if (chain == null) continue;
                    var type = chain.GetType();
                    if (type.GetField("tip", flags)?.GetValue(chain) as Transform != hand) continue;
                    if (type.GetField("tipToHandAlignmentRotation", flags)?.GetValue(chain) is Quaternion alignment)
                        return Quaternion.Inverse(alignment);
                }
            }
            return Quaternion.identity;
        }
    }
}
