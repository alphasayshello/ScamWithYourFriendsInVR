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
            Check((bool)near.Invoke(null, new object[] { root.transform, center, center + Vector3.up * .6f }),
                "Eye adjustment allowance did not respect avatar scale");
            root.transform.localScale = Vector3.one;
            var standing = type.GetMethod("ValidStandingOffset", BindingFlags.Static | BindingFlags.NonPublic);
            Check((bool)standing.Invoke(null, new object[] { root.transform, new Vector3(.05f, 1.85f, .1f) })
                && !(bool)standing.Invoke(null, new object[] { root.transform, new Vector3(0, 1.85f, -.6f) })
                && !(bool)standing.Invoke(null, new object[] { root.transform, Vector3.zero }),
                "Standing calibration accepted a displaced/transient native eye or rejected a normal body offset");
            Transform ChestHead(string name, float x, out Transform chest)
            {
                chest = Own(new GameObject(name)).transform; chest.SetParent(root.transform, false);
                chest.localPosition = new Vector3(x, 1, 0);
                var head = Own(new GameObject("mixamorig:Head")).transform; head.SetParent(chest, false);
                head.localPosition = Vector3.up * .3f; return head;
            }
            ChestHead("other active role", 10, out _);
            Transform correct = ChestHead("current ragdoll role", 0, out var selectedChest);
            anchor = Activator.CreateInstance(type, true);
            var headPick = type.GetMethod("TryHeadPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] headArgs = { root.transform, selectedChest, Vector3.zero };
            Check((bool)headPick.Invoke(anchor, headArgs) && Vector3.Distance((Vector3)headArgs[2], correct.position + Vector3.up * .12f) < .001f,
                "Ragdoll head fallback selected another active role's skeleton");
        }
    }
}
