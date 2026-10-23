using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private static object creatorFixture;
        private static int creatorUnsafeAttempts;
        private static bool creatorThrowEditor;
        private static void ThrowCreatorFixtureEditor(object __instance)
        {
            if (!ReferenceEquals(__instance, creatorFixture) || !creatorThrowEditor) return;
            creatorThrowEditor = false;
            throw new InvalidOperationException("isolated creator editor failure");
        }
        private static string creatorUserDraftPath;
        private static byte[] creatorUserDraftBytes;
        private static void InstallCreatorDraftIsolation(Harmony harmony)
        {
            creatorUserDraftPath = Path.Combine(Application.persistentDataPath, "Character Creator", "draft.json");
            creatorUserDraftBytes = File.Exists(creatorUserDraftPath) ? File.ReadAllBytes(creatorUserDraftPath) : null;
            var type = Find("Kolkata.UI.KolkataOsCharacterCreatorApp");
            var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 4);
            harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(IsolateCreatorDraftPath)));
        }
        private static void IsolateCreatorDraftPath(object __instance)
        {
            string folder = Path.Combine(Application.temporaryCachePath, "swyf-creator-owner-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            AccessTools.Field(__instance.GetType(), "m_draftPath").SetValue(__instance, Path.Combine(folder, "draft.json"));
        }
        private static void VerifyCreatorUserDraft()
        {
            Require(creatorUserDraftBytes == null ? !File.Exists(creatorUserDraftPath)
                : File.Exists(creatorUserDraftPath) && File.ReadAllBytes(creatorUserDraftPath).SequenceEqual(creatorUserDraftBytes),
                "Private harness changed the real user's character draft");
        }
        private static bool BlockCreatorFixtureAction(object __instance)
        {
            if (!ReferenceEquals(__instance, creatorFixture)) return true;
            creatorUnsafeAttempts++; return false;
        }

        private IEnumerator TestNativeCreator(object os, VRPanelSurface surface)
        {
            Require(NetworkServer.connections.Count == 1, "Creator fixture requires one private host");
            bool laptop = (bool)Member(os, "m_useLaptopDesktop");
            var mode = AccessTools.Method(os.GetType(), "SetLaptopDesktop");
            var field = AccessTools.Field(os.GetType(), "m_characterCreatorApp");
            object original = field.GetValue(os), fixture = null, app = null;
            object originalDirty = Member(original, "m_draftDirty");
            Require(!(Member(original, "m_recording") is true) && !(Member(original, "m_voicePreviewGenerating") is true) && !(Member(original, "m_pointerDrawing") is true),
                "Creator fixture requires no active recording or generation");
            string userDraft = (string)Member(original, "m_draftPath");
            byte[] originalBytes = userDraft != null && File.Exists(userDraft) ? File.ReadAllBytes(userDraft) : null;
            string folder = Path.Combine(Application.temporaryCachePath, "swyf-creator-" + Guid.NewGuid().ToString("N"));
            string draftPath = Path.Combine(folder, "draft.json");
            var probe = new Harmony("com.swyf.vr.creatorfixture");
            bool built = false;
            object contentBuilt = null;
            VisualElement[] originalChildren = null;
            try
            {
                mode.Invoke(os, new object[] { true }); yield return null; yield return null;
                app = (Member(os, "m_appsById") as IDictionary)["character-creator"];
                Require(Member(app, "IsAvailable") is true && Member(app, "State").ToString() == "Closed", "Native creator unavailable or open");
                contentBuilt = Member(app, "ContentBuilt");
                originalChildren = (Member(app, "WindowContent") as VisualElement).Children().ToArray();
                Directory.CreateDirectory(folder);
                var type = Find("Kolkata.UI.KolkataOsCharacterCreatorApp");
                var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(c => c.GetParameters().Length == 4);
                fixture = ctor.Invoke(new object[] { Member(os, "m_assets"), null, null, null });
                if (userDraft == null)
                {
                    userDraft = (string)Member(fixture, "m_draftPath");
                    originalBytes = File.Exists(userDraft) ? File.ReadAllBytes(userDraft) : null;
                }
                // Detached native roots autosave. Keep that original owner's writes isolated too.
                if (original != null) AccessTools.Field(type, "m_draftPath").SetValue(original, Path.Combine(folder, "original-draft.json"));
                AccessTools.Field(type, "m_draftPath").SetValue(fixture, draftPath);
                creatorFixture = fixture; creatorUnsafeAttempts = 0;
                foreach (string action in new[] { "SaveCaller", "StartRecording", "BeginVoicePreviewGeneration", "EditCloudCharacter" })
                    probe.Patch(AccessTools.Method(type, action), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(BlockCreatorFixtureAction)));
                field.SetValue(os, fixture);
                built = true; AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                AccessTools.Field(app.GetType(), "ContentBuilt").SetValue(app, true);
                AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "character-creator" });
                yield return null; yield return null;
                Require(Member(fixture, "m_canvas") != null && (Member(fixture, "m_tabStrip") as VisualElement).childCount == 3,
                    "Native creator editor did not initialize");
                var tabs = Member(fixture, "m_tabStrip") as VisualElement;
                var canvasTexture = Member(Member(fixture, "m_canvas"), "Texture") as Texture2D;
                byte[] blank = canvasTexture.GetRawTextureData();
                var click = ClickAppControl(surface, Member(fixture, "m_canvasSurface") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                byte[] painted = canvasTexture.GetRawTextureData();
                Require(!blank.SequenceEqual(painted) && Member(Member(fixture, "m_canvas"), "CanUndo") is true,
                    "Controller pointer did not change native creator canvas pixels/history");
                Logger.LogInfo("CREATOR DRAW PASS: controller pointer changes native canvas pixels and creates undo history");
                var layerChecks = TestCreatorLayers(fixture, app, surface);
                try { while (layerChecks.MoveNext()) yield return layerChecks.Current; }
                finally { (layerChecks as IDisposable)?.Dispose(); }
                tabs = Member(fixture, "m_tabStrip") as VisualElement;
                click = ClickAppControl(surface, tabs[1]);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                const string name = "fictional vr caller";
                var typing = EnterScamText(surface, Member(fixture, "m_nameField") as TextField, name);
                try { while (typing.MoveNext()) yield return typing.Current; }
                finally { (typing as IDisposable)?.Dispose(); }
                foreach (var entry in new[] { Tuple.Create("m_descriptionField", "fictional careful caller"), Tuple.Create("m_openingField", "hello from a fixture") })
                {
                    typing = EnterScamText(surface, Member(fixture, entry.Item1) as TextField, entry.Item2);
                    try { while (typing.MoveNext()) yield return typing.Current; }
                    finally { (typing as IDisposable)?.Dispose(); }
                }
                (Member(fixture, "m_genderField") as DropdownField).value = "Non-binary";
                var language = Member(fixture, "m_languageField") as DropdownField;
                int languageIndex = Math.Min(1, language.choices.Count - 1); language.index = languageIndex;
                Require((int)Member(fixture, "m_draftTab") == 1, "Controller did not select the personality tab");
                AccessTools.Method(type, "SaveDraft").Invoke(fixture, null);
                Require(File.Exists(draftPath) && File.ReadAllText(draftPath).Contains(name), "Native autosave did not preserve controller-entered text in the isolated draft");
                Logger.LogInfo("CREATOR DRAFT PASS: native personality tab, controller-focused text and isolated native draft autosave work");
                object draft = Member(fixture, "m_draft"), canvas = Member(fixture, "m_canvas");
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app });
                    yield return null; yield return null;
                    Require(ReferenceEquals(draft, Member(fixture, "m_draft")) && ReferenceEquals(canvas, Member(fixture, "m_canvas"))
                        && (Member(fixture, "m_nameField") as TextField).value == name && (int)Member(fixture, "m_draftTab") == 1,
                        "Native creator rebuild discarded the in-progress draft, canvas, text or selected tab");
                    Require((Member(fixture, "m_descriptionField") as TextField).value == "fictional careful caller"
                        && (Member(fixture, "m_openingField") as TextField).value == "hello from a fixture"
                        && (Member(fixture, "m_genderField") as DropdownField).value == "Non-binary"
                        && (Member(fixture, "m_languageField") as DropdownField).index == languageIndex
                        && (Member(Member(fixture, "m_canvas"), "Texture") as Texture2D).GetRawTextureData().SequenceEqual(painted)
                        && Member(Member(fixture, "m_canvas"), "CanUndo") is true,
                        "Creator rebuild lost personality fields, seeded selections, painted pixels or undo history");
                }
                Require(!Directory.Exists(Path.Combine(folder, "Previous Drafts")), "Content rebuild archived the draft as though New Caller was pressed");
                Logger.LogInfo("CREATOR REBUILD PASS: repeated rebuilds preserve the same native draft/canvas, text and selected tab without creating archived drafts");
                click = ClickAppControl(surface, (Member(fixture, "m_tabStrip") as VisualElement)[0]);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                click = ClickManagerControl(surface, Member(fixture, "m_undoButton") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(canvasTexture.GetRawTextureData().SequenceEqual(blank), "Rebuilt creator undo did not restore original pixels");
                click = ClickManagerControl(surface, Member(fixture, "m_redoButton") as VisualElement);
                try { while (click.MoveNext()) yield return click.Current; }
                finally { (click as IDisposable)?.Dispose(); }
                Require(canvasTexture.GetRawTextureData().SequenceEqual(painted), "Rebuilt creator redo did not restore the stroke");
                Logger.LogInfo("CREATOR HISTORY PASS: controller undo/redo still restore exact pixels after content rebuilds");
                var window = TestAppWindow(os, app, "character-creator", surface);
                try { while (window.MoveNext()) yield return window.Current; }
                finally { (window as IDisposable)?.Dispose(); }
                Logger.LogInfo("CREATOR WINDOW PASS: controller maximize/restore, minimize/reopen and close reach the native editor window");
                probe.Patch(AccessTools.Method(type, "BuildEditor"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(ThrowCreatorFixtureEditor)));
                bool sawFailure = false; creatorThrowEditor = true;
                try { AccessTools.Method(type, "BuildContent").Invoke(fixture, new[] { app }); }
                catch (TargetInvocationException ex) when (ex.GetBaseException().Message == "isolated creator editor failure") { sawFailure = true; }
                finally { creatorThrowEditor = false; }
                Require(sawFailure, "Creator failure fixture did not reach the native editor builder");
                AccessTools.Method(type, "BeginNewCaller").Invoke(fixture, null);
                Require(!ReferenceEquals(draft, Member(fixture, "m_draft")) && !ReferenceEquals(canvas, Member(fixture, "m_canvas"))
                    && (Member(fixture, "m_nameField") as TextField).value == "" && Directory.Exists(Path.Combine(folder, "Previous Drafts")),
                    "Explicit native New Caller no longer creates a fresh draft and archives the isolated previous one");
                Logger.LogInfo("CREATOR NEW PASS: explicit native New Caller still replaces the draft and archives only the isolated fixture");
                Logger.LogInfo("CREATOR FAILURE PASS: a failed rebuild releases its preservation scope so the subsequent native New Caller works");
                Require(creatorUnsafeAttempts == 0, "Creator fixture attempted publication, recording, preview or cloud editing");
            }
            finally
            {
                try { if (app != null && built) AccessTools.Method(os.GetType(), "CloseApp").Invoke(os, new[] { app }); }
                finally
                {
                    try
                    {
                        try { if (app != null && built) (Member(app, "WindowContent") as VisualElement).Clear(); }
                        finally { (fixture as IDisposable)?.Dispose(); }
                    }
                    finally
                    {
                        field.SetValue(os, original);
                        if (app != null && built)
                        {
                            var content = Member(app, "WindowContent") as VisualElement;
                            foreach (var child in originalChildren) content.Add(child);
                            AccessTools.Field(app.GetType(), "ContentBuilt").SetValue(app, contentBuilt);
                        }
                        probe.UnpatchSelf(); creatorFixture = null; creatorThrowEditor = false;
                        try { mode.Invoke(os, new object[] { laptop }); }
                        finally
                        {
                            if (original != null)
                            {
                                AccessTools.Field(original.GetType(), "m_draftPath").SetValue(original, userDraft);
                                if (originalDirty is bool) AccessTools.Field(original.GetType(), "m_draftDirty").SetValue(original, originalDirty);
                            }
                            VerifyCreatorUserDraft();
                        }
                    }
                }
            }
            Require(originalBytes == null ? !File.Exists(userDraft) : File.Exists(userDraft) && File.ReadAllBytes(userDraft).SequenceEqual(originalBytes),
                "Creator fixture changed the user's original draft");
            Logger.LogInfo("CREATOR COMPLETE PASS: original app/desktop restored and user draft unchanged; recording/publication/headset acceptance remain unverified");
        }
    }
}
