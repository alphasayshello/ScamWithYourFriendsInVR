using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR
{
    [BepInPlugin(Id, "Scam With Your Friends In VR", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public enum PlayerMode { VR, PC }
        public enum ControllerLayoutKind { Auto, FaceButtons, ViveWand, Wmr }
        public const string Id = "com.swyf.vr";
        public static ManualLogSource Log;
        public static ConfigEntry<bool> Seated;
        public static ConfigEntry<float> SeatedHeight, SnapAngle, Deadzone, PanelDistance, PanelWidth;
        public static ConfigEntry<int> CollisionMask;
        public static ConfigEntry<string> RuntimeBundle;
        public static ConfigEntry<PlayerMode> Mode;
        public static ConfigEntry<ControllerLayoutKind> ControllerLayout;
        public static ConfigEntry<bool> SyncPoses, DirectHandshake;
        public static ConfigEntry<int> PoseRate;
        public static ConfigEntry<bool> OccludeFloatingUI;
        public static ConfigEntry<bool> MirrorFullView;
        public static ConfigEntry<bool> PhysicalDesktop;
        public static ConfigEntry<bool> BalancedVR;
        public static ConfigEntry<float> SceneRenderScale, VRShadowDistance;
        public static ConfigEntry<int> PanelPixels;
        public static ConfigEntry<int> DesktopCameraFPS;
        public static ConfigEntry<bool> VirtualKeyboard;
        public static ConfigEntry<bool> AlwaysShowPointer;
        public static ConfigEntry<float> PointerSmoothing, DoubleClickWindow;
        private Component manager;
        private Component multiplayer;

        private void Awake()
        {
            Log = Logger;
            Mode = Config.Bind("General", "Mode", PlayerMode.VR, "VR starts tracked play; PC keeps normal controls and displays other players' VR poses. Restart the game after changing mode.");
            SyncPoses = Config.Bind("Multiplayer", "SyncPoses", true, "Share head/hand poses with modded peers through a modded host. Unmodded peers retain native animation.");
            DirectHandshake = Config.Bind("Multiplayer", "DirectHandshake", false, "For direct/LAN connections only: enable on clients ONLY when the host has this mod. Steam lobbies negotiate automatically.");
            PoseRate = Config.Bind("Multiplayer", "PoseRate", 20, new ConfigDescription("Pose packets per second", new AcceptableValueRange<int>(5, 30)));
            Seated = Config.Bind("Comfort", "Seated", false, "Use a recentered 1.65m eye height.");
            SeatedHeight = Config.Bind("Comfort", "SeatedEyeHeight", 1.65f, new ConfigDescription("Meters", new AcceptableValueRange<float>(0.8f, 2.2f)));
            SnapAngle = Config.Bind("Comfort", "SnapDegrees", 30f, new ConfigDescription("Degrees per stick deflection", new AcceptableValueRange<float>(10f, 90f)));
            Deadzone = Config.Bind("Input", "StickDeadzone", 0.2f, new ConfigDescription("Radial deadzone", new AcceptableValueRange<float>(0.05f, 0.8f)));
            ControllerLayout = Config.Bind("Input", "ControllerLayout", ControllerLayoutKind.Auto, "Auto recognises the bundled Vive wand and Windows MR OpenXR profiles. FaceButtons keeps Quest/Index-style controls. ViveWand/Wmr use pad clicks for missing face buttons. Restart after changing this override.");
            PanelDistance = Config.Bind("UI", "PanelDistance", .9f, new ConfigDescription("Distance in metres for floating HUD and menus; physical computer screens keep their desk position", new AcceptableValueRange<float>(0.5f, 3f)));
            PanelWidth = Config.Bind("UI", "PanelWidth", 1.6f, new ConfigDescription("Meters", new AcceptableValueRange<float>(0.4f, 3f)));
            OccludeFloatingUI = Config.Bind("UI", "OccludeFloatingUI", false, "Let world geometry block floating menu rays. Off keeps floating menus usable when near a desk or wall.");
            MirrorFullView = Config.Bind("UI", "MirrorFullView", true, "Fit the full left eye image in the PC window when the XR provider exposes a 2D eye texture. Unsupported array/foveated/packed mirrors retain provider behavior.");
            PhysicalDesktop = Config.Bind("UI", "PhysicalDesktop", true, "Keep computer UI on its desk monitor with controller-pointer input. Off opens an optional floating desktop.");
            VirtualKeyboard = Config.Bind("UI", "VirtualKeyboard", false, "Experimental floating text keyboard. Disabled by default; computer interaction uses the controller pointer and text entry uses your PC keyboard.");
            AlwaysShowPointer = Config.Bind("UI", "AlwaysShowPointer", false, "Keep the laser visible during ordinary gameplay. Off shows it for menus, computers and usable targets.");
            PointerSmoothing = Config.Bind("UI", "PointerSmoothing", .025f, new ConfigDescription("Seconds of adaptive aim smoothing for UI; 0 disables it. Hand tracking stays direct.", new AcceptableValueRange<float>(0, .1f)));
            DoubleClickWindow = Config.Bind("UI", "DoubleClickWindow", .4f, new ConfigDescription("Maximum interval between controller clicks for a double click", new AcceptableValueRange<float>(.2f, .7f)));
            PanelPixels = Config.Bind("UI", "PanelPixels", 2560, new ConfigDescription("Floating desktop texture width; 16:9. Higher improves text clarity and costs GPU memory.", new AcceptableValueRange<int>(1280, 3840)));
            BalancedVR = Config.Bind("Performance", "BalancedVR", true, "Apply reversible VR scene render/shadow limits without changing saved game graphics preferences.");
            DesktopCameraFPS = Config.Bind("Performance", "DesktopCameraFPS", 15, new ConfigDescription("Maximum frame rate of the in-game camera app preview. The headset keeps its own frame rate; photographs use the most recent preview frame.", new AcceptableValueRange<int>(5, 60)));
            SceneRenderScale = Config.Bind("Performance", "SceneRenderScale", 0.85f, new ConfigDescription("URP scene render scale while VR is active", new AcceptableValueRange<float>(0.5f, 1f)));
            VRShadowDistance = Config.Bind("Performance", "ShadowDistance", 20f, new ConfigDescription("Maximum real-time shadow distance while VR is active", new AcceptableValueRange<float>(0, 80)));
            CollisionMask = Config.Bind("Comfort", "HeadCollisionMask", ~((1 << 2) | (1 << 5)), "World layers only; exclude player, UI, triggers and Ignore Raycast.");
            RuntimeBundle = Config.Bind("XR", "SettingsBundle", "", "Optional absolute path to a matching Unity asset bundle containing configured XRManagerSettings. Native provider must already be registered by the player.");
            if (!Config.Bind("General", "Enabled", true, "Enable after an XR display is available.").Value) return;
            if (SyncPoses.Value)
            {
                try
                {
                    string path = Path.Combine(Path.GetDirectoryName(Info.Location), "ScamWithYourFriendsInVR.Multiplayer.dll");
                    multiplayer = gameObject.AddComponent(Assembly.LoadFrom(path).GetType("ScamWithYourFriendsInVR.Multiplayer.PoseNetwork", true));
                }
                catch (Exception ex) { Logger.LogError("VR pose networking could not load: " + ex.GetBaseException().Message); }
            }
            if (Mode.Value == PlayerMode.PC)
            {
                Logger.LogInfo("PC mode: native controls retained; OpenXR startup skipped; VR pose receiver enabled when configured.");
                return;
            }
            if (typeof(UnityEngine.XR.InputDevices).GetMethod("GetDeviceAtXRNode") == null)
            {
                Logger.LogError("This player has stripped XR APIs. VR disabled before loading the rig. See docs/XR-RUNTIME.md. Do not replace Unity DLLs with a different editor version.");
                return;
            }
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Info.Location), "ScamWithYourFriendsInVR.Runtime.dll");
                Type runtime = Assembly.LoadFrom(path).GetType("ScamWithYourFriendsInVR.VRManager", true);
                manager = gameObject.AddComponent(runtime);
            }
            catch (Exception ex) { Logger.LogError("VR runtime could not load: " + ex.GetBaseException().Message); }
        }

        private void OnDestroy()
        {
            if (manager != null) Destroy(manager);
            if (multiplayer != null) Destroy(multiplayer);
        }
    }
}
