using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestCreatorResume(object os, object owner, object app, VRPanelSurface surface)
        {
            AccessTools.Method(os.GetType(), "OpenAppById").Invoke(os, new object[] { "character-creator" });
            yield return null; yield return null;
            VisualElement Root() => Member(owner, "m_root") as VisualElement;
            var type = owner.GetType();
            var action = ClickAppControl(surface, (Member(owner, "m_tabStrip") as VisualElement)[1]);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            const string name = "saved resume fixture";
            var typing = EnterScamText(surface, Member(owner, "m_nameField") as TextField, name);
            try { while (typing.MoveNext()) yield return typing.Current; }
            finally { (typing as IDisposable)?.Dispose(); }
            (Member(owner, "m_descriptionField") as TextField).value = "saved personality fixture";
            (Member(owner, "m_openingField") as TextField).value = "saved opening fixture";
            (Member(owner, "m_genderField") as DropdownField).value = "Non-binary";
            var language = Member(owner, "m_languageField") as DropdownField;
            int languageIndex = Math.Min(1, language.choices.Count - 1); language.index = languageIndex;
            action = ClickAppControl(surface, (Member(owner, "m_tabStrip") as VisualElement)[0]);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            action = ClickAppControl(surface, Member(owner, "m_canvasSurface") as VisualElement);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            action = ClickManagerControl(surface, Root().Q<Button>(className: "character-creator__add-layer"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            action = ClickManagerControl(surface, ((Member(owner, "m_colorSwatchButtons") as IList)[1]) as VisualElement);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            action = ClickAppControl(surface, Member(owner, "m_canvasSurface") as VisualElement);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            var layers = Member(owner, "m_artworkLayers") as IList;
            Require(layers.Count == 2, "Resume fixture did not create two native layers");
            VisualElement Row(object layer) => (Member(owner, "m_layerList") as VisualElement).Children().Single(r => ReferenceEquals(r.userData, layer));
            action = ClickManagerControl(surface, Row(layers[1]).Q<Button>(className: "character-creator__layer-visibility"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            action = ClickManagerControl(surface, Row(layers[0]).Q<VisualElement>(className: "character-creator__layer-identity"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            action = ClickAppControl(surface, (Member(owner, "m_tabStrip") as VisualElement)[1]);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            var oldLayers = layers.Cast<object>().ToArray();
            var names = oldLayers.Select(l => (string)Member(l, "Name")).ToArray();
            // PNG import may choose ARGB32 instead of the original RGBA32.
            // Compare logical colours rather than native byte channel order.
            var pixels = oldLayers.Select(l => (Member(Member(l, "Canvas"), "Texture") as Texture2D).GetPixels32()).ToArray();
            Require(!pixels[0].SequenceEqual(pixels[1]), "Resume fixture layers must have distinguishable painted pixels");
            string path = (string)Member(owner, "m_draftPath");
            AccessTools.Method(type, "SaveDraft").Invoke(owner, null);
            byte[] saved = File.ReadAllBytes(path);
            string archive = Path.Combine(Path.GetDirectoryName(path), "Previous Drafts");
            int archives = Directory.Exists(archive) ? Directory.GetFiles(archive).Length : 0;
            action = ClickAppControl(surface, Root().Q<VisualElement>(className: "character-creator__editor-header").Q<Button>());
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            Require(Member(owner, "m_draft") == null && Root().Q<Button>("resume-draft") != null
                && File.ReadAllBytes(path).SequenceEqual(saved), "Native Back did not retain the saved draft and expose Resume");
            Logger.LogInfo("CREATOR LANDING PASS: controller Back opens the native landing page without changing the isolated saved draft");
            for (int cycle = 0; cycle < 2; cycle++)
            {
                AccessTools.Method(type, "BuildContent").Invoke(owner, new[] { app });
                yield return null; yield return null;
                Require(Member(owner, "m_draft") == null && Root().Q<Button>("resume-draft") != null
                    && File.ReadAllBytes(path).SequenceEqual(saved)
                    && (Directory.Exists(archive) ? Directory.GetFiles(archive).Length : 0) == archives,
                    "Landing-page rebuild started a new caller or changed/archived the saved draft");
            }
            Logger.LogInfo("CREATOR LANDING REBUILD PASS: two rebuilds retain the landing page, resume control and unchanged draft bytes without new archives");
            action = ClickAppControl(surface, Root().Q<Button>("resume-draft"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            layers = Member(owner, "m_artworkLayers") as IList;
            Require(layers.Count == 2 && (Member(owner, "m_nameField") as TextField).value == name
                && (int)Member(owner, "m_draftTab") == 1 && layers.IndexOf(Member(owner, "m_activeArtworkLayer")) == 0,
                "Controller Resume did not restore draft identity, tab, layer count or selection");
            Require((Member(owner, "m_descriptionField") as TextField).value == "saved personality fixture"
                && (Member(owner, "m_openingField") as TextField).value == "saved opening fixture"
                && (Member(owner, "m_genderField") as DropdownField).value == "Non-binary"
                && (Member(owner, "m_languageField") as DropdownField).index == languageIndex,
                "Native Resume lost seeded personality fields or selections");
            for (int i = 0; i < 2; i++)
            {
                var restoredTexture = Member(Member(layers[i], "Canvas"), "Texture") as Texture2D;
                Color32[] restoredPixels = restoredTexture.GetPixels32();
                Require(!ReferenceEquals(layers[i], oldLayers[i]) && (string)Member(layers[i], "Name") == names[i]
                    && restoredPixels.SequenceEqual(pixels[i])
                    && (bool)Member(layers[i], "Visible") == (i == 0), "Native file restore mismatch; layer=" + i
                    + "; sameName=" + ((string)Member(layers[i], "Name") == names[i]) + "; newObject=" + !ReferenceEquals(layers[i], oldLayers[i])
                    + "; visible=" + Member(layers[i], "Visible") + "; pixelsEqual=" + restoredPixels.SequenceEqual(pixels[i])
                    + "; format=" + restoredTexture.format + "; pixels=" + restoredPixels.Length + "; expectedPixels=" + pixels[i].Length);
            }
            Logger.LogInfo("CREATOR RESUME PASS: controller loads native saved JSON/PNG into new layer objects with exact painted pixels, visibility, selection, name and tab");
            action = ClickAppControl(surface, Root().Q<VisualElement>(className: "character-creator__editor-header").Q<Button>());
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            action = ClickAppControl(surface, Root().Q<Button>(className: "character-creator__start-card--primary"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            Require(Member(owner, "m_draft") != null && (Member(owner, "m_nameField") as TextField).value == ""
                && (Member(owner, "m_artworkLayers") as IList).Count == 1
                && Directory.GetFiles(archive).Length == archives + 1,
                "Controller New Caller from the refreshed landing page did not create and archive native drafts normally");
            Logger.LogInfo("CREATOR LANDING NEW PASS: controller New Caller still starts fresh and archives the owned previous draft after landing rebuilds");
            action = ClickAppControl(surface, Root().Q<VisualElement>(className: "character-creator__editor-header").Q<Button>());
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            byte[] malformed = System.Text.Encoding.UTF8.GetBytes("{ invalid owned draft fixture");
            File.WriteAllBytes(path, malformed);
            string oldStatus = (Member(owner, "m_status") as Label).text;
            action = ClickAppControl(surface, Root().Q<Button>("resume-draft"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            Require(Member(owner, "m_draft") == null && Root().Q<Button>("resume-draft") != null
                && (Member(owner, "m_status") as Label).text != oldStatus && File.ReadAllBytes(path).SequenceEqual(malformed),
                "Malformed native draft did not retain the landing page/file and show a failure status");
            Logger.LogInfo("CREATOR RESUME ERROR PASS: native malformed-file response keeps the owned file and landing page, changes status and creates no caller");
        }
    }
}
