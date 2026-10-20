using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private void CaptureRemoteControls(VRPanelSurface surface, string filename)
        {
            RenderTexture previous = RenderTexture.active; Texture2D image = null;
            try
            {
                RenderTexture.active = surface.Texture;
                image = new Texture2D(surface.Texture.width, surface.Texture.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(readyFile), filename), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (image != null) Destroy(image); }
        }

        private IEnumerator RemoteClose(VRPanelSurface surface, object app)
        {
            if (Member(app, "State")?.ToString() != "OpenVisible") yield break;
            var click = RemoteClick(surface, (Member(app, "Window") as VisualElement)?.Q<Button>(className: "kolkata-window-button--close"));
            try { while (click.MoveNext()) yield return click.Current; }
            finally { (click as IDisposable)?.Dispose(); }
            Require(Member(app, "State")?.ToString() == "Closed", "Controller did not close native remote app");
        }

        private IEnumerator RemoteOpenFile(VRPanelSurface surface, object owner, object[] entries, object file)
        {
            var apps = Member(owner, "m_appsById") as IDictionary;
            object explorer = apps["file-explorer"];
            VisualElement Content() => Member(explorer, "WindowContent") as VisualElement;
            var action = RemoteClick(surface, Member(explorer, Member(explorer, "State")?.ToString() == "OpenVisible" ? "DragHandle" : "TaskbarButton") as VisualElement);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            int remaining = 20;
            while (!string.IsNullOrEmpty(Member(owner, "m_currentFolderId") as string))
            {
                Require(remaining-- > 0, "Native remote explorer could not reach desktop root");
                action = RemoteClick(surface, Content().Query<Button>(className: "kolkata-rdp-explorer__toolbar-button").ToList()[2]);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
            }
            var path = new List<object>();
            string folder = Member(file, "parentId") as string;
            while (!string.IsNullOrEmpty(folder))
            {
                object parent = entries.FirstOrDefault(e => Member(e, "id") as string == folder);
                Require(parent != null && !path.Contains(parent) && path.Count < 20, "Native remote media ancestry is invalid");
                path.Insert(0, parent); folder = Member(parent, "parentId") as string;
            }
            path.Add(file);
            foreach (object entry in path)
            {
                var button = Content().Query<Button>(className: "kolkata-rdp-explorer__entry").ToList()
                    .FirstOrDefault(b => b.tooltip == Member(entry, "name") as string);
                action = RemoteClick(surface, button, true);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
            }
        }

        private IEnumerator RemoteDragSlider(VRPanelSurface surface, Slider slider, float fraction, float hold = .45f)
        {
            Require(slider?.panel != null && slider.enabledInHierarchy, "Native remote media slider unavailable");
            var reveal = ScrollToScamControl(surface, slider);
            try { while (reveal.MoveNext()) yield return reveal.Current; }
            finally { (reveal as IDisposable)?.Dispose(); }
            yield return null;
            var dragger = slider.Q<VisualElement>(className: "unity-base-slider__dragger");
            var track = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
            Require(dragger?.worldBound.width > 1 && track?.worldBound.width > 10, "Native remote media slider has no layout");
            var manager = VRManager.Instance;
            var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            try
            {
                appFixtureSurface = surface; appFixtureAim = true; appFixturePoint = dragger.worldBound.center;
                holdInjectedTrigger = true; Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                AimAppFixture(); tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                var recipient = Member(manager.Pointer, "pressedElement") as VisualElement;
                if (recipient == null || !(recipient == slider || slider.Contains(recipient)))
                {
                    for (VisualElement parent = slider; parent != null; parent = parent.parent)
                        Logger.LogInfo("REMOTE SLIDER LAYOUT: " + string.Join(",", parent.GetClasses()) + "; bounds=" + parent.worldBound);
                    CaptureRemoteControls(surface, "remote-media-layout.png");
                }
                Require(recipient != null && (recipient == slider || slider.Contains(recipient)), "Controller missed native remote slider handle: recipient=" + recipient
                    + "; classes=" + (recipient == null ? "none" : string.Join(",", recipient.GetClasses())) + "; handle=" + dragger.worldBound + "; slider=" + slider.worldBound);
                appFixturePoint = new Vector2(track.worldBound.xMin + track.worldBound.width * fraction, track.worldBound.center.y);
                yield return null; yield return new WaitForEndOfFrame(); tick.Invoke(manager.Pointer, null);
                float target = slider.value;
                Require(Mathf.Abs(target - fraction) < .12f, "Controller drag did not move native remote slider to target");
                yield return new WaitForSecondsRealtime(hold);
                Require(Mathf.Abs(slider.value - target) < .015f, "Native remote media refresh moved the held slider; target=" + target + "; actual=" + slider.value);
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerUp", true); tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                Require(slider.panel.GetCapturingElement(VRPointer.ToolkitPointerId) == null, "Native remote media slider retained capture after release");
            }
            finally
            {
                holdInjectedTrigger = false; appFixtureAim = false; appFixtureSurface = null;
                Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false); manager.Pointer.Cancel();
            }
        }

        private IEnumerator TestNativeRemoteMedia(object owner, VRPanelSurface surface, object[] entries)
        {
            var apps = Member(owner, "m_appsById") as IDictionary;
            object media = Member(owner, "m_media");
            var audio = Member(media, "Audio") as AudioSource;
            var videoAudio = Member(media, "m_videoAudio") as AudioSource;
            Require(audio != null && videoAudio != null, "Native remote media owner missing");
            bool audioMute = audio.mute, videoMute = videoAudio.mute;
            try
            {
                // Decode bundled fictional media normally, without playing test
                // audio through the user's speakers while they are away.
                audio.mute = videoAudio.mute = true;
                var action = RemoteClose(surface, apps["notepad"]);
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                foreach (string kind in new[] { "packaged-image", "image-recipe", "audio", "video" })
                {
                    object file = entries.FirstOrDefault(e => Member(e, "entryType") as string == kind);
                    Require(file != null, "Native generated desktop has no media entry for fixture: " + kind);
                    action = RemoteOpenFile(surface, owner, entries, file);
                    try { while (action.MoveNext()) yield return action.Current; }
                    finally { (action as IDisposable)?.Dispose(); }
                    string appId = kind == "audio" ? "audio-player" : kind == "video" ? "video-player" : "photos";
                    object app = apps[appId];
                    VisualElement Content() => Member(app, "WindowContent") as VisualElement;
                    float deadline = Time.realtimeSinceStartup + 20;
                    if (appId == "photos")
                    {
                        Image Preview() => Content()?.Q<Image>(className: "kolkata-rdp-media__preview");
                        while (!(Preview()?.image is Texture2D loaded) || loaded.width < 3)
                        { Require(Time.realtimeSinceStartup < deadline, "Native remote photo did not decode: " + kind); yield return null; }
                        var image = Preview();
                        var pixels = ((Texture2D)image.image).GetPixels32();
                        Require(pixels.Length > 100 && pixels.Where((p, i) => i % Math.Max(1, pixels.Length / 1024) == 0).Count(p => !p.Equals(pixels[0])) > 10,
                            "Native remote photo is blank or uniform: " + kind);
                        for (int toggle = 0; toggle < 2; toggle++)
                        {
                            ScaleMode before = image.scaleMode;
                            action = RemoteClick(surface, Content().Q<Button>(className: "kolkata-rdp-media__button--fit"));
                            try { while (action.MoveNext()) yield return action.Current; }
                            finally { (action as IDisposable)?.Dispose(); }
                            Require(image.scaleMode != before, "Controller did not switch native photo fit/fill");
                        }
                        Logger.LogInfo("REMOTE MEDIA PASS: native " + kind + " opens through controller folder/file double-clicks, decodes nonblank pixels and toggles fit/fill");
                    }
                    else
                    {
                        bool video = kind == "video";
                        object playback = video ? Member(media, "Video") : null;
                        bool Playing() => video ? (bool)Member(playback, "IsPlaying") : audio.isPlaying;
                        double Duration() => video ? Convert.ToDouble(Member(playback, "Duration")) : audio.clip?.length ?? 0;
                        double Position() => video ? Convert.ToDouble(Member(playback, "CurrentTime")) : audio.time;
                        while (Duration() <= .5 || !Playing())
                        { Require(Time.realtimeSinceStartup < deadline, "Native remote media did not start: " + kind + "; status=" + Member(owner, "m_mediaStatus")); yield return null; }
                        action = RemoteClick(surface, Content().Q<Button>(className: "kolkata-rdp-media__play-button"));
                        try { while (action.MoveNext()) yield return action.Current; }
                        finally { (action as IDisposable)?.Dispose(); }
                        Require(!Playing(), "Controller did not pause native remote media: " + kind);
                        if (video)
                        {
                            var texture = Content().Q<Image>(className: "kolkata-rdp-media__preview")?.image as RenderTexture;
                            Require(texture != null && texture.IsCreated(), "Native remote video has no decoded texture");
                            Color pixel = RecordingCentre(texture);
                            Require(pixel.maxColorComponent > .05f, "Native remote video decoded a black center frame");
                        }
                        var seek = Content().Q<Slider>(className: "kolkata-rdp-media__seek");
                        action = RemoteDragSlider(surface, seek, .6f);
                        try { while (action.MoveNext()) yield return action.Current; }
                        finally { (action as IDisposable)?.Dispose(); }
                        deadline = Time.realtimeSinceStartup + 5;
                        while (Math.Abs(Position() / Duration() - seek.value) > .06)
                        { Require(Time.realtimeSinceStartup < deadline, "Native remote playhead did not follow controller seek: " + kind); yield return null; }
                        Require(!Playing(), "Seeking resumed paused native remote media: " + kind);
                        var volume = Content().Q<Slider>(className: "kolkata-rdp-media__volume-slider");
                        action = RemoteDragSlider(surface, volume, .32f, .1f);
                        try { while (action.MoveNext()) yield return action.Current; }
                        finally { (action as IDisposable)?.Dispose(); }
                        Require(Mathf.Abs((video ? videoAudio.volume : audio.volume) - volume.value) < .01f, "Controller volume did not reach native media source");
                        CaptureRemoteControls(surface, "remote-" + kind + "-controls.png");
                        action = RemoteClick(surface, Content().Q<Button>(className: "kolkata-rdp-media__play-button"));
                        try { while (action.MoveNext()) yield return action.Current; }
                        finally { (action as IDisposable)?.Dispose(); }
                        Require(Playing(), "Controller did not resume native remote media: " + kind);
                        Logger.LogInfo("REMOTE MEDIA PASS: native " + kind + " decoding, controller pause, held seek, source volume and resume; output muted");
                        if (video)
                        {
                            object outer = Member(owner, "m_outerApp");
                            for (int resize = 0; resize < 2; resize++)
                            {
                                action = RemoteClick(surface, Member(outer, "MaximizeButton") as VisualElement);
                                try { while (action.MoveNext()) yield return action.Current; }
                                finally { (action as IDisposable)?.Dispose(); }
                                Rect available = (Member(owner, "m_windowLayer") as VisualElement).worldBound;
                                foreach (object visible in apps.Values)
                                {
                                    if (Member(visible, "State")?.ToString() != "OpenVisible") continue;
                                    Rect bounds = (Member(visible, "Window") as VisualElement).worldBound;
                                    Require(bounds.xMin >= available.xMin + 2 && bounds.yMin >= available.yMin + 2
                                        && bounds.xMax <= available.xMax - 2 && bounds.yMax <= available.yMax - 2,
                                        "Native remote window escaped its usable desktop after outer resize: " + Member(Member(visible, "Definition"), "id"));
                                }
                            }
                            Logger.LogInfo("REMOTE MEDIA PASS: visible remote windows remain inside the usable desktop when AnyViewer is maximised and restored");
                        }
                    }
                    action = RemoteClose(surface, app);
                    try { while (action.MoveNext()) yield return action.Current; }
                    finally { (action as IDisposable)?.Dispose(); }
                    if (kind == "audio") Require(audio.clip == null && !audio.isPlaying, "Closing native remote audio retained its clip/playback");
                    if (kind == "video") Require(!(bool)Member(Member(media, "Video"), "HasSelection"), "Closing native remote video retained playback selection");
                }
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel(); holdInjectedTrigger = false;
                if (audio != null) audio.mute = audioMute;
                if (videoAudio != null) videoAudio.mute = videoMute;
            }
        }
    }
}
