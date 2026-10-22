using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool voiceFixture, voiceHeld, voiceEdge;
        private static object voiceFilterFixture;
        private static void KeepVoiceInput(object input)
        {
            if (!voiceFixture) return;
            AccessTools.Property(input.GetType(), "Voice").SetValue(input, voiceHeld);
            AccessTools.Property(input.GetType(), "VoiceDown").SetValue(input, voiceEdge);
        }
        private static bool SkipFixtureVoiceAudio(object __instance) => !ReferenceEquals(__instance, voiceFilterFixture);

        private IEnumerator TestNativeVoice()
        {
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            var voice = player.GetComponent(Find("PlayerPushToTalk"));
            var chat = Member(voice, "playerVoiceChat") as Behaviour;
            object filter = Member(voice, "filter");
            Type controls = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRVoiceControls", true);
            Require(voice != null && chat != null && filter != null && (bool)AccessTools.Field(controls, "Available").GetValue(null), "Native voice components/hooks unavailable");
            Require(!(bool)Member(voice, "isPressed") && !(bool)Member(voice, "controllerTalkLatched") && !(bool)Member(voice, "IsGlobalPressed"),
                "Voice fixture requires no existing keyboard/controller/global talk hold");
            var nextField = AccessTools.Field(filter.GetType(), "optionalNextInputFilter");
            var echoProperty = AccessTools.Property(filter.GetType(), "LocalEchoPipeline");
            object next = nextField.GetValue(filter), echo = echoProperty.GetValue(filter);
            bool enabled = chat.enabled, muted = (bool)Member(voice, "IsInputMuted"), focus = manager.Input.HasInputFocus;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(manager.BodyActions.GetType(), "Update");
            var mute = AccessTools.Method(voice.GetType(), "SetInputMuted");
            var nativePressed = AccessTools.Field(voice.GetType(), "isPressed");
            var probe = new Harmony("com.swyf.vr.voicestatefixture");
            bool paused = false, ownedDesktop = false;
            void Drive(bool held)
            {
                voiceEdge = held && !voiceHeld; voiceHeld = held;
                KeepVoiceInput(manager.Input); tick.Invoke(manager.BodyActions, null);
                voiceEdge = false; AccessTools.Property(manager.Input.GetType(), "VoiceDown").SetValue(manager.Input, false);
            }
            void Silent()
            {
                Require(ReferenceEquals(voiceFilterFixture, filter) && nextField.GetValue(filter) == null && echoProperty.GetValue(filter) == null,
                    "Voice fixture output isolation changed");
            }
            IEnumerator WaitState(bool active)
            {
                float deadline = Time.realtimeSinceStartup + Convert.ToSingle(Member(filter, "debounceSeconds")) + 3;
                while ((bool)Member(voice, "NetworkisActive") != active || (bool)Member(filter, "isPressed") != active)
                { Silent(); Require(Time.realtimeSinceStartup < deadline, "Native voice network/filter state did not become " + active); yield return null; }
                Silent();
            }
            try
            {
                chat.enabled = false; mute.Invoke(voice, new object[] { true }); yield return null;
                voiceFilterFixture = filter;
                probe.Patch(AccessTools.Method(filter.GetType(), "Process"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipFixtureVoiceAudio)));
                nextField.SetValue(filter, null); echoProperty.SetValue(filter, null);
                AccessTools.Method(chat.GetType(), "SetKolkataSpeechRecognitionInputBlocked")?.Invoke(chat, new object[] { true });
                voiceFixture = true; mute.Invoke(voice, new object[] { false }); Drive(false);
                Drive(true);
                Require((bool)Member(voice, "IsLocalTalkPressed") && !(bool)nativePressed.GetValue(voice), "VR hold did not add native talk without rewriting keyboard state");
                yield return WaitState(true);
                Drive(false); Require(!(bool)Member(voice, "IsLocalTalkPressed"), "Voice release retained its logical press");
                yield return WaitState(false);
                Logger.LogInfo("VOICE HOLD PASS: controller hold/release reaches native logical, network and filter state with audio processing suppressed and outputs detached");
                nativePressed.SetValue(voice, true); Drive(true); Drive(false);
                Require((bool)Member(voice, "IsLocalTalkPressed") && (bool)nativePressed.GetValue(voice), "VR release overwrote an existing native keyboard hold");
                nativePressed.SetValue(voice, false); yield return WaitState(false);
                Drive(true); yield return WaitState(true);
                mute.Invoke(voice, new object[] { true }); tick.Invoke(manager.BodyActions, null);
                Require(!(bool)Member(voice, "IsLocalTalkPressed") && (bool)Member(filter, "IsInputMuted"), "Native mute failed to suppress VR talk");
                yield return WaitState(false);
                mute.Invoke(voice, new object[] { false }); Drive(true); yield return new WaitForSecondsRealtime(.3f);
                Require(!(bool)Member(voice, "IsLocalTalkPressed") && !(bool)Member(voice, "NetworkisActive"), "Unmuting restarted a held VR voice button");
                Logger.LogInfo("VOICE MUTE PASS: native keyboard hold remains independent; mute overrides VR and unmuting requires a fresh voice press");
                Drive(false); Drive(true); yield return WaitState(true);
                toggle.Invoke(null, null); paused = true; tick.Invoke(manager.BodyActions, null);
                yield return WaitState(false);
                toggle.Invoke(null, null); paused = false; Drive(true);
                Require(!(bool)Member(voice, "IsLocalTalkPressed"), "Closing settings replayed held voice input");
                Drive(false); Drive(true); yield return WaitState(true);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, false); tick.Invoke(manager.BodyActions, null);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus); Drive(true);
                yield return WaitState(false);
                Require(!(bool)Member(voice, "IsLocalTalkPressed"), "Focus recovery replayed held voice input");
                Drive(false); Drive(true); yield return WaitState(true);
                manager.BodyActions.enabled = false; yield return WaitState(false);
                manager.BodyActions.enabled = true; Drive(true);
                Require(!(bool)Member(voice, "IsLocalTalkPressed"), "Component re-enable resumed held voice input");
                Drive(false); Drive(true); yield return WaitState(true);
                manager.BodyActions.Cancel(); Drive(true); yield return WaitState(false);
                Require(!(bool)Member(voice, "IsLocalTalkPressed"), "Session cancellation replayed held voice input");
                Drive(false);
                Logger.LogInfo("VOICE RECOVERY PASS: settings, focus, component disable and session cleanup stop native talk and require a fresh button press");
                Require(!MonitorCaptured(), "Voice desktop check requires an initially uncaptured monitor");
                var interactor = player.GetComponent(Find("PlayerInteractor")); Type monitorType = Find("MonitorInteractable");
                Component monitor = FindObjectsByType(monitorType, FindObjectsSortMode.None).Cast<Component>()
                    .Where(candidate => candidate.gameObject.activeInHierarchy && (bool)AccessTools.Method(monitorType, "CanInteract").Invoke(candidate, new object[] { interactor }))
                    .OrderBy(candidate => Vector3.Distance(candidate.transform.position, player.transform.position)).FirstOrDefault();
                Require(monitor != null, "No native computer is available for voice context check");
                AccessTools.Method(monitorType, "CmdInteract").Invoke(monitor, new object[] { interactor }); ownedDesktop = true;
                float ready = Time.realtimeSinceStartup + 15;
                while (!MonitorCaptured()) { Silent(); Require(Time.realtimeSinceStartup < ready, "Native computer did not capture for voice context check"); yield return null; }
                Drive(true); yield return WaitState(true);
                Require((bool)Member(voice, "IsLocalTalkPressed"), "Native desktop capture blocked controller push-to-talk");
                Drive(false); yield return WaitState(false);
                AccessTools.Method(adapter, "Back").Invoke(null, null); ownedDesktop = false;
                Logger.LogInfo("VOICE DESKTOP PASS: controller hold/release reaches native talk while a real desk monitor is captured; microphone samples remain suppressed");
            }
            finally
            {
                mute.Invoke(voice, new object[] { true });
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus);
                manager.BodyActions.enabled = true; Drive(false); manager.BodyActions.Cancel(); voiceFixture = false;
                nativePressed.SetValue(voice, false);
                if (paused) toggle.Invoke(null, null);
                if (ownedDesktop && MonitorCaptured()) AccessTools.Method(adapter, "Back").Invoke(null, null);
                nextField.SetValue(filter, next); echoProperty.SetValue(filter, echo);
                AccessTools.Method(chat.GetType(), "ApplyRuntimeSettings")?.Invoke(chat, null);
                mute.Invoke(voice, new object[] { muted }); chat.enabled = enabled;
                probe.UnpatchSelf(); voiceFilterFixture = null;
            }
            Require(ReferenceEquals(nextField.GetValue(filter), next) && ReferenceEquals(echoProperty.GetValue(filter), echo)
                && chat.enabled == enabled && (bool)Member(voice, "IsInputMuted") == muted && !(bool)nativePressed.GetValue(voice),
                "Voice fixture failed to restore native outputs, component state, mute or keyboard press");
            Logger.LogInfo("VOICE COMPLETE PASS: native controller voice state, mute/ownership/recovery/desktop context and output restoration checked; no headset or transmitted-audio claim");
        }
    }
}
