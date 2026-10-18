using System.Reflection;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestKeyboard(UIDocument document)
        {
            var keyboard = Own(new GameObject("SWYF keyboard test")).AddComponent<VRVirtualKeyboard>();
            var keyboardType = typeof(VRVirtualKeyboard);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            bool dispatchingTestKey = false;
            void Dispatch(string name, object[] args)
            {
                dispatchingTestKey = true;
                try { keyboardType.GetMethod(name, flags).Invoke(keyboard, args); }
                finally { dispatchingTestKey = false; }
            }
            void Target(string name, object value) => keyboardType.GetField(name, flags).SetValue(keyboard, value);
            void Type(string value) => Dispatch("Type", new object[] { value });
            void Back() => Dispatch("Backspace", null);
            void Enter() => Dispatch("Enter", null);
            void Navigate(KeyCode code, int browserKey) => Dispatch("Navigation", new object[] { '\0', code, browserKey });
            void Select(TextField target, int cursor, int selection)
            {
                target.SelectRange(cursor, selection);
            }
            string Geometry(TextField target)
            {
                object Read(object value, string name)
                {
                    if (value == null) return null;
                    for (System.Type type = value.GetType(); type != null; type = type.BaseType)
                    {
                        var property = type.GetProperty(name, flags | BindingFlags.Public | BindingFlags.DeclaredOnly);
                        if (property != null) return property.GetValue(value);
                        var member = type.GetField(name, flags | BindingFlags.Public | BindingFlags.DeclaredOnly);
                        if (member != null) return member.GetValue(value);
                    }
                    return null;
                }
                TextElement editor = target.Q("unity-text-input")?.Q<TextElement>();
                object handle = Read(editor, "uitkTextHandle");
                object selecting = Read(Read(editor, "selectingManipulator"), "m_SelectingUtilities");
                return "glyphs=" + Read(handle, "characterCount") + "; raw=" + Read(selecting, "m_CursorIndex")
                    + "; innerText=" + editor?.text
                    + "; utilityText=" + Read(Read(Read(editor, "editingManipulator"), "editingUtilities"), "text");
            }

            var field = document.rootVisualElement.Q<TextField>("keyboard-test-field");
            document.rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (dispatchingTestKey) return;
                Logger.LogInfo("SMOKE INFO: isolated non-test keyboard event; character=" + (int)evt.character + "; keyCode=" + evt.keyCode);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<KeyDownEvent>(evt => Logger.LogInfo("SMOKE keyboard event: character=" + (int)evt.character
                + "; keyCode=" + evt.keyCode + "; before=" + field.text), TrickleDown.TrickleDown);
            Target("toolkit", field);
            field.style.width = 220; field.style.height = 30;
            field.Focus();
            float layoutDeadline = Time.realtimeSinceStartup + 3;
            do
            {
                yield return null;
                Select(field, 1, 4);
            } while ((field.cursorIndex != 1 || field.selectIndex != 4) && Time.realtimeSinceStartup < layoutDeadline);
            Check(field.cursorIndex == 1 && field.selectIndex == 4, "Toolkit test field could not establish selection after layout: " + field.cursorIndex + "/" + field.selectIndex);
            string unchanged = field.value;
            using (var outside = KeyDownEvent.GetPooled('s', KeyCode.S, EventModifiers.None))
            {
                var editor = field.Q("unity-text-input")?.Q<TextElement>();
                outside.target = editor; editor.SendEvent(outside);
            }
            Check(field.value == unchanged, "Keyboard fixture accepted a non-test key event");
            Type("X"); Check(field.value == "aXe", "Toolkit selection: " + field.value);
            yield return null;
            Select(field, 2, 2);
            Check(field.panel != null && field.cursorIndex == 2 && field.selectIndex == 2, "Toolkit lost its panel or selection before backspace");
            Back(); Check(field.value == "ae", "Toolkit backspace: " + field.value);
            Logger.LogInfo("SMOKE INFO: keyboard toolkit selection and backspace passed");
            field.maxLength = 3; field.value = ""; Select(field, 0, 0);
            yield return null;
            foreach (char character in "12345")
            {
                string before = field.text + ":" + field.cursorIndex + "/" + field.selectIndex;
                Type(character.ToString());
                Logger.LogInfo("SMOKE keyboard key=" + character + "; before=" + before + "; after=" + field.text + ":" + field.cursorIndex + "/" + field.selectIndex + "; " + Geometry(field));
                yield return null;
            }
            Check(field.value == "123", "Toolkit character limit: " + field.value);
            Check(field.cursorIndex == 3 && field.selectIndex == 3, "Toolkit limit left an invalid caret");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                field.value = ""; Select(field, 0, 0);
                yield return null;
                foreach (char character in "12345")
                {
                    string before = field.text;
                    Type(character.ToString());
                    Logger.LogInfo("SMOKE keyboard repeat=" + repeat + "; key=" + character + "; before=" + before + "; text=" + field.text + "; caret=" + field.cursorIndex + "/" + field.selectIndex);
                    yield return null;
                }
                Check(field.value == "123" && field.cursorIndex == 3 && field.selectIndex == 3, "Toolkit repeated character limit/caret: " + field.value);
            }
            Logger.LogInfo("SMOKE INFO: keyboard toolkit limit passed");
            Select(field, 1, 2); Type("X");
            Check(field.value == "1X3", "Toolkit selection replacement at character limit: " + field.value);
            field.value = "123"; Select(field, 3, 3);
            field.isReadOnly = true; Type("9"); Back(); Check(field.value == "123", "Toolkit read-only was changed");
            field.isReadOnly = false; field.maxLength = -1; field.value = "abcd";
            yield return null;
            Select(field, 2, 2);
            Navigate(KeyCode.Delete, 46); Check(field.value == "abd", "Toolkit forward delete: " + field.value);
            Navigate(KeyCode.LeftArrow, 37); Type("X"); Check(field.value == "aXbd", "Toolkit left arrow: " + field.value);
            yield return null;
            Navigate(KeyCode.Home, 36); Type("H");
            yield return null;
            Navigate(KeyCode.End, 35); Type("E");
            Check(field.value == "HaXbdE", "Toolkit home/end: " + field.value);

            const string address = "https://example.invalid/a?b=1&x=@";
            field.value = ""; Select(field, 0, 0);
            yield return null;
            foreach (char character in address) { Type(character.ToString()); Logger.LogInfo("SMOKE URL key=" + character + "; text=" + field.text + "; " + Geometry(field)); yield return null; }
            Check(field.value == address, "Toolkit URL punctuation/scrolling text: " + field.value);

            var delayed = document.rootVisualElement.Q<TextField>("keyboard-test-delayed");
            Navigate(KeyCode.Tab, 9);
            yield return null;
            yield return null;
            Check(ReferenceEquals(keyboardType.GetField("toolkit", flags).GetValue(keyboard), delayed), "Toolkit Tab did not follow focus to the next field");
            Target("toolkit", delayed);
            delayed.Focus(); Select(delayed, 0, 3);
            foreach (char character in "new") { Type(character.ToString()); yield return null; }
            yield return null;
            Check(delayed.value == "old" && delayed.text == "new", "Delayed edit committed early: " + delayed.value + "/" + delayed.text);
            Enter(); Check(delayed.value == "new", "Delayed enter did not commit: " + delayed.value);
            Target("toolkit", field);
            field.value = ""; field.Focus();
            yield return null;
            Select(field, 0, 0);
            EventCallback<ChangeEvent<string>> closeOnChange = _ => keyboard.Close();
            field.RegisterValueChangedCallback(closeOnChange);
            try { Type("X"); Check(field.value == "X", "Field callback interrupted keyboard editing"); }
            finally { field.UnregisterValueChangedCallback(closeOnChange); }
            Check(keyboardType.GetField("toolkit", flags).GetValue(keyboard) == null, "Native change callback did not release keyboard target");
            Target("toolkit", null);

            var fieldObject = Own(new GameObject("SWYF legacy input test", typeof(RectTransform), typeof(InputField)));
            var labelObject = Own(new GameObject("test input label", typeof(RectTransform), typeof(Text)));
            labelObject.transform.SetParent(fieldObject.transform, false);
            var label = labelObject.GetComponent<Text>();
            label.font = Own(Font.CreateDynamicFontFromOSFont("Arial", 24));
            var legacy = fieldObject.GetComponent<InputField>();
            legacy.textComponent = label; legacy.text = "abcde";
            Target("legacy", legacy);
            legacy.selectionAnchorPosition = 1; legacy.selectionFocusPosition = 4;
            Type("X"); Check(legacy.text == "aXe", "Legacy selection: " + legacy.text);
            Back(); Check(legacy.text == "ae", "Legacy backspace: " + legacy.text);
            legacy.contentType = InputField.ContentType.IntegerNumber; legacy.text = "12"; legacy.caretPosition = 2;
            Type("a3"); Check(legacy.text == "123", "Legacy integer validation: " + legacy.text);
            legacy.characterLimit = 3; Type("4"); Check(legacy.text == "123", "Legacy character limit: " + legacy.text);
            legacy.readOnly = true; Type("5"); Back(); Check(legacy.text == "123", "Legacy read-only was changed");
            legacy.readOnly = false; legacy.characterLimit = 0; legacy.contentType = InputField.ContentType.Standard;
            legacy.onValidateInput = (text, position, character) => char.ToUpperInvariant(character);
            legacy.text = ""; legacy.caretPosition = 0; Type("ab");
            Check(legacy.text == "AB", "Legacy custom validator was bypassed: " + legacy.text);
            var keyboardSurface = Own(new GameObject("temporary keyboard visibility probe"));
            Target("keyboard", keyboardSurface);
            legacy.gameObject.SetActive(false);
            keyboardType.GetMethod("Update", flags).Invoke(keyboard, null);
            Check(!keyboard.Visible, "Keyboard remained visible after its native input was disabled");
            Target("toolkit", field); keyboardSurface.SetActive(true);
            field.style.display = DisplayStyle.None;
            yield return null;
            keyboardType.GetMethod("Update", flags).Invoke(keyboard, null);
            Check(!keyboard.Visible, "Keyboard remained visible after its document field was hidden");
            keyboard.Close();
            var tmpCanvasObject = Own(new GameObject("SWYF TMP test canvas", typeof(RectTransform), typeof(Canvas)));
            tmpCanvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var tmpObject = Own(new GameObject("SWYF TMP input test", typeof(RectTransform), typeof(TMPro.TMP_InputField)));
            tmpObject.transform.SetParent(tmpCanvasObject.transform, false);
            var tmpLabelObject = Own(new GameObject("TMP test text", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)));
            tmpLabelObject.transform.SetParent(tmpObject.transform, false);
            var tmp = tmpObject.GetComponent<TMPro.TMP_InputField>();
            tmp.textComponent = tmpLabelObject.GetComponent<TMPro.TextMeshProUGUI>();
            tmp.textComponent.font = TMPro.TMP_Settings.defaultFontAsset;
            Check(tmp.textComponent.font != null, "No native TMP font available for the input test");
            tmp.textViewport = (RectTransform)tmpObject.transform;
            ((RectTransform)tmpObject.transform).sizeDelta = new Vector2(300, 60);
            ((RectTransform)tmpLabelObject.transform).sizeDelta = new Vector2(300, 60);
            tmp.text = "abcde";
            tmp.ForceLabelUpdate();
            tmp.textComponent.ForceMeshUpdate(true);
            yield return null;
            Check(tmp.textComponent.textInfo.characterCount >= 5, "TMP test text did not generate character layout");
            tmp.selectionAnchorPosition = 1; tmp.selectionFocusPosition = 4;
            tmp.selectionStringAnchorPosition = 1; tmp.selectionStringFocusPosition = 4;
            Target("tmp", tmp);
            Type("X"); Check(tmp.text == "aXe", "TMP selection: " + tmp.text);
            Back(); Check(tmp.text == "ae", "TMP backspace: " + tmp.text);
            tmp.readOnly = true; Type("9"); Back(); Check(tmp.text == "ae", "TMP read-only changed");
            tmp.readOnly = false; tmp.contentType = TMPro.TMP_InputField.ContentType.IntegerNumber;
            tmp.text = "12"; tmp.stringPosition = 2; Type("a3");
            Check(tmp.text == "123", "TMP integer validation: " + tmp.text);
            int submitted = 0;
            tmp.onSubmit.AddListener(_ => submitted++); Enter();
            Check(submitted == 1, "TMP submit callback missing");
            keyboard.Close();

            var browser = new BrowserKeyboardProbe();
            Target("browserClient", browser);
            Target("browserSend", typeof(BrowserKeyboardProbe).GetMethod("SendKeyboardControls"));
            Type("https://example.invalid/a?b=1&x=@");
            Navigate(KeyCode.LeftArrow, 37); Back(); Enter();
            Check(browser.Text == "https://example.invalid/a?b=1&x=@" && string.Join(",", browser.Down) == "37,8,13"
                && string.Join(",", browser.Up) == "37,8,13", "Browser keyboard text/key release dispatch failed");
            browser.Fail = true; Type("x");
            Check(keyboardType.GetField("browserClient", flags).GetValue(keyboard) == null && !keyboard.Visible, "Disposed browser target retained keyboard input");
        }
    }

    public enum BrowserTestKey { Backspace = 8, Enter = 13, Left = 37 }
    public sealed class BrowserKeyboardProbe
    {
        public readonly System.Collections.Generic.List<int> Down = new System.Collections.Generic.List<int>();
        public readonly System.Collections.Generic.List<int> Up = new System.Collections.Generic.List<int>();
        public string Text = "";
        public bool Fail;
        public void SendKeyboardControls(BrowserTestKey[] down, BrowserTestKey[] up, char[] characters)
        {
            if (Fail) throw new System.ObjectDisposedException("simulated browser client");
            foreach (var key in down) Down.Add((int)key);
            foreach (var key in up) Up.Add((int)key);
            Text += new string(characters);
        }
    }
}
