using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static string paintFixtureFolder;
        private static bool PaintFixtureFolder(ref string __result)
        { if (paintFixtureFolder == null) return true; __result = paintFixtureFolder; return false; }

        private IEnumerator TestNativePaint(object os, VRPanelSurface surface)
        {
            object game = Instance("ScammerGameManager");
            Require(game != null && Member(game, "isServer") is bool server && server, "Paint fixture requires the owned private host");
            bool originalUnlock = (bool)Member(game, "DeveloperAllAppsUnlocked");
            FieldInfo paintField = AccessTools.Field(os.GetType(), "m_paintApp");
            object originalPaint = paintField?.GetValue(os);
            Require(!originalUnlock && paintField != null && (Member(os, "m_appsById") as IDictionary)?["jw-paint"] == null
                && Member(originalPaint, "m_currentPath") == null, "Paint fixture requires an unavailable paint app without existing artwork open");
            MethodInfo unlock = AccessTools.Method(game.GetType(), "ServerSetDeveloperLobbySettings");
            MethodInfo refresh = AccessTools.Method(os.GetType(), "RefreshApplicationAccessRuntimes");
            Type paintType = Find("Kolkata.UI.KolkataOsPaintApp");
            Require(unlock != null && refresh != null && paintType != null, "Native paint fixture APIs missing");
            string tempRoot = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string folder = Path.Combine(tempRoot, "swyf-paint-fixture-" + Guid.NewGuid().ToString("N"));
            var probe = new Harmony("com.swyf.vr.paintfixture");
            object app = null, paint = null;
            var manager = VRManager.Instance;
            var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
            void Set(string key, bool value) => manager.Input.GetType().GetProperty(key).SetValue(manager.Input, value);
            Texture2D Texture() => Member(Member(paint, "m_canvas"), "Texture") as Texture2D;
            bool DarkCenter()
            {
                Texture2D texture = Texture(); Color pixel = texture.GetPixel(texture.width / 2, texture.height / 2 - 1);
                return pixel.r < .4f && pixel.g < .4f && pixel.b < .4f;
            }
            void Point(float x, float y)
            {
                var image = Member(paint, "m_canvasImage") as VisualElement;
                Texture2D texture = Texture(); Rect rect = image.contentRect;
                float scale = Mathf.Min(rect.width / texture.width, rect.height / texture.height);
                appFixturePoint = image.LocalToWorld(rect.center + new Vector2((x - .5f) * texture.width * scale, (y - .5f) * texture.height * scale));
            }
            void Trigger(bool pressed)
            {
                holdInjectedTrigger = pressed;
                Set("Trigger", pressed); Set("TriggerDown", pressed); Set("TriggerUp", !pressed);
                tick.Invoke(manager.Pointer, null); Set("TriggerDown", false); Set("TriggerUp", false);
            }
            try
            {
                Directory.CreateDirectory(folder); paintFixtureFolder = folder;
                probe.Patch(AccessTools.Method(paintType, "GetArtFolderPath"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(PaintFixtureFolder)));
                ConstructorInfo constructor = paintType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(c => c.GetParameters().Length == 6);
                paintField.SetValue(os, constructor.Invoke(new object[] { Member(os, "m_assets"), Member(os, "m_root"), os,
                    Member(os, "m_notifications"), new Action<string>(_ => { }),
                    new Action(() => AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "jw-paint" })) }));
                // The native developer flag is transient lobby state. Do not buy
                // licences or change the user's saved application entitlements.
                unlock.Invoke(game, new object[] { true, true }); refresh.Invoke(os, null);
                float deadline = Time.realtimeSinceStartup + 10;
                while (app == null)
                {
                    app = (Member(os, "m_appsById") as IDictionary)?["jw-paint"];
                    Require(Time.realtimeSinceStartup < deadline, "Native paint app did not become available in the fixture");
                    yield return null;
                }
                Require(Member(app, "IsAvailable") is bool available && available, "Native paint is unavailable on this desktop");
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "jw-paint" });
                yield return null; yield return null;
                paint = paintField.GetValue(os);
                var image = Member(paint, "m_canvasImage") as VisualElement;
                Require(paint != null && image?.panel != null && image.contentRect.width > 10 && Texture() != null,
                    "Native paint canvas did not initialize");
                Require(!DarkCenter(), "Native paint fixture did not start with a blank canvas");

                appFixtureSurface = surface; appFixtureAim = true;
                yield return new WaitForEndOfFrame();
                Point(.35f, .5f); Trigger(true);
                Require(manager.Pointer.HasPanelHit && Member(paint, "m_pointerDrawing") is bool drawing && drawing,
                    "Controller ray did not begin a native paint stroke");
                Point(.5f, .5f); yield return null; yield return new WaitForEndOfFrame(); tick.Invoke(manager.Pointer, null);
                Point(.65f, .5f); yield return null; yield return new WaitForEndOfFrame(); Trigger(false);
                Require(DarkCenter() && Member(Member(paint, "m_canvas"), "CanUndo") is bool undo && undo,
                    "Controller stroke did not change native canvas pixels/history");
                string saved = Member(paint, "m_currentPath") as string;
                Require(saved != null && Path.GetFullPath(saved).StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(saved), "Native paint did not save inside its isolated art folder");
                var decoded = new Texture2D(2, 2);
                try
                {
                    Require(decoded.LoadImage(File.ReadAllBytes(saved)), "Native paint did not save a decodable image");
                    Require(decoded.width == Texture().width && decoded.height == Texture().height
                        && decoded.GetPixel(decoded.width / 2, decoded.height / 2 - 1).r < .4f,
                        "Saved paint image does not contain the controller stroke");
                }
                finally { Destroy(decoded); }
                Logger.LogInfo("PAINT PASS: controller ray draws a native stroke and saves matching image pixels in an isolated folder");

                appFixtureAim = false;
                var click = ClickAppControl(surface, Member(paint, "m_undoButton") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(!DarkCenter() && Member(Member(paint, "m_canvas"), "CanRedo") is bool redo && redo,
                    "Controller undo did not restore the blank native canvas");
                click = ClickAppControl(surface, Member(paint, "m_redoButton") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(DarkCenter(), "Controller redo did not restore native paint pixels");
                Logger.LogInfo("PAINT PASS: controller undo/redo changes actual native canvas history and pixels");

                appFixtureAim = true; Point(.7f, .5f);
                var size = Member(paint, "m_sizeSlider") as SliderInt;
                Require(size != null, "Native paint brush size control missing");
                int before = size.value;
                holdInjectedSecondary = true; Set("Secondary", true); Set("SecondaryDown", true); tick.Invoke(manager.Pointer, null); Set("SecondaryDown", false);
                Require(Member(paint, "m_pointerResizing") is bool resizing && resizing, "Controller secondary hold did not begin native brush resizing");
                Point(.6f, .5f); yield return null; yield return new WaitForEndOfFrame(); tick.Invoke(manager.Pointer, null);
                Require(size.value != before, "Held controller secondary drag did not change native brush size");
                Set("TriggerUp", true); tick.Invoke(manager.Pointer, null); Set("TriggerUp", false);
                Require(Member(paint, "m_pointerResizing") is bool stillHeld && stillHeld, "Primary release canceled a held secondary UI drag");
                holdInjectedSecondary = false; Set("Secondary", false); tick.Invoke(manager.Pointer, null);
                Require(Member(paint, "m_pointerResizing") is bool ended && !ended && !image.HasPointerCapture(VRPointer.ToolkitPointerId),
                    "Secondary release left native brush resizing/capture active");
                Logger.LogInfo("PAINT PASS: held controller secondary drag changes native brush size and preserves button ownership");

                appFixtureAim = true; Point(.5f, .65f); Trigger(true);
                Require(Member(paint, "m_pointerDrawing") is bool held && held, "Paint focus-cancel stroke did not begin");
                Set("HasInputFocus", false); tick.Invoke(manager.Pointer, null); Set("HasInputFocus", true);
                holdInjectedTrigger = false; Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false);
                Require(Member(paint, "m_pointerDrawing") is bool stopped && !stopped && !image.HasPointerCapture(VRPointer.ToolkitPointerId),
                    "Controller focus loss left native paint drawing/capture active");
                Logger.LogInfo("PAINT PASS: controller focus cancellation finishes the native stroke and clears capture");
            }
            finally
            {
                appFixtureAim = false; appFixtureSurface = null; holdInjectedTrigger = holdInjectedSecondary = false;
                Set("HasInputFocus", true); Set("Trigger", false); Set("TriggerDown", false); Set("TriggerUp", false); manager.Pointer.Cancel();
                Set("Secondary", false); Set("SecondaryDown", false);
                if (app != null) AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app });
                unlock.Invoke(game, new object[] { true, originalUnlock }); refresh.Invoke(os, null);
                if (paintField.GetValue(os) is IDisposable owned && !ReferenceEquals(owned, originalPaint)) owned.Dispose();
                paintField.SetValue(os, originalPaint);
                probe.UnpatchSelf(); paintFixtureFolder = null;
                string resolved = Path.GetFullPath(folder);
                if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("swyf-paint-fixture-"))
                    try { if (Directory.Exists(resolved)) Directory.Delete(resolved, true); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    { Logger.LogInfo("Native paint fixture folder is still closing; retained its temporary directory."); }
            }
        }
    }
}
