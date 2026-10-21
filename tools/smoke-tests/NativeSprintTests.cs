using System;
using System.Collections;
using BepInEx.Configuration;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool sprintFixture;
        private static Vector2 sprintMove;
        private static void KeepSprintInput(object input)
        {
            if (!sprintFixture) return;
            AccessTools.Property(input.GetType(), "Move").SetValue(input, sprintMove);
            AccessTools.Property(input.GetType(), "Turn").SetValue(input, Vector2.zero);
            AccessTools.Property(input.GetType(), "Jump").SetValue(input, false);
            AccessTools.Property(input.GetType(), "JumpDown").SetValue(input, false);
        }
        private IEnumerator TestNativeSprint()
        {
            var manager = VRManager.Instance;
            Component player = NetworkClient.localPlayer;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            object controller = AccessTools.Property(adapter, "Controller").GetValue(null);
            var tick = AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.PlayerPatches"), "BeforeController");
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            var body = Member(controller, "CharacterController") as CharacterController;
            Require(body != null, "Sprint fixture needs native collision body");
            var automatic = typeof(Plugin).GetField("AutoSprint")?.GetValue(null) as ConfigEntry<bool>;
            bool originalAutomatic = automatic?.Value ?? false;
            var threshold = typeof(Plugin).GetField("SprintThreshold")?.GetValue(null) as ConfigEntry<float>;
            float originalThreshold = threshold?.Value ?? .85f;
            object settings = Instance("SettingsUi");
            var toggleSprint = AccessTools.Property(settings.GetType(), "ToggleSprint");
            bool originalToggle = (bool)toggleSprint.GetValue(settings);
            Vector3 originalPosition = player.transform.position;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated sprint floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - originalPosition.y - .12f, 1050);
            bool originalFocus = manager.Input.HasInputFocus, paused = false, feedbackOwned = false;
            Type feedback = Find("FeedbackUi");
            object sprintBlockers = Member(Member(controller, "blocker"), "SprintBlockers");
            var sprintEvent = AccessTools.Field(controller.GetType(), "OnSprintInputChanged");
            int events = 0; bool eventValue = false;
            Action<bool> observe = value => { events++; eventValue = value; };
            sprintEvent.SetValue(controller, (sprintEvent.GetValue(controller) as Action<bool>) + observe);
            void Focus(bool value) => AccessTools.Property(manager.Input.GetType(), "HasInputFocus").SetValue(manager.Input, value);
            void NativeSprint(bool pressed) => AccessTools.Method(typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.NativeItemInput"), "SendNamed")
                .Invoke(null, new object[] { controller, "OnSprint", pressed });
            void Drive(Vector2 move)
            { sprintMove = move; KeepSprintInput(manager.Input); tick.Invoke(null, new[] { controller }); }
            try
            {
                if (automatic != null) automatic.Value = true;
                if (threshold != null) threshold.Value = .85f;
                toggleSprint.SetValue(settings, false);
                sprintFixture = true; manager.Pointer.Cancel();
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.8f);
                Require(!manager.Rig.HeadObstructed, "Sprint fixture did not obtain a clear head");
                Drive(Vector2.up);
                Require((bool)Member(controller, "isSprinting"), "Full forward VR stick did not activate native sprint");
                Require(automatic != null && threshold != null && events > 0 && eventValue,
                    "Sprint did not use native state/event callbacks");
                Drive(new Vector2(0, .8f)); Require((bool)Member(controller, "isSprinting"), "Auto sprint flickered below its entry threshold");
                Drive(new Vector2(0, .6f)); Require(!(bool)Member(controller, "isSprinting") && !eventValue, "Partial stick did not release native sprint");
                Drive(new Vector2(0, .8f)); Require(!(bool)Member(controller, "isSprinting"), "Auto sprint restarted below entry threshold");
                Drive(Vector2.right); Require((bool)Member(controller, "isSprinting"), "Full strafe stick did not retain native sprint direction support");
                Drive(Vector2.down); Require((bool)Member(controller, "isSprinting"), "Full reverse stick did not retain native sprint direction support");
                Drive(Vector2.zero);
                Logger.LogInfo("SPRINT INPUT PASS: full stick in native movement directions drives native sprint/events; threshold hysteresis and neutral release work");
                toggleSprint.SetValue(settings, true); Drive(Vector2.up);
                Require((bool)Member(controller, "isSprinting") && (bool)Member(controller, "toggleSprintActive"), "Native toggle sprint did not latch on");
                Drive(Vector2.zero);
                Require(!(bool)Member(controller, "isSprinting") && !(bool)Member(controller, "toggleSprintActive"), "Releasing auto sprint left native toggle latched");
                toggleSprint.SetValue(settings, false); Drive(Vector2.up); toggleSprint.SetValue(settings, true); Drive(Vector2.up);
                Require((bool)Member(controller, "isSprinting") && (bool)Member(controller, "toggleSprintActive"), "Changing native sprint mode lost running state");
                toggleSprint.SetValue(settings, false); Drive(Vector2.up);
                Require((bool)Member(controller, "isSprinting") && !(bool)Member(controller, "toggleSprintActive"), "Returning to hold mode retained toggle latch");
                Logger.LogInfo("SPRINT MODES PASS: native hold/toggle callbacks start and release correctly, including mode changes while running");
                AccessTools.Method(sprintBlockers.GetType(), "Add").Invoke(sprintBlockers, new object[] { this }); Drive(Vector2.up);
                Require(!(bool)Member(controller, "isSprinting") && !(bool)Member(controller, "IsActuallySprinting"), "Auto sprint ignored native sprint blocker");
                AccessTools.Method(sprintBlockers.GetType(), "Remove").Invoke(sprintBlockers, new object[] { this }); Drive(Vector2.up);
                Require((bool)Member(controller, "IsActuallySprinting"), "Native sprint did not recover after blocker removal");
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = true; Drive(Vector2.up);
                Require(!(bool)Member(controller, "isSprinting"), "Settings allowed auto sprint");
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = false;
                yield return new WaitForSecondsRealtime(.7f);
                Drive(Vector2.zero); Drive(Vector2.up); Focus(false); Drive(Vector2.up);
                Require(!(bool)Member(controller, "isSprinting"), "Lost XR focus retained auto sprint");
                Focus(originalFocus); Drive(Vector2.zero); Drive(Vector2.up);
                AccessTools.Method(feedback, "Show").Invoke(null, null); feedbackOwned = true; Drive(Vector2.up);
                Require(!(bool)Member(controller, "isSprinting"), "Native feedback allowed auto sprint");
                AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null); feedbackOwned = false; Drive(Vector2.zero);
                Logger.LogInfo("SPRINT GUARDS PASS: native sprint blockers, settings, feedback and XR focus stop sprint without a stuck latch");
                automatic.Value = false; Drive(Vector2.zero); NativeSprint(true); Drive(Vector2.up);
                Require((bool)Member(controller, "isSprinting"), "Disabled auto sprint overwrote ordinary native sprint input");
                NativeSprint(false); automatic.Value = true; Drive(Vector2.up);
                Type controls = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRSprintControls");
                AccessTools.Method(controls, "Clear").Invoke(null, null);
                Require(!(bool)Member(controller, "isSprinting"), "VR session cleanup left sprint held");
                Drive(Vector2.zero); Drive(Vector2.up);
                Require((bool)Member(controller, "isSprinting"), "VR sprint did not recover after cleanup");
                automatic.Value = false; Drive(Vector2.zero);
                Logger.LogInfo("SPRINT OWNERSHIP PASS: disabling preserves ordinary native input; session cleanup releases its own sprint and recovers");
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.35f); Drive(Vector2.up);
                Vector3 walkStart = player.transform.position;
                yield return new WaitForSecondsRealtime(.6f);
                float walk = Vector3.ProjectOnPlane(player.transform.position - walkStart, Vector3.up).magnitude;
                Drive(Vector2.zero); automatic.Value = true;
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.35f); Drive(Vector2.up);
                Vector3 runStart = player.transform.position;
                yield return new WaitForSecondsRealtime(.6f);
                float run = Vector3.ProjectOnPlane(player.transform.position - runStart, Vector3.up).magnitude;
                Require(walk > .1f && run > walk * 1.15f,
                    "Native auto sprint did not produce faster actual movement; walk=" + walk + "; sprint=" + run);
                Logger.LogInfo("SPRINT PHYSICS PASS: same full stick and native motor move farther with sprint; walk=" + walk + "; sprint=" + run);

            }
            finally
            {
                Focus(originalFocus);
                if (paused) AccessTools.Method(adapter, "TogglePause").Invoke(null, null);
                if (feedbackOwned) AccessTools.Method(feedback, "TryDismissWithEscape").Invoke(null, null);
                AccessTools.Method(sprintBlockers.GetType(), "Remove").Invoke(sprintBlockers, new object[] { this });
                Drive(Vector2.zero); sprintFixture = false;
                sprintEvent.SetValue(controller, (sprintEvent.GetValue(controller) as Action<bool>) - observe);
                if (automatic != null) automatic.Value = originalAutomatic;
                if (threshold != null) threshold.Value = originalThreshold;
                toggleSprint.SetValue(settings, originalToggle);
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter();
                Destroy(floor);
            }
        }
    }
}
