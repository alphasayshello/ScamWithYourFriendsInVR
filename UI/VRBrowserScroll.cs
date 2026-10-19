using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.Patches;

namespace ScamWithYourFriendsInVR.UI
{
    internal sealed class VRBrowserScroll
    {
        private object owner;
        private float remainder;

        internal bool TryScroll(VisualElement target, Vector2 position, float amount)
        {
            object os = GameAdapter.Read(GameAdapter.Static("KolkataOsSingleton", "Instance"), "Controller");
            return Send(GameAdapter.Read(os, "m_browserHost"), target, position, amount);
        }

        internal bool Send(object host, VisualElement target, Vector2 position, float amount)
        {
            var viewport = GameAdapter.Read(host, "viewport") as VisualElement;
            if (viewport?.panel == null || target == null || target.panel != viewport.panel
                || target != viewport && !viewport.Contains(target)
                || !(GameAdapter.Call(host, "CanUseBrowserClient") is bool ready && ready))
            { Reset(); return false; }
            object client = GameAdapter.Read(host, "browserClient");
            if (client == null) { Reset(); return false; }
            var image = GameAdapter.Read(host, "browserImage") as VisualElement;
            VisualElement content = image ?? viewport;
            Vector2 local = viewport.WorldToLocal(position);
            if (image != null) local -= image.layout.position;
            float width = content.resolvedStyle.width, height = content.resolvedStyle.height;
            if (!(width > 0) || !(height > 0) || local.x < 0 || local.y < 0 || local.x > width || local.y > height)
            { Reset(); return false; }
            int pixelsWide = GameAdapter.Read(host, "browserPixelWidth") is int w ? w : 0;
            int pixelsHigh = GameAdapter.Read(host, "browserPixelHeight") is int h ? h : 0;
            if (pixelsWide <= 0 || pixelsHigh <= 0) { Reset(); return false; }
            if (!ReferenceEquals(owner, host)) { Reset(); owner = host; }
            // UI Toolkit wheel units are small. Preserve fractional pixels so
            // slow stick movement also works at high headset refresh rates.
            remainder += -amount * 32f;
            int pixels = (int)remainder;
            remainder -= pixels;
            if (pixels != 0)
                GameAdapter.Call(client, "SendMouseScroll", new Vector2(local.x / width * pixelsWide, local.y / height * pixelsHigh), pixels);
            return true;
        }

        internal void Reset() { owner = null; remainder = 0; }
    }
}
