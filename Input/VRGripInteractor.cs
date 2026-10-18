using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal sealed class VRGripInteractor
    {
        private Component acquired;
        private bool pending, interacting;
        private float deadline;
        private Vector3 previousPosition, velocity;
        private bool havePrevious;

        internal void Tick(bool overUI)
        {
            VRManager manager = VRManager.Instance;
            Transform hand = manager.Rig.Right.transform;
            if (havePrevious && Time.unscaledDeltaTime > 0)
                velocity = (hand.position - previousPosition) / Time.unscaledDeltaTime;
            previousPosition = hand.position; havePrevious = true;
            Component interactor = GameAdapter.Interactor;
            Component held = GameAdapter.Read(interactor, "CurrentHeldItem") as Component;
            // A native swap/drop can replace the item between grip frames.
            if (acquired != null && acquired != held) acquired = null;
            if (manager.Input.GripDown && !overUI && GameAdapter.Allowed(interactor))
            {
                acquired = held;
                pending = held == null;
                deadline = Time.unscaledTime + 2f;
                // Native interact toggles/drops an already held item. Taking
                // grip ownership must keep it held until the grip is released.
                if (pending) { GameAdapter.Interact(true); interacting = true; }
            }
            if (pending)
            {
                acquired = GameAdapter.Read(interactor, "CurrentHeldItem") as Component;
                if (acquired != null) pending = false;
                if (Time.unscaledTime > deadline) pending = false;
            }
            if (manager.Input.GripUp && interacting)
            {
                GameAdapter.Interact(false);
                interacting = false;
            }
            if (!manager.Input.Grip && acquired != null)
            {
                GameAdapter.Drop(acquired, velocity, hand);
                acquired = null;
            }
        }

        internal void Cancel()
        {
            VRManager manager = VRManager.Instance;
            if (interacting) GameAdapter.Interact(false);
            if (acquired != null && manager?.Rig?.Right != null)
                GameAdapter.Drop(acquired, Vector3.zero, manager.Rig.Right.transform);
            acquired = null; pending = false; interacting = false; havePrevious = false;
        }
    }
}
