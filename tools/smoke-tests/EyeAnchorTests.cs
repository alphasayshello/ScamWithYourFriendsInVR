using System;
using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestEyeAnchor()
        {
            var root = Own(new GameObject("SWYF eye anchor fixture"));
            root.transform.position = new Vector3(900, 50, 900);
            Renderer Eye(string name, float x)
            {
                var eye = Own(GameObject.CreatePrimitive(PrimitiveType.Sphere));
                eye.name = name; eye.transform.SetParent(root.transform, false);
                eye.transform.localPosition = new Vector3(x, 1.8f, .1f); eye.transform.localScale = Vector3.one * .06f;
                return eye.GetComponent<Renderer>();
            }
            var left = Eye("Eye_L", -.04f); var right = Eye("Eye_R", .04f);
            Type type = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Camera.VRAvatarEyeAnchor");
            object anchor = Activator.CreateInstance(type, true);
            var pick = type.GetMethod("TryPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            bool Try(out Vector3 position)
            {
                object[] args = { root.transform, Vector3.zero };
                bool result = (bool)pick.Invoke(anchor, args); position = (Vector3)args[1]; return result;
            }
            Check(Try(out var center) && Vector3.Distance(center, root.transform.TransformPoint(new Vector3(0, 1.8f, .1f))) < .001f,
                "Valid eye meshes did not anchor at their average position");
            left.bounds = new Bounds(root.transform.position + Vector3.right * 100, Vector3.one * .06f);
            Check(Try(out center) && Vector3.Distance(center, right.bounds.center) < .001f, "Stale eye bounds poisoned the valid eye anchor");
            right.bounds = new Bounds(root.transform.position - Vector3.right * 100, Vector3.one * .06f);
            Check(!Try(out _), "Two stale eye bounds produced a tracked-origin anchor");
            left.ResetBounds(); right.ResetBounds();
            Check(Try(out _), "Reset bounds did not restore normal eye anchoring");
            var near = type.GetMethod("NearAuthoredEye", BindingFlags.Static | BindingFlags.NonPublic);
            Check((bool)near.Invoke(null, new object[] { root.transform, center, center + Vector3.up * .2f }),
                "A normal mesh-to-authored eye adjustment was rejected");
            Check(!(bool)near.Invoke(null, new object[] { root.transform, center, center + Vector3.forward * 2 }),
                "A stale eye near the body but far from its authored camera was accepted");
            root.transform.localScale = Vector3.one * 2;
            Check((bool)near.Invoke(null, new object[] { root.transform, center, center + Vector3.up * 1.5f }),
                "Eye adjustment allowance did not respect avatar scale");
        }
    }
}
