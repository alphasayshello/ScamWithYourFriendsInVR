using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool injectGiveUp;
        private static Component ragdollProbe;
        private static int struggleCommands;
        private static void ObserveStruggle(object __instance)
        { if (ReferenceEquals(__instance, ragdollProbe)) struggleCommands++; }

        private IEnumerator TestNativeRagdoll()
        {
            var manager = VRManager.Instance;
            Component player = NetworkClient.localPlayer;
            Component ragdoll = player.GetComponent(Find("PlayerRagdollManager"));
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            var tick = AccessTools.Method(manager.BodyActions.GetType(), "Update");
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            Vector3 original = player.transform.position;
            Require(body != null && body.enabled && !(bool)Member(ragdoll, "IsRagdollActive"), "Ragdoll fixture requires a recovered native body");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated ragdoll fixture floor";
            floor.transform.position = new Vector3(1000, 50 + body.bounds.min.y - original.y - .12f, 1000);
            floor.transform.localScale = new Vector3(20, .2f, 20);
            var probe = new Harmony("com.swyf.vr.ragdollprobe");
            probe.Patch(AccessTools.Method(ragdoll.GetType(), "CmdJumpedWhileRagdolled"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveStruggle)));
            ragdollProbe = ragdoll; struggleCommands = 0;
            bool paused = false;
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            Multiplayer.PoseFrame Pose() => (Multiplayer.PoseFrame)AccessTools.Field(typeof(Multiplayer.PoseNetwork), "local").GetValue(Multiplayer.PoseNetwork.Instance);
            void GiveUpPress()
            {
                Input("GiveUp", true); Input("GiveUpDown", true); tick.Invoke(manager.BodyActions, null); Input("GiveUpDown", false);
            }
            try
            {
                Physics.SyncTransforms(); teleport.Invoke(controller, new object[] { new Vector3(1000, 50, 1000), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.8f);
                bool began = (bool)AccessTools.Method(ragdoll.GetType(), "ServerBeginRagdoll").Invoke(ragdoll,
                    new object[] { Vector3.zero, 3f, Vector3.zero, false, false, true, false });
                Require(began, "Native server refused ragdoll fixture");
                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForSecondsRealtime(.6f);
                yield return new WaitForEndOfFrame();
                Transform eye = AccessTools.Method(ragdoll.GetType(), "GetCurrentRagdollCameraTransform").Invoke(ragdoll, null) as Transform;
                object local = AccessTools.Property(adapter, "LocalPlayer").GetValue(null);
                object selectedRagdoll = AccessTools.Method(adapter, "Read").Invoke(null, new object[] { local, "RagdollManager" });
                Logger.LogInfo("RAGDOLL anchor diagnostic: direct=" + eye?.position + "; view=" + manager.Rig.Head.transform.position
                    + "; nativeParent=" + manager.Rig.Head.transform.parent?.name + "; local=" + local
                    + "; selected=" + selectedRagdoll + "; actual=" + ragdoll + "; active=" + Member(ragdoll, "IsRagdollActive"));
                Transform chest = AccessTools.Method(ragdoll.GetType(), "GetCurrentChestTransform").Invoke(ragdoll, null) as Transform;
                Require(eye != null ? Vector3.Distance(manager.Rig.Head.transform.position, eye.position) < .35f
                        : chest != null && Vector3.Distance(manager.Rig.Head.transform.position, chest.position) < .75f,
                    "VR view did not follow the native ragdoll camera anchor");
                Require(Pose().Tracked == 0, "Ragdoll retained tracked-avatar ownership");
                object hints = Instance("StrugglingAndGiveUpUi");
                foreach (string key in new[] { "struggleText", "giveUpText", "escapeText" })
                    if (Member(hints, key) is GameObject root)
                        foreach (TMPro.TMP_Text label in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
                            Logger.LogInfo("RAGDOLL HINT: " + key + "/" + label.name + "=" + label.text.Replace('\n', ' '));
                Input("JumpDown", true); tick.Invoke(manager.BodyActions, null); Input("JumpDown", false); tick.Invoke(manager.BodyActions, null);
                Require(struggleCommands == 1, "Ragdoll jump did not issue exactly one native struggle command");
                Logger.LogInfo("RAGDOLL PASS: native camera anchor, cleared tracked pose and controller struggle command");

                injectGiveUp = true; GiveUpPress();
                yield return new WaitForSecondsRealtime(.15f);
                Require((bool)Member(ragdoll, "localIsGiveUpPressed") && Convert.ToSingle(Member(ragdoll, "localGiveUpTimer")) > 0,
                    "Native give-up hold did not begin its normal charge");
                toggle.Invoke(null, null); paused = true; tick.Invoke(manager.BodyActions, null);
                Require(!(bool)Member(ragdoll, "localIsGiveUpPressed"), "Settings left give-up held");
                toggle.Invoke(null, null); paused = false;
                yield return null;
                Require(!(bool)Member(ragdoll, "localIsGiveUpPressed"), "Closing settings restarted held give-up without a new press");
                GiveUpPress(); manager.BodyActions.enabled = false;
                Require(!(bool)Member(ragdoll, "localIsGiveUpPressed"), "Disabling body actions left give-up held");
                manager.BodyActions.enabled = true;
                GiveUpPress(); Input("HasInputFocus", false); tick.Invoke(manager.BodyActions, null); Input("HasInputFocus", true);
                Require(!(bool)Member(ragdoll, "localIsGiveUpPressed"), "Focus loss left give-up held");
                injectGiveUp = false; Input("GiveUp", false); Input("GiveUpDown", false);
                Logger.LogInfo("RAGDOLL PASS: native short give-up charge, menu/focus/disable cancellation and no held-input restart");

                float deadline = Time.realtimeSinceStartup + 12;
                while ((bool)Member(ragdoll, "IsRagdollActive"))
                { Require(Time.realtimeSinceStartup < deadline, "Native ragdoll did not recover"); yield return null; }
                deadline = Time.realtimeSinceStartup + 3;
                while (!body.enabled || (Pose().Tracked & 1) == 0)
                { Require(Time.realtimeSinceStartup < deadline, "Body collision or tracked avatar failed to resume after recovery"); yield return null; }
                yield return new WaitForSecondsRealtime(.8f); yield return new WaitForEndOfFrame();
                Require(Vector3.Distance(manager.Rig.Head.transform.position, manager.Rig.AuthoredEye) < .15f,
                    "Recovered camera retained its ragdoll offset");
                Logger.LogInfo("RAGDOLL PASS: native recovery restores collision, tracked avatar and authored eye alignment");
            }
            finally
            {
                injectGiveUp = false; Input("GiveUp", false); Input("GiveUpDown", false); Input("JumpDown", false); Input("HasInputFocus", true);
                manager.BodyActions.Cancel(); manager.BodyActions.enabled = true;
                if (paused) toggle.Invoke(null, null);
                if ((bool)Member(ragdoll, "IsRagdollActive")) AccessTools.Method(ragdoll.GetType(), "ServerRecover").Invoke(ragdoll, null);
                teleport.Invoke(controller, new object[] { original, true }); manager.Rig.Recenter();
                probe.UnpatchSelf(); ragdollProbe = null; Destroy(floor);
            }
        }
    }
}
