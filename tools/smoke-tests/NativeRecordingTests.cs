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
        private static string recordingFixtureFolder;
        private static object recordingFixtureOriginalApp;
        private static bool RecordingFixtureFolder(ref string __result)
        { if (recordingFixtureFolder == null) return true; __result = recordingFixtureFolder; return false; }
        private static bool RecordingFixturePath(ref string __result)
        { if (recordingFixtureFolder == null) return true; __result = Path.Combine(recordingFixtureFolder, "controller-test.webm"); return false; }
        private static bool RecordingFixtureNoClipboard(ref bool __result) { __result = false; return false; }
        private static bool RecordingFixtureNoWatermark(ref bool __result) { __result = false; return false; }
        private static bool RecordingFixtureKeepOriginalDetached(object __instance) => !ReferenceEquals(__instance, recordingFixtureOriginalApp);

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
                yield return new WaitForSecondsRealtime(3);
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
                        if (!colours[index].Equals(colours[0])) different++;
                    Require(different > 10, "Saved native video decoded to a blank or uniform image");
                }
                finally { RenderTexture.active = active; if (pixels != null) Destroy(pixels); }
                click = ClickAppControl(surface, Member(fixture, "m_playPauseButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(playback, "IsPlaying") is bool playing && !playing, "Controller pause did not stop native playback");
                click = ClickAppControl(surface, Member(fixture, "m_playPauseButton") as Button);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(Member(playback, "IsPlaying") is bool resumed && resumed, "Controller play did not resume native playback");
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
