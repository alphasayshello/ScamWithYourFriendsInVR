using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static bool polaroidGripFixture;
        private static Vector3 polaroidGripPosition;
        private static Quaternion polaroidGripRotation;
        private static void HoldPolaroidGrip(Input.VRHandController __instance)
        {
            if (polaroidGripFixture && ReferenceEquals(__instance, VRManager.Instance?.Rig?.Right))
                __instance.transform.SetPositionAndRotation(polaroidGripPosition, polaroidGripRotation);
        }
        private IEnumerator TestNativePolaroid()
        {
            var manager = VRManager.Instance;
            var player = NetworkClient.localPlayer;
            var interactor = player.GetComponent(Find("PlayerInteractor"));
            Type type = Find("PolaroidInteractable"), pictureType = Find("PolaroidPicture");
            var prefab = NetworkManager.singleton.spawnPrefabs.FirstOrDefault(go => go != null && go.GetComponent(type) != null);
            Require(prefab != null && Member(interactor, "CurrentHeldItem") == null, "Polaroid check requires native prefab and empty hands");
            object controller = Instance("Metater.MetaPlayerController");
            var body = Member(controller, "CharacterController") as CharacterController;
            var teleport = AccessTools.Method(controller.GetType(), "Teleport");
            var visibility = AccessTools.Method(controller.GetType(), "UpdateMeshRendererVisibility");
            Require(body != null, "Polaroid check needs native collision body");
            Vector3 savedPosition = player.transform.position;
            var headRenderers = new List<Renderer>();
            foreach (Component toggle in ((Array)Member(controller, "meshRendererToggles")).Cast<Component>())
                if (toggle != null && toggle.gameObject.activeInHierarchy)
                    foreach (Renderer renderer in toggle.GetComponentsInChildren<Renderer>())
                        if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                            && (bool)AccessTools.Method(toggle.GetType(), "IsHeadVisual").Invoke(toggle, new object[] { renderer.transform })
                            && !headRenderers.Contains(renderer)) headRenderers.Add(renderer);
            Require(headRenderers.Count > 0, "Polaroid check did not find native head visuals");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "SWYF isolated photography floor"; floor.transform.localScale = new Vector3(24, .2f, 24);
            floor.transform.position = new Vector3(1050, 50 + body.bounds.min.y - savedPosition.y - .12f, 1050);
            GameObject item = null, card = null;
            Material material = null;
            Component cameraItem = null, held = null;
            UnityEngine.Camera photoCamera = null;
            var originals = new HashSet<uint>(NetworkServer.spawned.Keys);
            var ownedPhotos = new List<GameObject>();
            var ownedTextures = new HashSet<object>();
            var probe = new Harmony("com.swyf.vr.polaroidfixture");
            int renders = 0;
            bool fullHeadAtRender = false, paused = false;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            var tick = AccessTools.Method(manager.Pointer.GetType(), "LateUpdate");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void Click(bool secondary)
            {
                manager.Rig.Right.Aim.SetPositionAndRotation(manager.Rig.Head.transform.position + Vector3.right * .35f, Quaternion.LookRotation(Vector3.up));
                Input("Grip", false); Input("LeftGrip", false);
                Input(secondary ? "Secondary" : "Trigger", true);
                Input(secondary ? "SecondaryDown" : "TriggerDown", true); tick.Invoke(manager.Pointer, null);
                Input(secondary ? "SecondaryDown" : "TriggerDown", false);
                Input(secondary ? "Secondary" : "Trigger", false);
                if (!secondary) Input("TriggerUp", true);
                tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
            }
            void BeforePhoto(ScriptableRenderContext context, UnityEngine.Camera camera)
            {
                if (camera != photoCamera) return;
                renders++;
                fullHeadAtRender = headRenderers.All(r => r != null && r.enabled && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly);
                card.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward * 2,
                    Quaternion.LookRotation(camera.transform.forward, camera.transform.up));
            }
            try
            {
                manager.Pointer.Cancel(); teleport.Invoke(controller, new object[] { new Vector3(1050, 50, 1050), true }); manager.Rig.Recenter();
                yield return new WaitForSecondsRealtime(.8f);
                visibility.Invoke(controller, new object[] { false });
                Require(headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "Native first-person head is not hidden before photo");
                item = Instantiate(prefab, interactor.transform.position + Vector3.up, Quaternion.identity);
                cameraItem = item.GetComponent(type); held = item.GetComponent(Find("HeldItemInteractable"));
                NetworkServer.Spawn(item);
                AccessTools.Method(held.GetType(), "CmdInteract").Invoke(held, new object[] { interactor });
                float deadline = Time.realtimeSinceStartup + 6;
                while (!ReferenceEquals(Member(interactor, "CurrentHeldItem"), held))
                { Require(Time.realtimeSinceStartup < deadline, "Native polaroid pickup did not complete"); yield return null; }
                polaroidGripPosition = manager.Rig.Head.transform.position + Quaternion.Euler(0, manager.Rig.Head.transform.eulerAngles.y, 0) * new Vector3(.45f, -.25f, .6f);
                polaroidGripRotation = manager.Rig.Right.transform.rotation; polaroidGripFixture = true;
                probe.Patch(AccessTools.Method(typeof(Input.VRHandController), "Sync"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(HoldPolaroidGrip)));
                yield return new WaitForSecondsRealtime(.4f);
                photoCamera = Member(cameraItem, "cameraGo") as UnityEngine.Camera;
                Require(photoCamera != null && photoCamera.transform.IsChildOf(item.transform) && photoCamera != manager.Rig.Head,
                    "Polaroid did not retain its own camera on the held model");
                card = GameObject.CreatePrimitive(PrimitiveType.Quad); card.name = "SWYF photography colour card";
                Destroy(card.GetComponent<Collider>()); card.transform.localScale = Vector3.one * 8;
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                material.color = Color.magenta;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.magenta);
                card.GetComponent<Renderer>().sharedMaterial = material;
                Quaternion cameraFromGrip = Quaternion.Inverse(polaroidGripRotation) * photoCamera.transform.rotation;
                polaroidGripRotation = Quaternion.Euler(0, manager.Rig.Head.transform.eulerAngles.y, 0) * Quaternion.Inverse(cameraFromGrip);
                yield return new WaitForSecondsRealtime(.4f);
                card.transform.SetPositionAndRotation(photoCamera.transform.position + photoCamera.transform.forward * 2,
                    Quaternion.LookRotation(photoCamera.transform.forward, photoCamera.transform.up));
                RenderPipelineManager.beginCameraRendering += BeforePhoto;
                RenderTexture savedTarget = photoCamera.targetTexture;
                Click(false);
                deadline = Time.realtimeSinceStartup + 6;
                Component picture = null;
                while (picture == null || !(Member(picture, "isPictureSet") is bool ready && ready))
                {
                    foreach (var identity in NetworkServer.spawned.Values)
                        if (identity != null && !originals.Contains(identity.netId) && identity.GetComponent(pictureType) is Component found)
                        { picture = found; if (!ownedPhotos.Contains(identity.gameObject)) ownedPhotos.Add(identity.gameObject); }
                    Require(Time.realtimeSinceStartup < deadline, "Controller shutter did not create a native textured photograph; renders=" + renders);
                    yield return null;
                }
                ownedTextures.Add(Member(picture, "cachedRenderTexture"));
                Require(renders == 1 && fullHeadAtRender && photoCamera.targetTexture == savedTarget,
                    "Native capture did not render once with full head and restore its camera target; renders=" + renders + "; head=" + fullHeadAtRender + "; target=" + (photoCamera.targetTexture == savedTarget));
                Require(headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "Photograph left the head visible in the headset");
                var texture = (Member(picture, "pictureMeshRenderer") as Renderer)?.sharedMaterial.mainTexture as RenderTexture;
                Require(texture != null && Member(picture, "pictureGuid") is Guid guid && guid != Guid.Empty, "Native photograph has no texture or synchronized picture identity");
                var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                RenderTexture previous = RenderTexture.active;
                Color centre;
                try { RenderTexture.active = texture; pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); pixels.Apply(); centre = pixels.GetPixel(texture.width / 2, texture.height / 2);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(readyFile), "polaroid-photo.png"), pixels.EncodeToPNG());
                    Logger.LogInfo("POLAROID CAMERA: camera=" + photoCamera.transform.position + " forward=" + photoCamera.transform.forward
                        + "; hand=" + manager.Rig.Right.transform.position + "; head=" + manager.Rig.Head.transform.position
                        + "; card=" + card.transform.position + "; mask=" + photoCamera.cullingMask + "; shader=" + material.shader.name); }
                finally { RenderTexture.active = previous; Destroy(pixels); }
                Require(centre.r > .6f && centre.b > .6f && centre.g < .35f, "Native polaroid texture did not capture its colour card: " + centre);
                Logger.LogInfo("POLAROID SHUTTER PASS: controller trigger creates one native network photograph with camera-rendered colour pixels, full head during capture and restored headset visibility/target");
                Vector3 cameraOffset = Quaternion.Inverse(polaroidGripRotation) * (photoCamera.transform.position - polaroidGripPosition);
                Quaternion rotationOffset = Quaternion.Inverse(polaroidGripRotation) * photoCamera.transform.rotation;
                Vector3 headBefore = manager.Rig.Head.transform.position;
                polaroidGripPosition += Vector3.right * .25f;
                polaroidGripRotation = Quaternion.AngleAxis(25, Vector3.up) * polaroidGripRotation;
                yield return new WaitForSecondsRealtime(.6f);
                Require(Vector3.Distance(photoCamera.transform.position, polaroidGripPosition + polaroidGripRotation * cameraOffset) < .02f
                    && Quaternion.Angle(photoCamera.transform.rotation, polaroidGripRotation * rotationOffset) < 1
                    && Vector3.Distance(headBefore, manager.Rig.Head.transform.position) < .1f,
                    "Native held camera did not follow controller translation/rotation independently of the headset");
                Logger.LogInfo("POLAROID MOTION PASS: native camera follows 25cm controller translation and 25-degree rotation independently of headset position");
                bool selfie = (bool)Member(cameraItem, "NetworkselfieMode");
                Transform hold = Member(cameraItem, "holdPointTransform") as Transform;
                Click(true); deadline = Time.realtimeSinceStartup + 3;
                while ((bool)Member(cameraItem, "NetworkselfieMode") == selfie)
                { Require(Time.realtimeSinceStartup < deadline, "Controller secondary did not flip native selfie mode"); yield return null; }
                var authored = Member(cameraItem, !selfie ? "selfieTransform" : "normalTransform") as Transform;
                Require(hold != null && authored != null && Vector3.Distance(hold.localPosition, authored.localPosition) < .001f
                    && Quaternion.Angle(hold.localRotation, authored.localRotation) < .01f, "Selfie flip did not apply its authored held-camera pose");
                Click(true); deadline = Time.realtimeSinceStartup + 3;
                while ((bool)Member(cameraItem, "NetworkselfieMode") != selfie)
                { Require(Time.realtimeSinceStartup < deadline, "Fresh secondary did not restore normal camera mode"); yield return null; }
                Logger.LogInfo("POLAROID SELFIE PASS: controller secondary toggles synchronized selfie state and authored held pose, then restores normal mode");
                int beforeBlocked = renders;
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = true; Click(false); Click(true);
                yield return new WaitForSecondsRealtime(.3f);
                Require(renders == beforeBlocked && (bool)Member(cameraItem, "NetworkselfieMode") == selfie, "Settings allowed a polaroid shutter or selfie action");
                AccessTools.Method(adapter, "TogglePause").Invoke(null, null); paused = false;
                Logger.LogInfo("POLAROID MENU PASS: settings block native shutter and selfie actions");
                var capture = manager.Rig.GetComponent<Camera.VRDesktopCamera>();
                Require(capture != null && capture.enabled, "Photo render scope missing");
                RenderTexture temporary = new RenderTexture(8, 8, 0);
                try
                {
                    photoCamera.targetTexture = temporary;
                    AccessTools.Method(capture.GetType(), "BeginPreview").Invoke(capture, new object[] { default(ScriptableRenderContext), photoCamera });
                    Require(headRenderers.All(r => r.shadowCastingMode != ShadowCastingMode.ShadowsOnly), "Registered native photo did not acquire full-head visibility");
                    capture.enabled = false;
                    Require(headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "Disabling capture left the local head visible");
                    capture.enabled = true;
                    AccessTools.Method(capture.GetType(), "BeginPreview").Invoke(capture, new object[] { default(ScriptableRenderContext), photoCamera });
                    Require(headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "Disabled photo scope retained a stale camera registration");
                }
                finally { capture.enabled = true; photoCamera.targetTexture = savedTarget; temporary.Release(); Destroy(temporary); }
                // Observe after the re-enabled render scope has acquired visibility.
                RenderPipelineManager.beginCameraRendering -= BeforePhoto;
                RenderPipelineManager.beginCameraRendering += BeforePhoto;
                yield return new WaitForSecondsRealtime(.7f);
                Click(false); deadline = Time.realtimeSinceStartup + 6;
                Component recoveredPhoto = null;
                while (recoveredPhoto == null || !(Member(recoveredPhoto, "isPictureSet") is bool recovered && recovered))
                {
                    foreach (var identity in NetworkServer.spawned.Values)
                        if (identity != null && !originals.Contains(identity.netId) && !ownedPhotos.Contains(identity.gameObject)
                            && identity.GetComponent(pictureType) is Component found) recoveredPhoto = found;
                    Require(Time.realtimeSinceStartup < deadline, "Native photo did not recover after render-scope cleanup"); yield return null;
                }
                ownedPhotos.Add(recoveredPhoto.gameObject); ownedTextures.Add(Member(recoveredPhoto, "cachedRenderTexture"));
                Require(renders == 2 && fullHeadAtRender && headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    && photoCamera.targetTexture == savedTarget, "Recovered native photo did not restore its render/head state; renders=" + renders + "; head=" + fullHeadAtRender + "; restored=" + headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly));
                Logger.LogInfo("POLAROID RECOVERY PASS: disable restores local visibility and forgets cameras; a new native shutter re-registers and restores normal headset state");
            }
            finally
            {
                polaroidGripFixture = false; probe.UnpatchSelf();
                RenderPipelineManager.beginCameraRendering -= BeforePhoto;
                if (paused) AccessTools.Method(adapter, "TogglePause").Invoke(null, null);
                Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false); Input("Secondary", false); Input("SecondaryDown", false);
                manager.Pointer.Cancel(); visibility.Invoke(controller, new object[] { false });
                if (item != null)
                {
                    if (ReferenceEquals(Member(interactor, "CurrentHeldItem"), held)) AccessTools.Method(interactor.GetType(), "ServerClearHeldItem").Invoke(interactor, null);
                    NetworkServer.Destroy(item);
                }
                foreach (var identity in NetworkServer.spawned.Values.ToArray())
                    if (identity != null && !originals.Contains(identity.netId) && identity.GetComponent(pictureType) is Component picture)
                    {
                        ownedTextures.Add(Member(picture, "cachedRenderTexture"));
                        if (!ownedPhotos.Contains(identity.gameObject)) ownedPhotos.Add(identity.gameObject);
                    }
                foreach (GameObject photo in ownedPhotos) if (photo != null) NetworkServer.Destroy(photo);
                object queue = AccessTools.Field(pictureType, "pictureQueue").GetValue(null);
                object[] retained = ((IEnumerable)queue).Cast<object>().Where(t => !ownedTextures.Contains(t)).ToArray();
                AccessTools.Method(queue.GetType(), "Clear").Invoke(queue, null);
                foreach (object entry in retained) AccessTools.Method(queue.GetType(), "Enqueue").Invoke(queue, new[] { entry });
                if (card != null) Destroy(card); if (material != null) Destroy(material);
                teleport.Invoke(controller, new object[] { savedPosition, true }); manager.Rig.Recenter(); Destroy(floor);
            }
        }
    }
}
