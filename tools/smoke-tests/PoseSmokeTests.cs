using System.Reflection;
using System.Collections;
using Mirror;
using UnityEngine;
using ScamWithYourFriendsInVR.Multiplayer;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestPoseHost()
        {
            float deadline = Time.realtimeSinceStartup + 30;
            while ((!NetworkServer.activeHost || NetworkClient.localPlayer == null || !NetworkClient.ready) && Time.realtimeSinceStartup < deadline)
                yield return null;
            // This optional test never sends synthetic poses in a shared lobby.
            if (!NetworkServer.activeHost || NetworkServer.connections.Count != 1 || !NetworkServer.connections.ContainsKey(0))
            {
                Logger.LogError("SMOKE FAIL: pose host test requires an isolated one-player local host");
                yield break;
            }
            yield return new WaitForSecondsRealtime(2);
            int before = PoseNetwork.Instance.AcceptedPosePackets;
            var pose = new PoseFrame { PlayerId = uint.MaxValue, Sequence = 1234, Tracked = 0,
                HeadRotation = Quaternion.identity, LeftRotation = Quaternion.identity, RightRotation = Quaternion.identity };
            NetworkClient.Send(pose, 1);
            yield return new WaitForSecondsRealtime(0.2f);
            bool accepted = PoseNetwork.Instance.AcceptedPosePackets == before + 1;
            NetworkClient.Send(pose, 1);
            yield return new WaitForSecondsRealtime(0.2f);
            bool duplicateRejected = PoseNetwork.Instance.AcceptedPosePackets == before + 1;
            pose.Sequence++;
            pose.Head.x = float.PositiveInfinity;
            NetworkClient.Send(pose, 1);
            yield return new WaitForSecondsRealtime(0.2f);
            bool invalidRejected = PoseNetwork.Instance.AcceptedPosePackets == before + 1;
            if (accepted && duplicateRejected && invalidRejected)
                Logger.LogInfo("SMOKE PASS: actual Mirror local-host pose message accepted; duplicate and invalid packets rejected");
            else Logger.LogError("SMOKE FAIL: pose host accepted=" + accepted + "; duplicate rejected=" + duplicateRejected + "; invalid rejected=" + invalidRejected);
        }

        private void TestPoseProtocol()
        {
            PoseProtocol.Register();
            var pose = new PoseFrame
            {
                PlayerId = 123, Sequence = uint.MaxValue, Tracked = 7,
                Head = new Vector3(0, 1.6f, 0), HeadRotation = Quaternion.Euler(0, 30, 0),
                Left = new Vector3(-0.3f, 1.1f, 0.3f), LeftRotation = Quaternion.identity,
                Right = new Vector3(0.3f, 1.1f, 0.3f), RightRotation = Quaternion.identity
            };
            var writer = new NetworkWriter();
            Writer<PoseFrame>.write(writer, pose);
            byte[] bytes = writer.ToArray();
            Check(bytes.Length == 93, "Unexpected pose wire size: " + bytes.Length);
            var decoded = Reader<PoseFrame>.read(new NetworkReader(bytes));
            Check(decoded.PlayerId == pose.PlayerId && decoded.Sequence == pose.Sequence && decoded.Tracked == 7
                && decoded.Left == pose.Left && Quaternion.Angle(decoded.HeadRotation, pose.HeadRotation) < 0.01f, "Pose round-trip changed data");
            Check(PoseProtocol.Valid(decoded), "Valid pose rejected");
            decoded.Head.x = float.NaN; Check(!PoseProtocol.Valid(decoded), "NaN pose accepted");
            decoded = pose; decoded.Left = Vector3.one * 20; Check(!PoseProtocol.Valid(decoded), "Out-of-range hand accepted");
            decoded = pose; decoded.HeadRotation = new Quaternion(0, 0, 0, 0); Check(!PoseProtocol.Valid(decoded), "Zero quaternion accepted");
            decoded = pose; decoded.Tracked = 128; Check(!PoseProtocol.Valid(decoded), "Unknown flags accepted");
            Check(PoseProtocol.Newer(0, uint.MaxValue) && !PoseProtocol.Newer(uint.MaxValue, 0) && !PoseProtocol.Newer(4, 4), "Sequence wrap handling failed");

            var root = Own(new GameObject("SWYF synthetic remote avatar"));
            Transform upper = new GameObject("mixamorig:LeftArm").transform;
            upper.SetParent(root.transform, false); upper.localPosition = new Vector3(-0.2f, 1.3f, 0);
            Transform lower = new GameObject("mixamorig:LeftForeArm").transform;
            lower.SetParent(upper, false); lower.localPosition = new Vector3(0, -0.3f, 0);
            Transform hand = new GameObject("mixamorig:LeftHand").transform;
            hand.SetParent(lower, false); hand.localPosition = new Vector3(0, -0.3f, 0);
            Vector3 oldPosition = hand.position;
            // Match the reflected native type name without depending on the game's assembly.
            var nativeAnimator = root.AddComponent<ProceduralArmAnimator>();
            var voice = new GameObject("synthetic native voice mouth").transform;
            voice.SetParent(root.transform, false); voice.localPosition = new Vector3(0, .8f, .2f);
            Vector3 nativeVoice = voice.localPosition;
            root.AddComponent<PlayerModeManager>().voiceChatMouthAudioSource = voice;
            var avatar = root.AddComponent<RemotePoseAvatar>();
            avatar.Receive(pose);
            typeof(RemotePoseAvatar).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(avatar, null);
            Check(Vector3.Distance(hand.position, root.transform.TransformPoint(pose.Left)) < 0.005f, "Remote arm did not reach tracked hand");
            Check(!nativeAnimator.enabled, "Native arm animation still owns a tracked remote arm");
            Check(Vector3.Distance(voice.position, root.transform.TransformPoint(pose.Head) + root.transform.rotation * pose.HeadRotation * new Vector3(0, -.1f, .05f)) < .001f,
                "Remote voice did not follow transmitted headset position/rotation");
            avatar.enabled = false;
            Check(Vector3.Distance(hand.position, oldPosition) < 0.005f, "Remote arm failed to restore native pose");
            Check(nativeAnimator.enabled, "Native arm animation was not restored on disable");
            Check(Vector3.Distance(voice.localPosition, nativeVoice) < .001f, "Remote voice mouth did not restore on disable");
            avatar.enabled = true;
            typeof(RemotePoseAvatar).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(avatar, null);
            typeof(RemotePoseAvatar).GetField("lastReceived", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(avatar, Time.unscaledTime - 1);
            typeof(RemotePoseAvatar).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(avatar, null);
            Check(nativeAnimator.enabled && Vector3.Distance(hand.position, oldPosition) < 0.005f, "Tracking timeout did not restore native ownership");

            foreach (var finger in new[] { ("Middle1", new Vector3(0, 0, .12f)), ("Index1", new Vector3(.04f, 0, .11f)), ("Pinky1", new Vector3(-.04f, 0, .1f)) })
            {
                var bone = new GameObject(hand.name + finger.Item1).transform;
                bone.SetParent(hand, false); bone.localPosition = finger.Item2;
            }
            Check(AvatarArmCalibration.GripCalibration(root.transform, hand, lower, out var alignment, out var gripOffset, out var lowerPalm),
                "Finger geometry did not produce a palm calibration");
            Check(gripOffset.magnitude > .03f && gripOffset.magnitude < .09f, "Grip target did not account for palm-to-wrist distance");
            Vector3 wristBeforeTwist = hand.position;
            Quaternion lowerBeforeTwist = lower.rotation;
            AvatarArmCalibration.ForearmTwist(lower, hand, Quaternion.Inverse(lower.rotation) * Vector3.forward, Vector3.right);
            Check(Vector3.Distance(wristBeforeTwist, hand.position) < .001f && Quaternion.Angle(lowerBeforeTwist, lower.rotation) > 20,
                "Forearm twist failed to distribute rotation without moving the tracked wrist");
            foreach (bool left in new[] { true, false })
            {
                var mitten = new GameObject("mixamorig:" + (left ? "Left" : "Right") + "Hand").transform;
                mitten.SetParent(lower, false);
                mitten.localRotation = Quaternion.Euler(81, 20, 170);
                var index = new GameObject(mitten.name + "Index1").transform;
                index.SetParent(mitten, false); index.localPosition = new Vector3(0, .12f, 0);
                Check(AvatarArmCalibration.GripCalibration(root.transform, mitten, lower, out var mittenAlignment, out var mittenOffset, out _),
                    "Shipped index-only mitten skeleton did not calibrate");
                Quaternion grip = Quaternion.Euler(25, 70, -30);
                mitten.rotation = grip * mittenAlignment;
                Check(Vector3.Dot((index.position - mitten.position).normalized, grip * Vector3.forward) > .999f,
                    "Mitten finger direction twisted away from the controller grip");
                Check(Vector3.Dot(mitten.right * (left ? 1 : -1), grip * Vector3.up) > .999f && mittenOffset.magnitude > .04f,
                    "Mitten handedness or wrist-to-palm offset is incorrect");
            }
            Transform wrapper = new GameObject("unexpected wrist helper").transform;
            wrapper.SetParent(lower, false);
            hand.SetParent(wrapper, true);
            Check(!AvatarArmCalibration.TryArmBones(root.transform, hand, out _, out _), "Unrecognized helper hierarchy was treated as a forearm");
        }
    }

    public sealed class ProceduralArmAnimator : MonoBehaviour { }
    public sealed class PlayerModeManager : MonoBehaviour { public Transform voiceChatMouthAudioSource; }
}
