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
        public sealed class RecordingFrameMarker : MonoBehaviour
        {
            public Color Colour = Color.red;
            private Material material;
            private Mesh mesh;
            private void Awake()
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Recording fixture colour"; quad.layer = 5; quad.transform.SetParent(transform, false);
                Destroy(quad.GetComponent<Collider>());
                var filter = quad.GetComponent<MeshFilter>();
                mesh = Instantiate(filter.sharedMesh); mesh.colors = Enumerable.Repeat(Color.white, mesh.vertexCount).ToArray();
                filter.sharedMesh = mesh;
                material = new Material(Shader.Find("UI/Default"));
                material.renderQueue = 5000; material.SetInt("unity_GUIZTestMode", 8); material.color = Colour;
                var renderer = quad.GetComponent<Renderer>(); renderer.sharedMaterial = material; renderer.sortingOrder = 32760;
            }
            private void LateUpdate() { if (material != null) material.color = Colour; }
            private void OnDestroy() { if (material != null) Destroy(material); if (mesh != null) Destroy(mesh); }
        }
        private static Color RecordingCentre(RenderTexture texture)
        {
            RenderTexture active = RenderTexture.active; var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                pixel.ReadPixels(new Rect(texture.width / 2, texture.height / 2, 1, 1), 0, 0); pixel.Apply();
                return pixel.GetPixel(0, 0);
            }
            finally { RenderTexture.active = active; Destroy(pixel); }
        }
        private static string recordingFixtureFolder;
        private static object recordingFixtureOriginalApp;
        private static bool RecordingFixtureFolder(ref string __result)
        { if (recordingFixtureFolder == null) return true; __result = recordingFixtureFolder; return false; }
        private static bool RecordingFixturePath(ref string __result)
        { if (recordingFixtureFolder == null) return true; __result = Path.Combine(recordingFixtureFolder, "controller-test.webm"); return false; }
        private static bool RecordingFixtureNoClipboard(ref bool __result) { __result = false; return false; }
        private static bool RecordingFixtureNoWatermark(ref bool __result) { __result = false; return false; }
        private static bool RecordingFixtureKeepOriginalDetached(object __instance) => !ReferenceEquals(__instance, recordingFixtureOriginalApp);

        private IEnumerator TestRecordingSeek(object fixture, object playback, VRPanelSurface surface)
        {
            var manager = VRManager.Instance;
            var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
            var slider = Member(fixture, "m_playbackSeek") as Slider;
            var dragger = slider?.Q<VisualElement>(className: "unity-base-slider__dragger");
            var track = slider?.Q<VisualElement>(className: "unity-base-slider__tracker");
            Require(slider?.enabledInHierarchy == true && dragger?.panel != null && track?.worldBound.width > 10,
                "Native playback seek slider is not ready");
            bool previousFocus = manager.Input.HasInputFocus;
            bool previousReady = manager.Ready;
            bool wasPlaying = Convert.ToBoolean(Member(playback, "IsPlaying"));
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Press()
            {
                appFixturePoint = dragger.worldBound.center;
                holdInjectedTrigger = true; Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                var recipient = Member(manager.Pointer, "pressedElement") as VisualElement;
                Require(Member(fixture, "m_seekingPlayback") is bool seeking && seeking && recipient != null
                    && (recipient == slider || slider.Contains(recipient)), "Controller did not start native seek dragging; seeking="
                    + Member(fixture, "m_seekingPlayback") + "; recipient=" + recipient + "; classes="
                    + (recipient == null ? "none" : string.Join(",", recipient.GetClasses()))
                    + "; owns=" + (recipient != null && (recipient == slider || slider.Contains(recipient))));
            }
            try
            {
                appFixtureSurface = surface; appFixtureAim = true;
                Press();
                float fraction = wasPlaying ? .4f : .8f;
                appFixturePoint = new Vector2(track.worldBound.xMin + track.worldBound.width * fraction, track.worldBound.center.y);
                yield return null; yield return new WaitForEndOfFrame(); tick.Invoke(manager.Pointer, null);
                float target = slider.value;
                Require(Mathf.Abs(target - fraction) < .15f, "Controller seek drag did not move the native playhead to its target range");
                yield return new WaitForSecondsRealtime(.35f);
                Require(Member(fixture, "m_seekingPlayback") is bool holding && holding && Mathf.Abs(slider.value - target) < .01f,
                    "Native playback refresh overwrote the held controller seek position");
                double expectedTime = target * Convert.ToDouble(Member(playback, "Duration"));
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerUp", true);
                tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                float deadline = Time.realtimeSinceStartup + 5;
                while (true)
                {
                    var texture = (Member(fixture, "m_preview") as Image)?.image as RenderTexture;
                    Require(texture != null, "Seek lost its native playback texture");
                    Color frame = RecordingCentre(texture);
                    bool correctFrame = wasPlaying ? frame.g > frame.r + .35f && frame.g > frame.b + .35f
                        : frame.b > frame.r + .35f && frame.b > frame.g + .35f;
                    if (correctFrame && (wasPlaying || Math.Abs(Convert.ToDouble(Member(playback, "CurrentTime")) - expectedTime) < .2)) break;
                    Require(Time.realtimeSinceStartup < deadline, "Native video frame did not follow controller seek; playing=" + wasPlaying + "; colour=" + frame
                        + "; requestedTime=" + expectedTime + "; currentTime=" + Member(playback, "CurrentTime") + "; duration=" + Member(playback, "Duration"));
                    yield return null;
                }
                Require(Member(fixture, "m_seekingPlayback") is bool released && !released
                    && Member(playback, "IsPlaying") is bool playing && playing == wasPlaying,
                    "Releasing a seek drag left seek ownership active or changed playback state");
                Logger.LogInfo("RECORDING SEEK PASS: controller drag survives native refresh ticks, decodes the expected video segment and preserves playback state; playing=" + wasPlaying);
                yield return null;
                Press(); Input("HasInputFocus", false); tick.Invoke(manager.Pointer, null);
                Require(Member(fixture, "m_seekingPlayback") is bool cancelled && !cancelled
                    && !(bool)Member(manager.Pointer, "toolkitDown")
                    && slider.panel.GetCapturingElement(VRPointer.ToolkitPointerId) == null,
                    "XR focus loss left native seek dragging or pointer capture active");
                Logger.LogInfo("RECORDING SEEK PASS: XR focus loss cancels native seek ownership and clears controller pointer capture");
                Input("HasInputFocus", previousFocus); Input("Trigger", false); Input("TriggerDown", false); holdInjectedTrigger = false;
                yield return null;
                Press();
                AccessTools.Property(typeof(VRManager), "Ready").SetValue(manager, false); tick.Invoke(manager.Pointer, null);
                Require(Member(fixture, "m_seekingPlayback") is bool stopped && !stopped
                    && slider.panel.GetCapturingElement(VRPointer.ToolkitPointerId) == null,
                    "Dropping VR readiness left native seek ownership active");
                Logger.LogInfo("RECORDING SEEK PASS: dropping VR readiness clears native seek ownership and controller capture");
            }
            finally
            {
                holdInjectedTrigger = false; appFixtureAim = false; appFixtureSurface = null;
                AccessTools.Property(typeof(VRManager), "Ready").SetValue(manager, previousReady);
                Input("HasInputFocus", previousFocus); Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                manager.Pointer.Cancel();
            }
        }

        private IEnumerator TestNativeRecording(object os, VRPanelSurface surface)
        {
            var apps = Member(os, "m_appsById") as IDictionary;
            string id = apps.Keys.Cast<string>().Single(key => Member(Member(apps[key], "Definition"), "contentType") as string == "banditcam");
            object app = apps[id];
            string originalState = Member(app, "State")?.ToString();
            object player = AccessTools.Property(Find("KolkataWebRtcPlayer"), "LocalPlayerInstance").GetValue(null);
            Require(player != null && Member(player, "RecordingState")?.ToString() == "Idle"
                && Member(os, "m_boundVictimCall") == null, "Recording fixture requires an idle private workstation");
            var appField = AccessTools.Field(os.GetType(), "m_banditCamApp");
            object originalApp = appField.GetValue(os);
            var content = Member(app, "WindowContent") as VisualElement;
            Require(originalApp != null && content != null, "Native recording app/content missing");
            var children = content.Children().ToArray();
            var recorderField = AccessTools.Field(player.GetType(), "recorder");
            object originalRecorder = recorderField.GetValue(player);
            Require(originalRecorder != null, "Native recorder missing");
            object originalPath = Member(player, "LastRecordingPath"), originalError = Member(player, "LastRecordingError");
            object originalPlayer = Member(originalApp, "m_recordingPlayer");
            string tempRoot = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string folder = Path.Combine(tempRoot, "swyf-recording-fixture-" + Guid.NewGuid().ToString("N"));
            var probe = new Harmony("com.swyf.vr.recordingfixture");
            object fixture = null, recorder = null;
            GameObject markerObject = null;
            try
            {
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                Directory.CreateDirectory(folder); recordingFixtureFolder = folder; recordingFixtureOriginalApp = originalApp;
                Type appType = originalApp.GetType(), recorderType = originalRecorder.GetType();
                probe.Patch(AccessTools.Method(appType, "RefreshRecordingPlayer"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(RecordingFixtureKeepOriginalDetached)));
                AccessTools.Method(appType, "SubscribeToRecordingPlayer").Invoke(originalApp, new object[] { null });
                foreach (var method in recorderType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(method => method.Name == "GetVideosDirectory"))
                    probe.Patch(method, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(RecordingFixtureFolder)));
                probe.Patch(AccessTools.Method(recorderType, "BuildFinalPath"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(RecordingFixturePath)));
                probe.Patch(AccessTools.Method(Find("Kolkata.UI.KolkataOsWebMClipboard"), "Copy"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(RecordingFixtureNoClipboard)));
                probe.Patch(AccessTools.PropertyGetter(Find("KolkataWatermarkSettings"), "BanditCamEnabled"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(RecordingFixtureNoWatermark)));
                // A separate recorder retains the real video pipeline but receives
                // no voice components and zero audio gains. No personal audio is saved.
                var recorderCtor = recorderType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(ctor => ctor.GetParameters().Length == 9);
                recorder = recorderCtor.Invoke(new object[] { player, Member(player, "capture"), null, null, 0f, 0f, 0, 96000, Member(originalRecorder, "stateChanged") });
                recorderField.SetValue(player, recorder);
                var appCtor = appType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(ctor => ctor.GetParameters().Length == 6);
                fixture = appCtor.Invoke(new object[] { Member(os, "m_assets"), Member(os, "m_root"), os, Member(os, "m_banditCamAudioSource"), null,
                    new Action(() => AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id })) });
                object fixturePlayback = Member(fixture, "m_videoPlayer");
                AccessTools.Field(fixturePlayback.GetType(), "m_cacheDirectory").SetValue(fixturePlayback, Path.Combine(folder, "playback"));
                appField.SetValue(os, fixture);
                AccessTools.Method(appType, "BuildContent").Invoke(fixture, new[] { app });
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                yield return null; yield return new WaitForSecondsRealtime(.5f);
                var preview = Member(fixture, "m_preview") as Image;
                Require(Member(fixture, "m_capturePreview") != null && preview?.image is RenderTexture
                    && VRManager.Instance.Rig.Head.targetTexture == null, "Native recording live preview did not acquire game-view capture independently of headset output");
                Logger.LogInfo("RECORDING PASS: native live game-view preview acquires capture without redirecting the headset");
                markerObject = new GameObject("SWYF recording frame marker");
                markerObject.transform.SetParent(VRManager.Instance.Rig.Head.transform, false);
                markerObject.transform.localPosition = Vector3.forward * .4f; markerObject.transform.localScale = Vector3.one * .2f;
                var marker = markerObject.AddComponent<RecordingFrameMarker>();
                float markerDeadline = Time.realtimeSinceStartup + 4;
                while (true)
                {
                    Color sourceFrame = RecordingCentre(preview.image as RenderTexture);
                    if (sourceFrame.r > sourceFrame.g + .35f && sourceFrame.r > sourceFrame.b + .35f) break;
                    Require(Time.realtimeSinceStartup < markerDeadline, "Recording colour fixture did not enter native game-view capture: " + sourceFrame);
                    yield return new WaitForEndOfFrame();
                }
                var click = ClickAppControl(surface, Member(fixture, "m_recordButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                float deadline = Time.realtimeSinceStartup + 25;
                while (Member(player, "RecordingState")?.ToString() != "Recording")
                {
                    Require(Member(player, "RecordingState")?.ToString() != "Failed" && Time.realtimeSinceStartup < deadline,
                        "Controller recording failed to produce a native keyframe: " + Member(player, "LastRecordingError"));
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(1.3f); marker.Colour = Color.green;
                yield return new WaitForSecondsRealtime(1.3f); marker.Colour = Color.blue;
                yield return new WaitForSecondsRealtime(1.3f);
                click = ClickAppControl(surface, Member(fixture, "m_recordButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                deadline = Time.realtimeSinceStartup + 25;
                while (Member(player, "RecordingState")?.ToString() != "Idle")
                {
                    Require(Member(player, "RecordingState")?.ToString() != "Failed" && Time.realtimeSinceStartup < deadline,
                        "Controller stop did not finalize native video: " + Member(player, "LastRecordingError"));
                    yield return null;
                }
                string saved = Member(player, "LastRecordingPath") as string;
                Require(saved == Path.Combine(folder, "controller-test.webm") && File.Exists(saved) && new FileInfo(saved).Length > 1024,
                    "Native video was not finalized in the isolated fixture folder");
                Logger.LogInfo("RECORDING PASS: controller record/stop produces a native WebM through real capture, WebRTC encoding and FFmpeg finalization; audio muted and watermark/clipboard excluded");
                var gallery = Member(fixture, "m_galleryItems") as IDictionary;
                Require(gallery != null && gallery.Contains(saved), "Native saved video did not appear in its gallery");
                click = ClickAppControl(surface, Member(gallery[saved], "Button") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                object playback = Member(fixture, "m_videoPlayer");
                deadline = Time.realtimeSinceStartup + 30;
                while (!(Member(playback, "IsPrepared") is bool prepared && prepared)
                    || !(Member(fixture, "m_playPauseButton") as Button).enabledInHierarchy
                    || !(Member(playback, "IsPlaying") is bool started && started))
                { Require(Time.realtimeSinceStartup < deadline, "Native gallery video did not prepare for playback"); yield return null; }
                yield return new WaitForSecondsRealtime(.15f);
                Require(Convert.ToDouble(Member(playback, "Duration")) > .5 && Member(fixture, "m_selectedPath") as string == saved
                    && preview.image is RenderTexture, "Native gallery did not select a playable video");
                RenderTexture active = RenderTexture.active; Texture2D pixels = null;
                try
                {
                    var videoTexture = preview.image as RenderTexture;
                    RenderTexture.active = videoTexture;
                    pixels = new Texture2D(videoTexture.width, videoTexture.height, TextureFormat.RGBA32, false);
                    pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply();
                    Color32[] colours = pixels.GetPixels32();
                    int different = 0;
                    for (int index = 0; index < colours.Length; index += Math.Max(1, colours.Length / 1024))
                    {
                        float x = (index % pixels.width) / (float)pixels.width, y = (index / pixels.width) / (float)pixels.height;
                        if (x > .3f && x < .7f && y > .3f && y < .7f) continue;
                        if (!colours[index].Equals(colours[0])) different++;
                    }
                    Require(different > 10, "Saved native video decoded to a blank or uniform image");
                }
                finally { RenderTexture.active = active; if (pixels != null) Destroy(pixels); }
                click = ClickAppControl(surface, Member(fixture, "m_playPauseButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(playback, "IsPlaying") is bool playing && !playing, "Controller pause did not stop native playback");
                var seek = TestRecordingSeek(fixture, playback, surface);
                try { while (seek.MoveNext()) yield return seek.Current; }
                finally { (seek as IDisposable)?.Dispose(); }
                click = ClickAppControl(surface, Member(fixture, "m_playPauseButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(playback, "IsPlaying") is bool resumed && resumed, "Controller play did not resume native playback");
                seek = TestRecordingSeek(fixture, playback, surface);
                try { while (seek.MoveNext()) yield return seek.Current; }
                finally { (seek as IDisposable)?.Dispose(); }
                Logger.LogInfo("RECORDING PASS: controller gallery selection decodes nonblank saved video pixels and pause/play controls operate native playback");
                click = ClickAppControl(surface, Member(fixture, "m_deleteButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(!File.Exists(saved) && gallery.Count == 0 && Member(fixture, "m_showingLive") is bool live && live,
                    "Controller delete did not remove the fixture clip and restore Live");
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                Require(Member(fixture, "m_capturePreview") == null, "Closed recording app retained its capture preview lease");
                Logger.LogInfo("RECORDING PASS: controller delete removes the fixture clip, restores Live and app close releases preview capture");
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel(); holdInjectedTrigger = false;
                if (markerObject != null) Destroy(markerObject);
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                (fixture as IDisposable)?.Dispose(); (recorder as IDisposable)?.Dispose();
                recorderField.SetValue(player, originalRecorder); appField.SetValue(os, originalApp);
                AccessTools.Property(player.GetType(), "LastRecordingPath").SetValue(player, originalPath);
                AccessTools.Property(player.GetType(), "LastRecordingError").SetValue(player, originalError);
                AccessTools.Property(player.GetType(), "RecordingState").SetValue(player, Enum.Parse(Find("KolkataWebRtcRecordingState"), "Idle"));
                content.Clear(); foreach (var child in children) content.Add(child);
                probe.UnpatchSelf(); recordingFixtureFolder = null; recordingFixtureOriginalApp = null;
                AccessTools.Method(originalApp.GetType(), "SubscribeToRecordingPlayer").Invoke(originalApp, new[] { originalPlayer });
                if (originalState != "Closed") AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { id });
                if (originalState == "OpenMinimized") AccessTools.Method(os.GetType(), "MinimizeApp").Invoke(os, new[] { app });
                string resolved = Path.GetFullPath(folder);
                if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("swyf-recording-fixture-"))
                    try { if (Directory.Exists(resolved)) Directory.Delete(resolved, true); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    { Logger.LogInfo("Native recording fixture directory is still closing; retained its temporary files."); }
            }
        }
    }
}
