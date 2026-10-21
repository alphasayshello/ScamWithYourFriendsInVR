using System;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRFlightControls
    {
        private static readonly WeakReference<Component> vehicle = new WeakReference<Component>(null);
        private static Component controller, inputOwner;
        private static Vector2 previousMove;
        private static bool previousJump, hasInput;
        private static bool bodyReleaseNeeded;
        internal static bool WaitingForBodyRelease => bodyReleaseNeeded && Current == null;

        internal static bool SuppressBodyInput(VRInputManager input)
        {
            if (!bodyReleaseNeeded) return false;
            if (Current != null || input.Move == Vector2.zero && input.Turn == Vector2.zero && !input.Jump)
            { bodyReleaseNeeded = false; return false; }
            return true;
        }

        internal static Component Current
        {
            get
            {
                if (!vehicle.TryGetTarget(out Component current) || current == null || !current.gameObject.activeInHierarchy) return null;
                return GameAdapter.Read(GameAdapter.Read(current, "Flyer"), "isLocalPlayer") is bool local && local ? current : null;
            }
        }

        internal static void Initialize()
        {
            Clear();
            Type type = ReflectionTypes.Find("FpvInteractable");
            if (type == null) return;
            // Once per XR session, also recover a flight that started before
            // the headset/runtime reconnected. Ownership hooks handle changes.
            foreach (var candidate in UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None))
                if (candidate is Component component && GameAdapter.Read(GameAdapter.Read(component, "Flyer"), "isLocalPlayer") is bool local && local)
                { vehicle.SetTarget(component); break; }
        }

        internal static void FlyerChanged(Component instance, object flyer)
        {
            if (GameAdapter.Read(flyer, "isLocalPlayer") is bool local && local)
            {
                if (Current != instance) ClearInput();
                vehicle.SetTarget(instance);
            }
            else if (vehicle.TryGetTarget(out Component previous) && previous == instance) Clear();
        }

        internal static void Update(Component playerController, Component current, bool allowed)
        {
            if (inputOwner != current || controller != playerController) ClearInput();
            if (current == null) return;
            controller = playerController; inputOwner = current;
            allowed &= GameAdapter.Read(current, "WasLaunched") is bool launched && launched
                && !(GameAdapter.Read(current, "Networkexploded") is bool exploded && exploded);
            var input = VRManager.Instance.Input;
            Vector2 move = allowed ? input.Move : Vector2.zero;
            bool jump = allowed && input.Jump;
            if (!hasInput || move != previousMove)
                (GameAdapter.Read(controller, "OnMoveInputChanged") as Action<Vector2>)?.Invoke(move);
            if (!hasInput || jump != previousJump)
                (GameAdapter.Read(controller, "OnJumpInputChanged") as Action<bool>)?.Invoke(jump);
            previousMove = move; previousJump = jump; hasInput = true;
            if (allowed && input.Turn.sqrMagnitude > 0)
            {
                float step = Plugin.FlightSteeringSpeed.Value * Mathf.Min(Time.deltaTime, .05f);
                (GameAdapter.Read(controller, "OnLookChanged") as Action<float, float>)?.Invoke(input.Turn.x * step, -input.Turn.y * step);
            }
        }

        private static void ClearInput()
        {
            // A flight stick/climb hold must not become a walking, snap-turn
            // or jump command when the native aircraft releases its pilot.
            if (hasInput && !ReferenceEquals(inputOwner, null)) bodyReleaseNeeded = true;
            if (controller != null && hasInput)
            {
                if (previousMove != Vector2.zero) (GameAdapter.Read(controller, "OnMoveInputChanged") as Action<Vector2>)?.Invoke(Vector2.zero);
                if (previousJump) (GameAdapter.Read(controller, "OnJumpInputChanged") as Action<bool>)?.Invoke(false);
            }
            controller = inputOwner = null; previousMove = Vector2.zero; previousJump = hasInput = false;
        }

        internal static void Clear() { ClearInput(); vehicle.SetTarget(null); }
    }
}
