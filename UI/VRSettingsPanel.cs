using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWithYourFriendsInVR.UI
{
    public sealed class VRSettingsPanel : MonoBehaviour
    {
        private sealed class Section
        {
            internal VisualElement Root, General;
            internal readonly List<Action> Refresh = new List<Action>();
        }
        private readonly Dictionary<UIDocument, Section> sections = new Dictionary<UIDocument, Section>();
        private readonly List<UIDocument> dead = new List<UIDocument>();
        private float nextRefresh;
        private static readonly Color Cream = new Color(.95f, .83f, .6f), Gold = new Color(.85f, .6f, .25f);

        internal void Track(UIDocument document)
        {
            VisualElement documentRoot = document?.rootVisualElement;
            if (document != null && sections.TryGetValue(document, out Section known)
                && known.General?.panel != null && known.Root.panel == known.General.panel
                && documentRoot != null && documentRoot.Contains(known.General) && known.General.Contains(known.Root)) return;
            VisualElement general = document?.rootVisualElement?.Q("general-screen");
            if (general == null || document.rootVisualElement.Q("general-tab-button") == null) return;
            if (sections.TryGetValue(document, out Section existing))
            {
                if (existing.Root.panel == general.panel && general.Contains(existing.Root)) return;
                existing.Root.RemoveFromHierarchy(); sections.Remove(document);
            }
            var section = new Section { Root = new VisualElement { name = "swyf-vr-options" }, General = general };
            section.Root.style.flexShrink = 0; section.Root.style.paddingLeft = 16; section.Root.style.paddingRight = 16;
            section.Root.style.paddingTop = 14; section.Root.style.paddingBottom = 14; section.Root.style.marginBottom = 20;
            section.Root.style.backgroundColor = new Color(.12f, .085f, .055f, .96f);
            section.Root.style.borderBottomColor = Gold; section.Root.style.borderBottomWidth = 2;
            var title = new Label("VR OPTIONS"); title.style.fontSize = 28; title.style.color = Gold; title.style.marginBottom = 10;
            section.Root.Add(title);
            Add(section, "recenter", () => "RECENTER VIEW", () => VRManager.Instance.Rig.Recenter());
            Add(section, "desktop", () => "COMPUTER SCREEN: " + (Plugin.PhysicalDesktop.Value ? "DESK MONITOR" : "FLOATING"),
                () => Plugin.PhysicalDesktop.Value = !Plugin.PhysicalDesktop.Value);
            Add(section, "seated", () => "SEATED COMFORT: " + OnOff(Plugin.Seated.Value), () =>
            {
                Plugin.Seated.Value = !Plugin.Seated.Value; VRManager.Instance.Rig.Recenter();
            });
            Add(section, "ui-distance", () => "MENU DISTANCE: " + Plugin.PanelDistance.Value.ToString("0.00") + " m",
                () => { Plugin.PanelDistance.Value = Next(Plugin.PanelDistance.Value, .75f, .9f, 1.1f, 1.3f); VRManager.Instance.UI.Place(); });
            Add(section, "aim", () => "AIM SMOOTHING: " + OnOff(Plugin.PointerSmoothing.Value > 0),
                () => { Plugin.PointerSmoothing.Value = Plugin.PointerSmoothing.Value > 0 ? 0 : .025f; VRManager.Instance.Pointer.ResetAim(); });
            Add(section, "laser", () => "LASER IN GAME: " + (Plugin.AlwaysShowPointer.Value ? "ALWAYS" : "USABLE TARGETS"),
                () => Plugin.AlwaysShowPointer.Value = !Plugin.AlwaysShowPointer.Value);
            Add(section, "turn", () => "SNAP TURN: " + Plugin.SnapAngle.Value.ToString("0") + " degrees",
                () => Plugin.SnapAngle.Value = Next(Plugin.SnapAngle.Value, 30, 45, 60));
            Add(section, "auto-sprint", () => "AUTO SPRINT: " + OnOff(Plugin.AutoSprint.Value),
                () => Plugin.AutoSprint.Value = !Plugin.AutoSprint.Value);
            Add(section, "flight-steering", () => "FPV STEERING: " + Plugin.FlightSteeringSpeed.Value.ToString("0") + " degrees / sec",
                () => Plugin.FlightSteeringSpeed.Value = Next(Plugin.FlightSteeringSpeed.Value, 45, 90, 135, 180));
            Add(section, "resolution", () => Plugin.BalancedVR.Value
                    ? "SCENE RESOLUTION: " + Mathf.RoundToInt(Plugin.SceneRenderScale.Value * 100) + "%"
                    : "SCENE RESOLUTION: BALANCED VR DISABLED",
                () => Plugin.SceneRenderScale.Value = Next(Plugin.SceneRenderScale.Value, .7f, .85f, 1), () => Plugin.BalancedVR.Value);
            var help = new Label("Changes save automatically. Use the controller laser and trigger. Text entry uses your PC keyboard.");
            section.Refresh.Add(() => help.text = "Changes save automatically. Use the controller laser and trigger. Text entry uses your PC keyboard. Storage: hold "
                + VRManager.Instance.Input.StorageHint + "; hold an item to store it, or use empty hands to retrieve.");
            help.style.fontSize = 18; help.style.color = Cream; help.style.whiteSpace = WhiteSpace.Normal; help.style.marginTop = 8;
            section.Root.Add(help);
            VisualElement container = general.Q<ScrollView>()?.contentContainer ?? general;
            container.Insert(0, section.Root); sections.Add(document, section);
        }

        private static string OnOff(bool enabled) => enabled ? "ON" : "OFF";
        private static float Next(float value, params float[] choices)
        {
            foreach (float choice in choices) if (choice > value + .001f) return choice;
            return choices[0];
        }
        private static void Add(Section section, string name, Func<string> caption, Action change, Func<bool> enabled = null)
        {
            var button = new Button { name = "swyf-vr-" + name };
            button.style.height = 48; button.style.minHeight = 48; button.style.marginBottom = 6;
            button.style.fontSize = 22; button.style.color = Cream; button.style.backgroundColor = new Color(.22f, .15f, .09f);
            button.style.borderLeftColor = button.style.borderRightColor = button.style.borderTopColor = button.style.borderBottomColor = Gold;
            button.style.borderLeftWidth = button.style.borderRightWidth = button.style.borderTopWidth = button.style.borderBottomWidth = 1;
            button.clicked += () => { change(); foreach (Action refresh in section.Refresh) refresh(); };
            Action update = () => { button.text = caption(); if (enabled != null) button.SetEnabled(enabled()); };
            section.Refresh.Add(update); update(); section.Root.Add(button);
        }
        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .5f; dead.Clear();
            foreach (var section in sections)
                if (section.Key == null) dead.Add(section.Key);
                else if (section.Value.Root.panel != null) foreach (Action refresh in section.Value.Refresh) refresh();
            foreach (UIDocument document in dead) { sections[document].Root.RemoveFromHierarchy(); sections.Remove(document); }
            dead.Clear();
        }
        private void OnDisable()
        {
            foreach (var section in sections) section.Value.Root.RemoveFromHierarchy();
            sections.Clear(); dead.Clear();
        }
    }
}
