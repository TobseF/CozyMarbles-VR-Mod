using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CozyMarblesVR
{
    [BepInPlugin(Guid, "Cozy Marbles VR", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "tobse.cozymarbles.vr";
        public const string Version = "0.1.0";

        public static ManualLogSource Log;
        public static VRConfig Cfg;

        void Awake()
        {
            Log = Logger;
            Cfg = new VRConfig(Config);

            if (!Cfg.Enabled.Value)
            {
                Log.LogInfo("Mod disabled in the config, the game runs unmodified.");
                return;
            }
            bool noVr = Environment.GetCommandLineArgs().Any(a => a.Equals("-novr", StringComparison.OrdinalIgnoreCase));
            bool dialog = Cfg.ShowStartDialog.Value && !noVr;
            bool startVr = !noVr && Cfg.StartIn.Value == StartMode.VR;

            try
            {
                new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception e)
            {
                Log.LogError("Applying game hooks failed, VR stays off: " + e);
                return;
            }

            // OpenXR is only started right away when VR is wanted and no start window asks first.
            if (!XRBootstrap.Initialize(start: startVr && !dialog))
                Log.LogWarning("OpenXR could not be started (is SteamVR running and the headset connected?). The game runs on the monitor; the mod keeps looking for the headset for ten minutes.");

            // The manager idles until a headset session is actually running.
            var go = new GameObject("CozyMarblesVR");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            var manager = go.AddComponent<VRManager>();
            manager.Init(startFlat: !startVr, waitForStartWindow: dialog);
            if (dialog) go.AddComponent<StartDialog>();
            Log.LogInfo("Cozy Marbles VR " + Version + " initialised.");
        }
    }
}
