using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestNativeRigMotion()
        {
            var manager = VRManager.Instance;
            var player = Mirror.NetworkClient.localPlayer.transform;
            object controller = Find("Metater.MetaPlayerController").GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            var body = Member(controller, "CharacterController") as CharacterController;
            Require(body != null && body.enabled, "Native body missing for integrated room movement");
            Vector3 original = player.position;
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var polar = adapter.GetProperty("Polar", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as Transform;
            Quaternion originalFacing = polar.rotation;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated native rig test floor";
            float foot = body.bounds.min.y - player.position.y;
            floor.transform.position = new Vector3(1000, 50 + foot - .12f, 1000);
            floor.transform.localScale = new Vector3(20, .2f, 20);
            try
            {
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                Physics.SyncTransforms();
                teleport.Invoke(controller, new object[] { new Vector3(1000, 50, 1000), true });
                manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(1.2f);
                yield return new WaitForEndOfFrame();
                Vector3 headBefore = manager.Rig.Head.transform.position;
                float yawBefore = manager.Rig.Head.transform.eulerAngles.y;
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.Euler(0, 45, 0));
                for (int i = 0; i < 20; i++) yield return null;
                yield return new WaitForEndOfFrame();
                float turned = Mathf.Abs(Mathf.DeltaAngle(yawBefore, manager.Rig.Head.transform.eulerAngles.y));
                Require(Mathf.Abs(turned - 45) < 1, "Native view rotation fed body yaw back into the headset: " + turned);
                Require(Mathf.Abs(Mathf.DeltaAngle(polar.eulerAngles.y, manager.Rig.Head.transform.eulerAngles.y)) < 1, "Native body did not follow headset heading");
                Require(Vector3.ProjectOnPlane(manager.Rig.Head.transform.position - headBefore, Vector3.up).magnitude < .04f, "Turning the head moved the native view sideways");
                Logger.LogInfo("RIG MOTION PASS: native body follows 45-degree head turn without repeated view rotation or sideways camera orbit");
                Vector3 beforeStep = manager.Rig.Head.transform.position, bodyBefore = body.transform.position;
                Vector3 expected = manager.Rig.Origin.right * .3f;
                SetMockHeadPose(new Vector3(.3f, 1.65f, 0), Quaternion.Euler(0, 45, 0));
                for (int i = 0; i < 20; i++) yield return null;
                yield return new WaitForEndOfFrame();
                Vector3 headStep = Vector3.ProjectOnPlane(manager.Rig.Head.transform.position - beforeStep, Vector3.up);
                float bodyStep = Vector3.ProjectOnPlane(body.transform.position - bodyBefore, Vector3.up).magnitude;
                Require(Vector3.Distance(headStep, expected) < .04f, "Native room step changed or doubled camera displacement: " + headStep + "/" + expected);
                Require(Mathf.Abs(bodyStep - .22f) < .04f, "Native body did not consume room movement with the lean allowance: " + bodyStep);
                Vector3 settled = manager.Rig.Head.transform.position;
                for (int i = 0; i < 20; i++) yield return null;
                Require(Vector3.ProjectOnPlane(manager.Rig.Head.transform.position - settled, Vector3.up).magnitude < .02f, "Stationary room pose continues to drift");
                Logger.LogInfo("RIG MOTION PASS: native 30cm room step, 8cm lean allowance and stationary drift check");
            }
            finally
            {
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                teleport.Invoke(controller, new object[] { original, true }); polar.rotation = originalFacing;
                manager.Rig.Recenter(); Destroy(floor);
            }
        }
    }
}
