using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ScamWithYourFriendsInVR.Patches
{
    internal static class NativeItemInput
    {
        private static readonly Dictionary<string, MethodInfo> callbacks = new Dictionary<string, MethodInfo>();
        private static MethodInfo pressedGetter;
        [ThreadStatic] private static object receiver;
        [ThreadStatic] private static string action;
        [ThreadStatic] private static bool pressed;

        internal static void Install(Harmony harmony)
        {
            callbacks.Clear();
            Type owner = ReflectionTypes.Find("PlayerInteractor"), input = ReflectionTypes.Find("UnityEngine.InputSystem.InputValue");
            pressedGetter = input == null ? null : AccessTools.PropertyGetter(input, "isPressed");
            if (owner == null || pressedGetter == null) { Plugin.Log.LogWarning("Native held-item callbacks unavailable."); return; }
            foreach (string name in new[] { "OnAttack", "OnBlock" })
            {
                MethodInfo method = AccessTools.Method(owner, name, new[] { input });
                if (method == null) { Plugin.Log.LogWarning("Native item callback missing: " + name); continue; }
                try
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NativeItemInput), nameof(PressedReads)));
                    callbacks[name] = method;
                }
                catch (Exception ex) { GameAdapter.Report("Native item callback " + name, ex); }
            }
        }

        internal static void Clear() => callbacks.Clear();

        internal static void Send(Component target, bool secondary, bool value)
        {
            string name = secondary ? "OnBlock" : "OnAttack";
            if (target == null || !callbacks.TryGetValue(name, out MethodInfo method)) return;
            object previousReceiver = receiver;
            string previousAction = action;
            bool previousPressed = pressed;
            try
            {
                receiver = target; action = name; pressed = value;
                method.Invoke(target, new object[] { null });
            }
            catch (Exception ex) { GameAdapter.Report("Controller native item " + name, ex); }
            finally { receiver = previousReceiver; action = previousAction; pressed = previousPressed; }
        }

        private static bool ReadPressed(object value, object target, string callback)
            => ReferenceEquals(receiver, target) && action == callback
                ? pressed : GameAdapter.Read(value, "isPressed") is bool result && result;

        private static IEnumerable<CodeInstruction> PressedReads(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replacements = 0;
            MethodInfo read = AccessTools.Method(typeof(NativeItemInput), nameof(ReadPressed));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(pressedGetter))
                {
                    var owner = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    yield return owner;
                    yield return new CodeInstruction(OpCodes.Ldstr, __originalMethod.Name);
                    instruction.opcode = OpCodes.Call; instruction.operand = read; replacements++;
                }
                yield return instruction;
            }
            if (replacements == 0) throw new InvalidOperationException("Native item input no longer reads InputValue.isPressed: " + __originalMethod.Name);
        }
    }
}
