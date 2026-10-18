using System.Reflection;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private void TestRoomScale()
        {
            var type = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Camera.VRRoomScaleBody");
            var follow = type.GetMethod("Follow", BindingFlags.Static | BindingFlags.NonPublic);
            var consume = type.GetMethod("Consume", BindingFlags.Static | BindingFlags.NonPublic);
            var root = Own(new GameObject("SWYF room-scale collision probe"));
            root.transform.position = new Vector3(1000, 50, 1000);
            var body = root.AddComponent<CharacterController>();
            body.radius = .2f; body.height = 1.6f; body.center = Vector3.up * .8f; body.skinWidth = .01f;
            Vector3 start = root.transform.position;
            Quaternion rotation = Quaternion.Euler(0, 90, 0);
            Vector3 raw = new Vector3(0, 1.65f, .6f), center = Vector3.up * 1.65f;
            Vector3 expectedHead = start + rotation * (raw - center);
            Physics.SyncTransforms();
            for (int i = 0; i < 8; i++)
            {
                Vector3 moved = (Vector3)follow.Invoke(null, new object[] { body, rotation, raw - center });
                Check(moved.magnitude <= .251f, "Room movement exceeded its per-frame correction limit");
                center = (Vector3)consume.Invoke(null, new object[] { center, rotation, moved });
                Check(Vector3.Distance(root.transform.position + rotation * (raw - center), expectedHead) < .001f,
                    "Following the body applied room movement to the view twice");
            }
            Check(Mathf.Abs(Vector3.Distance(root.transform.position, start) - .52f) < .01f, "Body did not follow physical room movement with a lean allowance");
            body.enabled = false; root.transform.position = start; body.enabled = true; center = Vector3.up * 1.65f;
            var wall = Own(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = start + new Vector3(.55f, .8f, 0); wall.transform.localScale = new Vector3(.1f, 2, 2);
            Physics.SyncTransforms();
            for (int i = 0; i < 10; i++)
            {
                Vector3 moved = (Vector3)follow.Invoke(null, new object[] { body, rotation, raw - center });
                center = (Vector3)consume.Invoke(null, new object[] { center, rotation, moved });
            }
            Check(root.transform.position.x - start.x < .4f, "Room-scale body passed through a solid wall");
            body.enabled = false;
            Check((Vector3)follow.Invoke(null, new object[] { body, rotation, Vector3.forward }) == Vector3.zero, "Disabled native body was moved");
            root.SetActive(false); wall.SetActive(false);
        }
    }
}
