using System;
using System.Collections;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool peeFixture, peeHeld, peeEdge;
        private static void KeepPeeInput(object input)
        {
            if (!peeFixture) return;
            AccessTools.Property(input.GetType(), "Move").SetValue(input, Vector2.zero);
            AccessTools.Property(input.GetType(), "Turn").SetValue(input, Vector2.zero);
            AccessTools.Property(input.GetType(), "Pee").SetValue(input, peeHeld);
            AccessTools.Property(input.GetType(), "PeeDown")?.SetValue(input, peeEdge);
        }
        private IEnumerator TestNativePee()
        {
            var manager = VRManager.Instance;
            Component player = NetworkClient.localPlayer;
            object native = player.GetComponent(Find("Metater.MetaPlayer"));
            Component stream = player.GetComponentInChildren(Find("PeeNetworking"), true);
            Require(stream != null, "Native pee networking component unavailable");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            Type playerPatches = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches");
            object controller = AccessTools.Property(adapter, "Controller").GetValue(null);
            var tick = AccessTools.Method(playerPatches, "BeforeController");
            var handle = AccessTools.Method(native.GetType(), "HandlePInput");
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            var body = Member(controller, "CharacterController") as CharacterController;
            Require(body != null, "Pee fixture needs native collision body");
            Vector3 originalPosition = player.transform.position;
            bool paused = false, feedbackOwned = false, playersOwned = false;
            bool focus = manager.Input.HasInputFocus;
            Type feedback = Find("FeedbackUi");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated body-action floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - originalPosition.y - .12f, 1050);
            var changed = AccessTools.Field(native.GetType(), "OnPInputChanged");
            int starts = 0, stops = 0;
            Action<bool> observe = value => { if (value) starts++; else stops++; };
            changed.SetValue(native, (changed.GetValue(native) as Action<bool>) + observe);
            void Drive(bool held)
            {
                peeEdge = held && !peeHeld; peeHeld = held;
                KeepPeeInput(manager.Input); tick.Invoke(null, new[] { controller }); handle.Invoke(native, null);
                peeEdge = false; AccessTools.Property(manager.Input.GetType(), "PeeDown")?.SetValue(manager.Input, false);
            }
            try
            {
                peeFixture = true; manager.Pointer.Cancel();
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.8f);
                float deadline = Time.realtimeSinceStartup + 5;
                while (!(AccessTools.Property(playerPatches, "GameplayInput").GetValue(null) is bool ready && ready))
                { Require(Time.realtimeSinceStartup < deadline, "Native gameplay was not ready for pee check"); yield return null; }
                Drive(true);
                deadline = Time.realtimeSinceStartup + 2;
                while (!(bool)Member(stream, "NetworkisPeeing"))
                { Require(Time.realtimeSinceStartup < deadline, "Native pee command did not synchronize; input=" + Member(native, "wasPPressed") + " starts=" + starts); yield return null; }
                Require((bool)Member(native, "wasPPressed") && (bool)Member(stream, "NetworkisPeeing") && starts == 1,
                    "Fresh VR pee chord did not reach native event/network state once");
                Drive(false);
                Require(!(bool)Member(native, "wasPPressed") && stops > 0,
                    "Pee chord release did not stop native stream");
                Logger.LogInfo("PEE NATIVE PASS: fresh chord starts native event/network stream once; release stops it");
                Drive(true); AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = true; Drive(true);
                Require(!(bool)Member(native, "wasPPressed"), "Settings did not cancel native pee stream");
                int beforeClose = starts;
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = false;
                yield return new WaitForSecondsRealtime(.7f); Drive(true);
                Require(!(bool)Member(native, "wasPPressed") && starts == beforeClose,
                    "Closing settings restarted held pee chord without a fresh press");
                Drive(false); Drive(true);
                Require((bool)Member(native, "wasPPressed") && starts == beforeClose + 1, "Fresh pee chord did not recover after settings");
                Logger.LogInfo("PEE MENU PASS: held chord stays cancelled when settings close and resumes only after fresh chord input");
                Drive(false);
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = true; Drive(true); beforeClose = starts;
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = false;
                yield return new WaitForSecondsRealtime(.7f); Drive(true);
                Require(!(bool)Member(native, "wasPPressed") && starts == beforeClose, "Pee first pressed inside settings escaped on close");
                Drive(false); Drive(true); Drive(false);
                AccessTools.Method(feedback, "Show").Invoke(null, null); feedbackOwned = true; Drive(true); beforeClose = starts;
                AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null); feedbackOwned = false;
                yield return new WaitForSecondsRealtime(.7f); Drive(true);
                Require(!(bool)Member(native, "wasPPressed") && starts == beforeClose, "Held pee escaped feedback into gameplay");
                Drive(false); Drive(true);
                Require((bool)Member(native, "wasPPressed") && starts == beforeClose + 1, "Fresh pee did not recover after feedback");
                Drive(false);
                AccessTools.Method(adapter, "TogglePlayers").Invoke(null, null); playersOwned = true; Drive(true); beforeClose = starts;
                AccessTools.Method(adapter, "TogglePlayers").Invoke(null, null); playersOwned = false;
                yield return new WaitForSecondsRealtime(.7f); Drive(true);
                Require(!(bool)Member(native, "wasPPressed") && starts == beforeClose, "Held pee escaped players menu into gameplay");
                Drive(false); Drive(true);
                Require((bool)Member(native, "wasPPressed") && starts == beforeClose + 1, "Fresh pee did not recover after players menu");
                Logger.LogInfo("PEE OVERLAY PASS: chords begun in settings/feedback/players wait for release and recover on fresh input");
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, false); Drive(true); beforeClose = starts;
                Require(!(bool)Member(native, "wasPPressed"), "XR focus loss retained native pee stream");
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus); Drive(true);
                Require(!(bool)Member(native, "wasPPressed") && starts == beforeClose, "Held pee restarted when focus returned");
                Drive(false); Drive(true);
                Type controls = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRPeeControls");
                int beforeCleanup = stops;
                AccessTools.Method(controls, "Clear").Invoke(null, null); beforeClose = starts;
                Require(!(bool)Member(native, "wasPPressed") && stops == beforeCleanup + 1,
                    "Session cleanup did not release owned native pee stream exactly once");
                AccessTools.Method(controls, "Clear").Invoke(null, null);
                Require(stops == beforeCleanup + 1, "Repeated cleanup duplicated native pee release");
                Drive(true);
                Require(!(bool)Member(native, "wasPPressed") && starts == beforeClose, "Cleanup restarted held pee without fresh input");
                Drive(false); Drive(true);
                Require((bool)Member(native, "wasPPressed") && starts == beforeClose + 1, "Fresh pee did not recover after session cleanup");
                Drive(false);
                deadline = Time.realtimeSinceStartup + 2;
                while ((bool)Member(stream, "NetworkisPeeing"))
                { Require(Time.realtimeSinceStartup < deadline, "Native pee release did not synchronize"); yield return null; }
                Logger.LogInfo("PEE RECOVERY PASS: focus and session cleanup stop owned native stream once and require a fresh chord");
            }
            finally
            {
                if (paused) AccessTools.Method(adapter, "TogglePause").Invoke(null, null);
                if (feedbackOwned) AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null);
                if (playersOwned) AccessTools.Method(adapter, "TogglePlayers").Invoke(null, null);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus);
                Drive(false); peeFixture = false;
                changed.SetValue(native, (changed.GetValue(native) as Action<bool>) - observe);
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter(); Destroy(floor);
            }
        }
    }
}
