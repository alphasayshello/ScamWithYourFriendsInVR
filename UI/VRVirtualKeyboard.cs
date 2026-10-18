using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using TMPro;
using ScamWithYourFriendsInVR.Patches;
using UIButton = UnityEngine.UI.Button;
using UIText = UnityEngine.UI.Text;

namespace ScamWithYourFriendsInVR.UI
{
    [DefaultExecutionOrder(30950)]
    public sealed class VRVirtualKeyboard : MonoBehaviour
    {
        public bool Visible => keyboard != null && keyboard.activeSelf;
        public bool MonitorDocked { get; private set; }
        private GameObject keyboard;
        private Font font;
        private Material keyboardMaterial;
        private TextField toolkit;
        private InputField legacy;
        private TMP_InputField tmp;
        private object browserClient;
        private MethodInfo browserSend;
        private object browserHost;
        private bool shift, symbols;
        private UIDocument sourceDocument;
        private static readonly string[] Letters = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm.,@" };
        private static readonly string[] Symbols = { "!@#$%^&*()", "-_=+[]{}\\|", ";:'\",.<>?", "/`~€£¥©®°…" };
        private readonly List<KeyLegend> legends = new List<KeyLegend>();
        private sealed class KeyLegend { internal int Row, Column; internal UIText Caption; }

        internal void TryOpen(VisualElement target, UIDocument document)
        {
            if (!Plugin.VirtualKeyboard.Value) return;
            TextField field = target as TextField ?? target?.GetFirstAncestorOfType<TextField>();
            if (field != null && !field.isReadOnly)
            {
                ClearTargets(); toolkit = field; sourceDocument = document; Show(); return;
            }
            if (document == null) return;
            foreach (MonoBehaviour component in document.GetComponents<MonoBehaviour>())
            {
                if (component.GetType().FullName != "Kolkata.UI.KolkataOsController") continue;
                object host = GameAdapter.Read(component, "m_browserHost");
                if (!(GameAdapter.Read(host, "inputFocused") is bool focused) || !focused) continue;
                object client = GameAdapter.Read(host, "browserClient");
                MethodInfo send = client?.GetType().GetMethod("SendKeyboardControls");
                if (send == null) continue;
                ClearTargets(); browserClient = client; browserSend = send; browserHost = host; sourceDocument = document; Show(); return;
            }
        }

        internal void TryOpen(GameObject target)
        {
            if (!Plugin.VirtualKeyboard.Value || target == null) return;
            InputField field = target.GetComponentInParent<InputField>();
            TMP_InputField text = target.GetComponentInParent<TMP_InputField>();
            if (field != null && !field.readOnly) { ClearTargets(); legacy = field; Show(); }
            else if (text != null && !text.readOnly) { ClearTargets(); tmp = text; Show(); }
        }

        private void ClearTargets() { toolkit = null; legacy = null; tmp = null; browserClient = null; browserSend = null; browserHost = null; sourceDocument = null; }

        public void Close()
        {

            if (keyboard != null) keyboard.SetActive(false);
            MonitorDocked = false;
            ClearTargets();
        }

        private static bool Displayed(VisualElement element)
        {
            if (element?.panel == null || !element.enabledInHierarchy) return false;
            for (VisualElement ancestor = element; ancestor != null; ancestor = ancestor.parent)
                if (!ancestor.visible || ancestor.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        private void Update()
        {
            if (!Visible) return;

            bool valid = toolkit != null && !toolkit.isReadOnly && Displayed(toolkit)
                || legacy != null && legacy.isActiveAndEnabled && !legacy.readOnly
                || tmp != null && tmp.isActiveAndEnabled && !tmp.readOnly;
            if (browserClient != null)
                valid = !(GameAdapter.Read(browserClient, "HasDisposed") is bool disposed && disposed)
                    && GameAdapter.Read(browserHost, "boundApp") is object app
                    && GameAdapter.Read(app, "State")?.ToString() == "OpenVisible";
            if (!valid) Close();
        }

        private void Show()
        {

            if (keyboard == null) Build();
            Transform anchor = VRManager.Instance.UI.Anchor;
            keyboard.transform.SetParent(anchor, false);
            keyboard.transform.localPosition = new Vector3(0, -0.42f, -0.1f);
            keyboard.transform.localRotation = Quaternion.Euler(15, 0, 0);
            keyboard.transform.localScale = Vector3.one * .00125f;
            keyboard.SetActive(true);
            Place();
        }

        private void LateUpdate() { if (Visible) Place(); }

        private void Place()
        {
            VRManager manager = VRManager.Instance;
            if (manager?.UI == null || keyboard == null) return;
            VRPanelSurface surface = sourceDocument == null ? null : manager.UI.Panels.Find(p => p != null
                && p.MonitorAnchored && p.Settings == sourceDocument.panelSettings && p.DocumentFor(sourceDocument.rootVisualElement) == sourceDocument);
            if (surface != null)
            {
                float width = Mathf.Clamp(surface.transform.lossyScale.x, .65f, 1.15f);
                float scale = width / 1000;
                float height = 395 * scale;
                Vector3 bottom = surface.transform.TransformPoint(new Vector3(0, -.5f, 0));
                Vector3 center = bottom - surface.transform.forward * .45f
                    - surface.transform.up * (.035f + height * .5f * Mathf.Cos(75 * Mathf.Deg2Rad));
                keyboard.transform.SetPositionAndRotation(center, surface.transform.rotation * Quaternion.Euler(75, 0, 0));
                Vector3 parent = keyboard.transform.parent != null ? keyboard.transform.parent.lossyScale : Vector3.one;
                keyboard.transform.localScale = new Vector3(scale / Mathf.Max(.0001f, Mathf.Abs(parent.x)),
                    scale / Mathf.Max(.0001f, Mathf.Abs(parent.y)), scale / Mathf.Max(.0001f, Mathf.Abs(parent.z)));
            }
            else
            {
                keyboard.transform.localPosition = new Vector3(0, -.42f, -.1f);
                keyboard.transform.localRotation = Quaternion.Euler(15, 0, 0);
                keyboard.transform.localScale = Vector3.one * .00125f;
            }
            MonitorDocked = surface != null;
            Canvas canvas = keyboard.GetComponent<Canvas>();
            if (canvas != null) canvas.worldCamera = manager.Rig?.Head;
        }

        private void Build()
        {
            keyboard = new GameObject("VR keyboard", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(VRKeyboardMarker));
            Canvas canvas = keyboard.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = VRManager.Instance.Rig.Head;
            canvas.sortingOrder = 1000;
            RectTransform rect = (RectTransform)keyboard.transform;
            rect.sizeDelta = new Vector2(1000, 395);
            rect.localScale = Vector3.one * 0.00125f;
            font = Font.CreateDynamicFontFromOSFont("Arial", 28);
            Shader shader = Shader.Find("UI/Default");
            if (shader != null)
            {
                keyboardMaterial = new Material(shader) { renderQueue = 4001 };
                keyboardMaterial.SetInt("unity_GUIZTestMode", Plugin.OccludeFloatingUI.Value ? 4 : 8);
            }
            for (int row = 0; row < Letters.Length; row++)
            {
                for (int col = 0; col < Letters[row].Length; col++)
                {
                    int keyRow = row, keyColumn = col;
                    UIText caption = AddKey(KeyValue(row, col), col * 96 + 5, -row * 62 - 5, 90, () => Type(KeyValue(keyRow, keyColumn)));
                    legends.Add(new KeyLegend { Row = row, Column = col, Caption = caption });
                }
            }
            AddKey("shift", 5, -253, 100, () => { shift = !shift; RefreshLegends(); });
            AddKey("symbols", 111, -253, 100, () => { symbols = !symbols; RefreshLegends(); });
            AddKey("space", 217, -253, 220, () => Type(" "));
            AddKey("back", 443, -253, 100, Backspace);
            AddKey("enter", 549, -253, 100, Enter);
            AddKey("seated", 655, -253, 145, () => { Plugin.Seated.Value = !Plugin.Seated.Value; VRManager.Instance.Rig.Recenter(); });
            AddKey("close", 806, -253, 145, Close);
            AddKey("tab", 5, -315, 100, () => Navigation('\t', KeyCode.Tab, 9));
            AddKey("left", 111, -315, 100, () => Navigation('\0', KeyCode.LeftArrow, 37));
            AddKey("right", 217, -315, 100, () => Navigation('\0', KeyCode.RightArrow, 39));
            AddKey("up", 323, -315, 100, () => Navigation('\0', KeyCode.UpArrow, 38));
            AddKey("down", 429, -315, 100, () => Navigation('\0', KeyCode.DownArrow, 40));
            AddKey("home", 535, -315, 100, () => Navigation('\0', KeyCode.Home, 36));
            AddKey("end", 641, -315, 100, () => Navigation('\0', KeyCode.End, 35));
            AddKey("delete", 747, -315, 100, () => Navigation('\0', KeyCode.Delete, 46));
            AddKey("esc", 853, -315, 100, () => { Navigation('\0', KeyCode.Escape, 27); Close(); });
        }

        private string KeyValue(int row, int column)
        {
            if (symbols) return Symbols[row][column].ToString();
            if (shift && row == 0) return Symbols[0][column].ToString();
            string value = Letters[row][column].ToString();
            return shift ? value.ToUpperInvariant() : value;
        }

        private void RefreshLegends()
        {
            foreach (KeyLegend key in legends) if (key.Caption != null) key.Caption.text = KeyValue(key.Row, key.Column);
        }

        private UIText AddKey(string label, float x, float y, float width, UnityEngine.Events.UnityAction action)
        {
            GameObject key = new GameObject(label, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UIButton));
            key.transform.SetParent(keyboard.transform, false);
            RectTransform rect = (RectTransform)key.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, 56);
            key.GetComponent<UnityEngine.UI.Image>().color = new Color(0.08f, 0.12f, 0.19f, 0.98f);
            if (keyboardMaterial != null) key.GetComponent<UnityEngine.UI.Image>().material = keyboardMaterial;
            UIButton button = key.GetComponent<UIButton>(); button.onClick.AddListener(action);
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            GameObject caption = new GameObject("Label", typeof(RectTransform), typeof(UIText));
            caption.transform.SetParent(key.transform, false);
            RectTransform textRect = (RectTransform)caption.transform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            UIText text = caption.GetComponent<UIText>();
            text.font = font; text.fontSize = 24; text.alignment = TextAnchor.MiddleCenter;
            text.text = label; text.color = Color.white; text.raycastTarget = false;
            if (keyboardMaterial != null) text.material = keyboardMaterial;
            return text;
        }

        private void Type(string value)
        {
            if (browserClient != null) { Browser(value, 0); return; }
            foreach (char character in value) SendKey(character, KeyCode.None);
        }

        private void SendKey(char character, KeyCode code) => SendKeyModified(character, code, EventModifiers.None);

        private void SendKeyModified(char character, KeyCode code, EventModifiers extraModifiers)
        {
            if (toolkit != null && toolkit.panel != null && !toolkit.isReadOnly && toolkit.enabledInHierarchy)
            {
                TextField field = toolkit;
                // Editing callbacks live on the inner TextElement, not the field container.
                int cursor = Mathf.Clamp(field.cursorIndex, 0, field.text.Length);
                int selection = Mathf.Clamp(field.selectIndex, 0, field.text.Length);
                TextElement editor = field.Q("unity-text-input")?.Q<TextElement>();
                VisualElement target = editor ?? (VisualElement)field;
                target.Focus();
                field.SelectRange(cursor, selection);
                // Reject a printable insertion that cannot fit before entering
                // Unity's editor: its clipping path can corrupt the next caret.
                if (code == KeyCode.None && character >= ' ' && field.maxLength >= 0
                    && field.text.Length - Mathf.Abs(cursor - selection) >= field.maxLength) return;
                bool function = code == KeyCode.Backspace || code == KeyCode.Delete || code == KeyCode.LeftArrow || code == KeyCode.RightArrow
                    || code == KeyCode.UpArrow || code == KeyCode.DownArrow || code == KeyCode.Home || code == KeyCode.End;
                EventModifiers modifiers = (function ? EventModifiers.FunctionKey : EventModifiers.None) | extraModifiers;
                using (KeyDownEvent evt = KeyDownEvent.GetPooled(character, code, modifiers)) { evt.target = target; target.SendEvent(evt); }
                using (KeyUpEvent evt = KeyUpEvent.GetPooled(character, code, modifiers)) { evt.target = target; target.SendEvent(evt); }
                // Unity can leave the editor caret beyond the clipped text after
                // maxLength rejects a character. Clamp before the next layout
                // pass maps that invalid index back into the middle of the text.
                int length = field.text.Length;
                int nextCursor = Mathf.Clamp(field.cursorIndex, 0, length), nextSelection = Mathf.Clamp(field.selectIndex, 0, length);
                if (field.cursorIndex != nextCursor || field.selectIndex != nextSelection) field.SelectRange(nextCursor, nextSelection);
            }
            else if (legacy != null && legacy.isActiveAndEnabled && !legacy.readOnly)
                legacy.ProcessEvent(new Event { type = EventType.KeyDown, character = character, keyCode = code, modifiers = extraModifiers });
            else if (tmp != null && tmp.isActiveAndEnabled && !tmp.readOnly)
                tmp.ProcessEvent(new Event { type = EventType.KeyDown, character = character, keyCode = code, modifiers = extraModifiers });
        }

        private void Navigation(char character, KeyCode code, int browserKey)
        {
            if (browserClient != null) Browser("", browserKey);
            else
            {
                TextField source = toolkit;
                UIDocument previousDocument = sourceDocument;
                SendKey(character, code);
                if (code == KeyCode.Tab && source?.panel != null)
                    source.schedule.Execute(() =>
                    {
                        if (!ReferenceEquals(toolkit, source)) return;
                        VisualElement focused = source.panel?.focusController?.focusedElement as VisualElement;
                        TextField next = focused as TextField ?? focused?.GetFirstAncestorOfType<TextField>();
                        if (next == null || next.isReadOnly || !Displayed(next)) { Close(); return; }
                        ClearTargets(); toolkit = next; sourceDocument = previousDocument; Show();
                    });
            }
        }

        private void Backspace()
        {
            if (browserClient != null) { Browser("", 8); return; }
            SendKey('\b', KeyCode.Backspace);
        }

        private void Enter()
        {
            if (browserClient != null) Browser("", 13);
            else if (toolkit != null && toolkit.panel != null)
            {
                SendKey('\n', KeyCode.Return);
            }
            else if (legacy != null && !legacy.readOnly)
            {
                if (legacy.lineType == InputField.LineType.MultiLineNewline) SendKey('\n', KeyCode.Return);
                else legacy.onEndEdit.Invoke(legacy.text);
            }
            else if (tmp != null && !tmp.readOnly)
            {
                if (tmp.lineType == TMP_InputField.LineType.MultiLineNewline) SendKey('\n', KeyCode.Return);
                else { tmp.onSubmit.Invoke(tmp.text); tmp.onEndEdit.Invoke(tmp.text); }
            }
        }

        private void Browser(string value, int key)
        {
            try
            {
                Type type = browserSend.GetParameters()[0].ParameterType.GetElementType();
                Array keys = Array.CreateInstance(type, key == 0 ? 0 : 1);
                if (key != 0) keys.SetValue(Enum.ToObject(type, key), 0);
                Array empty = Array.CreateInstance(type, 0);
                browserSend.Invoke(browserClient, new object[] { keys, empty, value.ToCharArray() });
                if (key != 0) browserSend.Invoke(browserClient, new object[] { empty, keys, Array.Empty<char>() });
            }
            catch (Exception ex) { GameAdapter.Report("Browser virtual keyboard", ex); Close(); }
        }

        private void BrowserChord(char character, bool control, bool alt)
        {
            try
            {
                Type keyType = browserSend.GetParameters()[0].ParameterType.GetElementType();
                var values = new List<int>(); if (control) values.Add(17); if (alt) values.Add(18); values.Add(char.ToUpperInvariant(character));
                Array down = Array.CreateInstance(keyType, values.Count), up = Array.CreateInstance(keyType, values.Count);
                for (int i = 0; i < values.Count; i++) { down.SetValue(Enum.ToObject(keyType, values[i]), i); up.SetValue(Enum.ToObject(keyType, values[values.Count - 1 - i]), i); }
                Array empty = Array.CreateInstance(keyType, 0);
                browserSend.Invoke(browserClient, new object[] { down, empty, Array.Empty<char>() });
                browserSend.Invoke(browserClient, new object[] { empty, up, Array.Empty<char>() });
            }
            catch (Exception ex) { GameAdapter.Report("Browser desk keyboard shortcut", ex); Close(); }
        }

        private void OnDestroy() { if (keyboard != null) Destroy(keyboard); if (font != null) Destroy(font); if (keyboardMaterial != null) Destroy(keyboardMaterial); }
    }

    public sealed class VRKeyboardMarker : MonoBehaviour { }
}
