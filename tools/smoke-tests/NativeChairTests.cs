using System;
using System.Collections;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestNativeChair()
        {
            var manager = VRManager.Instance;
            Component interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            object controller = AccessTools.Property(adapter, "Controller").GetValue(null);
            var body = Member(controller, "CharacterController") as CharacterController;
            Require(body != null && body.enabled && !(bool)Member(interactor, "isSitting"), "Chair fixture requires a standing local player");
            Type chairType = Find("ChairInteractable");
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            var tick = AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches"), "BeforeController");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            Vector3 originalPosition = interactor.transform.position;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated chair floor";
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - originalPosition.y - .12f, 1050);
            floor.transform.localScale = new Vector3(24, .2f, 24);
            var fixture = new GameObject("SWYF owned native chair");
            fixture.SetActive(false);
            var sit = new GameObject("sit point").transform; sit.SetParent(fixture.transform);
            var exit = new GameObject("exit point").transform; exit.SetParent(fixture.transform);
            sit.position = new Vector3(1050, 50, 1050); exit.position = sit.position + Vector3.right * 2;
            fixture.AddComponent<NetworkIdentity>();
            Component chair = fixture.AddComponent(chairType);
            AccessTools.Field(chairType, "sitPoint").SetValue(chair, sit);
            AccessTools.Field(chairType, "exitPoint").SetValue(chair, exit);
            bool paused = false;
            void DriveJump(bool held)
            {
                jumpEdge = held && !jumpHeld; jumpHeld = held;
                KeepJumpInput(manager.Input); tick.Invoke(null, new[] { controller });
                jumpEdge = false; AccessTools.Property(manager.Input.GetType(), "JumpDown").SetValue(manager.Input, false);
            }
            bool Sitting() => Member(interactor, "isSitting") is bool seated && seated;
            try
            {
                jumpFixture = true; manager.Pointer.Cancel();
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                fixture.SetActive(true); NetworkServer.Spawn(fixture); Physics.SyncTransforms();
                teleport.Invoke(controller, new object[] { sit.position + Vector3.left * 2, true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(1.2f);
                yield return new WaitForEndOfFrame();
                Vector3 standingOffset = manager.Rig.Head.transform.position - interactor.transform.position;
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    DriveJump(false);
                    AccessTools.Method(chairType, "CmdInteract").Invoke(chair, new object[] { interactor });
                    float deadline = Time.realtimeSinceStartup + 5;
                    while (!Sitting()) { Require(Time.realtimeSinceStartup < deadline, "Native chair command did not seat local player"); yield return null; }
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return new WaitForEndOfFrame();
                    Require(Vector3.Distance(interactor.transform.position, sit.position) < .02f, "Native sitter left its authored seat");
                    Vector3 headBefore = manager.Rig.Head.transform.position, bodyBefore = interactor.transform.position;
                    SetMockHeadPose(new Vector3(.3f, 1.4f, 0), Quaternion.Euler(0, 40, 0));
                    for (int frame = 0; frame < 20; frame++) yield return null;
                    yield return new WaitForEndOfFrame();
                    Vector3 expected = manager.Rig.Origin.right * .3f + Vector3.down * .25f;
                    Require(Vector3.Distance(manager.Rig.Head.transform.position - headBefore, expected) < .05f,
                        "Seated room movement changed or doubled the headset displacement");
                    Require(Vector3.Distance(interactor.transform.position, bodyBefore) < .02f, "Room-scale motion moved the native seated body");
                    Vector3 settled = manager.Rig.Head.transform.position;
                    for (int frame = 0; frame < 20; frame++) yield return null;
                    Require(Vector3.Distance(manager.Rig.Head.transform.position, settled) < .02f, "Stationary seated head drifted");
                    Logger.LogInfo("CHAIR TRACKING PASS: native seated body stays anchored while 30cm lean, 25cm crouch and head rotation retain stable tracking; cycle=" + cycle);
                    manager.Rig.Recenter();
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return new WaitForEndOfFrame();
                    Require(Vector3.Distance(manager.Rig.Head.transform.position, manager.Rig.AuthoredEye) < .02f,
                        "Seated recenter retained the previous physical lean/crouch offset");
                    Vector3 beforeChairMove = manager.Rig.Head.transform.position;
                    Vector3 chairStep = new Vector3(.2f, .1f, -.1f);
                    fixture.transform.position += chairStep;
                    for (int frame = 0; frame < 20; frame++) yield return null;
                    yield return new WaitForEndOfFrame();
                    Require(Vector3.Distance(manager.Rig.Head.transform.position - beforeChairMove, chairStep) < .02f
                        && Vector3.Distance(interactor.transform.position, sit.position) < .02f,
                        "Seated body/head failed to follow the moving native chair; headStep=" + (manager.Rig.Head.transform.position - beforeChairMove).ToString("F3")
                        + "; expected=" + chairStep.ToString("F3") + "; body=" + interactor.transform.position.ToString("F3") + "; seat=" + sit.position.ToString("F3"));
                    Logger.LogInfo("CHAIR RECENTER PASS: seated recenter clears lean/crouch while a moving native seat carries the body and headset once; cycle=" + cycle);
                    toggle.Invoke(null, null); paused = true; DriveJump(true);
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(Sitting(), "Settings allowed a controller jump to exit the chair");
                    toggle.Invoke(null, null); paused = false; DriveJump(true);
                    yield return new WaitForSecondsRealtime(.2f);
                    Require(Sitting(), "Closing settings replayed a held jump into chair exit");
                    SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                    DriveJump(false); DriveJump(true);
                    deadline = Time.realtimeSinceStartup + 5;
                    while (Sitting()) { Require(Time.realtimeSinceStartup < deadline, "Fresh VR jump failed to request native chair exit"); yield return null; }
                    DriveJump(false);
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return new WaitForEndOfFrame();
                    Require(body.enabled && Vector3.Distance(Vector3.ProjectOnPlane(interactor.transform.position - exit.position, Vector3.up), Vector3.zero) < .1f,
                        "Native chair exit failed to restore body/exit position");
                    Vector3 returnedOffset = manager.Rig.Head.transform.position - interactor.transform.position;
                    Logger.LogInfo("CHAIR EYE: before=" + standingOffset.ToString("F3") + "; after=" + returnedOffset.ToString("F3")
                        + "; authored=" + manager.Rig.AuthoredEye.ToString("F3") + "; player=" + interactor.transform.position.ToString("F3")
                        + "; nativeEye=" + (Member(controller, "defaultCamTransform") as Transform)?.position.ToString("F3"));
                    Require(Vector3.Distance(returnedOffset, standingOffset) < .05f,
                        "Chair exit failed to restore standing eye offset; before=" + standingOffset.ToString("F3") + "; after=" + returnedOffset.ToString("F3"));
                    Logger.LogInfo("CHAIR EXIT PASS: settings cancel jump ownership, held jump stays blocked and fresh VR jump restores native standing body/eye alignment; cycle=" + cycle);
                }
                Logger.LogInfo("CHAIR COMPLETE PASS: three native sit/lean/recenter/move/menu/exit cycles completed");
            }
            finally
            {
                if (paused) toggle.Invoke(null, null);
                DriveJump(false); jumpFixture = false;
                AccessTools.Method(chairType, "ServerClearSitter").Invoke(null, new object[] { interactor });
                if (fixture != null) NetworkServer.Destroy(fixture);
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter();
                Destroy(floor);
            }
        }
    }
}
