using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScamWithYourFriendsInVR
{
    internal static class OpenXRBootstrap
    {
        internal static readonly List<ScriptableObject> OwnedAssets = new List<ScriptableObject>();

        internal static object CreateManager()
        {
            string folder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            foreach (string name in new[] { "Unity.XR.Management.dll", "Unity.XR.OpenXR.dll" })
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) Assembly.LoadFrom(path);
            }
            Type managerType = Require("UnityEngine.XR.Management.XRManagerSettings");
            Type loaderType = Require("UnityEngine.XR.OpenXR.OpenXRLoader");
            Type settingsType = Require("UnityEngine.XR.OpenXR.OpenXRSettings");
            Type featureType = Require("UnityEngine.XR.OpenXR.Features.OpenXRFeature");
            object settings = settingsType.GetProperty("Instance").GetValue(null);
            PropertyInfo mode = settingsType.GetProperty("renderMode");
            mode.SetValue(settings, Enum.ToObject(mode.PropertyType, 0));
            var profiles = new Dictionary<string, string>
            {
                { "OculusTouchControllerProfile", "" },
                { "ValveIndexControllerProfile", "" },
                { "HTCViveControllerProfile", "" },
                { "MicrosoftMotionControllerProfile", "" },
                { "KHRSimpleControllerProfile", "" },
                { "HPReverbG2ControllerProfile", "XR_EXT_hp_mixed_reality_controller" },
                { "MetaQuestTouchProControllerProfile", "XR_FB_touch_controller_pro" },
                { "MetaQuestTouchPlusControllerProfile", "XR_META_touch_controller_plus" }
            };
            Array features = Array.CreateInstance(featureType, profiles.Count + 1);
            int index = 0;
            foreach (var profile in profiles)
            {
                Type type = Require("UnityEngine.XR.OpenXR.Features.Interactions." + profile.Key);
                ScriptableObject feature = Create(type);
                AccessTools.Field(featureType, "nameUi").SetValue(feature, profile.Key);
                AccessTools.Field(featureType, "openxrExtensionStrings").SetValue(feature, profile.Value);
                AccessTools.Field(featureType, "featureIdInternal").SetValue(feature,
                    type.GetField("featureId", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue());
                AccessTools.Field(featureType, "version").SetValue(feature, "0.0.1");
                featureType.GetProperty("enabled").SetValue(feature, true);
                features.SetValue(feature, index++);
            }
            ScriptableObject focus = Create(Require("ScamWithYourFriendsInVR.Provider.VRSessionFocusFeature"));
            AccessTools.Field(featureType, "nameUi").SetValue(focus, "VR session focus");
            AccessTools.Field(featureType, "openxrExtensionStrings").SetValue(focus, "");
            AccessTools.Field(featureType, "featureIdInternal").SetValue(focus, "com.swyf.vr.sessionfocus");
            featureType.GetProperty("enabled").SetValue(focus, true);
            features.SetValue(focus, index);
            AccessTools.Field(settingsType, "features").SetValue(settings, features);
            ScriptableObject loader = Create(loaderType);
            ScriptableObject manager = Create(managerType);
            managerType.GetProperty("automaticLoading").SetValue(manager, false);
            managerType.GetProperty("automaticRunning").SetValue(manager, false);
            // XR Management only accepts loaders registered at build time through its public API.
            // This player has no XR settings, so seed this newly owned manager before initialization.
            IList loaders = AccessTools.Field(managerType, "m_Loaders").GetValue(manager) as IList;
            if (loaders == null) throw new InvalidOperationException("XR Management loader layout changed.");
            loaders.Add(loader);
            Type generalType = Require("UnityEngine.XR.Management.XRGeneralSettings");
            object general = generalType.GetProperty("Instance").GetValue(null);
            if (general == null) general = Create(generalType);
            generalType.GetProperty("Manager").SetValue(general, manager);
            AccessTools.Field(generalType, "m_InitManagerOnStart").SetValue(general, false);
            Plugin.Log.LogInfo("Created OpenXR settings with eight controller profiles and multipass rendering.");
            return manager;
        }

        private static Type Require(string name) => ReflectionTypes.Find(name) ?? throw new TypeLoadException(name);
        private static ScriptableObject Create(Type type)
        {
            ScriptableObject asset = ScriptableObject.CreateInstance(type);
            OwnedAssets.Add(asset);
            return asset;
        }
        internal static void Dispose()
        {
            for (int i = OwnedAssets.Count - 1; i >= 0; i--)
                if (OwnedAssets[i] != null) UnityEngine.Object.Destroy(OwnedAssets[i]);
            OwnedAssets.Clear();
        }
    }
}
