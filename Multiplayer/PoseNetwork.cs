using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Multiplayer
{
    public sealed class PoseNetwork : MonoBehaviour
    {
        private const int UnreliableChannel = 1;
        public static PoseNetwork Instance { get; private set; }
        public int AcceptedPosePackets { get; private set; }
        private sealed class Peer
        {
            internal NetworkConnectionToClient Connection;
            internal float LastPacket, LastHello;
            internal uint Sequence;
            internal bool HasSequence;
        }
        private readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        private readonly Dictionary<uint, RemotePoseAvatar> avatars = new Dictionary<uint, RemotePoseAvatar>();
        private readonly List<int> stalePeers = new List<int>();
        private readonly List<uint> staleAvatars = new List<uint>();
        private bool serverRegistered, clientRegistered, welcomed, hostAdvertised;
        private float nextDiscovery, nextHello, nextSend;
        private uint sequence;
        private PoseFrame local;
        private float localUpdated = -100;
        private NetworkConnectionToServer observedConnection;

        private void Awake() { Instance = this; PoseProtocol.Register(); }
        private void OnEnable() { Instance = this; }

        // Called only by the VR runtime. PC clients never load the XR runtime assembly.
        public static void ClearLocalPose()
        {
            if (Instance == null) return;
            Instance.local = new PoseFrame { HeadRotation = Quaternion.identity, LeftRotation = Quaternion.identity, RightRotation = Quaternion.identity };
            Instance.localUpdated = Time.unscaledTime;
        }
        public static void SetLocalPose(Transform root, Transform head, Transform left, Transform right, byte tracked)
        {
            if (Instance == null || !Instance.isActiveAndEnabled || root == null || head == null || left == null || right == null) return;
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            Instance.local = new PoseFrame
            {
                Tracked = tracked,
                Head = root.InverseTransformPoint(head.position), HeadRotation = inverse * head.rotation,
                Left = root.InverseTransformPoint(left.position), LeftRotation = inverse * left.rotation,
                Right = root.InverseTransformPoint(right.position), RightRotation = inverse * right.rotation
            };
            Instance.localUpdated = Time.unscaledTime;
        }

        private void Update()
        {
            if (!ReferenceEquals(observedConnection, NetworkClient.connection))
            {
                observedConnection = NetworkClient.connection;
                welcomed = false; clientRegistered = false; hostAdvertised = false; nextHello = 0;
                ClearAvatars();
            }
            if (NetworkServer.active && !serverRegistered)
            {
                NetworkServer.RegisterHandler<PoseHello>(OnHello);
                NetworkServer.RegisterHandler<PoseFrame>(OnServerPose);
                serverRegistered = true;
            }
            else if (!NetworkServer.active && serverRegistered) { peers.Clear(); serverRegistered = false; }
            if (NetworkClient.active && !clientRegistered)
            {
                NetworkClient.RegisterHandler<PoseWelcome>(OnWelcome);
                NetworkClient.RegisterHandler<PoseFrame>(OnClientPose);
                clientRegistered = true;
            }
            else if (!NetworkClient.active && clientRegistered)
            {
                clientRegistered = false; welcomed = false; hostAdvertised = false; nextHello = 0;
                ClearAvatars();
            }
            float now = Time.unscaledTime;
            if (now >= nextDiscovery)
            {
                nextDiscovery = now + 1;
                try { hostAdvertised = SteamPoseDiscovery.Update(NetworkServer.active, true); }
                catch (Exception ex) { Plugin.Log.LogDebug("Pose discovery: " + ex.GetBaseException().Message); hostAdvertised = false; }
            }
            if (NetworkClient.isConnected && NetworkClient.connection != null && NetworkClient.connection.isAuthenticated)
            {
                bool localHarness = Array.IndexOf(Environment.GetCommandLineArgs(), "--swyf-pair-client") >= 0
                    && NetworkManager.singleton != null && NetworkManager.singleton.networkAddress == "127.0.0.1";
                if (!welcomed && now >= nextHello && (hostAdvertised || NetworkServer.active || Plugin.DirectHandshake.Value || localHarness))
                {
                    nextHello = now + 3;
                    NetworkClient.Send(new PoseHello { Version = PoseProtocol.Version });
                }
                if (welcomed && Plugin.Mode.Value == Plugin.PlayerMode.VR && now >= nextSend && NetworkClient.localPlayer != null)
                {
                    nextSend = now + 1f / Plugin.PoseRate.Value;
                    PoseFrame frame = local;
                    frame.PlayerId = NetworkClient.localPlayer.netId;
                    frame.Sequence = ++sequence;
                    if (now - localUpdated > 0.25f) frame.Tracked = 0;
                    if (PoseProtocol.Valid(frame)) NetworkClient.Send(frame, UnreliableChannel);
                }
            }
            if (serverRegistered)
            {
                stalePeers.Clear();
                foreach (var entry in peers)
                    if (!NetworkServer.connections.TryGetValue(entry.Key, out var conn) || conn != entry.Value.Connection) stalePeers.Add(entry.Key);
                foreach (int id in stalePeers) peers.Remove(id);
            }
            staleAvatars.Clear();
            foreach (var entry in avatars)
                if (entry.Value == null || !NetworkClient.spawned.ContainsKey(entry.Key)) staleAvatars.Add(entry.Key);
            foreach (uint id in staleAvatars)
            {
                if (avatars[id] != null) Destroy(avatars[id]);
                avatars.Remove(id);
            }
        }

        private void OnHello(NetworkConnectionToClient conn, PoseHello message)
        {
            if (!conn.isAuthenticated || message.Version != PoseProtocol.Version) return;
            if (peers.TryGetValue(conn.connectionId, out var peer) && peer.Connection == conn && Time.unscaledTime - peer.LastHello < 1) return;
            if (peer == null || peer.Connection != conn) peers[conn.connectionId] = peer = new Peer { Connection = conn };
            peer.LastHello = Time.unscaledTime;
            conn.Send(new PoseWelcome { Version = PoseProtocol.Version });
            Plugin.Log.LogInfo("VR pose protocol negotiated with connection " + conn.connectionId);
        }
        private void OnWelcome(PoseWelcome message)
        {
            if (message.Version != PoseProtocol.Version) return;
            welcomed = true;
            Plugin.Log.LogInfo("Modded host accepted VR pose protocol; mode=" + Plugin.Mode.Value);
        }
        private void OnServerPose(NetworkConnectionToClient conn, PoseFrame message)
        {
            if (!peers.TryGetValue(conn.connectionId, out var peer) || peer.Connection != conn || conn.identity == null
                || !conn.isAuthenticated || !conn.isReady || !PoseProtocol.Valid(message)) return;
            float now = Time.unscaledTime;
            if (peer.HasSequence && (!PoseProtocol.Newer(message.Sequence, peer.Sequence) || now - peer.LastPacket < 1f / 35)) return;
            peer.HasSequence = true; peer.Sequence = message.Sequence; peer.LastPacket = now;
            AcceptedPosePackets++;
            // The sender never selects another player's identity.
            message.PlayerId = conn.identity.netId;
            message.HeadRotation = message.HeadRotation.normalized;
            message.LeftRotation = message.LeftRotation.normalized;
            message.RightRotation = message.RightRotation.normalized;
            foreach (Peer recipient in peers.Values)
            {
                var connection = recipient.Connection;
                if (connection != conn && connection.isAuthenticated && connection.isReady)
                    connection.Send(message, UnreliableChannel);
            }
        }
        private void OnClientPose(PoseFrame message)
        {
            if (!welcomed || !PoseProtocol.Valid(message) || !NetworkClient.spawned.TryGetValue(message.PlayerId, out var identity)
                || identity == null || identity.isLocalPlayer) return;
            if (!avatars.TryGetValue(message.PlayerId, out var avatar) || avatar == null)
                avatars[message.PlayerId] = avatar = identity.gameObject.AddComponent<RemotePoseAvatar>();
            avatar.Receive(message);
        }
        private void ClearAvatars()
        {
            foreach (var avatar in avatars.Values)
                if (avatar != null) { avatar.enabled = false; Destroy(avatar); }
            avatars.Clear();
        }
        private void OnDisable()
        {
            if (serverRegistered) { NetworkServer.UnregisterHandler<PoseHello>(); NetworkServer.UnregisterHandler<PoseFrame>(); }
            if (clientRegistered) { NetworkClient.UnregisterHandler<PoseWelcome>(); NetworkClient.UnregisterHandler<PoseFrame>(); }
            try { if (NetworkServer.active) SteamPoseDiscovery.Update(true, false); } catch (Exception) { }
            ClearAvatars(); peers.Clear();
            serverRegistered = clientRegistered = welcomed = hostAdvertised = false;
            observedConnection = null;
            nextDiscovery = nextHello = nextSend = 0;
            local = default; localUpdated = -100;
            if (Instance == this) Instance = null;
        }
    }
}
