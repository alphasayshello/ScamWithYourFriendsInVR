using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestDocumentDiscovery(UIDocument template)
        {
            var manager = VRManager.Instance;
            Check(manager?.Ready == true && manager.UI != null, "Document discovery needs the running VR UI");
            // Create after the initial registry seed; this must use lifecycle
            // discovery rather than being present in the startup snapshot.
            yield return new WaitForSecondsRealtime(.6f);
            var settings = Own(ScriptableObject.CreateInstance<PanelSettings>());
            settings.themeStyleSheet = template.panelSettings.themeStyleSheet;
            var root = Own(new GameObject("SWYF late document lifecycle fixture"));
            root.SetActive(false);
            var document = root.AddComponent<UIDocument>(); document.panelSettings = settings;
            VRPanelSurface surface = null;
            void BuildButton()
            {
                var tree = document.rootVisualElement;
                tree.Clear(); tree.style.width = 400; tree.style.height = 200;
                tree.style.unityFont = Own(Font.CreateDynamicFontFromOSFont("Arial", 24));
                var button = new Button { name = "vr-real-button", text = "late document" };
                button.style.width = 160; button.style.height = 45; tree.Add(button);
            }
            try
            {
                root.SetActive(true); BuildButton();
                float deadline = Time.realtimeSinceStartup + 3;
                while ((surface = manager.UI.Panels.FirstOrDefault(p => p != null && p.Settings == settings && p.Document == document)) == null
                    || surface.Panel == null || document.rootVisualElement.Q<Button>()?.worldBound.width < 1)
                { Check(Time.realtimeSinceStartup < deadline, "Late enabled document was not automatically discovered"); yield return null; }
                yield return new WaitForEndOfFrame(); TestToolkitButton(document);
                root.SetActive(false);
                deadline = Time.realtimeSinceStartup + 3;
                while (surface.Panel != null)
                { Check(Time.realtimeSinceStartup < deadline, "Disabled document kept an input panel"); yield return null; }
                root.SetActive(true); BuildButton();
                deadline = Time.realtimeSinceStartup + 3;
                while (surface.Document != document || surface.Panel == null || document.rootVisualElement.Q<Button>()?.worldBound.width < 1)
                { Check(Time.realtimeSinceStartup < deadline, "Re-enabled document did not recover its existing surface"); yield return null; }
                yield return new WaitForEndOfFrame(); TestToolkitButton(document);
                Check(manager.UI.Panels.Count(p => p != null && p.Settings == settings) == 1, "Document re-enable duplicated its VR surface");
            }
            finally
            {
                if (surface != null)
                {
                    typeof(VRPanelSurface).GetMethod("Remove", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(surface, null);
                    manager.UI.Panels.Remove(surface);
                }
                if (root != null) Destroy(root);
            }
        }
    }
}
