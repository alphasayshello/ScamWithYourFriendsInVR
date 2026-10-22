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
        private IEnumerator TestNativeSprayRecovery()
        {
            foreach (IEnumerator test in new[] { TestNativeSpray(), TestNativeCombat(), TestNativeRagdoll(), TestNativeRigMotion() })
            {
                try { while (test.MoveNext()) yield return test.Current; }
                finally { (test as IDisposable)?.Dispose(); }
            }
        }

        private static bool sprayGripFixture;
        private static Vector3 sprayGripPosition;
        private static Quaternion sprayGripRotation;
        private static void HoldSprayGrip(Input.VRHandController __instance)
        {
            if (sprayGripFixture && ReferenceEquals(__instance, VRManager.Instance?.Rig?.Right))
                __instance.transform.SetPositionAndRotation(sprayGripPosition, sprayGripRotation);
        }

        private IEnumerator TestNativeSpray()
        {
            var manager = VRManager.Instance;
            var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            Type type = Find("PepperSprayInteractable");
            var prefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(type) != null);
            Require(prefab != null && Member(interactor, "CurrentHeldItem") == null, "Pepper spray check requires native prefab and empty hands");
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            Require(body != null, "Pepper spray check needs native collision body");
            Vector3 originalPosition = interactor.transform.position;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated pepper spray floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - originalPosition.y - .12f, 1050);
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            var probe = new Harmony("com.swyf.vr.peppersprayfixture");
            GameObject item = null;
            Component spray = null;
            bool paused = false, focus = manager.Input.HasInputFocus;
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Press()
            {
                manager.Rig.Right.Aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.right * .35f, Quaternion.LookRotation(Vector3.up));
                Input("Grip", false); Input("LeftGrip", false);
                holdInjectedTrigger = true; Input("Trigger", true); Input("TriggerDown", true);
                tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
            }
            void Release()
            {
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", true);
                tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
            }
            bool Spraying() => spray != null && (bool)Member(spray, "NetworkisSpraying");
            try
            {
                manager.Pointer.Cancel(); Physics.SyncTransforms();
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(1.2f);
                item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity);
                spray = item.GetComponent(type); NetworkServer.Spawn(item);
                AccessTools.Method(type, "CmdInteract").Invoke(spray, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 6;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), spray))
                { Require(Time.realtimeSinceStartup < deadline, "Native pepper spray pickup failed"); yield return null; }
                sprayGripPosition = manager.Rig.Head.transform.position + new Vector3(.45f, -.25f, .6f);
                sprayGripRotation = manager.Rig.Right.transform.rotation; sprayGripFixture = true;
                probe.Patch(AccessTools.Method(typeof(Input.VRHandController), "Sync"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(HoldSprayGrip)));
                yield return new WaitForSecondsRealtime(.6f);
                var begin = Member(spray, "spherecastBegin") as Transform;
                var end = Member(spray, "spherecastEnd") as Transform;
                var particles = Member(spray, "pepperSprayVfx") as ParticleSystem;
                Require(begin != null && end != null && particles != null, "Native spray geometry/effects missing");
                Vector3 beginOffset = Quaternion.Inverse(sprayGripRotation) * (begin.position - sprayGripPosition);
                Vector3 endOffset = Quaternion.Inverse(sprayGripRotation) * (end.position - sprayGripPosition);
                Vector3 headBefore = manager.Rig.Head.transform.position;
                sprayGripPosition += Vector3.right * .25f;
                sprayGripRotation = Quaternion.AngleAxis(30, Vector3.up) * sprayGripRotation;
                yield return new WaitForSecondsRealtime(.6f);
                Require(Vector3.Distance(begin.position, sprayGripPosition + sprayGripRotation * beginOffset) < .03f
                    && Vector3.Distance(end.position, sprayGripPosition + sprayGripRotation * endOffset) < .03f
                    && Vector3.Distance(headBefore, manager.Rig.Head.transform.position) < .1f,
                    "Native pepper spray origin/direction did not follow controller independently of headset");
                Logger.LogInfo("SPRAY AIM PASS: native spherecast endpoints follow 25cm controller movement and 30-degree rotation independently of headset");
                toggle.Invoke(null, null); paused = true; Press();
                yield return new WaitForSecondsRealtime(.2f);
                Require(!Spraying() && !(bool)Member(spray, "isSprayingPressed"), "Settings accepted native spray input");
                toggle.Invoke(null, null); paused = false;
                yield return new WaitForSecondsRealtime(.2f);
                Require(!Spraying(), "Closing settings resumed held spray without a fresh trigger");
                Release(); Press(); deadline = Time.realtimeSinceStartup + 3;
                while (!Spraying()) { Require(Time.realtimeSinceStartup < deadline, "Fresh controller trigger did not start native spray"); yield return null; }
                yield return new WaitForSecondsRealtime(.3f);
                Require(particles.isPlaying && Convert.ToSingle(Member(spray, "SprayTime")) > 0, "Native spray did not play or accumulate use time");
                Logger.LogInfo("SPRAY USE PASS: settings block input and fresh held controller trigger starts native authority state, particles and use time");
                Release(); deadline = Time.realtimeSinceStartup + 3;
                while (Spraying()) { Require(Time.realtimeSinceStartup < deadline, "Trigger release did not stop native spray"); yield return null; }
                Require(!particles.isEmitting, "Released native spray kept emitting particles");
                Press(); yield return new WaitForSecondsRealtime(.2f);
                toggle.Invoke(null, null); paused = true; tick.Invoke(manager.Pointer, null);
                yield return new WaitForSecondsRealtime(.2f);
                Require(!Spraying(), "Opening settings did not cancel held spray");
                toggle.Invoke(null, null); paused = false;
                yield return new WaitForSecondsRealtime(.2f);
                Require(!Spraying(), "Held spray restarted after closing settings");
                Release(); Press(); yield return new WaitForSecondsRealtime(.2f);
                Require(Spraying(), "Fresh spray did not recover after settings");
                Input("HasInputFocus", false); tick.Invoke(manager.Pointer, null); Input("HasInputFocus", focus);
                yield return new WaitForSecondsRealtime(.2f);
                Require(!Spraying(), "XR focus cancellation did not stop native spray or held input resumed it");
                Logger.LogInfo("SPRAY CANCEL PASS: trigger release, settings and XR focus cancellation stop native spray; held trigger cannot resume it");
                float duration = Convert.ToSingle(Member(spray, "spraySeconds"));
                Release(); Press(); deadline = Time.realtimeSinceStartup + duration + 4;
                // Native Update ends by duration before writing another volume
                // sample. A small final-frame remainder is not remaining use.
                while (Spraying() || Convert.ToSingle(Member(spray, "SprayTime")) < duration)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                        Require(false, "Native held spray did not reach authored duration and stop; time=" + Member(spray, "SprayTime")
                            + "; duration=" + duration + "; volume=" + Member(spray, "currentVolume") + "; spraying=" + Spraying());
                    yield return null;
                }
                Require(Convert.ToSingle(Member(spray, "currentVolume")) < 1, "Native spray volume never depleted");
                Release(); Press(); yield return new WaitForSecondsRealtime(.2f);
                Require(!Spraying() && !particles.isEmitting, "Empty native spray restarted from fresh controller input");
                Logger.LogInfo("SPRAY DEPLETION PASS: authored duration stops native spray, volume falls and fresh controller input cannot sustain an expired can; finalVolume=" + Member(spray, "currentVolume"));
                Release(); AccessTools.Method(interactor.GetType(), "ServerDropHeldItem").Invoke(interactor, new object[] { Vector3.zero, Vector3.zero });
                yield return new WaitForSecondsRealtime(.2f);
                Require(Member(spray, "Holder") == null && Member(interactor, "CurrentHeldItem") == null
                    && !Spraying() && !(bool)Member(spray, "isSprayingPressed") && !(bool)Member(spray, "localIsSpraying"),
                    "Native spray drop retained input, holder or network state");
                Logger.LogInfo("SPRAY DROP PASS: native drop clears holder, pressed/local/network spray state");
            }
            finally
            {
                if (paused) toggle.Invoke(null, null);
                Input("HasInputFocus", focus); Release(); manager.Pointer.Cancel();
                sprayGripFixture = false; probe.UnpatchSelf();
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), spray)) AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter(); Destroy(floor);
            }
            Logger.LogInfo("SPRAY COMPLETE PASS: native controller spray aim, hold/release, settings/focus recovery, authored depletion and drop checked");
        }
    }
}
