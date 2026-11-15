using System;
using System.Collections.Generic;
using System.Reflection;
using TimR.Cam;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace CozyMarblesVR
{
    /// <summary>
    /// Owns the VR rig: play-space origin that follows the game's camera, the stereo eye camera,
    /// the HUD overlay camera, laser visuals and locomotion.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    public class VRManager : MonoBehaviour
    {
        public static VRManager Instance { get; private set; }

        readonly Tracking _tracking = new Tracking();
        Transform _rig;
        HudPanel _hud;
        VRInput _input;

        Camera _gameCam;        // the game's main camera: still driven by Cinemachine, no longer rendering
        Camera _vrCam;          // renders both eyes
        Camera _hudCam;         // draws panel + laser on top of the world
        AudioListener _gameListener, _vrListener;

        LineRenderer _laser;
        Transform _reticle, _tipBall, _handL, _handR;
        MarbleLauncher _launcher;
        static readonly Color MarbleBallColor = new Color(0.35f, 0.65f, 1f, 0.95f); // marble mode: the ball turns blue
        bool _lastGrabMode;
        float _grabPulse;
        Material _laserMat, _reticleMat, _handMat, _ballMat;

        bool _active;
        float _scale = 8f;
        Vector3 _anchor;        // head position (tracking space) that coincides with the game camera
        float _yawOffset;
        float _heldYaw;
        bool _needRecenter = true;
        bool _recenterHud;
        bool _wasWorn, _recenterPending;
        float _wornSince;

        // locomotion
        int _locomotionFrame = -10;
        bool _turnLatch;
        bool _grabP, _grabO;
        Vector3 _grabPrevP, _grabPrevO;
        float _savedScale = -1f;

        public bool LocomotionActive => Time.frameCount - _locomotionFrame <= 1;

        // development helpers (see Diagnostics)
        Diagnostics _diag;
        bool _handModels;
        VRSettingsPanel _settings;
        VRHelpPanel _help;
        public HudPanel Hud => _hud;
        public VRInput VrInput => _input;
        public MarbleLauncher Launcher => _launcher;
        public VRSettingsPanel Settings => _settings;
        public VRHelpPanel Help => _help;
        public Tracking Tracked => _tracking;
        public Vector3 DebugRigTilt;

        void Awake()
        {
            Instance = this;
            _scale = Mathf.Clamp(Plugin.Cfg.WorldScale.Value, 0.25f, 100f);

            var rigGo = new GameObject("CozyVR Rig");
            DontDestroyOnLoad(rigGo);
            _rig = rigGo.transform;
            _hud = new HudPanel(_rig);
            _settings = new VRSettingsPanel(_rig, _hud);
            _help = new VRHelpPanel(_rig);
            _input = new VRInput(_tracking, _hud, _rig, this);
            _tracking.Rig = _rig;
            if (Plugin.Cfg.Diagnostics.Value) _diag = new Diagnostics(this);
            CreateVisuals();
            Application.onBeforeRender += OnBeforeRender;
            Plugin.Cfg.WorldScale.SettingChanged += (s, e) => _scale = Mathf.Clamp(Plugin.Cfg.WorldScale.Value, 0.25f, 100f);
        }

        void OnDestroy()
        {
            Application.onBeforeRender -= OnBeforeRender;
            Deactivate();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit()
        {
            Deactivate();
            XRBootstrap.Shutdown();
        }

        // ------------------------------------------------------------------ visuals

        static Material ColorMaterial(Color c)
        {
            var m = new Material(Shader.Find("Sprites/Default")) { color = c };
            m.renderQueue = (int)RenderQueue.Transparent + 10;
            return m;
        }

        Transform Primitive(string name, PrimitiveType type, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.layer = HudPanel.HudLayer;
            go.transform.SetParent(_rig, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        void CreateVisuals()
        {
            _laserMat = ColorMaterial(new Color(0.35f, 0.85f, 1f, 0.85f));
            _reticleMat = ColorMaterial(new Color(1f, 1f, 1f, 0.95f));
            _handMat = ColorMaterial(new Color(0.9f, 0.9f, 0.95f, 0.7f));

            var lg = new GameObject("CozyVR Laser") { layer = HudPanel.HudLayer };
            lg.transform.SetParent(_rig, false);
            _laser = lg.AddComponent<LineRenderer>();
            _laser.sharedMaterial = _laserMat;
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.numCapVertices = 2;
            _laser.shadowCastingMode = ShadowCastingMode.Off;
            _laser.receiveShadows = false;
            _laser.alignment = LineAlignment.View;
            _laser.enabled = false;

            _reticle = Primitive("CozyVR Reticle", PrimitiveType.Sphere, _reticleMat);
            _ballMat = ColorMaterial(new Color(1f, 1f, 1f, 0.9f));
            _tipBall = Primitive("CozyVR Grab Ball", PrimitiveType.Sphere, _ballMat);
            _launcher = new MarbleLauncher(_rig);
            _handL = Primitive("CozyVR Hand L", PrimitiveType.Cube, _handMat);
            _handR = Primitive("CozyVR Hand R", PrimitiveType.Cube, _handMat);
            _handL.localScale = _handR.localScale = new Vector3(0.03f, 0.03f, 0.09f);
            if (Plugin.Cfg.ControllerModels.Value)
            {
                bool l = ControllerModels.TryApply(_handL, left: true);
                bool r = ControllerModels.TryApply(_handR, left: false);
                if (l && r) _handModels = true;
            }
        }

        // ------------------------------------------------------------------ activation

        Camera FindGameCamera()
        {
            // Set by the game once its main scene is initialised; earlier cameras (splash screen) are left alone.
            Camera c = CamMovementManager.MainCamera;
            return c != null && c != _vrCam ? c : null;
        }

        static int RendererIndex(UniversalAdditionalCameraData data)
        {
            var f = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            return f != null ? (int)f.GetValue(data) : -1;
        }

        void Activate(Camera gameCam)
        {
            _gameCam = gameCam;
            var src = gameCam.GetUniversalAdditionalCameraData();
            Camera uiCam = CamMovementManager.UiCamera;
            if (uiCam == null)
            {
                var uiGo = GameObject.FindWithTag("UICamera");
                if (uiGo != null) uiCam = uiGo.GetComponent<Camera>();
            }

            // --- eye camera
            var headGo = new GameObject("CozyVR Head");
            headGo.transform.SetParent(_rig, false);
            _vrCam = headGo.AddComponent<Camera>();
            _vrCam.CopyFrom(gameCam);
            headGo.layer = 0;
            headGo.transform.localPosition = _tracking.HeadPos;
            headGo.transform.localRotation = _tracking.HeadRot;
            _vrCam.targetTexture = null;
            _vrCam.cullingMask = gameCam.cullingMask & ~(1 << HudPanel.HudLayer);
            _vrCam.rect = new Rect(0f, 0f, 1f, 1f);
            headGo.tag = "MainCamera";

            var dst = _vrCam.GetUniversalAdditionalCameraData();
            dst.renderType = CameraRenderType.Base;
            dst.SetRenderer(RendererIndex(src));
            dst.renderPostProcessing = src.renderPostProcessing && Plugin.Cfg.PostProcessing.Value;
            dst.antialiasing = src.antialiasing;
            dst.antialiasingQuality = src.antialiasingQuality;
            dst.volumeLayerMask = src.volumeLayerMask;
            dst.volumeTrigger = headGo.transform;
            dst.renderShadows = src.renderShadows;
            dst.requiresColorOption = src.requiresColorOption;
            dst.requiresDepthOption = src.requiresDepthOption;
            dst.stopNaN = src.stopNaN;
            dst.dithering = src.dithering;
            dst.allowXRRendering = true;

            // --- overlay camera for panel, laser and hands (always on top, not post-processed)
            var hudGo = new GameObject("CozyVR HUD Camera");
            hudGo.transform.SetParent(headGo.transform, false);
            _hudCam = hudGo.AddComponent<Camera>();
            _hudCam.cullingMask = 1 << HudPanel.HudLayer;
            _hudCam.clearFlags = CameraClearFlags.Depth;
            _hudCam.nearClipPlane = _vrCam.nearClipPlane;
            _hudCam.farClipPlane = _vrCam.farClipPlane;
            _hudCam.allowHDR = _vrCam.allowHDR;
            _hudCam.allowMSAA = _vrCam.allowMSAA;
            var hudData = _hudCam.GetUniversalAdditionalCameraData();
            hudData.renderType = CameraRenderType.Overlay;
            hudData.renderPostProcessing = false;
            hudData.renderShadows = false;
            hudData.allowXRRendering = true;
            hudData.SetRenderer(RendererIndex(src)); // must be the base camera's renderer, otherwise the overlay is dropped in XR
            dst.cameraStack.Add(_hudCam);

            // --- desktop window: a spectator view instead of the (black) XR mirror
            if (Plugin.Cfg.DesktopView.Value)
            {
                var deskGo = new GameObject("CozyVR Desktop View");
                DontDestroyOnLoad(deskGo);
                _deskCam = deskGo.AddComponent<Camera>();
                _deskCam.CopyFrom(_vrCam);
                _deskCam.targetTexture = null;
                _deskCam.stereoTargetEye = StereoTargetEyeMask.None;
                _deskCam.depth = 10f;
                _deskCam.rect = new Rect(0f, 0f, 1f, 1f);
                _deskCam.cullingMask = _vrCam.cullingMask | (1 << HudPanel.HudLayer);
                _deskCam.allowMSAA = false;
                var deskData = _deskCam.GetUniversalAdditionalCameraData();
                deskData.renderType = CameraRenderType.Base;
                deskData.allowXRRendering = false;
                deskData.SetRenderer(RendererIndex(src));
                deskData.renderPostProcessing = dst.renderPostProcessing;
                deskData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                deskData.volumeLayerMask = src.volumeLayerMask;
                deskData.volumeTrigger = deskGo.transform;
                deskData.requiresColorOption = src.requiresColorOption;
                deskData.requiresDepthOption = src.requiresDepthOption;
                deskGo.transform.SetPositionAndRotation(headGo.transform.position, headGo.transform.rotation);
            }

            // --- UI into the panel texture
            _hud.Attach(uiCam, gameCam);

            // --- hand over from the flat camera
            _gameListener = gameCam.GetComponent<AudioListener>();
            if (_gameListener != null) _gameListener.enabled = false;
            _vrListener = headGo.AddComponent<AudioListener>();
            gameCam.enabled = false;

            VRShim.VRCamera = _vrCam;
            VRShim.GameCamera = gameCam;
            VRShim.PassThroughDesktopInput = Plugin.Cfg.DesktopInput.Value && !Plugin.Cfg.SimulateControllers.Value;
            VRShim.Active = true;
            _active = true;
            _needRecenter = true;

            float rs = Plugin.Cfg.RenderScale.Value;
            if (Mathf.Abs(rs - 1f) > 0.01f) XRSettings.eyeTextureResolutionScale = rs;

            var display = XRBootstrap.Display;
            Plugin.Log.LogInfo($"VR active. Eye texture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, refresh {(display != null && display.TryGetDisplayRefreshRate(out float hz) ? hz.ToString("F0") : "?")} Hz, device '{XRSettings.loadedDeviceName}', world scale {_scale:F1}.");
        }

        void Deactivate()
        {
            _notVisibleSince = -1f;
            if (!_active) return;
            _active = false;
            if (_deskCam != null) Destroy(_deskCam.gameObject);
            _deskCam = null;
            _settings.Close();
            _help.Close();
            VRShim.Active = false;
            _input.RemoveDevices();
            _hud.Detach();
            Game.RestoreFlatBehaviour();
            if (_vrCam != null) Destroy(_vrCam.gameObject);
            _vrCam = null;
            _hudCam = null;
            if (_gameCam != null)
            {
                _gameCam.enabled = true;
                if (_gameListener != null) _gameListener.enabled = true;
            }
            _gameCam = null;
            if (_laser != null) _laser.enabled = false;
            _launcher?.Hide();
            foreach (var t in new[] { _reticle, _tipBall, _handL, _handR })
                if (t != null) t.gameObject.SetActive(false);
            Plugin.Log.LogInfo("VR inactive, back to the monitor.");
        }

        // ------------------------------------------------------------------ frame loop

        // ------------------------------------------------------------------ VR <-> monitor

        bool _userFlat, _autoFlat, _everVisible;
        float _notVisibleSince = -1f, _hiddenSessionSince = -1f;
        public bool UserFlat => _userFlat;
        readonly ModeToggleButton _modeButton = new ModeToggleButton();

        public enum ModeState { VR, VRNotShown, Connecting, Monitor, NotShown }

        public ModeState Mode
        {
            get
            {
                if (_active) return XRBootstrap.Visible ? ModeState.VR : ModeState.VRNotShown;
                if (_userFlat) return _autoFlat ? ModeState.NotShown : ModeState.Monitor;
                return ModeState.Connecting;
            }
        }

        /// <summary>The button next to the version number.</summary>
/// <summary>Opens (or closes) the VR settings next to the main menu panel, e.g. from the game's main menu.</summary>
        public void ToggleSettingsAtMainMenu()
        {
            if (!_active) return;
            if (_settings.Open) { _settings.Close(); return; }
            // the main menu panel is wide: open right in front of the viewer, a little to the right of the menu buttons
            Vector3 head = _tracking.HeadPos;
            Vector3 f = _tracking.HeadRot * Vector3.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            _settings.ShowAt(head + f * 1.1f + right * 0.25f - Vector3.up * 0.1f, head);
        }

                public void ToggleMode()
        {
            if (_userFlat) SetFlat(false);
            else SetFlat(true);
        }

        void FallBackToMonitor(string why)
        {
            Plugin.Log.LogInfo(why);
            SetFlat(true);
            _autoFlat = true;
        }

        public bool EverVisible => _everVisible;
        bool _waitingForStart;
        public bool WaitingForStart => _waitingForStart;

        public void Init(bool startFlat, bool waitForStartWindow)
        {
            _userFlat = startFlat;
            _waitingForStart = waitForStartWindow;
        }

        /// <summary>The start window was confirmed.</summary>
        public void StartChosen(bool vr)
        {
            _waitingForStart = false;
            _userFlat = !vr;
            if (vr && !XRBootstrap.Connected && !XRBootstrap.TryStartLoader())
            {
                Plugin.Log.LogWarning("OpenXR could not be started (is SteamVR running and the headset connected?). Retrying every 20 s.");
                _connectAttempts = 0;
                _nextConnectAttempt = Time.unscaledTime + 20f;
            }
        }

        /// <summary>F9 / VR settings: play on the monitor (headset released) or go back to VR.</summary>
        public void SetFlat(bool flat)
        {
            if (flat == _userFlat) return;
            _userFlat = flat;
            _autoFlat = false;
            _everVisible = false;
            _hiddenSessionSince = -1f;
            if (flat)
            {
                Deactivate();
                XRBootstrap.Suspend();
                Plugin.Log.LogInfo("Switched to the monitor (F9 switches back to VR).");
            }
            else
            {
                _connectAttempts = 0;
                XRBootstrap.Resume();
                Plugin.Log.LogInfo("Switching to VR...");
            }
        }

        /// <summary>
        /// While the game runs on the monitor its own cameras must not render into the (running but hidden) XR
        /// session, otherwise the desktop window stays black - e.g. inside SteamVR's desktop theater.
        /// </summary>
        void KeepFlatCamerasOutOfXr()
        {
            foreach (var cam in new[] { CamMovementManager.MainCamera, CamMovementManager.UiCamera })
            {
                if (cam == null) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null && data.allowXRRendering) data.allowXRRendering = false;
            }
        }

        /// <summary>The waiting marble of marble mode moves with the physics step (so it can push other marbles).</summary>
        void FixedUpdate()
        {
            if (_active) _launcher?.FixedTick();
        }

        void Update()
        {
            _diag?.Tick();
            _modeButton.Tick(this);
            if (_waitingForStart)
            {
                KeepFlatCamerasOutOfXr();
                return;
            }
            if (Input.GetKeyDown(KeyCode.F9)) ToggleMode();

            bool running = XRBootstrap.Running;
            bool visible = running && XRBootstrap.Visible;
            if (visible) _everVisible = true;
            // Render VR as soon as the session runs: SteamVR only shows an app (session VISIBLE) after it has
            // submitted frames, so waiting for VISIBLE first would wait forever. Never give up automatically -
            // the headset may be connected minutes later. The mode button / F9 switch to the monitor.
            if (_active && (!running || _gameCam == null)) Deactivate();
            if (!_active)
            {
                KeepFlatCamerasOutOfXr();
                if (_userFlat) return;
                if (!running)
                {
                    RetryConnect();
                    return;
                }
                Camera cam = FindGameCamera();
                if (cam == null) return;
                _tracking.UpdateHead();
                Activate(cam);
            }

            // The game caps the frame rate in its menus; the headset compositor sets the pace in VR.
            if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (_gameCam.enabled) _gameCam.enabled = false;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Game.Refresh();
            SuppressDepthOfField();
            _tracking.UpdateHead();
            _tracking.UpdateHands();

            // Centre the player on the game's camera whenever the headset is put on (and once at start).
            if (_tracking.HeadWorn)
            {
                if (!_wasWorn) _wornSince = Time.unscaledTime;
                if ((_needRecenter || !_wasWorn) && !_recenterPending) _recenterPending = true;
                if (_recenterPending && Time.unscaledTime - _wornSince > 0.4f)
                {
                    _recenterPending = false;
                    _needRecenter = false;
                    Recenter();
                }
            }
            _wasWorn = _tracking.HeadWorn;

            UpdateRig();
            ApplyHeadPose();
            _hud.Tick(_tracking, _input.OffHandState, Game.InMenu);
            UpdateVirtualScreen();
            _hud.PlaceOverlay();
            _input.Process(dt);
            _settings.Refresh();
            UpdateVisuals();
        }

        // ------------------------------------------------------------------ virtual screen

        Quaternion _vsRot = Quaternion.identity;   // tracking space
        bool _vsInit;

        /// <summary>
        /// The game thinks in screen pixels. In VR its "screen" is an imaginary 100° window in front of the eyes,
        /// independent of where the UI panel floats. It stays put while you look around a little (so a held click
        /// does not wander) and re-aims when you turn your head or point well outside it, never while a button is held.
        /// </summary>
        void UpdateVirtualScreen()
        {
            Vector3 headFwd = _tracking.HeadRot * Vector3.forward;
            if (!_vsInit || _recenterHud) { _vsRot = _tracking.HeadRot; _vsInit = true; _recenterHud = false; }
            else if (!_input.AnyButtonHeld)
            {
                Vector3 n = _vsRot * Vector3.forward;
                bool reaim = Vector3.Angle(headFwd, n) > 35f;
                if (!reaim && _input.PointerValid && !_input.OverUi)
                {
                    Vector3 toAim = _rig.InverseTransformPoint(_input.ReticleWorld) - _tracking.HeadPos;
                    reaim = toAim.sqrMagnitude > 1e-6f && Vector3.Angle(toAim, n) > 42f;
                }
                if (reaim) _vsRot = _tracking.HeadRot;
            }

            float w = _hud.Width, h = _hud.Height;
            float halfW = Mathf.Tan(50f * Mathf.Deg2Rad), halfH = halfW * h / Mathf.Max(1f, w);
            Quaternion rot = _rig.rotation * _vsRot;
            Vector3 eye = _vrCam.transform.position;
            Vector3 normal = rot * Vector3.forward, right = rot * Vector3.right, up = rot * Vector3.up;
            VRShim.EyePosition = eye;
            VRShim.ScreenNormal = normal;
            VRShim.ScreenRight = right * (2f * halfW / w);
            VRShim.ScreenUp = up * (2f * halfH / h);
            VRShim.ScreenOrigin = eye + normal - right * halfW - up * halfH;
            VRShim.ScreenWidth = w;
            VRShim.ScreenHeight = h;
        }

        float _nextConnectAttempt = 20f;
        int _connectAttempts;

        /// <summary>SteamVR was not up when the game started: look for it again every 20 s for about ten minutes.</summary>
        void RetryConnect()
        {
            if (XRBootstrap.Connected || _connectAttempts >= 30 || Time.unscaledTime < _nextConnectAttempt) return;
            _nextConnectAttempt = Time.unscaledTime + 20f;
            _connectAttempts++;
            if (XRBootstrap.TryStartLoader())
                Plugin.Log.LogInfo("Headset runtime found, switching to VR.");
            else if (_connectAttempts == 30)
                Plugin.Log.LogInfo("No headset runtime found, staying on the monitor for this session.");
        }

        float _nextDofCheck;

        /// <summary>The game blurs everything outside its focus distance (tilt-shift look); with two eyes that is just a blurry image.</summary>
        void SuppressDepthOfField()
        {
            if (Plugin.Cfg.DepthOfField.Value || Time.unscaledTime < _nextDofCheck) return;
            _nextDofCheck = Time.unscaledTime + 0.5f;
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                VolumeProfile profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                if (profile != null && profile.TryGet(out DepthOfField dof) && dof.active)
                    dof.active = false;
            }
        }

        /// <summary>Late pose update right before rendering: follows the camera Cinemachine just moved and the newest head pose.</summary>
        void OnBeforeRender()
        {
            if (!_active || _vrCam == null) return;
            _tracking.UpdateHead();
            UpdateRig();
            ApplyHeadPose();
            UpdateVisuals();
        }

        void ApplyHeadPose()
        {
            _vrCam.transform.localPosition = _tracking.HeadPos;
            _vrCam.transform.localRotation = _tracking.HeadRot;
            UpdateDesktopView();
        }

        Camera _deskCam;
        float _deskLast;

        /// <summary>Follows the head with a little smoothing and without roll, so the desktop picture is calm.</summary>
        void UpdateDesktopView()
        {
            if (_deskCam == null) return;
            float now = Time.unscaledTime, dt = Mathf.Clamp(now - _deskLast, 0f, 0.1f);
            _deskLast = now;
            Transform head = _vrCam.transform, t = _deskCam.transform;
            Vector3 f = head.forward;
            Quaternion target = Quaternion.LookRotation(f, Vector3.up);
            t.SetPositionAndRotation(head.position, Quaternion.Slerp(t.rotation, target, 1f - Mathf.Exp(-dt * 10f)));
            float aspect = Mathf.Max(0.1f, _deskCam.aspect);
            _deskCam.fieldOfView = aspect >= 1f ? 65f : Camera.HorizontalToVerticalFieldOfView(75f, aspect);
            _deskCam.nearClipPlane = _vrCam.nearClipPlane;
        }

        /// <summary>Places the play space so that the anchored head position sits at the game's camera.</summary>
        void UpdateRig()
        {
            if (_gameCam == null) return;
            Transform cam = _gameCam.transform;
            float yaw = cam.eulerAngles.y;
            if (Plugin.Cfg.FollowCameraYaw.Value || LocomotionActive) _heldYaw = yaw;
            else yaw = _heldYaw;

            Quaternion rot = Quaternion.Euler(DebugRigTilt.x, yaw + _yawOffset + DebugRigTilt.y, 0f);
            _rig.localScale = Vector3.one * _scale;
            _rig.SetPositionAndRotation(cam.position - rot * (_anchor * _scale), rot);
        }

        /// <summary>Makes the current head position and viewing direction the new "centre".</summary>
        public void Recenter()
        {
            _anchor = _tracking.HeadPos;
            Vector3 f = _tracking.HeadRot * Vector3.forward;
            _yawOffset = -Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            _recenterHud = true;
            _hud.SetFrame(_tracking.HeadPos, _tracking.HeadRot);
            UpdateRig();
        }

        void UpdateVisuals()
        {
            bool show = _active && _input.PointerValid;
            bool grabMode = show && _input.Grab.Active && !_input.NavActive;
            bool grab = grabMode && !_input.OverUi;
            bool marbles = grabMode && _input.Grab.MarbleActive;
            bool showLaser = show && !_input.NavActive && !grab;
            if (_laser.enabled != showLaser) _laser.enabled = showLaser;
            if (_reticle.gameObject.activeSelf != show) _reticle.gameObject.SetActive(show);

            // switching between pointer and hand mode: the ball pops up (or vanishes) right away, with a short pulse
            bool grabNow = _input.Grab.Active;
            if (grabNow != _lastGrabMode) { _lastGrabMode = grabNow; _grabPulse = grabNow ? 1f : 0f; }
            _grabPulse = Mathf.Max(0f, _grabPulse - Time.unscaledDeltaTime / 0.35f);
            float pulse = 1f + 0.9f * Mathf.Sin(_grabPulse * Mathf.PI) * _grabPulse;
            float ballSize = 0.014f * _scale * pulse;

            // hand mode while the laser is on a menu: the laser works the menu, the ball still sits on the controller
            bool tipBall = grabMode && _input.OverUi;
            if (_tipBall.gameObject.activeSelf != tipBall) _tipBall.gameObject.SetActive(tipBall);
            if (tipBall)
            {
                _tipBall.position = _input.Grab.Tip;
                _tipBall.localScale = Vector3.one * (ballSize / _scale);
            }
            if (show)
            {
                Vector3 a = _input.AimOriginWorld, b = _input.ReticleWorld;
                float w = 0.0025f * _scale;
                _laser.startWidth = w;
                _laser.endWidth = w * 0.6f;
                _laser.SetPosition(0, a);
                _laser.SetPosition(1, b);
                float dist = Vector3.Distance(_vrCam.transform.position, b);
                _reticle.position = b;
                float size = grab ? ballSize :Mathf.Max(0.004f * _scale, dist * (_input.OverUi ? 0.009f : 0.012f));
                _reticle.localScale = Vector3.one * (size / _scale);
                Color rc = _input.OverUi ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(1f, 1f, 1f, 0.95f);
                if (grab) rc = _input.Grab.Holding ? new Color(1f, 0.55f, 0.1f, 1f) : _input.Grab.Touching ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(1f, 1f, 1f, 0.9f);
                if (grab && marbles) rc = MarbleBallColor;
                _reticleMat.color = rc;
            }
            _ballMat.color = marbles ? MarbleBallColor : new Color(1f, 1f, 1f, 0.9f);

            // marble mode: the next marble waits at the tip (with direction cone and speed dial) instead of the ball
            bool launcher = marbles && !Game.InMenu && !Game.Placing;
            Vector3 aim = _input.Grab.TipDir.sqrMagnitude > 1e-6f ? _input.Grab.TipDir.normalized : _rig.forward;
            bool rightPointer = Plugin.Cfg.PointerHand.Value == Hand.Right;
            var pointerHand = rightPointer ? _tracking.Right : _tracking.Left;
            _launcher.Tick(launcher, _input.Grab.Tip, aim, _vrCam.transform.position, rightPointer ? _handR : _handL, _rig.rotation * pointerHand.GripRot);
            if (_launcher.Radius > 0f)
            {
                if (grab && _reticle.gameObject.activeSelf) _reticle.gameObject.SetActive(false);
                if (_tipBall.gameObject.activeSelf) _tipBall.gameObject.SetActive(false);
            }
            SetHand(_handL, _tracking.Left);
            SetHand(_handR, _tracking.Right);
            _input.Grab.UpdateGhost();
        }

        void SetHand(Transform t, HandState s)
        {
            // hidden while (nearly) inside the head, e.g. a controller that reports a pose without being tracked yet
            bool show = _active && s.Valid && (s.GripPos - _tracking.HeadPos).sqrMagnitude > 0.1f * 0.1f;
            if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
            if (!show) return;
            if (_handModels)
            {
                ControllerModels.ModelOffset(ReferenceEquals(t, _handL), out Vector3 op, out Quaternion orr);
                t.localPosition = s.GripPos + s.GripRot * op;
                t.localRotation = s.GripRot * orr;
                return;
            }
            t.localPosition = s.GripPos;
            t.localRotation = s.GripRot;
        }

        // ------------------------------------------------------------------ development helpers

        public void DebugHudMode(string mode)
        {
            if (_vrCam == null || _hudCam == null) return;
            var hudData = _hudCam.GetUniversalAdditionalCameraData();
            int baseIndex = RendererIndex(_vrCam.GetUniversalAdditionalCameraData());
            int uiIndex = _hud.UiCamera != null ? RendererIndex(_hud.UiCamera.GetUniversalAdditionalCameraData()) : -1;
            switch (mode)
            {
                case "base":
                    _hudCam.enabled = false;
                    _vrCam.cullingMask |= 1 << HudPanel.HudLayer;
                    break;
                case "overlay":
                    _hudCam.enabled = true;
                    _vrCam.cullingMask &= ~(1 << HudPanel.HudLayer);
                    hudData.SetRenderer(uiIndex);
                    break;
                case "overlay0":
                    _hudCam.enabled = true;
                    _vrCam.cullingMask &= ~(1 << HudPanel.HudLayer);
                    hudData.SetRenderer(baseIndex);
                    break;
                case "nopost":
                    _vrCam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                    break;
                case "post":
                    _vrCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                    break;
            }
            Plugin.Log.LogInfo($"[diag] hud mode {mode}: baseRenderer={baseIndex} uiRenderer={uiIndex} hudRenderer={RendererIndex(hudData)} hudCamEnabled={_hudCam.enabled} vrMask=0x{_vrCam.cullingMask:X8}");
        }

        public Vector3 DebugPanelPoint(float x, float y) => _hud.PixelToWorld(new Vector2(x, y));

        /// <summary>World position of a point on the mod's own widgets (diagnostics): bar, corner, vr; "b" prefix = build panel.</summary>
        public Vector3 DebugWidgetPoint(string name) => _hud.DebugWidgetPoint(name);

        public string DebugState()
        {
            return $"rig: pos={_rig.position} rot={_rig.eulerAngles} scale={_scale:F2} anchor={_anchor} yawOffset={_yawOffset:F1} head(local)={_tracking.HeadPos}/{_tracking.HeadRot.eulerAngles} headValid={_tracking.HeadValid} sim={_tracking.Simulated}\n" +
                   $"hands: L valid={_tracking.Left.Valid} pos={_tracking.Left.GripPos} | R valid={_tracking.Right.Valid} pos={_tracking.Right.GripPos} aimRot={_tracking.Right.AimRot.eulerAngles} trig={_tracking.Right.Trigger:F2}\n" +
                   $"handR obj: active={_handR.gameObject.activeSelf} model={_handModels} pos={_handR.position} scale={_handR.lossyScale} shader={_handR.GetComponent<MeshRenderer>().sharedMaterial.shader.name} bounds={_handR.GetComponent<MeshRenderer>().bounds.size} layer={_handR.gameObject.layer}\n" +
                   _launcher.DebugState() + "\n" +
                   $"ball: active={_tipBall.gameObject.activeInHierarchy} pos={_tipBall.position} scale={_tipBall.lossyScale}\n" +
                   $"input: pointerValid={_input.PointerValid} overUi={_input.OverUi} reticle={_input.ReticleWorld} origin={_input.AimOriginWorld} locomotion={LocomotionActive} gameCam={(_gameCam ? _gameCam.transform.position.ToString() : "-")}\n" +
                   _hud.DebugState() + "\n" +
                   $"menu: context={_input.MenuContext} nav={_input.NavActive} frameL={_tracking.Left.IsFrame} frameR={_tracking.Right.IsFrame} | {_input.DescribeUiClickControls()}";
        }

        /// <summary>Renders what the headset roughly sees (mono, from the head position) into a PNG.</summary>
        /// <summary>Test helper: puts the simulated right hand 'above' metres (world units) above a world point and aims at it.</summary>
        public void DebugHandAbove(Vector3 world, float above)
        {
            Vector3 baseLocal = _tracking.HeadPos + _tracking.HeadRot * new Vector3(0.18f, -0.2f, 0.25f);
            SimOverride.Enabled = true;
            SimOverride.RightHandOffset = _rig.InverseTransformPoint(world + Vector3.up * above) - baseLocal;
            SimOverride.HasAimWorld = true;
            SimOverride.AimWorld = world;
        }

        public void DebugHandShot(string path, string which, float yaw, float pitch)
        {
            // b = ball at the controller tip, m = newest marble (whole scene), l / r = hands
            Vector3 center;
            bool scene = which == "m" || which == "b";
            if (which == "b") center = _input.Grab.Tip;
            else if (scene)
            {
                var all = TimR.Marbles.MarbleManager._ != null ? TimR.Marbles.MarbleManager._.GetAllCurrentMarbles() : null;
                if (all == null || all.Length == 0) return;
                center = all[all.Length - 1].Position;
            }
            else
            {
                var h = which == "l" ? _handL : _handR;
                if (h == null || !h.gameObject.activeSelf) return;
                center = h.position;
            }
            var rt = new RenderTexture(900, 900, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("CozyVR Hand Capture");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.3f, 0.4f, 1f);
            cam.cullingMask = scene ? ~0 : 1 << HudPanel.HudLayer;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.01f;
            cam.targetTexture = rt;
            Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
            go.transform.SetPositionAndRotation(center - look * Vector3.forward * (0.35f * _scale), look);
            var data = cam.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            if (_vrCam != null) data.SetRenderer(RendererIndex(_vrCam.GetUniversalAdditionalCameraData()));
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
            Diagnostics.SaveRenderTexture(rt, path);
            cam.targetTexture = null;
            Destroy(go);
            rt.Release();
            Destroy(rt);
        }

        public void DebugCaptureEye(string path)
        {
            if (_vrCam == null) return;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            var go = new GameObject("CozyVR Debug Capture");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(_vrCam);
            go.transform.SetPositionAndRotation(_vrCam.transform.position, _vrCam.transform.rotation);
            go.tag = "Untagged";
            cam.fieldOfView = 75f;
            cam.cullingMask = _vrCam.cullingMask | (1 << HudPanel.HudLayer);
            cam.targetTexture = rt;
            var data = cam.GetUniversalAdditionalCameraData();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            data.SetRenderer(RendererIndex(_vrCam.GetUniversalAdditionalCameraData()));
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();
            Diagnostics.SaveRenderTexture(rt, path);
            cam.targetTexture = null;
            Destroy(go);
            rt.Release();
            Destroy(rt);
        }

        // ------------------------------------------------------------------ locomotion

        /// <summary>
        /// Called from the game's own camera update, i.e. only while free camera movement is allowed.
        /// Moves the game's virtual camera; the play space follows it.
        /// </summary>
        public void Locomotion(CamMovementManager mgr)
        {
            if (!_active) return;
            var cfg = Plugin.Cfg;
            bool continued = LocomotionActive;
            _locomotionFrame = Time.frameCount;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            Transform cam = mgr.transform;
            Vector3 pos = cam.position;
            float yaw = cam.eulerAngles.y;
            float startYaw = yaw;
            Quaternion rigRot = _rig.rotation;

            HandState p = _input.PointerHandState, o = _input.OffHandState;
            bool grabP = _input.PointerGrab && p.Valid, grabO = _input.OffGrab && o.Valid;
            if (!continued) { _grabP = _grabO = false; }

            if (grabP && grabO && _grabP && _grabO)
            {
                // two hands: the world is pinned to both hands -> translate, rotate about the vertical axis and scale
                Vector3 midPrev = 0.5f * (_grabPrevP + _grabPrevO), midCur = 0.5f * (p.GripPos + o.GripPos);
                Vector3 vPrev = _grabPrevP - _grabPrevO, vCur = p.GripPos - o.GripPos;
                float newScale = _scale;
                if (vPrev.magnitude > 0.05f && vCur.magnitude > 0.05f)
                    newScale = Mathf.Clamp(_scale * vPrev.magnitude / vCur.magnitude, cfg.MinWorldScale.Value, cfg.MaxWorldScale.Value);
                Vector3 fPrev = new Vector3(vPrev.x, 0f, vPrev.z), fCur = new Vector3(vCur.x, 0f, vCur.z);
                float theta = (fPrev.magnitude > 0.05f && fCur.magnitude > 0.05f) ? Vector3.SignedAngle(fPrev, fCur, Vector3.up) : 0f;

                Vector3 pivotWorld = _rig.TransformPoint(midPrev);
                yaw -= theta;
                Quaternion newRigRot = rigRot * Quaternion.AngleAxis(-theta, Vector3.up);
                pos = pivotWorld - newRigRot * (newScale * (midCur - _anchor));
                _scale = newScale;
                rigRot = newRigRot;
            }
            else if ((grabP && _grabP) || (grabO && _grabO))
            {
                // one hand: drag the world along with the hand
                Vector3 delta = (grabP && _grabP) ? p.GripPos - _grabPrevP : o.GripPos - _grabPrevO;
                pos -= rigRot * (_scale * delta);
            }

            _grabP = grabP;
            _grabO = grabO;
            if (grabP) _grabPrevP = p.GripPos;
            if (grabO) _grabPrevO = o.GripPos;

            // sticks
            Vector3 headWorld = _vrCam.transform.position;
            Vector3 headFwd = _vrCam.transform.forward;
            float headYaw = Mathf.Atan2(headFwd.x, headFwd.z) * Mathf.Rad2Deg;
            Vector2 move = _input.MenuContext ? Vector2.zero : _input.OffStick; // in menus the stick moves the focus cursor
            if (move != Vector2.zero)
                pos += Quaternion.Euler(0f, headYaw, 0f) * new Vector3(move.x, 0f, move.y) * (cfg.MoveSpeed.Value * _scale * dt);

            Vector2 ps = _input.PointerStick;
            if (!_input.StickYConsumed && Mathf.Abs(ps.y) > 0.2f && Mathf.Abs(ps.y) > Mathf.Abs(ps.x))
                pos.y += ps.y * cfg.VerticalSpeed.Value * _scale * dt;

            float turn = 0f;
            if (!(grabP && grabO))
            {
                if (cfg.Turning.Value == TurnMode.Smooth)
                {
                    if (Mathf.Abs(ps.x) > 0.2f && Mathf.Abs(ps.x) >= Mathf.Abs(ps.y)) turn = ps.x * cfg.SmoothTurnSpeed.Value * dt;
                }
                else
                {
                    if (!_turnLatch && Mathf.Abs(ps.x) > 0.7f && Mathf.Abs(ps.x) > Mathf.Abs(ps.y))
                    {
                        turn = Mathf.Sign(ps.x) * cfg.SnapTurnDegrees.Value;
                        _turnLatch = true;
                    }
                    else if (Mathf.Abs(ps.x) < 0.35f) _turnLatch = false;
                }
            }
            if (turn != 0f)
            {
                // rotate about the head so the player stays in place
                pos = headWorld + Quaternion.AngleAxis(turn, Vector3.up) * (pos - headWorld);
                yaw += turn;
            }

            if (Mathf.Abs(Mathf.DeltaAngle(startYaw, yaw)) > 1e-4f)
                mgr.SetToPositionAndRotation(pos, Quaternion.Euler(cam.eulerAngles.x, yaw, 0f));
            else if (pos != cam.position)
                cam.position = pos;

            if (Mathf.Abs(_scale - _savedScale) > 0.01f && !(grabP && grabO))
            {
                // remember a scale chosen with the two-hand gesture
                if (_savedScale > 0f) Plugin.Cfg.WorldScale.Value = _scale;
                _savedScale = _scale;
            }
        }
    }
}
