using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class LocalPairHarness
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetReferenceSpace(int space, Vector3 position, Quaternion rotation, ulong flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetViewPose(int type, int index, Vector3 position, Quaternion rotation, Vector4 fov);
        private bool mockPoseInitialized;
        private SetReferenceSpace setMockSpace;
        private SetViewPose setMockView;

        private void SetMockHeadPose(Vector3 position, Quaternion rotation)
        {
            if (setMockSpace == null || setMockView == null) throw new InvalidOperationException("Mock tracking is not initialized");
            setMockSpace(1, position, rotation, 15);
            var fov = new Vector4(-.785f, .785f, .785f, -.785f);
            setMockView(2, 0, position + rotation * new Vector3(-.032f, 0, 0), rotation, fov);
            setMockView(2, 1, position + rotation * new Vector3(.032f, 0, 0), rotation, fov);
        }

        private void InitializeMockPose()
        {
            if (mockPoseInitialized) return;
            mockPoseInitialized = true;
            try
            {
                Type runtime = Find("UnityEngine.XR.OpenXR.OpenXRRuntime");
                if (runtime?.GetProperty("name", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string != "Unity Mock Runtime")
                    throw new InvalidOperationException("Local simulated tracking requires Unity Mock Runtime");
                string json = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
                if (string.IsNullOrEmpty(json) || Path.GetFileName(json) != "unity-mock-runtime.json")
                    throw new InvalidOperationException("Expected the pinned mock runtime manifest");
                // mock_api.dll is a trampoline that requires a pre-instance hook.
                // This late initializer must call exports on the loaded runtime
                // itself; otherwise its setters silently do nothing.
                string path = Path.Combine(Path.GetDirectoryName(json), "windows", "x64", "mock_runtime.dll");
                if (!File.Exists(path)) throw new FileNotFoundException("Mock tracking API missing", path);
                IntPtr library = LoadLibraryW(path);
                if (library == IntPtr.Zero) throw new InvalidOperationException("Mock tracking API load failed: " + Marshal.GetLastWin32Error());
                IntPtr spaceFunction = GetProcAddress(library, "MockRuntime_SetReferenceSpace");
                IntPtr viewFunction = GetProcAddress(library, "MockRuntime_SetView");
                if (spaceFunction == IntPtr.Zero || viewFunction == IntPtr.Zero) throw new MissingMethodException("Mock pose exports missing");
                setMockSpace = Marshal.GetDelegateForFunctionPointer<SetReferenceSpace>(spaceFunction);
                setMockView = Marshal.GetDelegateForFunctionPointer<SetViewPose>(viewFunction);
                // XrReferenceSpaceType.View=1; all four valid/tracked location bits.
                SetMockHeadPose(new Vector3(0, 1.65f, 0), Quaternion.identity);
                VRManager.Instance.Rig.Recenter();
                Logger.LogInfo("PAIR: mock headset reference and stereo views initialized at standing eye height");
            }
            catch (Exception ex) { Logger.LogError("PAIR: mock pose initialization failed: " + ex.GetBaseException().Message); }
        }
    }
}
