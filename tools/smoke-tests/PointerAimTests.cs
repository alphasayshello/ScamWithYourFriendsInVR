using System;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestPointerAim()
        {
            Type type = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.UI.VRPointerAim");
            object filter = Activator.CreateInstance(type, true);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var sample = type.GetMethod("Sample", flags);
            Quaternion Sample(Quaternion raw, float seconds = .025f) => (Quaternion)sample.Invoke(filter, new object[] { raw, seconds, 1f / 90 });
            Check(Quaternion.Angle(Sample(Quaternion.identity), Quaternion.identity) < .01f, "Initial aim was delayed");
            float rawEnergy = 0, filteredEnergy = 0;
            for (int i = 0; i < 60; i++)
            {
                Quaternion raw = Quaternion.Euler(0, i % 2 == 0 ? .5f : -.5f, 0);
                float angle = Quaternion.Angle(Sample(raw), Quaternion.identity);
                rawEnergy += .25f; filteredEnergy += angle * angle;
            }
            Check(filteredEnergy < rawEnergy * .6f, "Small aiming tremor was not reduced");
            Quaternion turn = Quaternion.Euler(0, 70, 0);
            Check(Quaternion.Angle(Sample(turn), turn) < .01f, "Large intentional aim turn lagged behind");
            Quaternion direct = Quaternion.Euler(8, 72, 2);
            Check(Quaternion.Angle(Sample(direct, 0), direct) < .01f, "Disabled smoothing changed aim");
            type.GetMethod("Reset", flags).Invoke(filter, null);
            Quaternion reset = Quaternion.Euler(0, 73, 0);
            Check(Quaternion.Angle(Sample(reset), reset) < .01f, "Recenter retained stale filtered aim");
            var visible = type.GetMethod("Visible", flags);
            Check(!(bool)visible.Invoke(null, new object[] { false, false, false, false, false }), "Idle walking keeps the laser visible");
            for (int i = 0; i < 5; i++)
            {
                object[] inputs = { false, false, false, false, false }; inputs[i] = true;
                Check((bool)visible.Invoke(null, inputs), "Pointer hidden for an active context or target");
            }
            var decoration = Own(new GameObject("non-interactive HUD decoration"));
            var buttonObject = Own(new GameObject("interactive UI target", typeof(UnityEngine.UI.Button)));
            var canUse = typeof(UI.VRPointer).GetMethod("CanUseGraphic", flags);
            Check(!(bool)canUse.Invoke(null, new object[] { decoration }) && (bool)canUse.Invoke(null, new object[] { buttonObject }),
                "Decorative HUD content consumes pointer input, or an actual button was ignored");
        }
    }
}
