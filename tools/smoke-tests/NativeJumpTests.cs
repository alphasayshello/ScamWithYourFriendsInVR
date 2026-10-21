using System;
using System.Collections;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool jumpFixture, jumpHeld, jumpEdge;
        private static void KeepJumpInput(object input)
        {
            if (!jumpFixture) return;
            AccessTools.Property(input.GetType(), "Move").SetValue(input, Vector2.zero);
            AccessTools.Property(input.GetType(), "Turn").SetValue(input, Vector2.zero);
            AccessTools.Property(input.GetType(), "Jump").SetValue(input, jumpHeld);
            AccessTools.Property(input.GetType(), "JumpDown").SetValue(input, jumpEdge);
        }
        private IEnumerator TestNativeJump()
        {
            var manager = VRManager.Instance;
            Component player = NetworkClient.localPlayer;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            object controller = AccessTools.Property(adapter, "Controller").GetValue(null);
            var tick = AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches"), "BeforeController");
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            var body = Member(controller, "CharacterController") as CharacterController;
            Require(body != null, "Jump fixture requires native collision body");
            Vector3 originalPosition = player.transform.position;
            bool focus = manager.Input.HasInputFocus, paused = false, feedbackOwned = false, playersOwned = false;
            Type feedback = Find("FeedbackUi");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated jump floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - originalPosition.y - .12f, 1050);
            var changed = AccessTools.Field(controller.GetType(), "OnJumpInputChanged");
            int presses = 0, releases = 0;
            Action<bool> observe = value => { if (value) presses++; else releases++; };
            changed.SetValue(controller, (changed.GetValue(controller) as Action<bool>) + observe);
            void Drive(bool held)
            {
                jumpEdge = held && !jumpHeld; jumpHeld = held;
                KeepJumpInput(manager.Input); tick.Invoke(null, new[] { controller });
                jumpEdge = false; AccessTools.Property(manager.Input.GetType(), "JumpDown").SetValue(manager.Input, false);
            }
            try
            {
                jumpFixture = true; manager.Pointer.Cancel();
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.8f);
                float deadline = Time.realtimeSinceStartup + 5;
                while (!(Member(controller, "isGrounded") is bool grounded && grounded))
                { Require(Time.realtimeSinceStartup < deadline, "Native jump fixture did not become grounded"); yield return null; }
                float before = player.transform.position.y;
                Drive(true);
                Require((bool)Member(controller, "isJumping") && presses == 1, "Fresh VR jump did not reach native jump state/event once");
                deadline = Time.realtimeSinceStartup + 3;
                while (player.transform.position.y - before < .1f)
                { Require(Time.realtimeSinceStartup < deadline, "Native jump input did not physically lift the player"); yield return null; }
                Drive(false);
                Require(!(bool)Member(controller, "isJumping") && releases > 0, "Jump release did not clear native input/event");
                Logger.LogInfo("JUMP PHYSICS PASS: fresh controller press reaches native jump state/event, lifts actual player and releases normally");
                Drive(true);
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = true; Drive(true);
                Require(!(bool)Member(controller, "isJumping"), "Settings did not cancel held jump");
                int beforeClose = presses;
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = false;
                yield return new WaitForSecondsRealtime(.7f);
                Drive(true);
                Require(!(bool)Member(controller, "isJumping") && presses == beforeClose,
                    "Closing settings restarted held jump without a fresh press");
                Drive(false); Drive(true);
                Require((bool)Member(controller, "isJumping") && presses == beforeClose + 1, "Fresh jump did not recover after settings");
                Logger.LogInfo("JUMP MENU PASS: held jump stays cancelled when settings close; release/fresh press restores exactly one native event");
                Drive(false);
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = true; Drive(true);
                beforeClose = presses;
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = false;
                yield return new WaitForSecondsRealtime(.7f); Drive(true);
                Require(!(bool)Member(controller, "isJumping") && presses == beforeClose, "Jump first pressed inside settings leaked into gameplay on close");
                Drive(false); Drive(true); Drive(false);
                AccessTools.Method(feedback, "Show").Invoke(null, null); feedbackOwned = true; Drive(true); beforeClose = presses;
                AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null); feedbackOwned = false;
                yield return new WaitForSecondsRealtime(.7f); Drive(true);
                Require(!(bool)Member(controller, "isJumping") && presses == beforeClose, "Held jump escaped native feedback into gameplay");
                Drive(false); Drive(true);
                Require((bool)Member(controller, "isJumping") && presses == beforeClose + 1, "Fresh jump did not recover after feedback");
                Drive(false);
                AccessTools.Method(adapter, "TogglePlayers").Invoke(null, null); playersOwned = true; Drive(true); beforeClose = presses;
                AccessTools.Method(adapter, "TogglePlayers").Invoke(null, null); playersOwned = false;
                yield return new WaitForSecondsRealtime(.7f); Drive(true);
                Require(!(bool)Member(controller, "isJumping") && presses == beforeClose, "Held jump escaped players menu into gameplay");
                Drive(false); Drive(true);
                Require((bool)Member(controller, "isJumping") && presses == beforeClose + 1, "Fresh jump did not recover after players menu");
                Logger.LogInfo("JUMP OVERLAY PASS: presses begun inside settings/feedback/players wait for release, with fresh-input recovery");
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, false); Drive(true); beforeClose = presses;
                Require(!(bool)Member(controller, "isJumping"), "XR focus loss did not cancel jump");
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus); Drive(true);
                Require(!(bool)Member(controller, "isJumping") && presses == beforeClose, "Held jump restarted when XR focus returned");
                Drive(false); Drive(true);
                Type controls = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRJumpControls");
                int beforeCleanup = releases;
                AccessTools.Method(controls, "Clear").Invoke(null, null); beforeClose = presses;
                Require(!(bool)Member(controller, "isJumping") && releases == beforeCleanup + 1, "Session cleanup did not release its native jump once");
                Drive(true);
                Require(!(bool)Member(controller, "isJumping") && presses == beforeClose, "Cleanup/restart resumed held jump without a fresh edge");
                Drive(false); Drive(true);
                Require((bool)Member(controller, "isJumping") && presses == beforeClose + 1, "Fresh jump did not recover after session cleanup");
                Logger.LogInfo("JUMP RECOVERY PASS: XR focus/session cleanup release jump and require a fresh press before resuming");
            }
            finally
            {
                if (paused) AccessTools.Method(adapter, "TogglePause").Invoke(null, null);
                if (feedbackOwned) AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null);
                if (playersOwned) AccessTools.Method(adapter, "TogglePlayers").Invoke(null, null);
                AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, focus);
                Drive(false); jumpFixture = false;
                changed.SetValue(controller, (changed.GetValue(controller) as Action<bool>) - observe);
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter(); Destroy(floor);
            }
        }
    }
}
