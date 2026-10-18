using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ScamWithYourFriendsInVR
{
    public sealed class VRMirrorView : MonoBehaviour
    {
        internal static int AdjustedFrames { get; private set; }
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        private void OnGUI()
        {
            VRManager manager = VRManager.Instance;
            if (Event.current.type != EventType.Repaint || manager == null || !manager.Ready
                || !Plugin.MirrorFullView.Value || manager.Rig?.Head == null) return;
            displays.Clear(); SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running || !display.GetMirrorViewBlitDesc(null, out var mirror, XRMirrorViewBlitMode.LeftEye)
                    || mirror.blitParamsCount != 1) continue;
                mirror.GetBlitParameter(0, out var blit);
                Texture texture = blit.srcTex;
                if (texture == null || texture.dimension != UnityEngine.Rendering.TextureDimension.Tex2D
                    || texture.height <= 0 || (float)texture.width / texture.height > 1.6f
                    || blit.foveatedRenderingInfo != System.IntPtr.Zero) continue;
                Color color = GUI.color;
                int depth = GUI.depth;
                Matrix4x4 matrix = GUI.matrix;
                try
                {
                    GUI.matrix = Matrix4x4.identity;
                    GUI.depth = 100;
                    Rect window = new Rect(0, 0, Screen.width, Screen.height);
                    GUI.color = Color.black;
                    GUI.DrawTexture(window, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
                    GUI.color = Color.white;
                    float scale = Mathf.Min(window.width / texture.width, window.height / texture.height);
                    var fitted = new Rect((window.width - texture.width * scale) * 0.5f,
                        (window.height - texture.height * scale) * 0.5f, texture.width * scale, texture.height * scale);
                    Rect uv = SystemInfo.graphicsUVStartsAtTop ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
                    GUI.DrawTextureWithTexCoords(fitted, texture, uv, false);
                    AdjustedFrames++;
                }
                finally { GUI.color = color; GUI.depth = depth; GUI.matrix = matrix; }
                break;
            }
        }
    }
}
