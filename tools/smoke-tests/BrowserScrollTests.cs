using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed class BrowserScrollClientProbe
    {
        public readonly List<Vector2> Positions = new List<Vector2>();
        public readonly List<int> Pixels = new List<int>();
        public void SendMouseScroll(Vector2 position, int pixels) { Positions.Add(position); Pixels.Add(pixels); }
    }
    public sealed class BrowserScrollHostProbe
    {
        public VisualElement viewport, browserImage;
        public BrowserScrollClientProbe browserClient = new BrowserScrollClientProbe();
        public int browserPixelWidth = 1920, browserPixelHeight = 1080;
        public bool Ready = true;
        public bool CanUseBrowserClient() => Ready;
    }
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestBrowserScroll(UIDocument document)
        {
            var viewport = new VisualElement { style = { position = Position.Absolute, left = 120, top = 180, width = 400, height = 200 } };
            var image = new Image { style = { position = Position.Absolute, left = 20, top = 10, width = 360, height = 180 } };
            var chrome = new VisualElement();
            document.rootVisualElement.Add(viewport); viewport.Add(image); document.rootVisualElement.Add(chrome);
            var host = new BrowserScrollHostProbe { viewport = viewport, browserImage = image };
            Type type = typeof(VRPointer).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRBrowserScroll");
            object bridge = Activator.CreateInstance(type, true);
            MethodInfo send = type.GetMethod("Send", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo reset = type.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance);
            bool Send(object h, VisualElement target, Vector2 point, float amount) => (bool)send.Invoke(bridge, new[] { h, target, (object)point, amount });
            try
            {
                yield return null; yield return null;
                Vector2 center = viewport.LocalToWorld(new Vector2(200, 100));
                Check(Send(host, image, center, -.5f), "Ready browser did not consume its controller scroll");
                Check(host.browserClient.Pixels.Count == 1 && host.browserClient.Pixels[0] == 16,
                    "Browser did not receive pixel scroll with native direction");
                Check(Vector2.Distance(host.browserClient.Positions[0], new Vector2(960, 540)) < .1f,
                    "Browser scroll ignored image offsets or texture resolution");
                Check(!Send(host, chrome, center, -.5f), "Browser intercepted another app/chrome wheel");
                Check(!Send(host, image, viewport.LocalToWorld(new Vector2(5, 100)), -.5f), "Browser accepted scrolling outside its image");
                host.Ready = false;
                Check(!Send(host, image, center, -.5f), "Unready/blocked browser received controller input");
                host.Ready = true; reset.Invoke(bridge, null); host.browserClient.Pixels.Clear();
                for (int i = 0; i < 4; i++) Check(Send(host, image, center, -.01f), "Fine scroll stopped reaching the browser");
                Check(host.browserClient.Pixels.Count == 1 && host.browserClient.Pixels[0] == 1,
                    "High-refresh fine scrolling lost fractional pixels");
                reset.Invoke(bridge, null); host.browserClient.Pixels.Clear();
                for (int i = 0; i < 9; i++) Send(host, image, center, -.003125f);
                var other = new BrowserScrollHostProbe { viewport = viewport, browserImage = image };
                Send(other, image, center, -.003125f);
                Check(other.browserClient.Pixels.Count == 0, "Browser rebinding carried old scroll into another host");
                host.browserImage = null; reset.Invoke(bridge, null);
                Check(Send(host, viewport, center, .5f) && host.browserClient.Pixels[0] == -16,
                    "Viewport-only browser failed reverse scrolling");
                viewport.RemoveFromHierarchy();
                Check(!Send(host, image, center, -.5f), "Detached browser retained wheel input");
            }
            finally { viewport.RemoveFromHierarchy(); chrome.RemoveFromHierarchy(); }
        }
    }
}
