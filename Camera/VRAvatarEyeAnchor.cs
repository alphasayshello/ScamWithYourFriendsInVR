using System.Collections.Generic;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Camera
{
    internal sealed class VRAvatarEyeAnchor
    {
        private Transform owner;
        private float nextScan;
        private readonly List<Renderer> eyes = new List<Renderer>();
        private readonly List<Transform> heads = new List<Transform>();

        internal bool TryPosition(Transform player, out Vector3 position)
        {
            position = default;
            if (player == null) return false;
            Scan(player);
            int count = 0;
            foreach (Renderer eye in eyes)
            {
                if (eye == null || !eye.enabled || !eye.gameObject.activeInHierarchy) continue;
                Bounds bounds = eye.bounds;
                if (bounds.extents.magnitude < .001f || bounds.extents.magnitude > .6f) continue;
                position += bounds.center;
                count++;
            }
            if (count == 0) return false;
            position /= count;
            return true;
        }

        internal bool TryHeadPosition(Transform player, out Vector3 position)
        {
            position = default;
            if (player == null) return false;
            Scan(player);
            foreach (Transform head in heads)
            {
                if (head == null || !head.gameObject.activeInHierarchy) continue;
                position = head.position + Vector3.up * (.12f * Mathf.Clamp(player.lossyScale.y, .5f, 2f));
                return true;
            }
            return false;
        }

        private void Scan(Transform player)
        {
            if (owner != player || Time.unscaledTime >= nextScan)
            {
                owner = player; nextScan = Time.unscaledTime + 2; eyes.Clear(); heads.Clear();
                foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(true))
                {
                    string name = renderer.name.ToLowerInvariant();
                    if (name.StartsWith("eye") && !name.StartsWith("eyebrow") && !name.StartsWith("eyelash") && !name.StartsWith("eyelid"))
                        eyes.Add(renderer);
                }
                foreach (Transform bone in player.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "mixamorig:Head") heads.Add(bone);
            }
        }
    }
}
