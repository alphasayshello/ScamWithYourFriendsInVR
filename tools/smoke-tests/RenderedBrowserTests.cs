using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        [Serializable]
        private sealed class BrowserPageState { public int clicks = 0, downs = 0, ups = 0, moves = 0, scroll = 0, contexts = 0; public bool dragging = false; }
        private static bool renderedBrowserStarting;
        private static object renderedBrowserHost;
        private static string renderedBrowserProfile;
        private static bool AllowRenderedBrowserOnly() => renderedBrowserStarting;
        private static void PrepareRenderedBrowserClient(object __instance)
        {
            if (!renderedBrowserStarting || !ReferenceEquals(Member(renderedBrowserHost, "browserClient"), __instance)) return;
            AccessTools.Field(__instance.GetType(), "initialUrl").SetValue(__instance, "about:blank");
            AccessTools.Field(__instance.GetType(), "incognitoMode").SetValue(__instance, true);
            AccessTools.Field(__instance.GetType(), "localStorage").SetValue(__instance, false);
            AccessTools.Field(__instance.GetType(), "cachePath").SetValue(__instance, new FileInfo(renderedBrowserProfile));
        }
        private static float renderedBrowserScroll;

        private IEnumerator TestRenderedBrowser(object os, VRPanelSurface surface)
        {
            object host = Member(os, "m_browserHost");
            Require(host != null && Member(host, "browserClient") == null, "Rendered browser fixture requires a closed native browser");
            var apps = Member(os, "m_appsById") as IDictionary;
            object app = apps?["browser"];
            Require(app != null, "Native browser app missing");
            var hostFields = new Dictionary<FieldInfo, object>();
            foreach (string key in new[] { "homeUrl", "currentUrl", "pendingUrl", "currentTitle", "privacyApproved" })
            {
                FieldInfo field = AccessTools.Field(host.GetType(), key); Require(field != null, "Native browser field missing: " + key);
                hostFields[field] = field.GetValue(host);
            }
            FieldInfo confirmed = AccessTools.Field(os.GetType(), "m_browserOpeningConfirmed");
            Require(confirmed != null, "Native browser opening state missing");
            object originalConfirmed = confirmed.GetValue(os);
            var nativeHistory = Member(host, "navigationHistory") as IList;
            var history = new List<object>();
            if (nativeHistory != null) foreach (object entry in nativeHistory) history.Add(entry);
            object historyIndex = Member(host, "navigationHistoryIndex");
            var probe = new Harmony("com.swyf.vr.renderedbrowserfixture");
            object client = null;
            var manager = VRManager.Instance;
            var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
            void Set(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            void SetHost(string name, object value) => AccessTools.Field(host.GetType(), name).SetValue(host, value);
            BrowserPageState State()
            {
                string title = Member(host, "currentTitle") as string;
                return title != null && title.StartsWith("SWYF_FIXTURE:") ? JsonUtility.FromJson<BrowserPageState>(title.Substring(13)) : null;
            }
            IEnumerator Await(Func<BrowserPageState, bool> predicate, string failure)
            {
                float deadline = Time.realtimeSinceStartup + 15;
                while (true)
                {
                    BrowserPageState state = State();
                    if (state != null && predicate(state)) yield break;
                    Require(Time.realtimeSinceStartup < deadline, failure + "; fixture=" + (state == null ? "not ready" : JsonUtility.ToJson(state)));
                    yield return null;
                }
            }
            void PagePoint(float x, float y)
            {
                var image = Member(host, "browserImage") as VisualElement;
                Require(image?.panel != null, "Rendered browser image detached");
                int width = Convert.ToInt32(Member(host, "browserPixelWidth")), height = Convert.ToInt32(Member(host, "browserPixelHeight"));
                appFixturePoint = image.LocalToWorld(new Vector2(x / width * image.contentRect.width, y / height * image.contentRect.height));
            }
            void Trigger(bool pressed)
            {
                holdInjectedTrigger = pressed;
                Set("Trigger", pressed); Set("TriggerDown", pressed); Set("TriggerUp", !pressed);
                tick.Invoke(manager.Pointer, null); Set("TriggerDown", false); Set("TriggerUp", false);
            }
            try
            {
                renderedBrowserHost = host;
                renderedBrowserProfile = Path.Combine(Application.temporaryCachePath, "swyf-browser-fixture-" + Guid.NewGuid().ToString("N"));
                renderedBrowserStarting = true;
                probe.Patch(AccessTools.Method(AccessTools.Field(host.GetType(), "browserClient").FieldType, "Init"),
                    prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(PrepareRenderedBrowserClient)));
                // Only this optional private fixture enables its blank browser.
                // Saved privacy settings are never changed, and the page has no
                // network resources or navigation targets.
                confirmed.SetValue(os, true);
                SetHost("homeUrl", "about:blank"); SetHost("currentUrl", "about:blank"); SetHost("pendingUrl", "about:blank");
                float startupBegan = Time.realtimeSinceStartup;
                AccessTools.Method(host.GetType(), "ApproveOpening").Invoke(host, null);
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "browser" });
                float deadline = Time.realtimeSinceStartup + 45;
                while (!(AccessTools.Method(host.GetType(), "CanUseBrowserClient").Invoke(host, null) is bool ready && ready))
                {
                    Require(Time.realtimeSinceStartup < deadline, "CEF did not become ready; state=" + Member(host, "browserInitializationState"));
                    yield return null;
                }
                client = Member(host, "browserClient");
                Require(Convert.ToInt32(Member(client, "engineStartupTimeout")) >= 20000, "VR native browser startup allowance was not applied");
                Logger.LogInfo("RENDERED BROWSER startup: readyMs=" + (Time.realtimeSinceStartup - startupBegan) * 1000f);
                string html = @"<!doctype html><html><head><meta http-equiv='Content-Security-Policy' content=""default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'""><style>
html,body{margin:0;background:#123456;color:white;font:28px sans-serif}body{height:4000px}button,#drag{position:absolute;top:32px;width:220px;height:96px}button{left:32px}#drag{left:320px;background:#c84220;user-select:none}#marker{position:absolute;top:1500px}</style></head><body>
<button id='tap'>click test</button><div id='drag'>drag test</div><div id='marker'>scroll test</div><script>
let s={clicks:0,downs:0,ups:0,moves:0,scroll:0,contexts:0,dragging:false};function report(){s.scroll=Math.round(scrollY);document.title='SWYF_FIXTURE:'+JSON.stringify(s)};
document.querySelector('#tap').onclick=()=>{s.clicks++;report()};document.querySelector('#drag').onmousedown=()=>{s.dragging=true;report()};
document.onmousedown=()=>{s.downs++;report()};document.onmouseup=()=>{s.ups++;s.dragging=false;report()};document.onmousemove=()=>{if(s.dragging){s.moves++;report()}};document.oncontextmenu=e=>{e.preventDefault();s.contexts++;report()};window.onscroll=report;report();</script></body></html>";
                AccessTools.Method(client.GetType(), "LoadHtml").Invoke(client, new object[] { html });
                IEnumerator wait = Await(s => s.clicks == 0, "Local CEF fixture did not load");
                while (wait.MoveNext()) yield return wait.Current;
                yield return new WaitForSecondsRealtime(.5f);
                var texture = Member(client, "BrowserTexture") as Texture2D;
                Require(texture != null && texture.width > 300 && texture.height > 150, "CEF has no rendered page texture");
                Color pixel = texture.GetPixel(texture.width / 2, texture.height / 2);
                Require(Vector3.Distance(new Vector3(pixel.r, pixel.g, pixel.b), new Vector3(18 / 255f, 52 / 255f, 86 / 255f)) < .12f,
                    "CEF page texture did not render its local background: " + pixel);
                probe.Patch(tick, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AimAppFixture)));
                appFixtureSurface = surface; appFixtureAim = true;
                yield return new WaitForEndOfFrame();
                PagePoint(120, 75); Trigger(true);
                Require(manager.Pointer.HasPanelHit, "Controller ray missed rendered browser");
                yield return null; yield return new WaitForEndOfFrame(); Trigger(false);
                wait = Await(s => s.clicks == 1 && s.ups == 1, "Rendered webpage did not receive one controller click");
                while (wait.MoveNext()) yield return wait.Current;
                Logger.LogInfo("RENDERED BROWSER PASS: real CEF page texture and controller-ray DOM click");

                PagePoint(370, 75); Trigger(true);
                wait = Await(s => s.dragging, "Rendered browser drag did not begin");
                while (wait.MoveNext()) yield return wait.Current;
                PagePoint(480, 110); yield return new WaitForSecondsRealtime(.2f);
                PagePoint(Convert.ToInt32(Member(host, "browserPixelWidth")) + 100, -100);
                yield return new WaitForEndOfFrame(); Trigger(false);
                wait = Await(s => s.ups == 2 && !s.dragging && s.moves > 0, "Rendered webpage retained a drag after off-page release");
                while (wait.MoveNext()) yield return wait.Current;
                Logger.LogInfo("RENDERED BROWSER PASS: real DOM drag/move and off-page controller release");

                PagePoint(600, 250); renderedBrowserScroll = -1;
                yield return new WaitForSecondsRealtime(.8f);
                renderedBrowserScroll = 0;
                manager.Input.GetType().GetProperty("Move").SetValue(manager.Input, Vector2.zero);
                wait = Await(s => s.scroll > 50, "Rendered webpage did not scroll from controller stick");
                while (wait.MoveNext()) yield return wait.Current;
                Logger.LogInfo("RENDERED BROWSER PASS: controller stick scrolls actual local webpage; scrollY=" + State().scroll);

                yield return new WaitForSecondsRealtime(.75f);
                AccessTools.Method(client.GetType(), "ExecuteJs").Invoke(client, new object[] { "window.scrollTo(0,0);report();" });
                wait = Await(s => s.scroll == 0, "Local webpage did not return to its top");
                while (wait.MoveNext()) yield return wait.Current;
                PagePoint(370, 75); Trigger(true);
                wait = Await(s => s.dragging, "Focus-cancel drag did not begin");
                while (wait.MoveNext()) yield return wait.Current;
                Set("HasInputFocus", false); tick.Invoke(manager.Pointer, null); Set("HasInputFocus", true);
                holdInjectedTrigger = false; Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false);
                wait = Await(s => s.ups == 3 && !s.dragging, "Controller focus loss left the rendered page dragging");
                while (wait.MoveNext()) yield return wait.Current;
                Require(!(bool)Member(manager.Pointer, "toolkitDown"), "Controller focus loss retained UI press ownership");
                Logger.LogInfo("RENDERED BROWSER PASS: controller focus cancellation releases the actual DOM drag");

                PagePoint(600, 250); Set("Secondary", true); Set("SecondaryDown", true); tick.Invoke(manager.Pointer, null); Set("SecondaryDown", false);
                Set("Secondary", false); tick.Invoke(manager.Pointer, null);
                wait = Await(s => s.contexts == 1, "Controller secondary button did not reach webpage context input");
                while (wait.MoveNext()) yield return wait.Current;
                Logger.LogInfo("RENDERED BROWSER PASS: controller secondary button reaches real webpage context click");

                PagePoint(370, 75); Trigger(true);
                wait = Await(s => s.dragging, "Close-during-drag fixture did not begin dragging");
                while (wait.MoveNext()) yield return wait.Current;
                var closingImage = Member(host, "browserImage") as VisualElement;
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                yield return null; yield return new WaitForEndOfFrame();
                tick.Invoke(manager.Pointer, null);
                Require(!(bool)Member(manager.Pointer, "toolkitDown")
                    && (closingImage?.panel == null || !closingImage.HasPointerCapture(VRPointer.ToolkitPointerId)),
                    "Closing the browser retained controller press/capture ownership");
                holdInjectedTrigger = false; Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false);
                Logger.LogInfo("RENDERED BROWSER PASS: closing the real browser during a controller drag clears press/capture ownership");
            }
            finally
            {
                appFixtureAim = false; renderedBrowserScroll = 0; appFixtureSurface = null; holdInjectedTrigger = false;
                Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false);
                Set("HasInputFocus", true); Set("SecondaryDown", false);
                manager.Input.GetType().GetProperty("Move").SetValue(manager.Input, Vector2.zero); manager.Pointer.Cancel();
                renderedBrowserStarting = false;
                probe.UnpatchSelf();
                AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                AccessTools.Method(host.GetType(), "ResetPrivacyApproval").Invoke(host, null);
                renderedBrowserStarting = false; renderedBrowserHost = null;
                string tempRoot = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string profile = Path.GetFullPath(renderedBrowserProfile);
                if (profile.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(profile).StartsWith("swyf-browser-fixture-"))
                    try { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    { Logger.LogInfo("Rendered browser fixture profile is still closing; retained its temporary directory."); }
                renderedBrowserProfile = null;
                foreach (var field in hostFields) field.Key.SetValue(host, field.Value);
                confirmed.SetValue(os, originalConfirmed);
                if (nativeHistory != null) { nativeHistory.Clear(); foreach (object entry in history) nativeHistory.Add(entry); }
                AccessTools.Field(host.GetType(), "navigationHistoryIndex")?.SetValue(host, historyIndex);
            }
        }
    }
}
