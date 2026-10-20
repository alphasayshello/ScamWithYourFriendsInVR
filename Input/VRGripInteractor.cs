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
        private Transform motionSpace;

        internal void Tick(bool overUI)
        {
            VRManager manager = VRManager.Instance;
            Transform hand = manager.Rig.Right.transform;
            if (motionSpace != hand.parent) ResetMotion();
            motionSpace = hand.parent;
            Vector3 position = hand.localPosition;
            // Measure physical controller movement before applying the rig's
            // artificial turn/translation, then express it in the current world frame.
            Vector3 delta = position - previousPosition;
            velocity = havePrevious && Time.unscaledDeltaTime > 0
                ? (motionSpace != null ? motionSpace.TransformVector(delta) : delta) / Time.unscaledDeltaTime : Vector3.zero;
            previousPosition = position; havePrevious = true;
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
            acquired = null; pending = false; interacting = false; ResetMotion();
        }

        internal void ResetMotion()
        { havePrevious = false; velocity = Vector3.zero; motionSpace = null; }
    }
}
