using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.Input
{
    internal static class VRMouseFallback
    {
        private static object owned;
        private static Type mouseType;
        private static PropertyInfo current, devices, added;
        private static MethodInfo add, remove, makeCurrent;
        private static float nextCheck;

        private static bool Resolve()
        {
            if (current != null && add != null && remove != null && devices != null && added != null && makeCurrent != null) return true;
            mouseType = ReflectionTypes.Find("UnityEngine.InputSystem.Mouse");
            Type system = ReflectionTypes.Find("UnityEngine.InputSystem.InputSystem");
            Type device = ReflectionTypes.Find("UnityEngine.InputSystem.InputDevice");
            if (mouseType == null || system == null || device == null) return false;
            current = AccessTools.Property(mouseType, "current");
            devices = AccessTools.Property(system, "devices"); added = AccessTools.Property(device, "added");
            add = AccessTools.Method(system, "AddDevice", new[] { typeof(string), typeof(string), typeof(string) });
            remove = AccessTools.Method(system, "RemoveDevice", new[] { device });
            makeCurrent = AccessTools.Method(mouseType, "MakeCurrent", Type.EmptyTypes);
            return current != null && devices != null && added != null && add != null && remove != null && makeCurrent != null;
        }

        internal static void Ensure()
        {
            if (VRManager.Instance?.Ready != true || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + .5f;
            try
            {
                if (!Resolve() || current.GetValue(null) != null) return;
                // Native weapon routines require Mouse.current even though their
                // ray is redirected to the motion controller. Supply an empty
                // Input System device only when no current mouse is available.
                object available = AvailableMouse();
                if (available != null) makeCurrent.Invoke(available, null);
                else owned = add.Invoke(null, new object[] { "Mouse", "SWYFVRPointer", null });
            }
            catch (Exception ex) { GameAdapter.Report("VR mouse compatibility device", ex); }
        }

        private static object AvailableMouse()
        {
            if (devices.GetValue(null) is IEnumerable available)
                foreach (object device in available)
                    if (mouseType.IsInstanceOfType(device) && added.GetValue(device) is bool connected && connected) return device;
            return null;
        }

        internal static void Release()
        {
            object created = owned; owned = null; nextCheck = 0;
            if (created == null) return;
            try
            {
                if (!Resolve()) return;
                if (added.GetValue(created) is bool present && present) remove.Invoke(null, new[] { created });
                // Removing the current device clears Mouse.current. Restore an
                // available native mouse without removing or editing its state.
                if (current.GetValue(null) != null) return;
                object available = AvailableMouse();
                if (available != null) makeCurrent.Invoke(available, null);
            }
            catch (Exception ex) { GameAdapter.Report("VR mouse compatibility cleanup", ex); }
        }
    }
}
