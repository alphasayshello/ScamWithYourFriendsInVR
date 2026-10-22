using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private Color32[][] CaptureXrayEyes(string phase)
        {
            var displays = new List<XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(displays);
            var images = new List<Color32[]>();
            foreach (var display in displays.Where(d => d.running))
            {
                int count = display.GetRenderPassCount();
                for (int i = 0; i < count; i++)
                {
                    RenderTexture target = display.GetRenderTextureForRenderPass(i);
                    Require(target != null && target.IsCreated() && target.dimension == TextureDimension.Tex2D && target.antiAliasing == 1,
                        "Eye readback requires the actual mock display's separate, resolved eye targets");
                    RenderTexture saved = RenderTexture.active;
                    Texture2D image = null;
                    try
                    {
                        RenderTexture.active = target;
                        image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                        images.Add(image.GetPixels32());
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(readyFile),
                            "xray-" + phase + "-eye-" + i + ".png"), image.EncodeToPNG());
                    }
                    finally { RenderTexture.active = saved; if (image != null) Destroy(image); }
                }
            }
            Require(images.Count == 2 && VRManager.Instance.Rig.Head.stereoEnabled, "Both native XR eye targets must render");
            return images.ToArray();
        }

        private IEnumerator TestNativeXray()
        {
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            var interactor = player.GetComponent(Find("PlayerInteractor"));
            var meta = player.GetComponent(Find("Metater.MetaPlayer"));
            Type gogglesType = Find("XrayGogglesInteractable"), contrabandType = Find("Contraband");
            var gogglesPrefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(gogglesType) != null);
            Type bagType = Find("BagInteractable"), sprayType = Find("PepperSprayInteractable");
            var targetPrefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(bagType) != null
                && go.GetComponent(contrabandType) is Component contraband && Member(contraband, "contrabandRedOutline") is Behaviour);
            var contentPrefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(sprayType) != null);
            var xrayField = AccessTools.Field(meta.GetType(), "LocalIsXrayEnabled");
            Logger.LogInfo("XRAY AVAILABILITY: goggles=" + gogglesPrefab?.name + "; target=" + targetPrefab?.name
                + "; held=" + Member(interactor, "CurrentHeldItem") + "; active=" + xrayField.GetValue(null)
                + "; loadedGoggles=" + Resources.FindObjectsOfTypeAll(gogglesType).Length);
            Require(gogglesPrefab != null && targetPrefab != null && contentPrefab != null && Member(interactor, "CurrentHeldItem") == null && !(bool)xrayField.GetValue(null),
                "Xray check requires native goggles/contraband, empty hands and no existing effect");
            var volumeOwner = Instance("XrayVolume");
            var volume = Member(volumeOwner, "volume") as Component;
            Require(volume != null && Member(volume, "sharedProfile") != null, "Native xray volume/profile unavailable");
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            Require(body != null, "Xray check needs native collision body");
            Vector3 originalPosition = player.transform.position;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated xray floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - originalPosition.y - .12f, 1050);
            GameObject item = null, target = null, content = null;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var toggle = AccessTools.Method(adapter, "TogglePause");
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            bool paused = false;
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Press()
            {
                manager.Rig.Right.Aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.right * .35f, Quaternion.LookRotation(Vector3.up));
                Input("Grip", false); Input("LeftGrip", false); holdInjectedTrigger = true;
                Input("Trigger", true); Input("TriggerDown", true); tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
            }
            void Release()
            {
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", true);
                tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
            }
            try
            {
                manager.Pointer.Cancel(); Physics.SyncTransforms();
                teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(1.2f); yield return new WaitForEndOfFrame();
                target = Instantiate(targetPrefab, manager.Rig.Head.transform.position + manager.Rig.Head.transform.forward * 3, Quaternion.identity);
                foreach (var rigidbody in target.GetComponentsInChildren<Rigidbody>()) rigidbody.isKinematic = true;
                NetworkServer.Spawn(target);
                var contraband = target.GetComponent(contrabandType);
                var outline = Member(contraband, "contrabandRedOutline") as Behaviour;
                content = Instantiate(contentPrefab, player.transform.position + Vector3.up, Quaternion.identity); NetworkServer.Spawn(content);
                var stored = content.GetComponent(Find("HeldItemInteractable"));
                AccessTools.Method(stored.GetType(), "CmdInteract").Invoke(stored, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 6;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), stored))
                { Require(Time.realtimeSinceStartup < deadline, "Native contraband pickup failed"); yield return null; }
                var bag = target.GetComponent(bagType);
                Require((bool)AccessTools.Method(bagType, "CanStoreItemInsideBag").Invoke(bag, new object[] { stored }), "Native bag rejects contraband fixture");
                AccessTools.Method(bagType, "CmdStoreItemInsideBag").Invoke(bag, new object[] { stored, interactor });
                deadline = Time.realtimeSinceStartup + 6;
                while (content != null || !(bool)Member(contraband, "IsContraband"))
                { Require(Time.realtimeSinceStartup < deadline, "Native bag storage did not establish actual contraband"); yield return null; }
                item = Instantiate(gogglesPrefab, player.transform.position + Vector3.up, Quaternion.identity); NetworkServer.Spawn(item);
                var held = item.GetComponent(Find("HeldItemInteractable")); var goggles = item.GetComponent(gogglesType);
                AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                deadline = Time.realtimeSinceStartup + 6;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native xray goggles pickup failed"); yield return null; }
                toggle.Invoke(null, null); paused = true; Press(); Release();
                Require(!(bool)Member(goggles, "localHasUsed") && !(bool)xrayField.GetValue(null), "Settings allowed native goggles consumption");
                toggle.Invoke(null, null); paused = false;
                yield return new WaitForEndOfFrame();
                Color32[][] before = CaptureXrayEyes("before");
                Press(); deadline = Time.realtimeSinceStartup + 3;
                while (!(bool)xrayField.GetValue(null) || Convert.ToSingle(Member(volume, "weight")) < .95f || !outline.enabled
                    || !(bool)Member(held, "NetworkisInteractionDisabled"))
                { Require(Time.realtimeSinceStartup < deadline, "Native xray effect did not enable its volume and contraband outline"); yield return null; }
                Require((bool)Member(goggles, "localHasUsed") && (bool)Member(held, "NetworkisInteractionDisabled"), "Native goggles use failed to own/disable its item");
                Require(Member(outline, "OutlineMode").ToString() == "FillAll" && !(bool)Member(contraband, "isOcclusionCullingEnabled"),
                    "Native xray outline did not enable through-wall fill and release occlusion");
                Logger.LogInfo("XRAY USE PASS: fresh controller trigger enables native xray state, authored volume and through-wall contraband fill while disabling used goggles");
                yield return new WaitForEndOfFrame();
                Color32[][] active = CaptureXrayEyes("active");
                for (int eye = 0; eye < 2; eye++)
                {
                    Require(active[eye].Length == before[eye].Length, "Native eye size changed during xray");
                    int changed = 0, visible = 0;
                    for (int pixel = 0; pixel < active[eye].Length; pixel++)
                    {
                        Color32 a = active[eye][pixel], b = before[eye][pixel];
                        if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > 24) changed++;
                        if (a.r + a.g + a.b > 24) visible++;
                    }
                    Require(changed > active[eye].Length / 100 && visible > active[eye].Length / 100,
                        "Native xray eye is blank or unchanged: eye=" + eye + "; changed=" + changed + "; visible=" + visible);
                    Logger.LogInfo("XRAY EYE PASS: native display eye " + eye + " renders nonblank changed pixels during the authored effect; changed=" + changed);
                }
                Release(); Press(); Release();
                deadline = Time.realtimeSinceStartup + Convert.ToSingle(Member(goggles, "xrayDuration")) + 5;
                while ((bool)xrayField.GetValue(null) || item != null || Convert.ToSingle(Member(volume, "weight")) > .01f)
                { Require(Time.realtimeSinceStartup < deadline, "Native xray duration, goggles destruction or volume expiry failed"); yield return null; }
                Require(Member(interactor, "CurrentHeldItem") == null, "Expired goggles retained native held ownership");
                Logger.LogInfo("XRAY EXPIRY PASS: authored duration clears xray, fades native volume and releases/destroys goggles without held-trigger reuse");
            }
            finally
            {
                Release(); manager.Pointer.Cancel(); if (paused) toggle.Invoke(null, null);
                if ((bool)xrayField.GetValue(null)) AccessTools.Method(meta.GetType(), "DisableXrayMode").Invoke(meta, null);
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), item.GetComponent(Find("HeldItemInteractable"))))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                if (target != null) NetworkServer.Destroy(target);
                if (content != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), content.GetComponent(Find("HeldItemInteractable"))))
                        AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(content);
                }
                teleport.Invoke(controller, new object[] { originalPosition, true }); manager.Rig.Recenter(); Destroy(floor);
            }
            Logger.LogInfo("XRAY COMPLETE PASS: native controller use, actual filled-bag outline state, both mock eye targets and expiry checked; headset acceptance remains pending");
        }
    }
}
