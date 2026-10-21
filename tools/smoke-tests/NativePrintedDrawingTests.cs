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
        private static Component drawingFixture;
        private static bool drawingProbeOnly;
        private static int drawingPlaceCalls, drawingThrowCalls;
        private static Vector3 drawingOrigin, drawingDirection;
        private static bool ObserveDrawingPlace(object __instance, Vector3 __0, Vector3 __1)
        {
            if (!ReferenceEquals(__instance, drawingFixture)) return true;
            drawingPlaceCalls++; drawingOrigin = __0; drawingDirection = __1; return !drawingProbeOnly;
        }
        private static bool ObserveDrawingThrow(object __instance, Vector3 __0)
        {
            if (!ReferenceEquals(__instance, drawingFixture)) return true;
            drawingThrowCalls++; drawingDirection = __0; return !drawingProbeOnly;
        }

        private IEnumerator TestNativePrintedDrawing()
        {
            var manager = VRManager.Instance;
            Type type = Find("JwPrintedDrawing");
            var interactor = NetworkClient.localPlayer.GetComponent(Find("PlayerInteractor"));
            GameObject prefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(type) != null);
            Require(prefab != null && Member(interactor, "CurrentHeldItem") == null, "Native drawing fixture needs its prefab and empty hands");
            GameObject item = null, wall = null;
            var image = new Texture2D(8, 8, TextureFormat.RGB24, false);
            var probe = new Harmony("com.swyf.vr.drawingfixture");
            bool focus = manager.Input.HasInputFocus, tracked = manager.Rig.Right.IsTracked;
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            Vector3 origin = manager.Rig.Head.transform.position + Vector3.right * .35f;
            void Aim() => manager.Rig.Right.Aim.SetPositionAndRotation(origin, Quaternion.LookRotation(Vector3.right));
            try
            {
                image.SetPixels(Enumerable.Repeat(Color.blue, 64).ToArray()); image.Apply();
                byte[] bytes = (byte[])AccessTools.Method(Find("JwPrintImage"), "Capture").Invoke(null, new object[] { image });
                item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity);
                drawingFixture = item.GetComponent(type);
                AccessTools.Method(type, "ServerSetImage").Invoke(drawingFixture, new object[] { bytes });
                NetworkServer.Spawn(item);
                probe.Patch(AccessTools.Method(type, "CmdPlace"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveDrawingPlace)));
                probe.Patch(AccessTools.Method(type, "CmdThrow"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ObserveDrawingThrow)));
                AccessTools.Method(type, "CmdInteract").Invoke(drawingFixture, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 5;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), drawingFixture))
                { Require(Time.realtimeSinceStartup < deadline, "Native printed drawing pickup did not complete"); yield return null; }
                yield return new WaitForEndOfFrame();
                drawingProbeOnly = true;
                var camera = UnityEngine.Camera.main;
                void NativeFallback(string reason)
                {
                    drawingPlaceCalls = drawingThrowCalls = 0; Aim();
                    AccessTools.Method(type, "OnPlacePressed").Invoke(drawingFixture, new object[] { true });
                    Require(drawingPlaceCalls == 1 && Vector3.Distance(drawingOrigin, camera.transform.position) < .001f
                        && Vector3.Angle(drawingDirection, camera.transform.forward) < .01f, "Drawing placement lost native fallback: " + reason);
                    AccessTools.Method(type, "OnThrowPressed").Invoke(drawingFixture, new object[] { true });
                    Require(drawingThrowCalls == 1 && Vector3.Angle(drawingDirection, camera.transform.forward) < .01f,
                        "Drawing throw lost native fallback: " + reason);
                }
                Input("HasInputFocus", false); NativeFallback("focus"); Input("HasInputFocus", focus);
                manager.Rig.Right.GetType().GetProperty("IsTracked").SetValue(manager.Rig.Right, false);
                NativeFallback("tracking"); manager.Rig.Right.GetType().GetProperty("IsTracked").SetValue(manager.Rig.Right, tracked);
                drawingProbeOnly = false; drawingPlaceCalls = drawingThrowCalls = 0;
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "SWYF isolated artwork wall";
                wall.transform.position = origin + Vector3.right * 2; wall.transform.localScale = new Vector3(.1f, 2, 2);
                Physics.SyncTransforms();
                Input("Grip", false); Input("LeftGrip", false);
                Aim(); Input("Secondary", true); Input("SecondaryDown", true); tick.Invoke(manager.Pointer, null); Input("SecondaryDown", false);
                Input("Secondary", false); tick.Invoke(manager.Pointer, null);
                deadline = Time.realtimeSinceStartup + 5;
                while (!(bool)Member(drawingFixture, "IsPlaced"))
                { Require(Time.realtimeSinceStartup < deadline, "Controller secondary did not place native drawing; commands=" + drawingPlaceCalls); yield return null; }
                Require(drawingPlaceCalls == 1 && Vector3.Distance(drawingOrigin, origin) < .001f
                    && Vector3.Angle(drawingDirection, Vector3.right) < .01f && Member(interactor, "CurrentHeldItem") == null
                    && ReferenceEquals(Member(drawingFixture, "surface"), wall.transform),
                    "Native drawing placement did not use one controller-directed authority command and selected wall");
                Vector3 attachedPoint = wall.transform.InverseTransformPoint(item.transform.position);
                wall.transform.position += Vector3.up * .2f; wall.transform.rotation = Quaternion.Euler(0, 15, 0);
                yield return null; yield return new WaitForEndOfFrame();
                Require(Vector3.Distance(item.transform.position, wall.transform.TransformPoint(attachedPoint)) < .001f,
                    "Native placed drawing did not stay attached to its moved wall");
                Logger.LogInfo("ARTWORK PLACE PASS: controller secondary uses native placement authority/range/surface checks, drops held ownership and follows the moved wall");
                AccessTools.Method(type, "CmdInteract").Invoke(drawingFixture, new object[] { interactor });
                deadline = Time.realtimeSinceStartup + 5;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), drawingFixture))
                { Require(Time.realtimeSinceStartup < deadline, "Placed drawing could not be picked up again"); yield return null; }
                Require(!(bool)Member(drawingFixture, "IsPlaced"), "Re-pickup retained native drawing attachment");
                Aim(); Input("Trigger", true); Input("TriggerDown", true); tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                Input("Trigger", false); Input("TriggerUp", true); tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                deadline = Time.realtimeSinceStartup + 5;
                while (Member(interactor, "CurrentHeldItem") != null)
                { Require(Time.realtimeSinceStartup < deadline, "Controller trigger did not throw the printed drawing"); yield return null; }
                Require(drawingThrowCalls == 1 && Vector3.Angle(drawingDirection, Vector3.right) < .01f,
                    "Native printed drawing throw did not use controller direction exactly once");
                yield return new WaitForFixedUpdate();
                var body = Member(Member(drawingFixture, "ephemeralRigidbody"), "Rigidbody") as Rigidbody;
                Require(body != null && body.linearVelocity.x > .1f, "Native drawing throw produced no controller-directed rigidbody velocity");
                Logger.LogInfo("ARTWORK THROW PASS: controller trigger releases native held ownership and produces physical velocity along controller aim; focus/tracking fallback preserved");
            }
            finally
            {
                Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false); Input("Secondary", false); Input("SecondaryDown", false);
                Input("HasInputFocus", focus); manager.Rig.Right.GetType().GetProperty("IsTracked").SetValue(manager.Rig.Right, tracked);
                manager.Pointer.Cancel();
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), drawingFixture))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                probe.UnpatchSelf(); drawingFixture = null; drawingProbeOnly = false;
                if (wall != null) Destroy(wall); Destroy(image);
            }
        }
    }
}
