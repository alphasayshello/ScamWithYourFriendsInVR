using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        private IEnumerator TestCreatorLayers(object owner, object app, VRPanelSurface surface)
        {
            var layers = Member(owner, "m_artworkLayers") as IList;
            Require(layers.Count == 1, "Creator layers fixture requires one initial layer");
            object original = layers[0];
            Texture2D Texture(object layer) => Member(Member(layer, "Canvas"), "Texture") as Texture2D;
            byte[] originalPixels = Texture(original).GetRawTextureData();
            VisualElement Root() => Member(owner, "m_root") as VisualElement;
            VisualElement Row(object layer) => (Member(owner, "m_layerList") as VisualElement).Children().Single(r => ReferenceEquals(r.userData, layer));
            var action = ClickManagerControl(surface, Root().Q<Button>(className: "character-creator__add-layer"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            Require(layers.Count == 2 && ReferenceEquals(layers[0], original), "Controller add-layer did not preserve the original layer");
            object added = layers[1];
            Require(ReferenceEquals(Member(owner, "m_activeArtworkLayer"), added), "New native layer was not selected");
            byte[] addedBlank = Texture(added).GetRawTextureData();
            action = ClickAppControl(surface, Member(owner, "m_canvasSurface") as VisualElement);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            byte[] addedPainted = Texture(added).GetRawTextureData();
            Require(!addedBlank.SequenceEqual(addedPainted) && Texture(original).GetRawTextureData().SequenceEqual(originalPixels),
                "Drawing on the added native layer did not isolate its pixels from the base layer");
            Logger.LogInfo("CREATOR LAYER ADD PASS: controller adds/selects a native layer and paints it without changing base-layer pixels");

            for (int cycle = 0; cycle < 2; cycle++)
            {
                action = ClickManagerControl(surface, Row(added).Q<Button>(className: "character-creator__layer-visibility"));
                try { while (action.MoveNext()) yield return action.Current; }
                finally { (action as IDisposable)?.Dispose(); }
                bool visible = cycle == 1;
                Require(Member(added, "Visible") is bool shown && shown == visible
                    && (Member(added, "Image") as Image).resolvedStyle.display == (visible ? DisplayStyle.Flex : DisplayStyle.None)
                    && Texture(added).GetRawTextureData().SequenceEqual(addedPainted), "Layer visibility toggle changed pixels or failed to update the native image");
            }
            Logger.LogInfo("CREATOR LAYER VISIBILITY PASS: controller hide/show updates the native layer image without destroying its pixels");
            action = ClickManagerControl(surface, Row(original).Q<VisualElement>(className: "character-creator__layer-identity"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            Require(ReferenceEquals(Member(owner, "m_activeArtworkLayer"), original)
                && ReferenceEquals(Member(owner, "m_canvas"), Member(original, "Canvas")), "Controller layer selection did not restore the original canvas/history owner");
            Logger.LogInfo("CREATOR LAYER SELECTION PASS: controller selects the original native layer and its canvas/history owner");

            var reveal = ScrollToScamControl(surface, Member(owner, "m_layerList") as VisualElement);
            try { while (reveal.MoveNext()) yield return reveal.Current; }
            finally { (reveal as IDisposable)?.Dispose(); }
            bool oldAim = appFixtureAim; var oldSurface = appFixtureSurface; var oldPoint = appFixturePoint;
            var manager = VRManager.Instance;
            var tick = AccessTools.Method(typeof(VRPointer), "LateUpdate");
            void Input(string name, bool value) => manager.Input.GetType().GetProperty(name).SetValue(manager.Input, value);
            try
            {
                appFixtureAim = true; appFixtureSurface = surface;
                appFixturePoint = Row(original).Q<VisualElement>(className: "character-creator__layer-identity").worldBound.center;
                holdInjectedTrigger = true; Input("Trigger", true); Input("TriggerDown", true); Input("TriggerUp", false);
                tick.Invoke(manager.Pointer, null); Input("TriggerDown", false);
                Require(ReferenceEquals(Member(owner, "m_draggedArtworkLayer"), original), "Controller did not acquire native layer drag ownership");
                Rect top = Row(added).worldBound;
                appFixturePoint = new Vector2(top.center.x, top.yMin + 5);
                yield return null; yield return new WaitForEndOfFrame(); tick.Invoke(manager.Pointer, null);
                Require(ReferenceEquals(Member(owner, "m_draggedArtworkLayer"), original)
                    && (int)Member(owner, "m_pendingLayerDisplayIndex") == 0,
                    "Scrollable UI stole the native layer drag or failed to update its drop target");
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerUp", true); tick.Invoke(manager.Pointer, null); Input("TriggerUp", false);
                yield return null;
                Require(ReferenceEquals(layers[1], original) && ReferenceEquals(layers[0], added)
                    && Member(owner, "m_draggedArtworkLayer") == null,
                    "Controller drag did not reorder native layers and release capture; originalIndex=" + layers.IndexOf(original)
                    + "; dragging=" + (Member(owner, "m_draggedArtworkLayer") != null));
            }
            finally
            {
                holdInjectedTrigger = false; Input("Trigger", false); Input("TriggerDown", false); Input("TriggerUp", false);
                manager.Pointer.Cancel(); appFixtureAim = oldAim; appFixtureSurface = oldSurface; appFixturePoint = oldPoint;
            }
            Logger.LogInfo("CREATOR LAYER REORDER PASS: controller drag changes native layer order and releases pointer capture");
            bool onion = (bool)Member(owner, "m_onionSkinVisible");
            action = ClickManagerControl(surface, Member(owner, "m_onionSkinButton") as VisualElement);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            AccessTools.Method(owner.GetType(), "BuildContent").Invoke(owner, new[] { app });
            yield return null; yield return null;
            Require(layers.Count == 2 && ReferenceEquals(layers[1], original) && ReferenceEquals(layers[0], added)
                && ReferenceEquals(Member(owner, "m_activeArtworkLayer"), original)
                && Texture(original).GetRawTextureData().SequenceEqual(originalPixels)
                && Texture(added).GetRawTextureData().SequenceEqual(addedPainted)
                && (bool)Member(owner, "m_onionSkinVisible") == !onion
                && (Member(owner, "m_onionSkinButton") as VisualElement).ClassListContains("character-creator__onion-toggle--selected") == !onion,
                "Creator rebuild lost layer order/selection/pixels or onion toggle state");
            Logger.LogInfo("CREATOR LAYER REBUILD PASS: native layer order, selection, both painted canvases and controller onion-toggle state survive content rebuild");
            action = ClickManagerControl(surface, Row(added).Q<Button>(className: "character-creator__layer-delete"));
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            Require(layers.Count == 1 && ReferenceEquals(layers[0], original)
                && ReferenceEquals(Member(owner, "m_activeArtworkLayer"), original)
                && !Row(original).Q<Button>(className: "character-creator__layer-delete").enabledInHierarchy,
                "Controller delete did not remove only the extra layer or protect the final layer");
            action = ClickManagerControl(surface, Member(owner, "m_onionSkinButton") as VisualElement);
            try { while (action.MoveNext()) yield return action.Current; }
            finally { (action as IDisposable)?.Dispose(); }
            Require((bool)Member(owner, "m_onionSkinVisible") == onion && Texture(original).GetRawTextureData().SequenceEqual(originalPixels),
                "Layer fixture did not restore the guide toggle and original canvas pixels");
            Logger.LogInfo("CREATOR LAYER DELETE PASS: controller removes the extra layer, final-layer deletion remains disabled and base pixels are unchanged");
        }
    }
}
