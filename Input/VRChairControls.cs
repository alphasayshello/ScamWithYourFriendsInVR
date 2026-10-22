using System.Collections;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRChairControls
    {
        internal static void Disabled(Component chair)
        {
            object sitter = GameAdapter.Read(chair, "Networksitter");
            if (!ReferenceEquals(sitter, GameAdapter.Interactor)
                || !(GameAdapter.Read(sitter, "isLocalPlayer") is bool local && local)) return;
            if (GameAdapter.Read(chair, "isServer") is bool server && server)
                GameAdapter.Call(chair, "ServerSetSitter", (object)null);
            else if (GameAdapter.Read(chair, "netId") is uint id && id != 0
                && GameAdapter.Static("Mirror.NetworkClient", "spawned") is IDictionary spawned && spawned.Contains(id))
                GameAdapter.Call(chair, "CmdStandUp");
            // Despawned seats cannot send another command. While a remote
            // acknowledgement is pending, native local cleanup also releases
            // the jump listener, animation and movement blockers immediately.
            // Run before OnDisable removes this chair from its native registry.
            if (ReferenceEquals(GameAdapter.Read(chair, "Networksitter"), sitter))
                GameAdapter.Call(chair, "LocalClearSitter", sitter);
        }
    }
}
