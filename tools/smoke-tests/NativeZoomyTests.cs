using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestNativeZoomy(object os, VRPanelSurface surface)
        {
            object game = Instance("ScammerGameManager");
            bool originalUnlock = (bool)Member(game, "DeveloperAllAppsUnlocked");
            var unlock = AccessTools.Method(game.GetType(), "ServerSetDeveloperLobbySettings");
            var refresh = AccessTools.Method(os.GetType(), "RefreshApplicationAccessRuntimes");
            bool changed = false;
            try
            {
                if (!originalUnlock)
                {
                    unlock.Invoke(game, new object[] { true, true }); changed = true; refresh.Invoke(os, null);
                    yield return null; yield return null;
                }
                var test = TestAvailableNativeZoomy(os, surface);
                try { while (test.MoveNext()) yield return test.Current; }
                finally { (test as IDisposable)?.Dispose(); }
            }
            finally { if (changed) { unlock.Invoke(game, new object[] { true, originalUnlock }); refresh.Invoke(os, null); } }
        }

        private IEnumerator TestAvailableNativeZoomy(object os, VRPanelSurface surface)
        {
            Require(NetworkServer.connections.Count == 1, "Zoomy fixture requires one private host connection");
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            object app = (Member(os, "m_appsById") as IDictionary)?["zoomy"];
            object zoomy = Member(os, "m_zoomyApp");
            var participant = player.GetComponent(Find("KolkataZoomyPlayer"));
            Require(app != null && zoomy != null && participant != null && (bool)Member(app, "IsAvailable")
                && Member(app, "State").ToString() == "Closed" && !(bool)Member(participant, "IsJoined"),
                "Zoomy fixture requires an available, closed native app and no existing meeting; app=" + (app != null)
                + "; available=" + Member(app, "IsAvailable") + "; state=" + Member(app, "State") + "; joined=" + Member(participant, "IsJoined"));
            var source = Member(Member(Instance("KolkataOsSingleton"), "boundWorkspace"), "zoomyWebcamCamera") as UnityEngine.Camera;
            Require(source != null && source != manager.Rig.Head && source.targetTexture == null, "Native workspace webcam unavailable or already owned");
            int savedMask = source.cullingMask;
            float savedAspect = source.aspect;
            bool savedHDR = source.allowHDR, savedMSAA = source.allowMSAA, savedEnabled = source.enabled, savedActive = source.gameObject.activeSelf;
            var voice = player.GetComponent(Find("PlayerPushToTalk"));
            var chat = Member(voice, "playerVoiceChat") as Behaviour;
            object filter = Member(voice, "filter");
            Require(voice != null && chat != null && filter != null, "Native audio isolation unavailable");
            var nextField = AccessTools.Field(filter.GetType(), "optionalNextInputFilter");
            var echoProperty = AccessTools.Property(filter.GetType(), "LocalEchoPipeline");
            object savedNext = nextField.GetValue(filter), savedEcho = echoProperty.GetValue(filter);
            bool savedChat = chat.enabled, savedMuted = (bool)Member(voice, "IsInputMuted");
            var mute = AccessTools.Method(voice.GetType(), "SetInputMuted");
            var controller = Instance("Metater.MetaPlayerController");
            var heads = new List<Renderer>();
            foreach (Component toggle in ((Array)Member(controller, "meshRendererToggles")).Cast<Component>())
                if (toggle != null && toggle.gameObject.activeInHierarchy)
                    foreach (Renderer renderer in toggle.GetComponentsInChildren<Renderer>())
                        if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                            && (bool)AccessTools.Method(toggle.GetType(), "IsHeadVisual").Invoke(toggle, new object[] { renderer.transform })
                            && !heads.Contains(renderer)) heads.Add(renderer);
            Require(heads.Count > 0, "Zoomy fixture requires actual local head renderers");
            int frames = 0;
            bool headInCapture = false, uiExcluded = false, restored = false;
            void Begin(ScriptableRenderContext context, UnityEngine.Camera camera)
            {
                if (camera != source) return;
                frames++; headInCapture = heads.All(r => r != null && r.enabled && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly);
                uiExcluded = (camera.cullingMask & (1 << 5)) == 0;
            }
            void End(ScriptableRenderContext context, UnityEngine.Camera camera)
            {
                if (camera == source) restored = camera.cullingMask == savedMask
                    && heads.All(r => r != null && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly);
            }
            GameObject marker = null;
            Material markerMaterial = null;
            var probe = new Harmony("com.swyf.vr.zoomyfixture");
            bool opened = false;
            int participantNotifications = 0;
            Action independentParticipantObserver = () => participantNotifications++;
            var participantsChanged = participant.GetType().GetEvent("ParticipantsChanged");
            participantsChanged.AddEventHandler(null, independentParticipantObserver);
            try
            {
                chat.enabled = false; mute.Invoke(voice, new object[] { true });
                voiceFilterFixture = filter;
                probe.Patch(AccessTools.Method(filter.GetType(), "Process"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipFixtureVoiceAudio)));
                nextField.SetValue(filter, null); echoProperty.SetValue(filter, null);
                AccessTools.Method(chat.GetType(), "SetKolkataSpeechRecognitionInputBlocked")?.Invoke(chat, new object[] { true });
                marker = GameObject.CreatePrimitive(PrimitiveType.Quad); marker.name = "SWYF Zoomy UI-layer marker";
                Destroy(marker.GetComponent<Collider>()); marker.layer = 5;
                marker.transform.SetPositionAndRotation(source.transform.position + source.transform.forward * .7f,
                    Quaternion.LookRotation(source.transform.forward, source.transform.up));
                marker.transform.localScale = Vector3.one * 2;
                markerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                markerMaterial.color = Color.magenta;
                if (markerMaterial.HasProperty("_BaseColor")) markerMaterial.SetColor("_BaseColor", Color.magenta);
                marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                RenderPipelineManager.beginCameraRendering += Begin; RenderPipelineManager.endCameraRendering += End;
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "zoomy" }); opened = true;
                float deadline = Time.realtimeSinceStartup + 8;
                while (!(bool)Member(participant, "IsJoined") || source.targetTexture == null || frames < 3)
                { Require(Time.realtimeSinceStartup < deadline, "Zoomy did not join or render its native webcam"); yield return null; }
                yield return new WaitForEndOfFrame();
                Logger.LogInfo("ZOOMY CAMERA STATE: frames=" + frames + "; head=" + headInCapture + "; uiExcluded=" + uiExcluded + "; restored=" + restored);
                Require(headInCapture && uiExcluded && restored, "Zoomy webcam must include local head, omit floating UI and restore native mask/first-person visibility");
                Require(manager.Rig.Head.targetTexture == null && source.targetTexture != null, "Zoomy redirected the headset render target");
                var texture = source.targetTexture;
                RenderTexture active = RenderTexture.active;
                Texture2D pixels = null;
                try
                {
                    RenderTexture.active = texture; pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); pixels.Apply();
                    Color center = pixels.GetPixel(pixels.width / 2, pixels.height / 2);
                    Require(!(center.r > .65f && center.b > .65f && center.g < .3f), "UI-layer marker leaked into actual native webcam pixels");
                    Color32[] image = pixels.GetPixels32();
                    Require(image.Any(p => p.r + p.g + p.b > 30) && image.Any(p => p.r != image[0].r || p.g != image[0].g || p.b != image[0].b),
                        "Zoomy webcam produced blank/uniform pixels");
                }
                finally { RenderTexture.active = active; if (pixels != null) Destroy(pixels); }
                Logger.LogInfo("ZOOMY CAMERA PASS: native webcam joins/renders nonblank pixels with local head visible only during capture, UI excluded and headset output unchanged");
                for (int rebuild = 0; rebuild < 2; rebuild++)
                {
                    AccessTools.Method(os.GetType(), "RefreshApp").Invoke(os, new[] { app });
                    yield return null; yield return null;
                    foreach (string name in new[] { "ParticipantsChanged", "ChatMessageReceived" })
                    {
                        var listeners = AccessTools.Field(participant.GetType(), name).GetValue(null) as Delegate;
                        Require(listeners != null && listeners.GetInvocationList().Count(d => ReferenceEquals(d.Target, zoomy)) == 1,
                            "Native Zoomy rebuild duplicated its " + name + " subscription");
                    }
                    var all = AccessTools.Field(participant.GetType(), "ParticipantsChanged").GetValue(null) as Delegate;
                    Require(all.GetInvocationList().Contains(independentParticipantObserver), "Zoomy rebuild removed another owner's participant listener");
                }
                Logger.LogInfo("ZOOMY REBUILD PASS: repeated native content rebuilds retain one app listener per event and preserve another owner's observer");
                bool originalChatOpen = (bool)Member(zoomy, "m_chatOpen");
                Func<bool> Muted = () => (bool)Member(participant, "IsZoomyMuted") && (bool)Member(participant, "IsZoomyMutedForOthers");
                Func<bool> Unmuted = () => !(bool)Member(participant, "IsZoomyMuted") && !(bool)Member(participant, "IsZoomyMutedForOthers");
                var steps = new (string Field, Func<bool> Ready, string Description)[]
                {
                    ("m_muteButton", Muted, "native local/network mute"),
                    ("m_muteButton", Unmuted, "native local/network unmute with audio samples suppressed"),
                    ("m_deafenButton", () => (bool)Member(participant, "IsLocallyDeafened") && Muted(), "deafen also mutes"),
                    ("m_deafenButton", () => !(bool)Member(participant, "IsLocallyDeafened") && Unmuted(), "undeafen restores prior unmuted state"),
                    ("m_chatButton", () => (bool)Member(zoomy, "m_chatOpen") != originalChatOpen, "chat visibility toggle"),
                    ("m_chatButton", () => (bool)Member(zoomy, "m_chatOpen") == originalChatOpen, "chat visibility restore"),
                    ("m_shareButton", () => (bool)Member(participant, "IsScreenSharingEnabled")
                        && Member(Member(zoomy, "m_localScreenPreview"), "Texture") is RenderTexture shared && shared.IsCreated(), "native game-view screen sharing"),
                    ("m_shareButton", () => !(bool)Member(participant, "IsScreenSharingEnabled") && Member(zoomy, "m_localScreenPreview") == null, "screen sharing stop/release")
                };
                foreach (var step in steps)
                {
                    var click = ClickAppControl(surface, Member(zoomy, step.Field) as VisualElement);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    deadline = Time.realtimeSinceStartup + 8;
                    while (!step.Ready())
                    { Require(Time.realtimeSinceStartup < deadline, "Zoomy controller action failed: " + step.Description); yield return null; }
                    Require(ReferenceEquals(voiceFilterFixture, filter) && nextField.GetValue(filter) == null && echoProperty.GetValue(filter) == null
                        && !chat.enabled && manager.Rig.Head.targetTexture == null, "Zoomy control changed audio isolation or headset output");
                    Logger.LogInfo("ZOOMY CONTROL PASS: controller " + step.Description);
                }
                if (originalChatOpen)
                {
                    const string message = "vr interface fixture message";
                    var messages = Member(zoomy, "m_messages") as IList;
                    Require(messages != null && messages.Count == 0 && NetworkServer.connections.Count == 1,
                        "Zoomy chat requires a fresh meeting with only the owned host");
                    var input = Member(zoomy, "m_chatInput") as TextField;
                    var typing = EnterScamText(surface, input, message);
                    try { while (typing.MoveNext()) yield return typing.Current; }
                    finally { (typing as IDisposable)?.Dispose(); }
                    Require(NetworkServer.connections.Count == 1, "Another participant joined the isolated chat fixture");
                    var send = (Member(app, "WindowContent") as VisualElement)?.Q<Button>(className: "kolkata-zoomy__chat-send");
                    var click = ClickAppControl(surface, send);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    deadline = Time.realtimeSinceStartup + 6;
                    bool ChatShown() => (Member(zoomy, "m_chatMessages") as VisualElement)?.Q<Label>(className: "kolkata-zoomy__chat-body")?.text == message;
                    while (messages.Count == 0 || string.IsNullOrEmpty(input.value) == false || !ChatShown())
                    {
                        Require(Time.realtimeSinceStartup < deadline, "Owned native chat confirmation incomplete; count=" + messages.Count
                            + "; fieldCleared=" + string.IsNullOrEmpty(input.value) + "; displayed=" + ChatShown());
                        yield return null;
                    }
                    Require(messages.Count == 1 && Member(messages[0], "Text") as string == message && string.IsNullOrEmpty(input.value)
                        && (Member(zoomy, "m_chatMessages") as VisualElement)?.Q<Label>(className: "kolkata-zoomy__chat-body")?.text == message,
                        "Zoomy chat duplicated or failed to display the owned fictional message; count=" + messages.Count
                        + "; textMatches=" + (messages.Count > 0 && Member(messages[0], "Text") as string == message)
                        + "; fieldCleared=" + string.IsNullOrEmpty(input.value) + "; displayed=" + ChatShown());
                    Logger.LogInfo("ZOOMY CHAT PASS: controller-focused synthetic typing and send return one fictional message through native authority to the single owned host");
                    yield return new WaitForSecondsRealtime(.35f);
                    typing = EnterScamText(surface, input, message);
                    try { while (typing.MoveNext()) yield return typing.Current; }
                    finally { (typing as IDisposable)?.Dispose(); }
                    Require(NetworkServer.connections.Count == 1, "Another participant joined the isolated repeat-message fixture");
                    click = ClickAppControl(surface, send);
                    try { while (click.MoveNext()) yield return click.Current; }
                    finally { (click as IDisposable)?.Dispose(); }
                    deadline = Time.realtimeSinceStartup + 6;
                    while (messages.Count < 2)
                    { Require(Time.realtimeSinceStartup < deadline, "Native chat dropped a legitimate repeated message"); yield return null; }
                    Require(messages.Count == 2 && messages.Cast<object>().All(m => Member(m, "Text") as string == message),
                        "Native chat rebuild duplicated messages or filtered legitimate identical text");
                    Logger.LogInfo("ZOOMY CHAT REPEAT PASS: two separate controller sends of identical fictional text produce exactly two native messages after content rebuilds");
                }
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app }); opened = false;
                deadline = Time.realtimeSinceStartup + 6;
                while ((bool)Member(participant, "IsJoined"))
                {
                    Require(Time.realtimeSinceStartup < deadline, "Native meeting leave acknowledgement did not arrive");
                    Require(ReferenceEquals(voiceFilterFixture, filter) && nextField.GetValue(filter) == null && echoProperty.GetValue(filter) == null,
                        "Audio isolation ended before the meeting left");
                    yield return null;
                }
                int closedFrames = frames;
                yield return new WaitForEndOfFrame();
                Require(source.targetTexture == null && !source.enabled && frames == closedFrames
                    && (Member(zoomy, "m_webcamFeeds") as IDictionary)?.Count == 0 && Member(zoomy, "m_localScreenPreview") == null,
                    "Closing Zoomy retained a render, webcam texture or screen preview");
                Require(participantNotifications > 0, "Native participant notifications stopped reaching another owner");
                Logger.LogInfo("ZOOMY LIFECYCLE PASS: native close acknowledges meeting leave, releases webcam/screen textures and stops camera rendering before audio restoration");
            }
            finally
            {
                manager.Pointer.Cancel();
                if (opened) AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End;
                participantsChanged.RemoveEventHandler(null, independentParticipantObserver);
                if (marker != null) Destroy(marker); if (markerMaterial != null) Destroy(markerMaterial);
                source.cullingMask = savedMask; source.aspect = savedAspect; source.allowHDR = savedHDR; source.allowMSAA = savedMSAA;
                source.enabled = savedEnabled; source.gameObject.SetActive(savedActive);
                nextField.SetValue(filter, savedNext); echoProperty.SetValue(filter, savedEcho);
                AccessTools.Method(chat.GetType(), "ApplyRuntimeSettings")?.Invoke(chat, null);
                mute.Invoke(voice, new object[] { savedMuted }); chat.enabled = savedChat;
                probe.UnpatchSelf(); voiceFilterFixture = null;
            }
            Require(!(bool)Member(participant, "IsJoined") && source.targetTexture == null
                && source.enabled == savedEnabled && source.gameObject.activeSelf == savedActive
                && ReferenceEquals(nextField.GetValue(filter), savedNext) && ReferenceEquals(echoProperty.GetValue(filter), savedEcho),
                "Zoomy fixture failed to release native webcam/meeting or restore audio outputs");
            Logger.LogInfo("ZOOMY COMPLETE PASS: native meeting/webcam lifecycle and audio-output restoration checked; microphone samples suppressed, remote audio/headset acceptance pending");
        }
    }
}
