using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestSelectedInput()
        {
            var root = Own(new GameObject("SWYF selected input fixture"));
            var system = root.AddComponent<EventSystem>(); system.enabled = false;
            var module = root.AddComponent<VRPointerModule>(); module.enabled = false;
            var first = Own(new GameObject("selected input first")).AddComponent<PointerProbe>();
            var second = Own(new GameObject("selected input second")).AddComponent<PointerProbe>();
            var input = VRManager.Instance.Input;
            var focus = input.GetType().GetProperty("HasInputFocus", BindingFlags.Instance | BindingFlags.Public);
            bool original = (bool)focus.GetValue(input);
            try
            {
                focus.SetValue(input, true);
                system.SetSelectedGameObject(first.gameObject); module.Process(); Same(first, "update"); Same(second);
                focus.SetValue(input, false); module.Process(); Same(first); Same(second);
                focus.SetValue(input, true);
                system.SetSelectedGameObject(second.gameObject); module.Process(); Same(first); Same(second, "update");
                system.SetSelectedGameObject(null); module.Process(); Same(first); Same(second);
            }
            finally { focus.SetValue(input, original); system.SetSelectedGameObject(null); }
        }
    }
}
