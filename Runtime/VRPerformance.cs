using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace ScamWithYourFriendsInVR
{
    public sealed class VRPerformance : MonoBehaviour
    {
        private sealed class AssetState
        {
            internal RenderPipelineAsset Asset;
            internal PropertyInfo Scale, Shadows, Samples;
            internal object OriginalScale, OriginalShadows, OriginalSamples;
        }
        private readonly List<AssetState> assets = new List<AssetState>();
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private static VRPerformance active;
        private float nextApply, nextReport, elapsed;
        private float slowestFrame;
        private int frames, originalVsync, originalFrameLimit;
        private FieldInfo visibilityFlag;
        private bool savedVisibility, visibilityConfigured;
        private void Awake()
        {
            active = this;
            RememberFlatFramePacing();
            ConfigureShaderCompatibility();
            nextReport = Time.unscaledTime + 10;
        }
        public static void RememberFlatFramePacing()
        {
            if (active == null) return;
            active.originalVsync = QualitySettings.vSyncCount;
            active.originalFrameLimit = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }
        private void Update()
        {
            elapsed += Time.unscaledDeltaTime; frames++;
            slowestFrame = Mathf.Max(slowestFrame, Time.unscaledDeltaTime);
            if (Time.unscaledTime >= nextApply)
            {
                nextApply = Time.unscaledTime + 1;
                ConfigureShaderCompatibility();
                if (Plugin.BalancedVR.Value) Apply(GraphicsSettings.currentRenderPipeline);
            }
            if (Time.unscaledTime < nextReport) return;
            nextReport = Time.unscaledTime + 10;
            var manager = VRManager.Instance;
            displays.Clear(); SubsystemManager.GetSubsystems(displays);
            float gpu = 0;
            bool hasGpu = false;
            foreach (var display in displays)
                if (display.running) { hasGpu = display.TryGetAppGPUTimeLastFrame(out gpu) && gpu > 0 && !float.IsNaN(gpu) && !float.IsInfinity(gpu); break; }
            Plugin.Log.LogInfo("VR frame sample: fps=" + (elapsed > 0 ? frames / elapsed : 0).ToString("F1")
                + "; slowestFrameMs=" + (slowestFrame * 1000).ToString("F1")
                + "; appGPUms=" + (hasGpu ? (gpu * 1000).ToString("F1") : "unavailable")
                + "; flatFrameLimit=" + Application.targetFrameRate + "; vSync=" + QualitySettings.vSyncCount
                + "; pipeline=" + (GraphicsSettings.currentRenderPipeline?.name ?? "built-in")
                + "; uiPanels=" + (manager?.UI?.Panels.Count ?? 0)
                + "; head=" + (manager?.Input?.HeadTracked ?? false) + "; focus=" + (manager?.Input?.HasInputFocus ?? false));
            elapsed = 0; frames = 0; slowestFrame = 0;
        }
        private void ConfigureShaderCompatibility()
        {
            if (visibilityConfigured || GraphicsSettings.currentRenderPipeline == null) return;
            foreach (string name in new[] { "Hidden/Universal Render Pipeline/UberPost", "Hidden/Universal Render Pipeline/FinalPost" })
            {
                Shader shader = Shader.Find(name);
                if (shader == null) continue;
                var probe = new Material(shader);
                int passes = probe.passCount;
                Destroy(probe);
                if (passes >= 2) continue;
                Type system = ReflectionTypes.Find("UnityEngine.Experimental.Rendering.XRSystem");
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo flag = system?.GetField("s_UseVisibilityMesh", flags);
                if (flag == null || flag.FieldType != typeof(bool) || flag.IsInitOnly || flag.IsLiteral)
                {
                    visibilityConfigured = true;
                    Plugin.Log.LogWarning("XR shader pass missing and visibility-mesh control is unavailable: " + name);
                    return;
                }
                savedVisibility = (bool)flag.GetValue(null);
                flag.SetValue(null, false);
                visibilityFlag = flag;
                visibilityConfigured = true;
                Plugin.Log.LogInfo("XR post-processing uses full-screen passes; visibility-mesh shader pass stripped from " + name);
                return;
            }
        }
        private void Apply(RenderPipelineAsset asset)
        {
            if (asset == null) return;
            AssetState state = assets.Find(s => s.Asset == asset);
            if (state == null)
            {
                Type type = asset.GetType();
                state = new AssetState { Asset = asset, Scale = type.GetProperty("renderScale"), Shadows = type.GetProperty("shadowDistance"), Samples = type.GetProperty("msaaSampleCount") };
                state.OriginalScale = state.Scale?.GetValue(asset); state.OriginalShadows = state.Shadows?.GetValue(asset); state.OriginalSamples = state.Samples?.GetValue(asset);
                assets.Add(state);
            }
            if (state.Scale?.CanWrite == true && state.OriginalScale is float scale) state.Scale.SetValue(asset, Mathf.Min(scale, Plugin.SceneRenderScale.Value));
            if (state.Shadows?.CanWrite == true && state.OriginalShadows is float shadows) state.Shadows.SetValue(asset, Mathf.Min(shadows, Plugin.VRShadowDistance.Value));
            if (state.Samples?.CanWrite == true && state.OriginalSamples is int samples) state.Samples.SetValue(asset, Mathf.Min(samples, 2));
        }
        private void OnDestroy()
        {
            QualitySettings.vSyncCount = originalVsync;
            Application.targetFrameRate = originalFrameLimit;
            if (active == this) active = null;
            visibilityFlag?.SetValue(null, savedVisibility);
            foreach (AssetState state in assets)
            {
                if (state.Asset == null) continue;
                if (state.Scale?.CanWrite == true) state.Scale.SetValue(state.Asset, state.OriginalScale);
                if (state.Shadows?.CanWrite == true) state.Shadows.SetValue(state.Asset, state.OriginalShadows);
                if (state.Samples?.CanWrite == true) state.Samples.SetValue(state.Asset, state.OriginalSamples);
            }
        }
    }
}
