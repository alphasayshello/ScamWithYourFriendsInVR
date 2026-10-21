using System;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRBagControls
    {
        private static Component previousController, previousBag;
        private static Vector2 previousMove;
        private static bool hasMove;

        internal static void Update(Component controller, Component bag, bool allowed)
        {
            if (previousController != controller || previousBag != bag) Clear();
            if (bag == null) return;
            previousController = controller; previousBag = bag;
            // The native bag handler converts camera-relative input into force.
            // Do not send the standing body's already rotated movement vector.
            Vector2 move = allowed ? VRManager.Instance.Input.Move : Vector2.zero;
            if (hasMove && move == previousMove) return;
            previousMove = move; hasMove = true;
            (GameAdapter.Read(controller, "OnMoveInputChanged") as Action<Vector2>)?.Invoke(move);
        }

        internal static void Clear()
        {
            if (previousController != null && hasMove && previousMove != Vector2.zero)
                (GameAdapter.Read(previousController, "OnMoveInputChanged") as Action<Vector2>)?.Invoke(Vector2.zero);
            previousController = previousBag = null; previousMove = Vector2.zero; hasMove = false;
        }
    }
}
