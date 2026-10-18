using System;
using System.Collections.Generic;
using System.Reflection;
using Steamworks;

namespace ScamWithYourFriendsInVR.Multiplayer
{
    internal static class SteamPoseDiscovery
    {
        private const string Key = "swyf_vr_pose_protocol";
        private static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        private static object ReadStatic(string name, string property)
        {
            if (!types.TryGetValue(name, out Type type))
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((type = assembly.GetType(name, false)) != null) break;
                if (type != null) types[name] = type;
            }
            return type?.GetProperty(property, BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
        }
        private static ulong Lobby(string name)
        {
            object instance = ReadStatic(name, "Instance");
            return instance?.GetType().GetProperty("LobbyId")?.GetValue(instance) is ulong id ? id : 0;
        }
        internal static bool Update(bool server, bool enabled)
        {
            if (!(ReadStatic("SteamManager", "Initialized") is bool initialized) || !initialized) return false;
            ulong id = Lobby(server ? "SteamCreateLobbyForServer" : "SteamJoinFriendsThroughOverlay");
            if (id == 0) return false;
            var lobby = new CSteamID(id);
            if (server && SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID())
                SteamMatchmaking.SetLobbyData(lobby, Key, enabled ? PoseProtocol.Version.ToString() : "");
            return SteamMatchmaking.GetLobbyData(lobby, Key) == PoseProtocol.Version.ToString();
        }
    }
}
