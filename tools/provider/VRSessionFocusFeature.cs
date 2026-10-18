using UnityEngine.XR.OpenXR.Features;

namespace ScamWithYourFriendsInVR.Provider
{
    public sealed class VRSessionFocusFeature : OpenXRFeature
    {
        public const string featureId = "com.swyf.vr.sessionfocus";
        public static bool HasSessionState { get; private set; }
        public static bool HasInputFocus { get; private set; }
        protected internal override void OnSessionStateChange(int oldState, int newState)
        {
            HasSessionState = true;
            HasInputFocus = newState == 5;
        }
        protected internal override void OnSessionDestroy(ulong session)
        {
            HasSessionState = false;
            HasInputFocus = false;
        }
    }
}
