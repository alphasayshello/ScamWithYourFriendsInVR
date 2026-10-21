using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private sealed class MethodCost
        {
            internal long Calls, Ticks, PeakTicks, Bytes;
        }
        private struct CostStart { internal long Ticks, Bytes; }
        private static readonly Dictionary<MethodBase, MethodCost> methodCosts = new Dictionary<MethodBase, MethodCost>();
        private static bool profileSampling, profileAllocationSupported;

        private static void BeginMethodCost(out CostStart __state)
        {
            __state = default;
            if (!profileSampling) return;
            __state.Bytes = profileAllocationSupported ? GC.GetAllocatedBytesForCurrentThread() : 0;
            __state.Ticks = Stopwatch.GetTimestamp();
        }
        private static void EndMethodCost(MethodBase __originalMethod, CostStart __state)
        {
            if (__state.Ticks == 0) return;
            long ticks = Stopwatch.GetTimestamp() - __state.Ticks;
            long bytes = profileAllocationSupported ? GC.GetAllocatedBytesForCurrentThread() - __state.Bytes : 0;
            var cost = methodCosts[__originalMethod];
            cost.Calls++; cost.Ticks += ticks; cost.PeakTicks = Math.Max(cost.PeakTicks, ticks); cost.Bytes += Math.Max(0, bytes);
        }

        private IEnumerator ProfileModFrames()
        {
            var manager = VRManager.Instance;
            Require(manager?.Interactive == true, "Frame profiling requires a focused tracked mock host");
            var probe = new Harmony("com.swyf.vr.framecostfixture");
            bool openedSettings = false;
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter", true);
            var toggle = AccessTools.Method(adapter, "TogglePause");
            try
            {
                try
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    var allocationProbe = new byte[16384];
                    long after = GC.GetAllocatedBytesForCurrentThread(); GC.KeepAlive(allocationProbe);
                    profileAllocationSupported = after - before >= allocationProbe.Length;
                }
                catch (Exception) { profileAllocationSupported = false; }
                foreach (Type type in new[] { typeof(Input.VRInputManager), typeof(Input.VRAvatarArms), typeof(Input.VRBodyActions),
                    typeof(Camera.VRCameraController), typeof(Camera.VRAudioController), typeof(Camera.VRDesktopCamera),
                    typeof(UI.VRPointer), typeof(UI.VRUIPatcher), typeof(UI.VRSettingsPanel), typeof(UI.VRVirtualKeyboard),
                    typeof(VRPerformance), typeof(Multiplayer.PoseNetwork), typeof(Multiplayer.RemotePoseAvatar) })
                    foreach (string name in new[] { "Update", "LateUpdate", "DiscoverTexturePresenters" })
                    {
                        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                        if (method == null) continue;
                        methodCosts.Add(method, new MethodCost());
                        probe.Patch(method, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BeginMethodCost)),
                            postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(EndMethodCost)));
                    }
                yield return new WaitForSecondsRealtime(10);
                foreach (string scenario in new[] { "office-idle", "settings-open" })
                {
                    if (scenario == "settings-open")
                    {
                        Require(!(bool)AccessTools.Property(adapter, "SettingsOpen").GetValue(null), "Profile fixture found settings already open");
                        toggle.Invoke(null, null); openedSettings = true;
                        yield return new WaitForSecondsRealtime(2);
                        Require((bool)AccessTools.Property(adapter, "SettingsOpen").GetValue(null), "Native settings did not open for profiling");
                    }
                    foreach (var cost in methodCosts.Values) { cost.Calls = cost.Ticks = cost.PeakTicks = cost.Bytes = 0; }
                    var frames = new List<float>(2048);
                    float started = Time.realtimeSinceStartup;
                    profileSampling = true;
                    while (Time.realtimeSinceStartup - started < 20)
                    {
                        yield return null;
                        frames.Add(Time.unscaledDeltaTime * 1000);
                    }
                    profileSampling = false;
                    Require(frames.Count > 30, "Profile window has too few frames");
                    frames.Sort();
                    Logger.LogInfo("PROFILE FRAME: scenario=" + scenario + "; frames=" + frames.Count + "; medianMs=" + frames[frames.Count / 2].ToString("F3")
                        + "; p95Ms=" + frames[(int)((frames.Count - 1) * .95f)].ToString("F3") + "; allocations=" + profileAllocationSupported);
                    double milliseconds = 1000.0 / Stopwatch.Frequency;
                    foreach (var entry in methodCosts.OrderByDescending(e => e.Value.Ticks))
                    {
                        var cost = entry.Value;
                        Logger.LogInfo("PROFILE METHOD: scenario=" + scenario + "; method=" + entry.Key.DeclaringType.Name + "." + entry.Key.Name
                            + "; calls=" + cost.Calls + "; meanMsPerFrame=" + (cost.Ticks * milliseconds / frames.Count).ToString("F4")
                            + "; peakCallMs=" + (cost.PeakTicks * milliseconds).ToString("F3")
                            + "; bytesPerFrame=" + (profileAllocationSupported ? (cost.Bytes / (double)frames.Count).ToString("F1") : "unavailable"));
                    }
                }
                Logger.LogInfo("PROFILE COMPLETE: inclusive mod callback costs sampled in one mock office host and native settings; no headset/GPU performance claim");
            }
            finally
            {
                profileSampling = false; probe.UnpatchSelf(); methodCosts.Clear();
                if (openedSettings && (bool)AccessTools.Property(adapter, "SettingsOpen").GetValue(null)) toggle.Invoke(null, null);
            }
        }
    }
}
