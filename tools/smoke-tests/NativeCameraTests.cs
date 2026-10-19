using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static string cameraFixtureFolder;
        private static object cameraFixtureOwner;
        private static Color32[] cameraFixturePixels;
        private static bool CameraFixtureFolder(ref string __result)
        { if (cameraFixtureFolder == null) return true; __result = cameraFixtureFolder; return false; }
        private static bool CameraFixtureClipboard(Action<bool> completed, ref IEnumerator __result)
        { __result = SkipCameraFixtureClipboard(completed); return false; }
        private static IEnumerator SkipCameraFixtureClipboard(Action<bool> completed)
        { completed(false); yield break; }
        private static void ObserveCameraCapture(object __instance, Texture2D __result)
        { if (ReferenceEquals(__instance, cameraFixtureOwner) && __result != null) cameraFixturePixels = __result.GetPixels32(); }

        private IEnumerator TestNativeCamera(object os, VRPanelSurface surface)
        {
            var apps = Member(os, "m_appsById") as IDictionary;
            object app = apps?["camera"];
            Require(app != null, "Native camera app missing");
            string originalState = Member(app, "State")?.ToString();
            AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
            FieldInfo field = AccessTools.Field(os.GetType(), "m_cameraApp");
            object original = field.GetValue(os);
            var content = Member(app, "WindowContent") as VisualElement;
            Require(original != null && content != null, "Native camera content missing");
            var originalChildren = content.Children().ToArray();
            string tempRoot = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string folder = Path.Combine(tempRoot, "swyf-camera-fixture-" + Guid.NewGuid().ToString("N"));
            var probe = new Harmony("com.swyf.vr.camerafixture");
            object camera = null;
            try
            {
                Directory.CreateDirectory(folder); cameraFixtureFolder = folder;
                Type type = original.GetType();
                probe.Patch(AccessTools.Method(type, "GetCameraFolderPath"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(CameraFixtureFolder)));
                // Native photo finalization normally changes the Windows clipboard.
                // Keep that external side effect outside this isolated fixture.
                probe.Patch(AccessTools.Method(type, "CopyPictureToClipboard"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(CameraFixtureClipboard)));
                probe.Patch(AccessTools.Method(type, "CaptureCurrentFrame"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveCameraCapture)));
                var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 6);
                camera = ctor.Invoke(new object[] { Member(os, "m_assets"), Member(os, "m_root"), os, Member(os, "m_notifications"), Member(os, "m_uiAudio"),
                    new Action(() => AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "camera" })) });
                cameraFixtureOwner = camera; field.SetValue(os, camera);
                AccessTools.Method(type, "BuildContent").Invoke(camera, new[] { app });
                AccessTools.Field(type, "m_watermarkEnabled").SetValue(camera, false);
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "camera" });
                yield return null; yield return null;
                var click = ClickAppControl(surface, Member(camera, "m_toolsToggle") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                yield return new WaitForSecondsRealtime(.5f);
                var preview = Member(camera, "m_preview") as Image;
                var texture = Member(camera, "m_renderTexture") as RenderTexture;
                var source = Member(Member(Instance("KolkataOsSingleton"), "boundWorkspace"), "zoomyWebcamCamera") as UnityEngine.Camera;
                var capture = Member(camera, "m_camera") as UnityEngine.Camera;
                Require(preview?.image == texture && texture != null && source != null && capture != null
                    && capture != VRManager.Instance.Rig.Head && capture.targetTexture == texture
                    && Vector3.Distance(capture.transform.position, source.transform.position) < .01f
                    && Quaternion.Angle(capture.transform.rotation, source.transform.rotation) < .1f
                    && VRManager.Instance.Rig.Head.targetTexture == null, "Native camera fixture did not use the desk webcam independently of the headset");
                click = ClickAppControl(surface, Member(camera, "m_captureButton") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                float deadline = Time.realtimeSinceStartup + 15;
                while (Member(camera, "m_selectedPhotoPath") == null || Member(camera, "m_pictureWatermarkRoutine") != null)
                { Require(Time.realtimeSinceStartup < deadline, "Controller capture did not finish and select its native photo"); yield return null; }
                string saved = Member(camera, "m_selectedPhotoPath") as string;
                Require(Path.GetFullPath(saved).StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(saved) && cameraFixturePixels != null, "Native camera did not capture inside its isolated picture folder");
                var decoded = new Texture2D(2, 2);
                try
                {
                    Require(decoded.LoadImage(File.ReadAllBytes(saved)) && decoded.width == texture.width && decoded.height == texture.height,
                        "Saved camera picture is invalid or has the wrong dimensions");
                    Color32[] pixels = decoded.GetPixels32();
                    Require(pixels.Length == cameraFixturePixels.Length, "Native camera picture has the wrong pixel count");
                    int variation = 0;
                    for (int i = 0; i < pixels.Length; i += Math.Max(1, pixels.Length / 1024))
                    {
                        Color32 actual = pixels[i], expected = cameraFixturePixels[i];
                        Require(actual.r == expected.r && actual.g == expected.g && actual.b == expected.b, "Saved camera picture differs from its captured frame");
                        if (actual.r != pixels[0].r || actual.g != pixels[0].g || actual.b != pixels[0].b) variation++;
                    }
                    Require(variation > 10, "Native webcam photo is blank or uniform");
                }
                finally { Destroy(decoded); }
                Require(Member(camera, "m_showingLivePreview") is bool live && !live && preview.image is Texture2D,
                    "Captured photo did not appear in the native preview");
                Logger.LogInfo("CAMERA APP PASS: controller shutter saves matching nonblank webcam pixels and selects the native photo without redirecting the headset");
                click = ClickAppControl(surface, Member(camera, "m_liveButton") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(camera, "m_showingLivePreview") is bool resumed && resumed && preview.image == texture,
                    "Controller Live button did not restore the webcam preview");
                var gallery = Member(camera, "m_galleryItems") as IDictionary;
                Require(gallery != null && gallery.Contains(saved), "Captured photo is missing from its native gallery");
                click = ClickAppControl(surface, Member(gallery[saved], "Button") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(camera, "m_selectedPhotoPath") as string == saved, "Controller gallery selection did not reopen its native photo");
                Logger.LogInfo("CAMERA APP PASS: controller Live and gallery buttons switch between the webcam and saved photo");
                click = ClickAppControl(surface, Member(camera, "m_deleteButton") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(!File.Exists(saved) && (Member(camera, "m_galleryItems") as IDictionary)?.Count == 0
                    && Member(camera, "m_showingLivePreview") is bool afterDelete && afterDelete,
                    "Controller delete did not remove only the fixture photo and return to Live");
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                yield return null; yield return null;
                Require(!capture.enabled && capture.targetTexture == null && VRManager.Instance.Rig.Head.targetTexture == null,
                    "Closing the camera left the preview rendering or redirected the headset");
                Logger.LogInfo("CAMERA APP PASS: controller delete removes the isolated photo; closing the app stops the preview render");
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel(); holdInjectedTrigger = false;
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                (camera as IDisposable)?.Dispose(); field.SetValue(os, original);
                content.Clear(); foreach (VisualElement child in originalChildren) content.Add(child);
                probe.UnpatchSelf(); cameraFixtureOwner = null; cameraFixturePixels = null; cameraFixtureFolder = null;
                if (originalState != "Closed") AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "camera" });
                if (originalState == "OpenMinimized") AccessTools.Method(os.GetType(), "MinimizeApp").Invoke(os, new[] { app });
                string resolved = Path.GetFullPath(folder);
                if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("swyf-camera-fixture-"))
                    try { if (Directory.Exists(resolved)) Directory.Delete(resolved, true); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    { Logger.LogInfo("Native camera fixture folder is still closing; retained its temporary directory."); }
            }
        }
    }
}
