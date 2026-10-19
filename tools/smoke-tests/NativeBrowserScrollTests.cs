using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object browserScrollHostProbe, browserScrollClientProbe;
        private static int browserScrollCalls, browserScrollPixels;
        private static Vector2 browserScrollPosition;
        private static int browserDownCalls, browserUpCalls;
        private static Vector2 browserUpPosition;
        private static bool BrowserClickProbe(object __instance, object[] __args)
        {
            if (!ReferenceEquals(__instance, browserScrollClientProbe)) return true;
            if (__args[3].ToString() == "Down") browserDownCalls++;
            if (__args[3].ToString() == "Up") { browserUpCalls++; browserUpPosition = (Vector2)__args[0]; }
            return false;
        }
        private static bool BrowserReadyProbe(object __instance, ref bool __result)
        {
            if (!ReferenceEquals(__instance, browserScrollHostProbe)) return true;
            __result = true; return false;
        }
        private static bool BrowserScrollProbe(object __instance, object[] __args)
        {
            if (!ReferenceEquals(__instance, browserScrollClientProbe)) return true;
            browserScrollCalls++; browserScrollPosition = (Vector2)__args[0]; browserScrollPixels = (int)__args[1]; return false;
        }

        private IEnumerator TestNativeBrowserScroll(object os)
        {
            object host = Member(os, "m_browserHost");
            Require(host != null, "Native browser host missing");
            AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "browser" });
            yield return null; yield return null;
            var viewport = Member(host, "viewport") as VisualElement;
            Require(viewport?.panel != null, "Native browser did not bind its viewport");
            var image = Member(host, "browserImage") as VisualElement ?? viewport;
            Require(image.worldBound.width > 1 && image.worldBound.height > 1, "Native browser viewport did not lay out");
            FieldInfo clientField = AccessTools.Field(host.GetType(), "browserClient");
            object original = clientField.GetValue(host);
            // Exercise the exact native signature, but intercept transport before
            // any IPC. The probe requires no live CEF connection and does not
            // change the game's browser privacy setting.
            object dummy = FormatterServices.GetUninitializedObject(clientField.FieldType);
            GC.SuppressFinalize(dummy);
            var probe = new Harmony("com.swyf.vr.browserscrollprobe");
            try
            {
                browserScrollHostProbe = host; browserScrollClientProbe = dummy; browserScrollCalls = 0;
                probe.Patch(AccessTools.Method(host.GetType(), "CanUseBrowserClient"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BrowserReadyProbe)));
                probe.Patch(AccessTools.Method(dummy.GetType(), "SendMouseScroll"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BrowserScrollProbe)));
                probe.Patch(AccessTools.Method(dummy.GetType(), "SendMouseClick"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BrowserClickProbe)));
                clientField.SetValue(host, dummy);
                var wheel = AccessTools.Method(typeof(VRPointer), "Scroll");
                Vector2 point = image.LocalToWorld(image.contentRect.center);
                wheel.Invoke(VRManager.Instance.Pointer, new object[] { viewport, point, -.5f });
                Require(browserScrollCalls == 1 && browserScrollPixels == 16, "Controller wheel did not reach the native browser transport signature exactly once");
                var expected = new Vector2(Convert.ToInt32(Member(host, "browserPixelWidth")) * .5f,
                    Convert.ToInt32(Member(host, "browserPixelHeight")) * .5f);
                Require(Vector2.Distance(browserScrollPosition, expected) < 2, "Controller browser wheel did not use native page coordinates");
                browserDownCalls = browserUpCalls = 0;
                var send = AccessTools.Method(typeof(VRPointer), "Send3D");
                send.Invoke(VRManager.Instance.Pointer, new object[] { viewport, EventType.MouseDown, (Vector3)point, 0, true });
                Vector2 outside = image.LocalToWorld(new Vector2(image.layout.width + 200, -200));
                send.Invoke(VRManager.Instance.Pointer, new object[] { viewport, EventType.MouseUp, (Vector3)outside, 0, false });
                Require(browserDownCalls == 1 && browserUpCalls == 1, "Native browser lost or duplicated the controller release outside its image");
                Require(Vector2.Distance(browserUpPosition, new Vector2(expected.x * 2, 0)) < 2,
                    "Native browser release did not clamp to the page boundary");
                Require(!viewport.HasPointerCapture(VRPointer.ToolkitPointerId), "Native browser retained controller capture after release");
                using (var mouseUp = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = outside, button = 0 }))
                {
                    mouseUp.target = viewport; viewport.SendEvent(mouseUp);
                }
                Require(browserUpCalls == 1, "Controller release correction changed native physical-mouse behaviour");
                send.Invoke(VRManager.Instance.Pointer, new object[] { viewport, EventType.MouseDown, (Vector3)point, 0, true });
                send.Invoke(VRManager.Instance.Pointer, new object[] { viewport, EventType.MouseUp, (Vector3)point, 0, false });
                Require(browserDownCalls == 2 && browserUpCalls == 2 && Vector2.Distance(browserUpPosition, expected) < 2,
                    "Normal in-page controller click changed or duplicated its release");
                VRManager.Instance.Pointer.Cancel();
                Logger.LogInfo("BROWSER PASS: desktop controller wheel resolves the native browser viewport/client and pixel-coordinate transport; page rendering not covered");
                Logger.LogInfo("BROWSER PASS: native controller press/off-image release clears capture, preserves in-page clicks and leaves physical-mouse behaviour unchanged");
            }
            finally
            {
                if (viewport.HasPointerCapture(VRPointer.ToolkitPointerId)) viewport.ReleasePointer(VRPointer.ToolkitPointerId);
                clientField.SetValue(host, original); probe.UnpatchSelf(); browserScrollHostProbe = browserScrollClientProbe = null;
                VRManager.Instance.Pointer.Cancel();
                object app = (Member(os, "m_appsById") as System.Collections.IDictionary)?["browser"];
                if (app != null) AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
            }
        }
    }
}
