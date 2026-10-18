using System;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Multiplayer
{
    public struct PoseHello : NetworkMessage { public byte Version; }
    public struct PoseWelcome : NetworkMessage { public byte Version; }
    public struct PoseFrame : NetworkMessage
    {
        public uint PlayerId, Sequence;
        public byte Tracked;
        public Vector3 Head, Left, Right;
        public Quaternion HeadRotation, LeftRotation, RightRotation;
    }

    public static class PoseProtocol
    {
        public const byte Version = 1;
        public static void Register()
        {
            Writer<PoseHello>.write = (w, m) => w.WriteByte(m.Version);
            Reader<PoseHello>.read = r => new PoseHello { Version = r.ReadByte() };
            Writer<PoseWelcome>.write = (w, m) => w.WriteByte(m.Version);
            Reader<PoseWelcome>.read = r => new PoseWelcome { Version = r.ReadByte() };
            Writer<PoseFrame>.write = Write;
            Reader<PoseFrame>.read = Read;
        }
        public static void Write(NetworkWriter w, PoseFrame m)
        {
            w.WriteUInt(m.PlayerId); w.WriteUInt(m.Sequence); w.WriteByte(m.Tracked);
            w.WriteVector3(m.Head); w.WriteQuaternion(m.HeadRotation);
            w.WriteVector3(m.Left); w.WriteQuaternion(m.LeftRotation);
            w.WriteVector3(m.Right); w.WriteQuaternion(m.RightRotation);
        }
        public static PoseFrame Read(NetworkReader r) => new PoseFrame
        {
            PlayerId = r.ReadUInt(), Sequence = r.ReadUInt(), Tracked = r.ReadByte(),
            Head = r.ReadVector3(), HeadRotation = r.ReadQuaternion(),
            Left = r.ReadVector3(), LeftRotation = r.ReadQuaternion(),
            Right = r.ReadVector3(), RightRotation = r.ReadQuaternion()
        };

        public static bool Valid(PoseFrame m)
            => (m.Tracked & ~7) == 0 && Position(m.Head) && Position(m.Left) && Position(m.Right)
                && Rotation(m.HeadRotation) && Rotation(m.LeftRotation) && Rotation(m.RightRotation);
        private static bool Position(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && p.sqrMagnitude <= 25;
        private static bool Rotation(Quaternion q)
        {
            float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return Finite(norm) && norm > 0.9f && norm < 1.1f;
        }
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        public static bool Newer(uint candidate, uint previous) => unchecked((int)(candidate - previous)) > 0;
    }
}
