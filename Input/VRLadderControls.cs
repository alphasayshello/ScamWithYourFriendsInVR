using System;
using System.Collections;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRLadderControls
    {
        private static readonly WeakReference<Component> ladder = new WeakReference<Component>(null);
        private static Component controller, inputOwner;
        private static Vector2 previousMove;
        private static bool hasInput, releaseNeeded;
        internal static Component Current
        {
            get
            {
                if (!ladder.TryGetTarget(out Component value) || value == null || !value.gameObject.activeInHierarchy) return null;
                return GameAdapter.Read(GameAdapter.Read(value, "Networkclimber"), "isLocalPlayer") is bool local && local ? value : null;
            }
        }
        internal static bool WaitingForBodyRelease => releaseNeeded && Current == null;
        internal static bool SuppressBodyInput(VRInputManager input)
        {
            if (!releaseNeeded) return false;
            if (Current != null || input.Move == Vector2.zero && input.Turn == Vector2.zero && !input.Jump)
            { releaseNeeded = false; return false; }
            return true;
        }
        internal static void Initialize()
        {
            Clear();
            Type type = ReflectionTypes.Find("LadderInteractable");
            if (type == null) return;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None))
                if (candidate is Component component && GameAdapter.Read(GameAdapter.Read(component, "Networkclimber"), "isLocalPlayer") is bool local && local)
                { ladder.SetTarget(component); break; }
        }
        internal static void ClimberChanged(Component instance, object climber)
        {
            if (GameAdapter.Read(climber, "isLocalPlayer") is bool local && local)
            {
                if (!ReferenceEquals(Current, instance)) ClearInput();
                ladder.SetTarget(instance);
            }
            else if (ladder.TryGetTarget(out Component previous) && ReferenceEquals(previous, instance)) Clear();
        }
        internal static void Disabled(Component instance)
        {
            object climber = GameAdapter.Read(instance, "Networkclimber");
            if (!(GameAdapter.Read(climber, "isLocalPlayer") is bool local && local)) return;
            // A disabled-but-spawned ladder can use its ordinary dismount
            // command. A despawned identity cannot send commands any more.
            if (GameAdapter.Read(instance, "netId") is uint id && id != 0
                && GameAdapter.Static("Mirror.NetworkClient", "spawned") is IDictionary spawned && spawned.Contains(id))
                GameAdapter.Call(instance, "LocalDismount");
            // Native OnDisable does not release its controller delegates. Run
            // the native ownership cleanup locally, also while a remote
            // dismount acknowledgement is still in flight.
            if (GameAdapter.Read(GameAdapter.Read(instance, "Networkclimber"), "isLocalPlayer") is bool remainsLocal && remainsLocal)
                GameAdapter.Call(instance, "DeltaClimber", climber, null);
            ClimberChanged(instance, null);
        }

        internal static void Update(Component playerController, Component current, bool allowed)
        {
            if (!ReferenceEquals(inputOwner, current) || controller != playerController) ClearInput();
            if (current == null) return;
            controller = playerController; inputOwner = current;
            // Native ladder movement uses the stick's vertical axis directly,
            // independent of where the headset/body is facing.
            Vector2 move = allowed ? new Vector2(0, VRManager.Instance.Input.Move.y) : Vector2.zero;
            if (!hasInput || move != previousMove)
                (GameAdapter.Read(controller, "OnMoveInputChanged") as Action<Vector2>)?.Invoke(move);
            previousMove = move; hasInput = true;
        }
        private static void ClearInput()
        {
            if (hasInput && !ReferenceEquals(inputOwner, null)) releaseNeeded = true;
            // Ownership hooks run after native unsubscription. Do not broadcast
            // stale zero input into a different newly occupied vehicle/bag.
            if (controller != null && hasInput && previousMove != Vector2.zero && ReferenceEquals(inputOwner, Current))
                (GameAdapter.Read(controller, "OnMoveInputChanged") as Action<Vector2>)?.Invoke(Vector2.zero);
            controller = inputOwner = null; previousMove = Vector2.zero; hasInput = false;
        }
        internal static void Clear() { ClearInput(); ladder.SetTarget(null); }
    }
}
