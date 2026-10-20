using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private IEnumerator TestGripMotion()
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (VRManager.Instance?.Rig?.Right == null || !VRManager.Instance.Ready)
            { Check(Time.realtimeSinceStartup < deadline, "Mock rig did not become ready for grip motion"); yield return null; }
            Transform hand = VRManager.Instance.Rig.Right.transform, frame = hand.parent;
            Check(frame != null && Time.unscaledDeltaTime > 0, "Tracking frame missing for grip motion");
            Vector3 framePosition = frame.position, localPosition = hand.localPosition;
            Quaternion frameRotation = frame.rotation;
            Type type = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Input.VRGripInteractor");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var tick = type.GetMethod("Tick", flags);
            var velocity = type.GetField("velocity", flags);
            object grip = Activator.CreateInstance(type, true);
            void Tick() => tick.Invoke(grip, new object[] { true });
            Vector3 Speed() => (Vector3)velocity.GetValue(grip);
            try
            {
                hand.localPosition = new Vector3(.3f, 1.2f, .4f); Tick();
                frame.position += new Vector3(8, 2, -3); frame.rotation = Quaternion.Euler(0, 45, 0) * frameRotation;
                Tick();
                Check(Speed().magnitude < .001f, "Stationary controller inherited rig translation/snap velocity: " + Speed());
                Vector3 delta = new Vector3(.025f, .01f, -.015f);
                hand.localPosition += delta; Tick();
                Check(Vector3.Distance(Speed(), frame.TransformVector(delta) / Time.unscaledDeltaTime) < .001f,
                    "Physical controller motion did not map into the current tracking frame");
                var reset = type.GetMethod("ResetMotion", flags);
                Check(reset != null, "Recenter motion reset missing");
                reset.Invoke(grip, null); hand.localPosition += new Vector3(.4f, .2f, 0); Tick();
                Check(Speed().magnitude < .001f, "Tracking reset generated a release velocity");
                object activeGrip = VRManager.Instance.Pointer.GetType().GetField("grab", flags).GetValue(VRManager.Instance.Pointer);
                velocity.SetValue(activeGrip, Vector3.one);
                type.GetField("havePrevious", flags).SetValue(activeGrip, true);
                VRManager.Instance.Rig.Recenter();
                Check(((Vector3)velocity.GetValue(activeGrip)).sqrMagnitude == 0
                    && !(bool)type.GetField("havePrevious", flags).GetValue(activeGrip), "Rig recenter did not reset the live pointer's release motion");
            }
            finally
            {
                type.GetMethod("Cancel", flags).Invoke(grip, null);
                frame.SetPositionAndRotation(framePosition, frameRotation); hand.localPosition = localPosition;
                VRManager.Instance.Pointer.GetType().GetMethod("ResetAim", flags).Invoke(VRManager.Instance.Pointer, null);
            }
        }
    }
}
