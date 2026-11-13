using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace CozyMarblesVR
{
    /// <summary>
    /// Brings up Unity's OpenXR plugin at run time. In a normal Unity project the editor bakes these
    /// settings objects into the build; here they are created in memory.
    /// </summary>
    /// <summary>Development only: connects Unity's mock runtime control API so tests can move the simulated headset.</summary>
    public class MockHookFeature : OpenXRFeature
    {
        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr LoadLibraryW(string path);

        [System.Runtime.InteropServices.DllImport("mock_api", EntryPoint = "MockRuntime_HookCreateInstance")]
        static extern IntPtr HookCreateInstance(IntPtr func);

        [System.Runtime.InteropServices.DllImport("mock_api", EntryPoint = "MockRuntime_SetReferenceSpace")]
        public static extern void SetReferenceSpace(int referenceSpace, Vector3 position, Quaternion orientation, int locationFlags);

        [System.Runtime.InteropServices.DllImport("mock_api", EntryPoint = "MockRuntime_SetView")]
        public static extern void SetView(int viewConfigurationType, int viewIndex, Vector3 position, Quaternion orientation, Vector4 fov);

        public static bool Loaded { get; private set; }

        public static bool Load()
        {
            string dll = Path.Combine(Paths.GameRootPath, "CozyMarblesVR", "lib", "mock", "windows", "x64", "mock_api.dll");
            Loaded = File.Exists(dll) && LoadLibraryW(dll) != IntPtr.Zero;
            return Loaded;
        }

        protected internal override IntPtr HookGetInstanceProcAddr(IntPtr func) => HookCreateInstance(func);
    }

    /// <summary>Tracks the OpenXR session state: only VISIBLE/FOCUSED means the headset actually shows the game.</summary>
    public class SessionStateFeature : OpenXRFeature
    {
        public const int Synchronized = 3, VisibleState = 4, FocusedState = 5;
        public static volatile int State;

        protected internal override void OnSessionStateChange(int oldState, int newState)
        {
            State = newState;
            Plugin.Log.LogInfo($"OpenXR session state {oldState} -> {newState}");
        }

        protected internal override void OnSessionEnd(ulong xrSession) => State = 0;
    }

    public static class XRBootstrap
    {
        public static XRManagerSettings Manager { get; private set; }
        static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();

        public static bool Initialize(bool start = true)
        {
            try
            {
                if (Plugin.Cfg.MockRuntime.Value)
                {
                    string mock = Path.Combine(Paths.GameRootPath, "CozyMarblesVR", "lib", "mock", "unity-mock-runtime.json");
                    if (File.Exists(mock))
                    {
                        Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", mock);
                        Plugin.Log.LogWarning("Using the mock OpenXR runtime: " + mock);
                    }
                    else Plugin.Log.LogError("Mock runtime requested but not found: " + mock);
                }

                var settings = OpenXRSettings.Instance;
                // The game was built without XR, so its shaders have no single-pass stereo variants.
                settings.renderMode = OpenXRSettings.RenderMode.MultiPass;
                settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
                var features = new List<OpenXRFeature>
                {
                    // SteamVR maps the Steam Frame controllers onto the Oculus Touch layout when an
                    // application does not ship Frame-specific bindings.
                    Feature<OculusTouchControllerProfile>("Oculus Touch Controller Profile"),
                    Feature<ValveIndexControllerProfile>("Valve Index Controller Profile"),
                    Feature<HTCViveControllerProfile>("HTC Vive Controller Profile"),
                    Feature<MicrosoftMotionControllerProfile>("Microsoft Motion Controller Profile"),
                    Feature<KHRSimpleControllerProfile>("Khronos Simple Controller Profile"),
                };
                features.Add(Feature<SessionStateFeature>("Cozy Marbles VR Session State"));
                if (Plugin.Cfg.FrameControllerProfile.Value)
                    features.Insert(0, Feature<SteamFrameControllerProfile>("Steam Frame Controller Profile", SteamFrameControllerProfile.Extension));
                if (Plugin.Cfg.MockRuntime.Value && MockHookFeature.Load())
                    features.Add(Feature<MockHookFeature>("Mock Runtime Hook"));
                settings.features = features.ToArray();

                var general = ScriptableObject.CreateInstance<XRGeneralSettings>();
                Manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                var loader = ScriptableObject.CreateInstance<OpenXRLoader>();
                Manager.automaticLoading = false;
                Manager.automaticRunning = false;
#pragma warning disable CS0618
                Manager.loaders.Clear();
                Manager.loaders.Add(loader);
#pragma warning restore CS0618
                general.Manager = Manager;

                return !start || TryStartLoader();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("OpenXR start-up failed: " + e);
                return false;
            }
        }

        /// <summary>Connects to the OpenXR runtime. Safe to call again after a failed attempt (e.g. SteamVR was not running yet).</summary>
        public static bool TryStartLoader()
        {
            try
            {
                if (Manager == null) return false;
                if (Manager.activeLoader == null)
                {
                    Manager.InitializeLoaderSync();
                    if (Manager.activeLoader == null)
                        return false;
                    Manager.StartSubsystems();
                }

                // The game's post-processing shaders were built without their XR "visible mesh" passes;
                // make URP draw full-screen passes the ordinary way.
                UnityEngine.Rendering.XRSRPSettings.useVisibilityMesh = false;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("OpenXR start-up failed: " + e);
                return false;
            }
        }

        public static bool Connected => Manager != null && Manager.activeLoader != null;

        /// <summary>SteamVR shows the game in the headset (not e.g. the desktop theater).</summary>
        public static bool Visible => SessionStateFeature.State == SessionStateFeature.VisibleState || SessionStateFeature.State == SessionStateFeature.FocusedState;

        public static int SessionState => SessionStateFeature.State;

        /// <summary>Leaves VR without quitting: the headset goes back to SteamVR home.</summary>
        public static void Suspend()
        {
            // End the session completely; a fresh one is created on Resume (like at game start).
            Shutdown();
            SessionStateFeature.State = 0;
        }

        public static void Resume()
        {
            if (!TryStartLoader())
                Plugin.Log.LogWarning("OpenXR could not be started again (is SteamVR running?). The mod keeps trying for a while.");
        }

        static T Feature<T>(string uiName, string extensions = "") where T : OpenXRFeature
        {
            var f = ScriptableObject.CreateInstance<T>();
            f.name = uiName;
            f.nameUi = uiName;
            f.version = "0.0.1";
            f.company = "Unity";
            f.openxrExtensionStrings = extensions;
            f.enabled = true;
            return f;
        }

        public static XRDisplaySubsystem Display
        {
            get
            {
                SubsystemManager.GetSubsystems(Displays);
                foreach (var d in Displays)
                    if (d != null && d.running)
                        return d;
                return null;
            }
        }

        /// <summary>True once the headset session is running and frames are being submitted.</summary>
        public static bool Running => Display != null;

        public static void Shutdown()
        {
            try
            {
                if (Manager != null && Manager.activeLoader != null)
                {
                    Manager.StopSubsystems();
                    Manager.DeinitializeLoader();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("OpenXR shutdown: " + e.Message);
            }
        }
    }
}
