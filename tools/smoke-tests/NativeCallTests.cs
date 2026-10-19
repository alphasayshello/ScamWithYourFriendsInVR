using System;
using System.Collections;
using System.Threading.Tasks;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object callFixtureCaller, callFixtureVoice;
        private static int callFixtureDialogueRequests;
        private static bool SkipFixtureDialogue(object __instance, ref Task __result)
        {
            if (!ReferenceEquals(__instance, callFixtureCaller)) return true;
            callFixtureDialogueRequests++; __result = Task.CompletedTask; return false;
        }
        private static bool SkipFixtureWarmup(object __instance) => !ReferenceEquals(__instance, callFixtureVoice);

        private IEnumerator TestNativeCall(object os, VRPanelSurface surface)
        {
            Require(NetworkServer.active && NetworkClient.localPlayer != null, "Call fixture requires the owned private host");
            object game = Instance("ScammerGameManager");
            Require(Member(game, "dayZeroTutorialActive") is bool tutorial && !tutorial
                && Member(game, "ScammerGameStarted") is bool started && !started,
                "Call fixture requires an idle lobby without tutorial progression");
            object desktop = Instance("KolkataOsSingleton");
            var workspace = Member(desktop, "boundWorkspace") as Component;
            Require(workspace != null && Member(workspace, "NetworkactiveCall") == null && Member(os, "m_boundVictimCall") == null,
                "Call fixture requires a workstation without another caller");
            object switchboard = Instance("CallSwitchboard");
            var prefab = Member(switchboard, "victimCallPrefab") as Component;
            Type callType = Find("NetworkedCall"), voiceType = Find("KolkataPlayerVoice");
            Require(prefab != null && Find("VictimCall").IsInstanceOfType(prefab), "Native victim call prefab missing");
            object osPlayer = AccessTools.Property(Find("KolkataOsPlayer"), "LocalPlayerInstance").GetValue(null);
            var voice = osPlayer == null ? null : AccessTools.Method(osPlayer.GetType(), "GetPlayerVoice").Invoke(osPlayer, null) as Component;
            Require(voice != null, "Native local phone voice component missing");
            Require(Member(voice, "IsOnCall") is bool onCall && !onCall, "Local player already owns another call");
            object phone = (Member(os, "m_appsById") as IDictionary)?["phone"];
            Require(phone != null, "Native phone runtime missing");
            string originalState = Member(phone, "State")?.ToString();
            object originalCooldown = Member(workspace, "ServerLastCallEndedAt");
            var cooldown = AccessTools.Property(workspace.GetType(), "ServerLastCallEndedAt");
            Require(cooldown?.GetSetMethod(true) != null, "Native call cooldown restoration API missing");
            var process = AccessTools.Method(callType, "ServerProcessPromptQueueAsync");
            Require(process?.ReturnType == typeof(Task), "Native caller dialogue task signature changed");
            var probe = new Harmony("com.swyf.vr.callfixture");
            Component call = null; uint id = 0;
            try
            {
                callFixtureVoice = voice; callFixtureDialogueRequests = 0;
                probe.Patch(process, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipFixtureDialogue)));
                probe.Patch(AccessTools.Method(voiceType, "ServerWarmUpTts"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(SkipFixtureWarmup)));
                call = UnityEngine.Object.Instantiate(prefab); callFixtureCaller = call;
                NetworkServer.Spawn(call.gameObject);
                id = Convert.ToUInt32(Member(call, "netId"));
                Require(id != 0 && AccessTools.Method(callType, "ServerInitialize").Invoke(call, new object[] { workspace }) is bool initialized && initialized,
                    "Native server could not initialize the fixture caller");
                float deadline = Time.realtimeSinceStartup + 15;
                while (!ReferenceEquals(Member(os, "m_boundVictimCall"), call))
                { Require(Time.realtimeSinceStartup < deadline, "Native ringing caller did not bind to the local computer"); yield return null; }
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "phone" });
                yield return null; yield return null;
                Require(NetworkClient.spawned.ContainsKey(id) && Member(call, "callState")?.ToString() == "Ringing"
                    && ReferenceEquals(Member(workspace, "NetworkactiveCall"), call), "Ringing caller did not replicate through native workspace state");
                var content = Member(phone, "WindowContent") as VisualElement;
                var pickup = Member(os, "m_incomingCallAnswerButton") as Button
                    ?? content?.Q<Button>(className: "kolkata-command-button--pickup");
                Require(pickup != null && pickup.enabledInHierarchy && pickup.resolvedStyle.display != DisplayStyle.None,
                    "Native incoming caller did not expose an enabled Pick Up control");
                var click = ClickAppControl(surface, pickup);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                deadline = Time.realtimeSinceStartup + 10;
                while (Member(call, "callState")?.ToString() != "Talking")
                { Require(Time.realtimeSinceStartup < deadline, "Controller Pick Up did not reach native server call authority"); yield return null; }
                Require(ReferenceEquals(Member(call, "Networktalker"), voice) && ReferenceEquals(Member(voice, "NetworkcurrentCall"), call)
                    && Member(voice, "IsOnCall") is bool talking && talking, "Native answer did not assign the local player as talker");
                Require(callFixtureDialogueRequests > 0, "Native answer did not reach its opening dialogue boundary");
                Logger.LogInfo("CALL PASS: real native caller rings on the desk, controller Pick Up reaches server authority and assigns replicated local talker ownership");
                Button hangup = null;
                deadline = Time.realtimeSinceStartup + 8;
                while (hangup == null)
                {
                    phone = (Member(os, "m_appsById") as IDictionary)?["phone"];
                    content = Member(phone, "WindowContent") as VisualElement;
                    var candidate = content?.Q<Button>(className: "kolkata-command-button--hangup");
                    if (candidate != null && candidate.enabledInHierarchy && candidate.worldBound.width > 1
                        && candidate.resolvedStyle.display != DisplayStyle.None && Member(os, "m_phoneOpenAnimationApp") == null) hangup = candidate;
                    else { Require(Time.realtimeSinceStartup < deadline, "Native answered caller did not expose an enabled Hang Up control"); yield return null; }
                }
                click = ClickAppControl(surface, hangup);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                deadline = Time.realtimeSinceStartup + 10;
                while (NetworkServer.spawned.ContainsKey(id) || NetworkClient.spawned.ContainsKey(id) || Member(os, "m_boundVictimCall") as Component != null)
                { Require(Time.realtimeSinceStartup < deadline, "Controller Hang Up did not remove the native call and release its computer binding"); yield return null; }
                Require(Member(workspace, "NetworkactiveCall") as Component == null && Member(voice, "NetworkcurrentCall") as Component == null
                    && Member(voice, "IsOnCall") is bool ended && !ended, "Native hang-up left workspace or talker ownership active");
                Logger.LogInfo("CALL PASS: controller Hang Up reaches native authority, despawns the caller and clears workspace, computer and local talker state; AI/TTS excluded");
            }
            finally
            {
                VRManager.Instance.Pointer.Cancel(); holdInjectedTrigger = false;
                if (call != null)
                {
                    if (id == 0) id = Convert.ToUInt32(Member(call, "netId"));
                    if (id != 0 && NetworkServer.spawned.ContainsKey(id))
                    {
                        AccessTools.Method(callType, "ServerForceHangUp").Invoke(call, null);
                        if (call != null && NetworkServer.spawned.ContainsKey(id)) NetworkServer.Destroy(call.gameObject);
                    }
                    else Destroy(call.gameObject);
                }
                cooldown.SetValue(workspace, originalCooldown);
                probe.UnpatchSelf(); callFixtureCaller = callFixtureVoice = null;
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { phone });
                if (originalState != "Closed") AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "phone" });
                if (originalState == "OpenMinimized") AccessTools.Method(os.GetType(), "MinimizeApp").Invoke(os, new[] { phone });
            }
        }
    }
}
