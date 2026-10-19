using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using ScamWithYourFriendsInVR.UI;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestCornerLifecycle(UIDocument document)
        {
            var surface = Own(new GameObject("SWYF corner lifecycle fixture")).AddComponent<VRPanelSurface>();
            var repair = typeof(VRPanelSurface).GetMethod("RepairTexturedCorners", BindingFlags.Instance | BindingFlags.NonPublic);
            var snapshots = (IDictionary)typeof(VRPanelSurface).GetField("texturedCorners", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(surface);
            var branch = new VisualElement();
            var control = new Button { text = "corner lifecycle" };
            control.style.borderTopLeftRadius = 7; control.style.borderTopRightRadius = 8;
            control.style.borderBottomLeftRadius = 9; control.style.borderBottomRightRadius = 10;
            branch.Add(control);
            bool Original() => control.style.borderTopLeftRadius.value.value == 7 && control.style.borderTopRightRadius.value.value == 8
                && control.style.borderBottomLeftRadius.value.value == 9 && control.style.borderBottomRightRadius.value.value == 10;
            try
            {
                for (int cycle = 0; cycle < 20; cycle++)
                {
                    document.rootVisualElement.Add(branch);
                    yield return null;
                    repair.Invoke(surface, new object[] { branch });
                    Check(snapshots.Count == 1 && control.style.borderTopLeftRadius.value.value == 0,
                        "Rounded control was not repaired on attach cycle " + cycle);
                    branch.RemoveFromHierarchy();
                    Check(snapshots.Count == 0 && Original(), "Detached app tree retained corner state or lost native styles on cycle " + cycle);
                }
                document.rootVisualElement.Add(branch);
                yield return null;
                repair.Invoke(surface, new object[] { branch });
                surface.Restore();
                Check(snapshots.Count == 0 && Original(), "VR shutdown did not restore attached native corners");
                control.style.borderTopLeftRadius = 13;
                branch.RemoveFromHierarchy();
                Check(control.style.borderTopLeftRadius.value.value == 13, "A stale detach callback overwrote native styling after restore");
            }
            finally { branch.RemoveFromHierarchy(); surface.Restore(); }
        }
    }
}
