using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestToolkitButton(UIDocument document)
        {
            var button = document.rootVisualElement.Q<Button>("vr-real-button");
            int clicked = 0;
            button.clicked += () => clicked++;
            var pointer = Own(new GameObject("SWYF real button pointer")).AddComponent<VRPointer>();
            pointer.enabled = false;
            typeof(VRPointer).GetField("hoverElement", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pointer, button);
            var send = typeof(VRPointer).GetMethod("Send", BindingFlags.NonPublic | BindingFlags.Instance);
            Vector2 position = button.worldBound.center;
            void Send(EventType type, bool held) => send.Invoke(pointer, new object[] { button, type, position, 0, held });
            Send(EventType.MouseMove, false); Send(EventType.MouseDown, true); Send(EventType.MouseUp, false);
            Check(clicked == 1, "Real button click count=" + clicked + "; bounds=" + button.worldBound);
            Send(EventType.MouseDown, true);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(VRPointer).GetField("pressedElement", flags).SetValue(pointer, button);
            typeof(VRPointer).GetField("toolkitDown", flags).SetValue(pointer, true);
            typeof(VRPointer).GetField("lastPanelPosition", flags).SetValue(pointer, (Vector3)position);
            pointer.Cancel();
            Check(clicked == 1, "Cancelled real button press clicked");
            button.SetEnabled(false);
            Send(EventType.MouseDown, true); Send(EventType.MouseUp, false);
            Check(clicked == 1, "Disabled real button clicked");
        }
    }
}
