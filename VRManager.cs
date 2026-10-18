using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using ScamWithYourFriendsInVR.Camera;
using ScamWithYourFriendsInVR.Input;
using ScamWithYourFriendsInVR.Patches;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR
{
    public sealed class VRManager : MonoBehaviour
    {
        public static VRManager Instance { get; private set; }
        public VRCameraController Rig { get; private set; }
        public VRInputManager Input { get; private set; }
        public VRPointer Pointer { get; private set; }
        public VRUIPatcher UI { get; private set; }
        public VRVirtualKeyboard Keyboard { get; private set; }
        public bool Ready { get; private set; }
        public bool Interactive => Ready && Input != null && Input.HasInputFocus && Input.HeadTracked;
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private object ownedManager;
        private AssetBundle bundle;
        private Harmony harmony;
        private GameObject session;
        private float nextRetry;

        private IEnumerator Start()
        {
            Instance = this;
            yield return null;
            if (!HasDisplay())
            {
                try { InitializeXR(); }
                catch (Exception ex) { Plugin.Log.LogError("XR initialization failed: " + ex.GetBaseException().Message); }
            }
            float until = Time.realtimeSinceStartup + 15f;
            while (!HasDisplay() && Time.realtimeSinceStartup < until) yield return null;
            if (!HasDisplay())
            {
                Plugin.Log.LogError("No running XR display. Game left unchanged. See docs/XR-RUNTIME.md; installing this DLL alone cannot add Unity's missing native OpenXR provider.");
                yield break;
            }
            try
            {
                session = new GameObject("SWYF VR Session");
                session.transform.SetParent(transform, false);
                Input = session.AddComponent<VRInputManager>();
                Rig = session.AddComponent<VRCameraController>();
                session.AddComponent<VRAudioController>();
                session.AddComponent<VRAvatarArms>();
                session.AddComponent<VRPerformance>();
                UI = session.AddComponent<VRUIPatcher>();
                Keyboard = session.AddComponent<VRVirtualKeyboard>();
                Pointer = session.AddComponent<VRPointer>();
                harmony = new Harmony("com.swyf.vr.runtime");
                PlayerPatches.Install(harmony);
                DesktopUIPatches.Install(harmony);
                session.AddComponent<VRMirrorView>();
                Ready = true;
                GameAdapter.Call(GameAdapter.Controller, "UpdateMeshRendererVisibility", false);
                Plugin.Log.LogInfo("VR session active. Hardware and gameplay acceptance checks are still required.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(ex);
                ShutdownSession();
            }
        }

        private bool HasDisplay()
        {
            displays.Clear();
            SubsystemManager.GetSubsystems(displays);
            return displays.Exists(d => d.running);
        }

        private void InitializeXR()
        {
            Type general = ReflectionTypes.Find("UnityEngine.XR.Management.XRGeneralSettings");
            object settings = general?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object manager = settings?.GetType().GetProperty("Manager")?.GetValue(settings);
            if (manager == null && !string.IsNullOrWhiteSpace(Plugin.RuntimeBundle.Value))
            {
                bundle = AssetBundle.LoadFromFile(Plugin.RuntimeBundle.Value);
                if (bundle == null) throw new InvalidOperationException("XR settings bundle could not be loaded.");
                foreach (UnityEngine.Object asset in bundle.LoadAllAssets())
                    if (asset.GetType().FullName == "UnityEngine.XR.Management.XRManagerSettings") manager = asset;
            }
            if (manager == null) manager = OpenXRBootstrap.CreateManager();
            if (manager.GetType().GetProperty("activeLoader")?.GetValue(manager) != null) return;
            ownedManager = manager;
            Invoke(manager, "InitializeLoaderSync");
            if (manager.GetType().GetProperty("activeLoader")?.GetValue(manager) == null)
                throw new InvalidOperationException("XR loader returned no provider.");
            Invoke(manager, "StartSubsystems");
        }

        private static void Invoke(object target, string name)
        {
            MethodInfo method = AccessTools.Method(target.GetType(), name, Type.EmptyTypes);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            method.Invoke(target, null);
        }

        private void Update()
        {
            if (!Ready || Time.unscaledTime < nextRetry) return;
            nextRetry = Time.unscaledTime + 1f;
            if (!HasDisplay())
            {
                Plugin.Log.LogWarning("XR display stopped; restoring flat-screen state. Restart the game to retry.");
                ShutdownSession();
            }
        }

        private void ShutdownSession()
        {
            Ready = false;
            if (Pointer != null) Pointer.Cancel();
            if (UI != null) UI.Restore();
            if (Rig != null) Rig.Restore();
            harmony?.UnpatchSelf();
            GameAdapter.Call(GameAdapter.Controller, "UpdateMeshRendererVisibility", false);
            harmony = null;
            if (session != null) Destroy(session);
            session = null;
        }

        private void OnDestroy()
        {
            ShutdownSession();
            if (ownedManager != null)
            {
                try { Invoke(ownedManager, "StopSubsystems"); Invoke(ownedManager, "DeinitializeLoader"); }
                catch (Exception ex) { Plugin.Log.LogWarning(ex.GetBaseException().Message); }
            }
            if (bundle != null) bundle.Unload(false);
            OpenXRBootstrap.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
