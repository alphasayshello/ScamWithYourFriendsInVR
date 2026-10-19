using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    [BepInPlugin("com.swyf.vr.localpair", "SWYF local two-client simulation", "0.1.0")]
    [BepInDependency("com.swyf.vr")]
    [DefaultExecutionOrder(30550)]
    public sealed partial class LocalPairHarness : BaseUnityPlugin
    {
        private bool host, client, active, queued, placed, singleComputer;
        private string readyFile;
        private Harmony patches;
        private float nextReport, placedAt, connectedAt;
        private bool captured;
        private bool testingComputer;
        private const ushort Port = 27910;

        private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
        private static object Instance(string name) => Find(name)?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        private static void KcpBeforeStart(object __instance) => AccessTools.Field(__instance.GetType(), "alwaysUseKcp")?.SetValue(__instance, true);
        private static bool AllowLoopbackOnly(NetworkConnectionToClient conn)
        {
            if (conn.connectionId == 0) return true;
            string text = Transport.active.ServerGetClientAddress(conn.connectionId);
            bool local = text == "127.0.0.1" || text == "::1" || text == "::ffff:127.0.0.1"
                || text.StartsWith("127.0.0.1:") || text.StartsWith("[::1]:") || text.StartsWith("[::ffff:127.0.0.1]:");
            if (local) return true;
            conn.Disconnect();
            return false;
        }

        private void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            singleComputer = args.Contains("--swyf-single-computer-tests");
            host = args.Contains("--swyf-pair-host") || singleComputer; client = args.Contains("--swyf-pair-client");
            active = host || client;
            if (!active) return;
            readyFile = args.FirstOrDefault(a => a.StartsWith("--swyf-pair-ready="))?.Substring("--swyf-pair-ready=".Length);
            if (string.IsNullOrWhiteSpace(readyFile)) { Logger.LogError("PAIR: ready-file path missing"); active = false; return; }
            patches = new Harmony("com.swyf.vr.localpair");
            patches.Patch(AccessTools.Method(typeof(ScamWithYourFriendsInVR.Input.VRInputManager), "Update"), postfix: new HarmonyMethod(typeof(LocalPairHarness), nameof(KeepTestTriggerHeld)));
            var initializer = Find("NetworkingInitializer");
            var start = initializer == null ? null : AccessTools.Method(initializer, "Start");
            if (start == null) { Logger.LogError("PAIR: networking initializer unavailable"); active = false; return; }
            patches.Patch(start, prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(KcpBeforeStart)));
            var managerType = Find("DeveloperSpectatorNetworkManager");
            if (managerType != null)
                patches.Patch(AccessTools.Method(managerType, "OnServerConnect"), prefix: new HarmonyMethod(typeof(LocalPairHarness), nameof(AllowLoopbackOnly)));
            Find("SteamCreateLobbyForServer")?.GetField("hostLobbyMode", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, 1);
            Application.runInBackground = true;
            Logger.LogInfo("PAIR: " + (singleComputer ? "single simulated VR host" : host ? "PC host, private lobby" : "simulated VR client") + "; loopback transport port=" + Port);
        }

        private IEnumerator Start()
        {
            if (!active) yield break;
            float deadline = Time.realtimeSinceStartup + 90;
            object initializer;
            while ((initializer = Instance("NetworkingInitializer")) == null || NetworkManager.singleton == null
                || !(Instance("MainMenuSingleton")?.GetType().GetProperty("IsInitialized")?.GetValue(Instance("MainMenuSingleton")) is bool initialized && initialized))
            {
                if (Time.realtimeSinceStartup > deadline) { Logger.LogError("PAIR: timed out waiting for main menu"); yield break; }
                yield return null;
            }
            if (Transport.active == null || Transport.active.GetType().Name != "KcpTransport")
            {
                Logger.LogError("PAIR: expected the game's KCP prefab; refusing to reconfigure a live Steam transport"); yield break;
            }
            if (client)
            {
                while (!File.Exists(readyFile))
                {
                    if (Time.realtimeSinceStartup > deadline) { Logger.LogError("PAIR: timed out waiting for private host"); yield break; }
                    yield return null;
                }
            }
            while (!(Find("BackendAuthenticationStatusSource")?.GetProperty("CanStartGame", BindingFlags.Static | BindingFlags.Public)?.GetValue(null) is bool signedIn && signedIn))
            {
                if (Time.realtimeSinceStartup > deadline) { Logger.LogError("PAIR: native sign-in did not become ready"); yield break; }
                yield return null;
            }
            object result = host
                ? AccessTools.Method(initializer.GetType(), "EnqueueHost").Invoke(initializer, new object[] { "127.0.0.1", Port, "ScammerGetScammed" })
                : AccessTools.Method(initializer.GetType(), "EnqueueClient").Invoke(initializer, new object[] { "127.0.0.1", "", Port, false });
            queued = result is bool accepted && accepted;
            Logger.LogInfo("PAIR: automatic " + (host ? "host" : "join") + " queued=" + queued);
            if (queued && (singleComputer || client && Environment.GetCommandLineArgs().Contains("--swyf-pair-computer-tests")))
            {
                IEnumerator test = TestComputer();
                while (true)
                {
                    bool more;
                    try { more = test.MoveNext(); }
                    catch (Exception ex) { Logger.LogError("COMPUTER FAIL: " + ex.GetBaseException()); (test as IDisposable)?.Dispose(); break; }
                    if (!more) break;
                    yield return test.Current;
                }
            }
            if ((client || singleComputer) && queued && Environment.GetCommandLineArgs().Contains("--swyf-pair-grip-tests"))
            {
                IEnumerator test = TestGrip();
                while (true)
                {
                    bool more;
                    try { more = test.MoveNext(); }
                    catch (Exception ex) { Logger.LogError("GRIP FAIL: " + ex.GetBaseException()); (test as IDisposable)?.Dispose(); yield break; }
                    if (!more) break;
                    yield return test.Current;
                }
            }
            if (singleComputer && queued)
            {
                IEnumerator settingsTest = TestSettingsRender();
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = settingsTest.MoveNext(); }
                        catch (Exception ex) { Logger.LogError("SETTINGS FAIL: " + ex.GetBaseException()); break; }
                        if (!more) break;
                        yield return settingsTest.Current;
                    }
                }
                finally { (settingsTest as IDisposable)?.Dispose(); }
                IEnumerator gameplayTest = TestGameplayControls();
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = gameplayTest.MoveNext(); }
                        catch (Exception ex) { Logger.LogError("GAMEPLAY FAIL: " + ex.GetBaseException()); break; }
                        if (!more) break;
                        yield return gameplayTest.Current;
                    }
                }
                finally { (gameplayTest as IDisposable)?.Dispose(); }
                IEnumerator foodTest = TestNativeFood();
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = foodTest.MoveNext(); }
                        catch (Exception ex) { Logger.LogError("FOOD FAIL: " + ex.GetBaseException()); break; }
                        if (!more) break;
                        yield return foodTest.Current;
                    }
                }
                finally { (foodTest as IDisposable)?.Dispose(); }
                IEnumerator boostTest = TestNativeBoost();
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = boostTest.MoveNext(); }
                        catch (Exception ex) { Logger.LogError("BOOST FAIL: " + ex.GetBaseException()); break; }
                        if (!more) break;
                        yield return boostTest.Current;
                    }
                }
                finally { (boostTest as IDisposable)?.Dispose(); }
                IEnumerator combatTest = TestNativeCombat();
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = combatTest.MoveNext(); }
                        catch (Exception ex) { Logger.LogError("COMBAT FAIL: " + ex.GetBaseException()); break; }
                        if (!more) break;
                        yield return combatTest.Current;
                    }
                }
                finally { (combatTest as IDisposable)?.Dispose(); }
                IEnumerator rigTest = TestNativeRigMotion();
                try
                {
                    while (true)
                    {
                        bool more;
                        try { more = rigTest.MoveNext(); }
                        catch (Exception ex) { Logger.LogError("RIG MOTION FAIL: " + ex.GetBaseException()); break; }
                        if (!more) break;
                        yield return rigTest.Current;
                    }
                }
                finally { (rigTest as IDisposable)?.Dispose(); }
            }
        }

        private void Update()
        {
            if (!active || !queued) return;
            if (host && NetworkServer.connections.Count >= 2)
            {
                if (connectedAt == 0) connectedAt = Time.unscaledTime;
                if (!captured && Time.unscaledTime - connectedAt > 15)
                {
                    captured = true;
                    string screenshot = Path.Combine(Path.GetDirectoryName(readyFile), "pair-host-view.png");
                    var capture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule", false)?.GetMethod("CaptureScreenshot", new[] { typeof(string), typeof(int) });
                    if (capture != null) capture.Invoke(null, new object[] { screenshot, 1 });
                    else Logger.LogWarning("PAIR: ScreenCapture module unavailable; screenshot not captured");
                    foreach (var identity in NetworkServer.connections.Values.Select(c => c.identity).Where(i => i != null && !i.isLocalPlayer))
                        foreach (Transform bone in identity.GetComponentsInChildren<Transform>(true))
                            if (bone.name == "mixamorig:Head" || bone.name.EndsWith("Hand") || bone.name.EndsWith("ForeArm") || bone.name.EndsWith(":LeftArm") || bone.name.EndsWith(":RightArm"))
                                Logger.LogInfo("PAIR bone: " + bone.name + "; parent=" + bone.parent?.name + "; position=" + identity.transform.InverseTransformPoint(bone.position) + "; rotation=" + bone.eulerAngles + "; scale=" + bone.lossyScale);
                    Logger.LogInfo("PAIR: requested host screenshot " + screenshot);
                }
            }
            if (host && NetworkServer.active && NetworkServer.listen && NetworkClient.localPlayer != null && NetworkClient.ready)
            {
                Transform player = NetworkClient.localPlayer.transform;
                Transform facing = Multiplayer.AvatarArmCalibration.FacingFrame(player);
                Vector3 position = player.position + facing.forward * 2;
                string marker = position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ","
                    + position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ","
                    + position.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ","
                    + Mathf.Repeat(facing.eulerAngles.y + 180, 360).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                if (!File.Exists(readyFile)) File.WriteAllText(readyFile, marker);
            }
            if (Time.unscaledTime >= nextReport)
            {
                nextReport = Time.unscaledTime + 10;
                Logger.LogInfo("PAIR state: host=" + host + "; connected=" + NetworkClient.isConnected + "; ready=" + NetworkClient.ready
                    + "; spawned objects=" + NetworkClient.spawned.Count + "; server connections=" + NetworkServer.connections.Count
                    + "; vr session=" + (VRManager.Instance != null && VRManager.Instance.Ready));
            }
        }

        private void LateUpdate()
        {
            if (!active || (!client && !singleComputer) || !queued || !NetworkClient.ready || NetworkClient.localPlayer == null) return;
            var manager = VRManager.Instance;
            if (manager == null || !manager.Ready || manager.Rig.Head == null) return;
            InitializeMockPose();
            if (singleComputer && !testingComputer) return;
            if (testingComputer)
            {
                // Mock controller devices otherwise return zero poses at the head.
                // Keep plausible seated grip poses while the probe drives its aim ray.
                Transform head = manager.Rig.Head.transform;
                Quaternion seatedFacing = Quaternion.Euler(0, head.eulerAngles.y, 0);
                manager.Rig.Left.transform.SetPositionAndRotation(head.position + seatedFacing * new Vector3(-0.3f, -0.4f, 0.35f), seatedFacing);
                manager.Rig.Right.transform.SetPositionAndRotation(head.position + seatedFacing * new Vector3(0.3f, -0.4f, 0.35f), seatedFacing);
                return;
            }
            Transform root = NetworkClient.localPlayer.transform;
            if (!placed && File.Exists(readyFile))
            {
                string[] xyz = File.ReadAllText(readyFile).Split(',');
                if (xyz.Length >= 3)
                {
                    var culture = System.Globalization.CultureInfo.InvariantCulture;
                    Vector3 location = new Vector3(float.Parse(xyz[0], culture), float.Parse(xyz[1], culture), float.Parse(xyz[2], culture));
                    var controllerType = Find("Metater.MetaPlayerController");
                    object controller = controllerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (controller != null) AccessTools.Method(controllerType, "Teleport").Invoke(controller, new object[] { location, true });
                    if (xyz.Length >= 4)
                        Multiplayer.AvatarArmCalibration.FacingFrame(root).rotation = Quaternion.Euler(0, float.Parse(xyz[3], culture), 0);
                }
                placed = true;
                placedAt = Time.unscaledTime;
                Logger.LogInfo("PAIR: simulated rig placed in front of PC host");
            }
            float t = Time.unscaledTime - placedAt;
            Quaternion facing = Multiplayer.AvatarArmCalibration.FacingFrame(root).rotation;
            manager.Rig.Head.transform.SetPositionAndRotation(root.position + Vector3.up * 1.65f,
                facing * Quaternion.Euler(Mathf.Sin(t * 0.7f) * 6, Mathf.Sin(t * 0.9f) * 20, 0));
            manager.Rig.Left.transform.SetPositionAndRotation(root.position + facing * new Vector3(-0.3f, 1.2f + 0.12f * Mathf.Sin(t * 1.4f), 0.3f), facing);
            manager.Rig.Right.transform.SetPositionAndRotation(root.position + facing * new Vector3(0.3f, 1.2f + 0.12f * Mathf.Sin(t * 1.1f), 0.3f), facing);
        }

        private void OnGUI()
        {
            if (!active) return;
            GUI.Box(new Rect(12, 12, 480, 48), host ? "LOCAL TEST: PC HOST / INVITE-ONLY / LOOPBACK" : "LOCAL TEST: SIMULATED VR / SCRIPTED HEAD AND HANDS");
        }
        private void OnDestroy() { patches?.UnpatchSelf(); }
    }
}
