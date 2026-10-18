using UnityEngine;

namespace ScamWithYourFriendsInVR.Camera
{
    internal static class VRRoomScaleBody
    {
        internal static Vector3 Follow(CharacterController body, Quaternion trackingRotation, Vector3 trackingOffset)
        {
            if (body == null || !body.enabled || !body.gameObject.activeInHierarchy) return Vector3.zero;
            trackingOffset.y = 0;
            float distance = trackingOffset.magnitude;
            const float leanRadius = .08f;
            if (distance <= leanRadius) return Vector3.zero;
            Vector3 step = trackingRotation * (trackingOffset / distance * Mathf.Min(distance - leanRadius, .25f));
            Vector3 before = body.transform.position;
            body.Move(step);
            return body.transform.position - before;
        }

        internal static Vector3 Consume(Vector3 center, Quaternion trackingRotation, Vector3 movement)
        {
            Vector3 local = Quaternion.Inverse(trackingRotation) * movement;
            return center + new Vector3(local.x, 0, local.z);
        }
    }
}
