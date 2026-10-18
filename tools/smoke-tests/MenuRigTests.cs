using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestMenuRig()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            object ReadStatic(string type, string name) => adapter.GetMethod("Static", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { type, name });
            float until = Time.realtimeSinceStartup + 25;
            while (VRManager.Instance?.Ready != true || VRManager.Instance.Rig.Head == null
                || !(ReadStatic("MainMenuSingleton", "IsGameUiForceDisabled") is bool menu && menu)
                || ReadStatic("Metater.MetaPlayer", "LocalPlayerInstance") == null)
            {
                Check(Time.realtimeSinceStartup < until, "Native main-menu demo did not initialize");
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            var manager = VRManager.Instance;
            var mockHarness = FindFirstObjectByType<LocalPairHarness>();
            Check(mockHarness != null, "Single-client mock pose helper missing");
            typeof(LocalPairHarness).GetMethod("InitializeMockPose", flags).Invoke(mockHarness, null);
            yield return null;
            yield return new WaitForEndOfFrame();
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running || display.GetRenderPassCount() == 0) continue;
                display.GetRenderPass(0, out var pass);
                pass.GetRenderParameter(manager.Rig.Head, 0, out var parameter);
                Vector3 eye = parameter.view.inverse.MultiplyPoint3x4(Vector3.zero);
                Logger.LogInfo("RIG RENDER: transform=" + manager.Rig.Head.transform.position + "; renderedEye=" + eye
                    + "; delta=" + (eye - manager.Rig.Head.transform.position));
                Check(Vector3.Distance(eye, manager.Rig.Head.transform.position) < .1f,
                    "Rendered headset view applies the tracked head pose twice");
            }
            Check(manager.Rig.GetType().GetField("player", flags).GetValue(manager.Rig) == null,
                "Menu tracking origin attached to the native demo player");
            Check(!manager.Rig.HeadObstructed, "Menu background geometry disabled menu input");
            var demo = ReadStatic("Metater.MetaPlayer", "LocalPlayerInstance") as Component;
            Logger.LogInfo("RIG ROOT: " + demo.transform.position + "; scale=" + demo.transform.lossyScale);
            foreach (Renderer renderer in demo.GetComponentsInChildren<Renderer>(true))
                if (renderer.gameObject.activeInHierarchy && (renderer.name.Contains("Head") || renderer.name.Contains("Eye") || renderer.name.Contains("eye")))
                    Logger.LogInfo("RIG HEAD MESH: " + renderer.name + "; bounds=" + renderer.bounds + "; enabled=" + renderer.enabled);
            Type eyeType = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Camera.VRAvatarEyeAnchor");
            object eyeAnchor = Activator.CreateInstance(eyeType, true);
            object[] eyeArguments = { demo.transform, Vector3.zero };
            Check((bool)eyeType.GetMethod("TryPosition", flags).Invoke(eyeAnchor, eyeArguments), "Visible native eye mesh was not found");
            Logger.LogInfo("RIG VISIBLE EYES: " + eyeArguments[1]);
            int calibratedHands = 0;
            foreach (Transform hand in demo.GetComponentsInChildren<Transform>(true))
            {
                if (!hand.gameObject.activeInHierarchy || (hand.name != "mixamorig:LeftHand" && hand.name != "mixamorig:RightHand")) continue;
                Check(Multiplayer.AvatarArmCalibration.TryArmBones(demo.transform, hand, out _, out var lower), "Native arm chain not found");
                Check(Multiplayer.AvatarArmCalibration.GripCalibration(demo.transform, hand, lower, out _, out _, out _), "Native mitten grip did not calibrate");
                calibratedHands++;
            }
            Check(calibratedHands == 2, "Expected two calibrated active native hands, got " + calibratedHands);
            foreach (Transform bone in demo.GetComponentsInChildren<Transform>(true))
                if ((bone.name.Contains("Hand") || bone.name.Contains("Head") || bone.name.Contains("Cam")) && !bone.name.Contains("_end") && !bone.name.Contains("_End"))
                    Logger.LogInfo("RIG GEOMETRY: " + bone.name + "; parent=" + bone.parent?.name + "; local=" + bone.localPosition
                        + "; world=" + bone.position + "; active=" + bone.gameObject.activeInHierarchy + "; rotation=" + bone.rotation.eulerAngles);
            object settingsUi = ReadStatic("SettingsUi", "Instance");
            object cursor = ReadStatic("MetaCursorNetDebug", "Instance");
            MethodInfo read = adapter.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo write = adapter.GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic);
            bool oldOpen = (bool)read.Invoke(null, new object[] { settingsUi, "IsOpen" });
            bool oldEscape = (bool)read.Invoke(null, new object[] { cursor, "escapeMenuOpen" });
            try
            {
                write.Invoke(null, new object[] { settingsUi, "IsOpen", false });
                write.Invoke(null, new object[] { cursor, "escapeMenuOpen", false });
                adapter.GetMethod("TogglePause", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                yield return null;
                yield return new WaitForEndOfFrame();
                Check(read.Invoke(null, new object[] { settingsUi, "IsOpen" }) is bool open && open,
                    "Native cursor Update closed the VR pause menu");
                adapter.GetMethod("TogglePause", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                yield return null;
                Check(read.Invoke(null, new object[] { settingsUi, "IsOpen" }) is bool closed && !closed,
                    "VR pause menu did not stay closed");
            }
            finally
            {
                write.Invoke(null, new object[] { cursor, "escapeMenuOpen", oldEscape });
                write.Invoke(null, new object[] { settingsUi, "IsOpen", oldOpen });
            }
            var input = manager.Input;
            bool oldHead = input.HeadTracked, oldFocus = input.HasInputFocus, oldHand = manager.Rig.Right.IsTracked;
            Vector3 oldPosition = manager.Rig.Right.Aim.localPosition;
            Quaternion oldRotation = manager.Rig.Right.Aim.localRotation;
            try
            {
                input.GetType().GetProperty("HeadTracked").SetValue(input, true);
                input.GetType().GetProperty("HasInputFocus").SetValue(input, true);
                manager.Rig.Right.GetType().GetProperty("IsTracked").SetValue(manager.Rig.Right, true);
                manager.Rig.Right.Aim.SetPositionAndRotation(Vector3.one * 10000, Quaternion.identity);
                typeof(VRPointer).GetMethod("LateUpdate", flags).Invoke(manager.Pointer, null);
                var line = manager.Pointer.GetComponent<LineRenderer>();
                Check(!manager.Pointer.HasPanelHit && line.enabled, "Missing a UI target hid the controller beam");
                Check(Vector3.Distance(line.GetPosition(0), manager.Rig.Right.Aim.position) < .001f,
                    "Beam origin detached from the controller aim transform");
                Check(manager.Pointer.gameObject.layer == 5 && line.sharedMaterial.GetInt("unity_GUIZTestMode") == 8,
                    "Menu beam is culled or depth-occluded by the background scene");
            }
            finally
            {
                input.GetType().GetProperty("HeadTracked").SetValue(input, oldHead);
                input.GetType().GetProperty("HasInputFocus").SetValue(input, oldFocus);
                manager.Rig.Right.GetType().GetProperty("IsTracked").SetValue(manager.Rig.Right, oldHand);
                manager.Rig.Right.Aim.SetLocalPositionAndRotation(oldPosition, oldRotation);
                manager.Pointer.Cancel();
                typeof(VRPointer).GetMethod("Hide", flags).Invoke(manager.Pointer, null);
            }
        }
    }
}
