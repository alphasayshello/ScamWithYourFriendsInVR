using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Patches
{
    internal static class GameAdapter
    {
        private readonly struct MemberKey : IEquatable<MemberKey>
        {
            private readonly Type type;
            private readonly string name;
            private readonly bool method;
            internal MemberKey(Type type, string name, bool method = false) { this.type = type; this.name = name; this.method = method; }
            public bool Equals(MemberKey other) => type == other.type && name == other.name && method == other.method;
            public override bool Equals(object other) => other is MemberKey key && Equals(key);
            public override int GetHashCode() { unchecked { return ((type.GetHashCode() * 397) ^ name.GetHashCode()) * 397 ^ (method ? 1 : 0); } }
            public override string ToString() => type.FullName + "." + name + (method ? "()" : "");
        }
        private static readonly Dictionary<MemberKey, MemberInfo> members = new Dictionary<MemberKey, MemberInfo>();
        private static readonly Dictionary<MethodInfo, ParameterInfo[]> methodParameters = new Dictionary<MethodInfo, ParameterInfo[]>();
        private static readonly HashSet<string> failures = new HashSet<string>();
        internal static Component LocalPlayer => Static("Metater.MetaPlayer", "LocalPlayerInstance") as Component;
        internal static Component Controller => Static("Metater.MetaPlayerController", "Instance") as Component;
        internal static Component ResolveViewPlayer(Component local)
            => Read(local, "isSpectating") is bool spectating && spectating
                ? Read(local, "LocalSpectatedPlayer") as Component ?? local : local;
        internal static bool AvatarUnavailable(Component local)
            => Read(local, "isSpectating") is bool spectating && spectating
                || Read(Read(local, "RagdollManager"), "IsRagdollActive") is bool ragdolled && ragdolled
                || Read(Read(local, "RagdollManager"), "IsHiding") is bool hiding && hiding;
        internal static Component Interactor => Static("PlayerInteractor", "LocalPlayerInstance") as Component;
        internal static Transform Polar => Read(Controller, "polarTransform") as Transform;
        internal static bool MonitorCaptured => Static("KolkataMonitorInputState", "IsMonitorInputCaptured") is bool value && value;
        internal static bool InMainMenu => Static("MainMenuSingleton", "IsGameUiForceDisabled") is bool value && value;
        internal static bool SettingsOpen => Read(Static("SettingsUi", "Instance"), "IsOpen") is bool value && value;

        internal static object Static(string typeName, string name)
        {
            Type type = ReflectionTypes.Find(typeName);
            return type == null ? null : Get(type, null, name);
        }

        internal static object Read(object target, string name)
        {
            if (target == null || target is UnityEngine.Object obj && obj == null) return null;
            return Get(target.GetType(), target, name);
        }

        private static object Get(Type type, object target, string name)
        {
            var key = new MemberKey(type, name);
            try
            {
                if (!members.TryGetValue(key, out MemberInfo info))
                {
                    for (Type current = type; current != null && info == null; current = current.BaseType)
                    {
                        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                        info = (MemberInfo)current.GetProperty(name, flags) ?? current.GetField(name, flags);
                    }
                    members[key] = info;
                }
                if (info is PropertyInfo property) return property.GetValue(target);
                return (info as FieldInfo)?.GetValue(target);
            }
            catch (Exception ex) { Report(key.ToString(), ex); return null; }
        }

        internal static void Write(object target, string name, object value)
        {
            if (target == null) return;
            var key = new MemberKey(target.GetType(), name);
            try
            {
                Get(target.GetType(), target, name);
                if (members[key] is FieldInfo field) field.SetValue(target, value);
                else if (members[key] is PropertyInfo property && property.CanWrite) property.SetValue(target, value);
            }
            catch (Exception ex) { Report(key.ToString(), ex); }
        }

        internal static object Call(object target, string name, params object[] args)
        {
            if (target == null || target is UnityEngine.Object obj && obj == null) return null;
            var key = new MemberKey(target.GetType(), name, true);
            try
            {
                if (!members.TryGetValue(key, out MemberInfo info))
                {
                    info = AccessTools.Method(target.GetType(), name);
                    members[key] = info;
                }
                if (!(info is MethodInfo method)) return null;
                if (!methodParameters.TryGetValue(method, out ParameterInfo[] parameters))
                    methodParameters[method] = parameters = method.GetParameters();
                if (args.Length < parameters.Length)
                {
                    var expanded = new object[parameters.Length];
                    Array.Copy(args, expanded, args.Length);
                    for (int i = args.Length; i < parameters.Length; i++)
                    {
                        if (!parameters[i].HasDefaultValue) throw new TargetParameterCountException("Required argument missing for " + key);
                        expanded[i] = parameters[i].DefaultValue;
                    }
                    args = expanded;
                }
                return method.Invoke(target, args);
            }
            catch (Exception ex) { Report(key.ToString(), ex); return null; }
        }

        internal static bool Allowed(Component interactor)
        {
            return interactor != null && Read(interactor, "isLocalPlayer") is bool local && local
                && Call(interactor, "IsAnyMenuOpen") is bool menu && !menu
                && Call(interactor, "IsInputAllowed") is bool allowed && allowed;
        }

        internal static void Interact(bool pressed)
        {
            Component interactor = Interactor;
            if (!pressed) { Call(interactor, "CancelCurrentInteraction"); return; }
            if (!Allowed(interactor)) return;
            Call(interactor, "HandleInteractionRay");
            Call(interactor, "HandleInteractionInput");
            Call(interactor, "UpdateTaserEnabledState");
        }

        internal static void TogglePause()
        {
            object settings = Static("SettingsUi", "Instance");
            if (!(Read(settings, "IsOpen") is bool opened)) return;
            // The native cursor controller writes this flag back to SettingsUi
            // every Update. Both must change or the menu closes next frame.
            object cursor = Static("MetaCursorNetDebug", "Instance");
            Write(cursor, "escapeMenuOpen", !opened);
            Write(settings, "IsOpen", !opened);
        }

        internal static void Back()
        {
            if (Static("KolkataMonitorInputState", "WasEscapeConsumedThisFrame") is bool consumed && consumed) return;
            VRManager manager = VRManager.Instance;
            if (manager?.Keyboard != null && manager.Keyboard.Visible) { manager.Keyboard.Close(); return; }
            if (Static("FeedbackUi", "IsOpen") is bool feedbackOpen && feedbackOpen)
            {
                try
                {
                    Type feedback = ReflectionTypes.Find("FeedbackUi");
                    if (feedback != null) AccessTools.Method(feedback, "TryDismissWithEscape")?.Invoke(null, null);
                }
                catch (Exception ex) { Report("Feedback back", ex); }
                return;
            }
            Type hudType = ReflectionTypes.Find("ScammerGameHudController");
            object hud = hudType == null ? null : UnityEngine.Object.FindAnyObjectByType(hudType);
            if (Read(hud, "runBreakdownVisible") is bool reviewOpen && reviewOpen)
            {
                Call(hud, "DismissRunBreakdownWithEscape");
                return;
            }
            object players = Static("TabMenu", "Instance");
            if (Read(players, "IsOpen") is bool playersOpen && playersOpen) { Write(players, "IsOpen", false); return; }
            if (MonitorCaptured)
            {
                object desktop = Static("KolkataOsSingleton", "Instance");
                if (desktop == null) return;
                Call(desktop, "RequestActiveMonitorClose", false);
                Type state = ReflectionTypes.Find("KolkataMonitorInputState");
                AccessTools.Method(state, "ConsumeEscapeThisFrame")?.Invoke(null, null);
                return;
            }
            TogglePause();
        }

        internal static void TogglePlayers()
        {
            if (InMainMenu || MonitorCaptured) return;
            if (SettingsOpen) TogglePause();
            object players = Static("TabMenu", "Instance");
            if (Read(players, "IsOpen") is bool open) Write(players, "IsOpen", !open);
        }

        internal static void Attack(bool pressed)
        {
            if (pressed && (VRManager.Instance?.Interactive != true || InMainMenu)) return;
            NativeItemInput.Send(Interactor, false, pressed);
        }

        internal static void SecondaryUse(bool pressed)
        {
            if (pressed && (VRManager.Instance?.Interactive != true || InMainMenu)) return;
            if (pressed) Call(Interactor, "HandleInteractionRay");
            NativeItemInput.Send(Interactor, true, pressed);
        }

        internal static void TertiaryUse(bool pressed)
        {
            if (pressed && (VRManager.Instance?.Interactive != true || InMainMenu)) return;
            NativeItemInput.SendNamed(Interactor, "OnTertiaryInteract", pressed);
        }

        internal static void SwapHip(bool pressed)
        {
            if (pressed && (VRManager.Instance?.Interactive != true || InMainMenu)) return;
            NativeItemInput.SendNamed(Interactor, "OnSecondaryInteract", pressed);
        }

        private static Component FartManager
        {
            get
            {
                Type type = ReflectionTypes.Find("PlayerFartManager");
                Component player = LocalPlayer;
                return player != null && type != null ? player.GetComponent(type) : null;
            }
        }
        internal static bool HasConeBoost => Call(FartManager, "HasConeHipItem") is bool cone && cone;
        internal static void FartBoost() => NativeItemInput.SendNamed(FartManager, "OnFart", true);

        internal static void Drop(Component item, Vector3 velocity, Transform hand)
        {
            Component interactor = Interactor;
            if (item == null || hand == null || !Allowed(interactor)
                || !ReferenceEquals(Read(interactor, "CurrentHeldItem"), item)) return;
            Call(interactor, "CmdDropHeldItem", hand.forward, Vector3.ClampMagnitude(velocity, 8f),
                item.transform.position, item.transform.rotation);
        }

        internal static void Report(string key, Exception exception)
        {
            if (failures.Add(key)) Plugin.Log.LogWarning(key + ": " + exception.GetBaseException().Message);
        }
    }
}
