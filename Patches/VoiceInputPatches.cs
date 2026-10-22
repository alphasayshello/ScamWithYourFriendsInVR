using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ScamWithYourFriendsInVR.Patches
{
    internal static class VoiceInputPatches
    {
        private static FieldInfo pressed;
        internal static void Install(Harmony harmony)
        {
            Input.VRVoiceControls.Clear();
            Type type = ReflectionTypes.Find("PlayerPushToTalk");
            MethodInfo update = type == null ? null : AccessTools.Method(type, "Update");
            MethodInfo local = type == null ? null : AccessTools.PropertyGetter(type, "IsLocalTalkPressed");
            pressed = type == null ? null : AccessTools.Field(type, "isPressed");
            if (update == null || local == null || pressed?.FieldType != typeof(bool))
            { Plugin.Log.LogWarning("Native controller push-to-talk hooks unavailable."); return; }
            try
            {
                var patch = new HarmonyMethod(typeof(VoiceInputPatches), nameof(VoiceReads));
                harmony.Patch(update, transpiler: patch);
                harmony.Patch(local, transpiler: patch);
                Input.VRVoiceControls.Available = true;
            }
            catch (Exception ex)
            {
                harmony.Unpatch(update, HarmonyPatchType.Transpiler, harmony.Id);
                harmony.Unpatch(local, HarmonyPatchType.Transpiler, harmony.Id);
                GameAdapter.Report("Controller push-to-talk", ex);
            }
        }

        private static bool ReadPressed(bool native, object target) => native || Input.VRVoiceControls.HeldFor(target);

        private static IEnumerable<CodeInstruction> VoiceReads(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (!instruction.LoadsField(pressed)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VoiceInputPatches), nameof(ReadPressed)));
                replacements++;
            }
            if (replacements == 0) throw new InvalidOperationException("Native voice input no longer reads its press state: " + __originalMethod.Name);
        }
    }
}
