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
        private IEnumerator TestNativeCameraTools()
        {
            var photos = TestNativePolaroid();
            try { while (photos.MoveNext()) yield return photos.Current; }
            finally { (photos as IDisposable)?.Dispose(); }
            var security = TestNativeCctv();
            try { while (security.MoveNext()) yield return security.Current; }
            finally { (security as IDisposable)?.Dispose(); }
        }
        private IEnumerator TestNativeCctv()
        {
            Type type = Find("CctvCamera");
            Component source = NetworkManager.singleton.spawnPrefabs.Where(go => go != null)
                .SelectMany(go => go.GetComponentsInChildren(type, true)).FirstOrDefault();
            if (source == null) source = FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None).Cast<Component>().FirstOrDefault();
            Require(source != null, "No native CCTV prefab or scene camera is available in this mode");
            var manager = VRManager.Instance;
            object controller = Instance("Metater.MetaPlayerController");
            var visibility = AccessTools.Method(controller.GetType(), "UpdateMeshRendererVisibility");
            var headRenderers = new List<Renderer>();
            foreach (Component toggle in ((Array)Member(controller, "meshRendererToggles")).Cast<Component>())
                if (toggle != null && toggle.gameObject.activeInHierarchy)
                    foreach (Renderer renderer in toggle.GetComponentsInChildren<Renderer>())
                        if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                            && (bool)AccessTools.Method(toggle.GetType(), "IsHeadVisual").Invoke(toggle, new object[] { renderer.transform })
                            && !headRenderers.Contains(renderer)) headRenderers.Add(renderer);
            Require(headRenderers.Count > 0, "CCTV check needs the native head renderers");
            GameObject owned = null, card = null, text = null;
            Material material = null;
            RenderTexture target = null;
            UnityEngine.Camera camera = null;
            bool fullHead = false;
            int renders = 0;
            RenderTexture originalActive = RenderTexture.active;
            void Observe(ScriptableRenderContext context, UnityEngine.Camera rendering)
            {
                if (rendering != camera) return;
                renders++;
                fullHead = headRenderers.All(r => r != null && r.enabled && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly);
            }
            try
            {
                owned = Instantiate(source.gameObject, new Vector3(1050, 50, 1050), Quaternion.identity);
                owned.name = "SWYF owned native CCTV render fixture";
                var native = owned.GetComponent(type);
                Require(native != null && native is Behaviour, "Native CCTV clone is unavailable");
                ((Behaviour)native).enabled = false;
                camera = Member(native, "cctvCamera") as UnityEngine.Camera;
                Require(camera != null && camera.transform.IsChildOf(owned.transform), "Native CCTV camera is not owned by its cloned model");
                camera.enabled = false;
                target = new RenderTexture(128, 128, 16); target.Create();
                camera.targetTexture = target;
                // Keep the copied native render method away from scene-owned
                // text or targets; no detection/server behaviour is run here.
                AccessTools.Field(type, "targetTransform").SetValue(native, null);
                text = new GameObject("SWYF owned CCTV caption");
                Component caption = text.AddComponent(Find("TMPro.TextMeshPro"));
                AccessTools.Field(type, "wantedText").SetValue(native, caption);
                text.SetActive(false);
                card = GameObject.CreatePrimitive(PrimitiveType.Quad);
                card.name = "SWYF CCTV colour card"; Destroy(card.GetComponent<Collider>());
                card.transform.localScale = Vector3.one * 8;
                card.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward * 2,
                    Quaternion.LookRotation(camera.transform.forward, camera.transform.up));
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                material.color = Color.cyan;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.cyan);
                card.GetComponent<Renderer>().sharedMaterial = material;
                RenderPipelineManager.beginCameraRendering += Observe;
                yield return null;
                visibility.Invoke(controller, new object[] { false });
                Require(headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "Native head is visible before CCTV render");
                Vector3 headPosition = manager.Rig.Head.transform.position;
                Quaternion headRotation = manager.Rig.Head.transform.rotation;
                RenderTexture headTarget = manager.Rig.Head.targetTexture;
                AccessTools.Method(type, "RenderFrame").Invoke(native, null);
                Require(renders == 1 && fullHead, "Native CCTV render did not include the local head; renders=" + renders + "; head=" + fullHead);
                Require(headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly), "CCTV left local head visible after capture");
                Require(camera.targetTexture == target && manager.Rig.Head.targetTexture == headTarget
                    && Vector3.Distance(manager.Rig.Head.transform.position, headPosition) < .001f
                    && Quaternion.Angle(manager.Rig.Head.transform.rotation, headRotation) < .01f,
                    "CCTV render changed the headset camera or its native render target");
                var pixels = new Texture2D(128, 128, TextureFormat.RGB24, false);
                Color centre;
                try
                {
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); pixels.Apply();
                    centre = pixels.GetPixel(64, 64);
                }
                finally { RenderTexture.active = originalActive; Destroy(pixels); }
                Require(centre.g > .6f && centre.b > .6f && centre.r < .35f, "CCTV did not capture its owned colour card: " + centre);
                Logger.LogInfo("CCTV RENDER PASS: native RenderFrame captures colour pixels with full local head, restores headset visibility and preserves camera targets/pose");
                AccessTools.Method(type, "RenderFrame").Invoke(native, null);
                Require(renders == 2 && fullHead && headRenderers.All(r => r.shadowCastingMode == ShadowCastingMode.ShadowsOnly),
                    "Repeated native CCTV capture retained or lost its head visibility state");
                Logger.LogInfo("CCTV REPEAT PASS: repeated native frame restores full-head capture and first-person visibility each time; detection and monitor interaction remain untested");
            }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= Observe;
                RenderTexture.active = originalActive; visibility.Invoke(controller, new object[] { false });
                if (owned != null) Destroy(owned);
                if (card != null) Destroy(card); if (text != null) Destroy(text); if (material != null) Destroy(material);
                if (target != null) { target.Release(); Destroy(target); }
            }
        }
    }
}
