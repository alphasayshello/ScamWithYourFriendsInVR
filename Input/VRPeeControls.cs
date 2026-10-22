using System;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRPeeControls
    {
        private static Component owner;
        private static bool held, ownsStream;
        internal static bool Read(VRInputManager input, bool allowed)
        {
            Component current = GameAdapter.LocalPlayer;
            if (owner != current) Clear();
            owner = current;
            if (current == null) { Clear(); return false; }
            if (!allowed || !input.Pee) held = false;
            else if (input.PeeDown) held = true;
            if (held) ownsStream = true;
            else if (ownsStream && GameAdapter.Read(owner, "wasPPressed") is bool active && !active) ownsStream = false;
            return held;
        }
        internal static void Clear()
        {
            // Leave independently held native PC input alone.
            if (ownsStream && owner != null && GameAdapter.Read(owner, "wasPPressed") is bool active && active)
            {
                GameAdapter.Write(owner, "wasPPressed", false);
                (GameAdapter.Read(owner, "OnPInputChanged") as Action<bool>)?.Invoke(false);
            }
            owner = null; held = ownsStream = false;
        }
    }
}
