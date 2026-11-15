using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace CozyMarblesVR
{
    /// <summary>Scripted values for the simulated controllers (development only).</summary>
    public static class SimOverride
    {
        public static bool Enabled;
        public static bool HasAimWorld;
        public static Vector3 AimWorld;
        public static float AimYaw, AimPitch;
        public static readonly Dictionary<string, bool> Buttons = new Dictionary<string, bool>();
        public static Vector2 LeftStick, RightStick;
        public static Vector3 LeftHandOffset, RightHandOffset;
        public static float AimForward;

        public static bool Button(string name) => Buttons.TryGetValue(name, out bool b) && b;
    }

    /// <summary>
    /// Development helper, active only with [Debug] Diagnostics = true. Polls BepInEx/cozyvr-cmd.txt for
    /// simple commands so the mod can be exercised without a headset (see Execute for the list).
    /// </summary>
    public class Diagnostics
    {
        readonly VRManager _manager;
        readonly Queue<string> _queue = new Queue<string>();
        float _nextPoll, _waitUntil;
        readonly Dictionary<string, float> _taps = new Dictionary<string, float>();

        static string CmdFile => Path.Combine(Paths.BepInExRootPath, "cozyvr-cmd.txt");
        static string OutDir => Path.Combine(Paths.BepInExRootPath, "cozyvr-out");

        public Diagnostics(VRManager manager)
        {
            _manager = manager;
            Directory.CreateDirectory(OutDir);
        }

        public void Tick()
        {
            float now = Time.unscaledTime;
            if (now >= _nextPoll)
            {
                _nextPoll = now + 0.2f;
                try
                {
                    if (File.Exists(CmdFile))
                    {
                        foreach (string line in File.ReadAllLines(CmdFile))
                            if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#")) _queue.Enqueue(line.Trim());
                        File.Delete(CmdFile);
                    }
                }
                catch (IOException) { }
            }

            // release tapped buttons
            if (_taps.Count > 0)
            {
                var done = new List<string>();
                foreach (var kv in _taps) if (now >= kv.Value) done.Add(kv.Key);
                foreach (string k in done) { SimOverride.Buttons[k] = false; _taps.Remove(k); }
            }

            while (_queue.Count > 0 && now >= _waitUntil)
            {
                string cmd = _queue.Dequeue();
                try { Execute(cmd); }
                catch (Exception e) { Plugin.Log.LogError($"[diag] '{cmd}' failed: {e}"); }
            }
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        void Execute(string line)
        {
            string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (a[0].ToLowerInvariant())
            {
                case "wait": _waitUntil = Time.unscaledTime + F(a[1]); break;
                case "log": Plugin.Log.LogInfo("[diag] " + line.Substring(3).Trim()); break;
                case "shot": ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, a[1] + ".png")); break;
                case "uishot": SaveUiTexture(Path.Combine(OutDir, a[1] + ".png")); break;
                case "eyeshot": _manager.DebugCaptureEye(Path.Combine(OutDir, a[1] + ".png")); break;
                case "handshot": _manager.DebugHandShot(Path.Combine(OutDir, a[1] + ".png"), a[2], F(a[3]), F(a[4])); break;
                case "grab": Plugin.Cfg.GrabMode.Value = a[1] == "on"; break;
                case "marble": Plugin.Cfg.MarbleMode.Value = a[1] == "on"; break;
                case "aimfwd": SimOverride.AimForward = F(a[1]); break;
                case "lockmouse": TimR.InputStuff.InputManager._?.SetMouseLocked(true); break;
                case "physics": Plugin.Cfg.MarblePhysics.Value = a[1] == "on"; break;
                case "throw": Plugin.Cfg.ThrowLevel.Value = int.Parse(a[1]); break;
                case "aimsethandle": SimOverride.Enabled = true; SimOverride.HasAimWorld = true; SimOverride.AimWorld = _manager.Settings.DebugHandlePoint(); break;
                case "handabove": _manager.DebugHandAbove(new Vector3(F(a[1]), F(a[2]), F(a[3])), F(a[4])); break;
                case "dump": Dump(); break;
                case "sim": SimOverride.Enabled = a[1] == "on"; break;
                case "aim": SimOverride.Enabled = true; SimOverride.HasAimWorld = false; SimOverride.AimYaw = F(a[1]); SimOverride.AimPitch = F(a[2]); break;
                case "aimworld": SimOverride.Enabled = true; SimOverride.HasAimWorld = true; SimOverride.AimWorld = new Vector3(F(a[1]), F(a[2]), F(a[3])); break;
                case "aimwidget": SimOverride.Enabled = true; SimOverride.HasAimWorld = true; SimOverride.AimWorld = _manager.DebugWidgetPoint(a[1]); break;
                case "aimsettings": SimOverride.Enabled = true; SimOverride.HasAimWorld = true; SimOverride.AimWorld = _manager.Settings.DebugButtonPoint(int.Parse(a[1])); break;
                case "aimpx": SimOverride.Enabled = true; SimOverride.HasAimWorld = true; SimOverride.AimWorld = _manager.DebugPanelPoint(F(a[1]), F(a[2])); break;
                case "down": SimOverride.Enabled = true; SimOverride.Buttons[a[1]] = true; break;
                case "up": SimOverride.Buttons[a[1]] = false; break;
                case "tap": SimOverride.Enabled = true; SimOverride.Buttons[a[1]] = true; _taps[a[1]] = Time.unscaledTime + (a.Length > 2 ? F(a[2]) : 0.15f); break;
                case "stick": SimOverride.Enabled = true; SimOverride.LeftStick = new Vector2(F(a[1]), F(a[2])); SimOverride.RightStick = new Vector2(F(a[3]), F(a[4])); break;
                case "tilt": _manager.DebugRigTilt = new Vector3(F(a[1]), F(a[2]), 0f); break;
                case "recenter": _manager.Recenter(); break;
                case "handr": SimOverride.Enabled = true; SimOverride.RightHandOffset = new Vector3(F(a[1]), F(a[2]), F(a[3])); break;
                case "handl": SimOverride.Enabled = true; SimOverride.LeftHandOffset = new Vector3(F(a[1]), F(a[2]), F(a[3])); break;
                case "head": MockHead(new Vector3(F(a[1]), F(a[2]), F(a[3])), Quaternion.Euler(F(a[5]), F(a[4]), 0f)); break;
                case "devices": DumpDevices(); break;
                case "mockenv":
                    // lets a later connection attempt reach the mock runtime (tests the "SteamVR started late" path)
                    Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", Path.Combine(Paths.GameRootPath, "CozyMarblesVR", "lib", "mock", "unity-mock-runtime.json"));
                    break;
                case "hud": _manager.DebugHudMode(a[1]); break;
                case "aimtag":
                {
                    var objs = GameObject.FindGameObjectsWithTag(a[1]);
                    int idx = a.Length > 2 ? int.Parse(a[2]) : 0;
                    if (objs.Length > idx)
                    {
                        var col = objs[idx].GetComponentInChildren<Collider>();
                        SimOverride.Enabled = true; SimOverride.HasAimWorld = true;
                        SimOverride.AimWorld = col != null ? col.bounds.center : objs[idx].transform.position;
                        Plugin.Log.LogInfo($"[diag] aim at {objs[idx].name} {SimOverride.AimWorld} ({objs.Length} tagged)");
                    }
                    break;
                }
                case "tree":
                {
                    // tree <canvasNameSubstring> [depth]: hierarchy with rects (canvas space) and components
                    int depth = a.Length > 2 ? int.Parse(a[2]) : 3;
                    var sbt = new StringBuilder("[diag] tree\n");
                    foreach (var cv in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        if (!cv.isRootCanvas || cv.name.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
                        sbt.AppendLine($"CANVAS {cv.name} active={cv.gameObject.activeInHierarchy} mode={cv.renderMode} cam={(cv.worldCamera ? cv.worldCamera.name : "-")}");
                        Dump(sbt, cv.transform, 1, depth);
                    }
                    Plugin.Log.LogInfo(sbt.ToString());
                    break;
                }
                case "start": { var d = StartDialog.Instance; if (d != null) d.Choose(a[1] == "vr"); break; }
                case "overlayshot": { var rt = _manager.Hud.OverlayTexture; if (rt != null) SaveRenderTexture(rt, Path.Combine(OutDir, a[1] + ".png")); break; }
                case "buildshot": { var rt = _manager.Hud.BuildTexture; if (rt != null) SaveRenderTexture(rt, Path.Combine(OutDir, a[1] + ".png")); break; }
                case "flat": _manager.SetFlat(a[1] == "on"); break;
                case "quit": Application.Quit(); break;
                default: Plugin.Log.LogWarning("[diag] unknown command: " + line); break;
            }
        }

        // --- mock runtime: move the simulated headset (only meaningful with [Debug] MockRuntime = true)
        static void MockHead(Vector3 position, Quaternion rotation)
        {
            if (!MockHookFeature.Loaded) { Plugin.Log.LogWarning("[diag] mock runtime API not loaded"); return; }
            MockHookFeature.SetReferenceSpace(1 /* view space */, position, rotation, 15 /* valid + tracked */);
            var fov = new Vector4(-0.82f, 0.82f, 0.87f, -0.87f);
            MockHookFeature.SetView(2 /* primary stereo */, 0, position + rotation * new Vector3(-0.032f, 0f, 0f), rotation, fov);
            MockHookFeature.SetView(2, 1, position + rotation * new Vector3(0.032f, 0f, 0f), rotation, fov);
        }

        static void DumpDevices()
        {
            var sb = new StringBuilder("[diag] XR input devices\n");
            var list = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevices(list);
            var usages = new List<InputFeatureUsage>();
            foreach (var d in list)
            {
                sb.AppendLine($"'{d.name}' manufacturer='{d.manufacturer}' characteristics={d.characteristics} valid={d.isValid}");
                usages.Clear();
                if (d.TryGetFeatureUsages(usages))
                    sb.AppendLine("    usages: " + string.Join(", ", usages.ConvertAll(u => u.name + ":" + u.type.Name)));
            }
            sb.AppendLine("Input System devices: " + string.Join(" | ", UnityEngine.InputSystem.InputSystem.devices.Select(d => d.name + "(" + d.layout + ")")));
            Plugin.Log.LogInfo(sb.ToString());
        }

        static void Dump(StringBuilder sb, Transform t, int level, int max)
        {
            foreach (Transform c in t)
            {
                var rt = c as RectTransform;
                var comps = string.Join(",", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name));
                sb.AppendLine($"{new string(' ', level * 2)}{c.name} [{(c.gameObject.activeSelf ? "on" : "off")}] {(rt != null ? $"anchor={rt.anchorMin}-{rt.anchorMax} pos={rt.anchoredPosition} size={rt.rect.size}" : "")} {{{comps}}}");
                if (level < max) Dump(sb, c, level + 1, max);
            }
        }

        void SaveUiTexture(string path)
        {
            Camera ui = _manager.Hud.UiCamera;
            RenderTexture rt = ui != null ? ui.targetTexture : null;
            if (rt == null) { Plugin.Log.LogWarning("[diag] no UI texture"); return; }
            SaveRenderTexture(rt, path);
        }

        public static void SaveRenderTexture(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        void Dump()
        {
            var sb = new StringBuilder("[diag] dump\n");
            sb.AppendLine($"XR: enabled={XRSettings.enabled} deviceActive={XRSettings.isDeviceActive} eye={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} scale={XRSettings.eyeTextureResolutionScale} useVisMesh={XRSRPSettings.useVisibilityMesh} srpXrEnabled={XRSRPSettings.enabled}");
            var d = XRBootstrap.Display;
            sb.AppendLine($"session: state={XRBootstrap.SessionState} visible={XRBootstrap.Visible} userFlat={_manager.UserFlat}");
            if (d != null) sb.AppendLine($"display: running={d.running} passes={d.GetRenderPassCount()} mirrorMode={d.GetPreferredMirrorBlitMode()} scale={d.scaleOfAllRenderTargets}");
            sb.AppendLine($"Screen {Screen.width}x{Screen.height} focus={Application.isFocused} fog={RenderSettings.fog} timeScale={Time.timeScale} fps={(1f / Mathf.Max(1e-4f, Time.smoothDeltaTime)):F0}");
            foreach (var c in Camera.allCameras)
            {
                var u = c.GetComponent<UniversalAdditionalCameraData>();
                sb.AppendLine($"cam '{c.name}' depth={c.depth} mask=0x{c.cullingMask:X8} clear={c.clearFlags} rt={(c.targetTexture ? c.targetTexture.name : "-")} type={(u ? u.renderType.ToString() : "?")} stack={(u != null && u.renderType == CameraRenderType.Base && u.cameraStack != null ? u.cameraStack.Count : 0)} xr={(u ? u.allowXRRendering : false)} post={(u ? u.renderPostProcessing : false)} stereo={c.stereoEnabled} pos={c.transform.position} rot={c.transform.eulerAngles}");
            }
            sb.AppendLine($"shim: active={VRShim.Active} mouse={VRShim.MousePosition} hasRay={VRShim.HasPointerRay} ray={VRShim.PointerRay.origin}->{VRShim.PointerRay.direction} eye={VRShim.EyePosition} origin={VRShim.ScreenOrigin} n={VRShim.ScreenNormal} size={VRShim.ScreenWidth}x{VRShim.ScreenHeight}");
            sb.AppendLine(_manager.DebugState());
            var es = EventSystem.current;
            if (es != null)
            {
                sb.AppendLine($"eventsystem: overUI={es.IsPointerOverGameObject()} selected={(es.currentSelectedGameObject ? es.currentSelectedGameObject.name : "-")}");
                var ped = new PointerEventData(es) { position = (Vector2)VRShim.MousePosition };
                var hits = new List<RaycastResult>();
                es.RaycastAll(ped, hits);
                var names = new List<string>();
                for (int i = 0; i < hits.Count && i < 6; i++) names.Add($"{hits[i].gameObject.name}@{hits[i].module.GetType().Name}/{hits[i].sortingOrder}");
                sb.AppendLine($"raycast at {ped.position}: " + string.Join(" | ", names));
            }
            sb.AppendLine($"grab: active={_manager.VrInput.Grab.Active} touching={_manager.VrInput.Grab.Touching} holding={_manager.VrInput.Grab.Holding} tip={_manager.VrInput.Grab.Tip}");
            sb.AppendLine($"game: mouseLocked={(TimR.InputStuff.InputManager._ != null && TimR.InputStuff.InputManager._.MouseLocked)} placing={Game.Placing} plane={(Game.TryGetPlacementPlane(out float h) ? h.ToString("F2") : "-")} mode={(TimR.MainMenu.GameModeManager._ != null ? TimR.MainMenu.GameModeManager._.Mode.Value.ToString() : "?")}");
            sb.AppendLine(Game.DebugDeco());
            var marbles = TimR.Marbles.MarbleManager._ != null ? TimR.Marbles.MarbleManager._.GetAllCurrentMarbles() : new TimR.Marbles.Marble[0];
            for (int i = Math.Max(0, marbles.Length - 3); i < marbles.Length; i++)
                if (marbles[i] != null) sb.AppendLine($"marble[{i}]: pos={marbles[i].Position} vel={marbles[i].Rigidbody.linearVelocity} kinematic={marbles[i].Rigidbody.isKinematic}");
            sb.AppendLine($"marbles: count={(TimR.Marbles.MarbleManager._ != null ? TimR.Marbles.MarbleManager._.CurrentNumberOfMarbles : -1)} mode={_manager.VrInput.Grab.MarbleActive} throw={Plugin.Cfg.ThrowLevel.Value} settingsOpen={_manager.Settings.Open} handle={_manager.Settings.DebugHandlePoint()} settings: {_manager.Settings.DebugState()}");
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
