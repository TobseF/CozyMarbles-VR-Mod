using System;
using TimR.Events;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CozyMarblesVR
{
    /// <summary>
    /// Turns controller state into what the game understands: a mouse position, mouse buttons,
    /// keys and shortcut events. Also decides whether the laser is on the menu panel or in the world.
    /// </summary>
    public class VRInput
    {
        enum Ctl { PTrigger, PGrip, PPrimary, PSecondary, PStick, OTrigger, OGrip, OPrimary, OSecondary, OStick, OMenu,
                   FrameX, FrameY, DpadUp, DpadDown, DpadLeft, DpadRight, PBumper, OBumper, Count }

        readonly Tracking _tracking;
        readonly HudPanel _hud;
        readonly Transform _rig;
        readonly VRManager _manager;

        Mouse _mouse;
        float _scrollUnit = 1f;
        InputSettings.BackgroundBehavior _savedBackground;
        bool _backgroundSaved;

        readonly bool[] _ctl = new bool[(int)Ctl.Count];
        readonly bool[] _now = new bool[(int)Ctl.Count];
        readonly VRAction[] _ctlAction = new VRAction[(int)Ctl.Count];
        static readonly int ActionCount = Enum.GetValues(typeof(VRAction)).Length;
        readonly bool[] _act = new bool[ActionCount];
        readonly bool[] _actPrev = new bool[ActionCount];

        // smoothed aim pose in tracking space
        Vector3 _aimPos;
        Quaternion _aimRot = Quaternion.identity;
        bool _aimInitialised;

        // click stabilisation
        bool _pressFrozen;
        Vector3 _pressPos;
        Quaternion _pressRot;

        bool _buttonLock;      // a mouse button is held; keep UI/world mode until release
        bool _lockedToUi;
        bool _stickUp, _stickDown;
        float _heightUpTimer, _heightDownTimer;

        // menu focus navigation (D-pad / off-hand stick, A = select, B = back)
        bool _menuContext, _navActive;
        UnityEngine.UI.Selectable _navTarget;
        Vector2 _navPixel, _navHeldDir, _laserPixel;
        bool _laserOnPanel;
        float _navRepeat;
        Quaternion _navAimRot = Quaternion.identity;
        int _synthClick;

        // results for the rest of the mod
        public bool PointerValid { get; private set; }
        public bool OverUi { get; private set; }
        public Vector3 AimOriginWorld { get; private set; }
        public Vector3 ReticleWorld { get; private set; }
        public Vector2 PointerStick { get; private set; }
        public Vector2 OffStick { get; private set; }
        public bool StickYConsumed { get; private set; }
        public bool NavActive => _navActive;
        public bool OverWidget { get; private set; }
        public bool AnyButtonHeld { get; private set; }
        public bool MenuContext => _menuContext;
        public HandState PointerHandState => Plugin.Cfg.PointerHand.Value == Hand.Right ? _tracking.Right : _tracking.Left;
        public HandState OffHandState => Plugin.Cfg.PointerHand.Value == Hand.Right ? _tracking.Left : _tracking.Right;
        public bool PointerGrab => _ctl[(int)Ctl.PGrip] && _ctlAction[(int)Ctl.PGrip] == VRAction.GrabWorld;
        public bool OffGrab => _ctl[(int)Ctl.OGrip] && _ctlAction[(int)Ctl.OGrip] == VRAction.GrabWorld;

        const int WorldMask = (1 << 0) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 13) | (1 << 14) | (1 << 18) | (1 << 19) | (1 << 20) | (1 << 21) | (1 << 23) | (1 << 24);

        public GrabMode Grab { get; }

        public VRInput(Tracking tracking, HudPanel hud, Transform rig, VRManager manager)
        {
            Grab = new GrabMode(rig);
            _tracking = tracking;
            _hud = hud;
            _rig = rig;
            _manager = manager;
        }

        public bool Held(VRAction a) => _act[(int)a];
        public bool Down(VRAction a) => _act[(int)a] && !_actPrev[(int)a];
        public bool Up(VRAction a) => !_act[(int)a] && _actPrev[(int)a];

        void EnsureMouse()
        {
            // With a headset on, the game window rarely has desktop focus; input must keep flowing regardless.
            if (InputSystem.settings.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus)
            {
                if (!_backgroundSaved) { _savedBackground = InputSystem.settings.backgroundBehavior; _backgroundSaved = true; }
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            }
            if (!Application.runInBackground) Application.runInBackground = true;

            if (_mouse != null && _mouse.added) return;
            _mouse = InputSystem.AddDevice<Mouse>("CozyVR Pointer");
            try
            {
                // Newer Input System versions report one unit per wheel notch, older ones 120.
                var prop = typeof(InputSettings).GetProperty("scrollDeltaBehavior");
                if (prop != null && Convert.ToInt32(prop.GetValue(InputSystem.settings)) != 0) _scrollUnit = 120f;
            }
            catch { }
        }

        public void RemoveDevices()
        {
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            _mouse = null;
            RestoreXrUiBindings();
            if (_backgroundSaved) { InputSystem.settings.backgroundBehavior = _savedBackground; _backgroundSaved = false; }
            ClearShim();
        }

        static void ClearShim()
        {
            for (int i = 0; i < 3; i++) VRShim.MouseHeld[i] = VRShim.MouseDown[i] = VRShim.MouseUp[i] = false;
            Array.Clear(VRShim.KeyHeld, 0, VRShim.KeyHeld.Length);
            Array.Clear(VRShim.KeyDown, 0, VRShim.KeyDown.Length);
            Array.Clear(VRShim.KeyUp, 0, VRShim.KeyUp.Length);
            VRShim.ScrollWheel = 0f;
            VRShim.HasPointerRay = false;
        }

        // ------------------------------------------------------------------ UI module

        InputActionAsset _silencedAsset;
        float _nextUiCheck;

        /// <summary>
        /// The game's UI actions are also bound to &lt;XRController&gt;/trigger and the controller pose. With real
        /// controllers the UI module then keeps switching its pointer between "mouse" and "tracked device" and
        /// drops press/release events on every switch, so clicks get stuck. Only the mod's laser pointer may
        /// drive the UI, so those bindings are switched off while VR is active.
        /// </summary>
        void SilenceXrUiBindings()
        {
            if (Time.unscaledTime < _nextUiCheck) return;
            _nextUiCheck = Time.unscaledTime + 1f;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return;
            var module = es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            InputActionAsset asset = module != null ? module.actionsAsset : null;
            if (asset == null || asset == _silencedAsset) return;

            int count = 0;
            foreach (var map in asset.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    var bindings = action.bindings;
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        string path = bindings[i].effectivePath;
                        if (string.IsNullOrEmpty(path)) continue;
                        if (path.IndexOf("XRController", StringComparison.OrdinalIgnoreCase) >= 0
                            || path.IndexOf("XRHMD", StringComparison.OrdinalIgnoreCase) >= 0
                            || path.IndexOf("TrackedDevice", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            action.ApplyBindingOverride(i, new InputBinding { overridePath = "" });
                            count++;
                        }
                    }
                }
            }
            _silencedAsset = asset;
            Plugin.Log.LogInfo($"UI input: switched off {count} XR controller bindings of '{asset.name}' (the laser pointer drives the menus).");
        }

        void RestoreXrUiBindings()
        {
            if (_silencedAsset != null) _silencedAsset.RemoveAllBindingOverrides();
            _silencedAsset = null;
        }

        /// <summary>Controls currently feeding the UI click action (diagnostics).</summary>
        public string DescribeUiClickControls()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var module = es != null ? es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() : null;
            if (module == null) return "no UI module";
            string click = module.leftClick != null && module.leftClick.action != null ? string.Join(", ", System.Linq.Enumerable.Select(module.leftClick.action.controls, c => c.path)) : "-";
            string tracked = module.trackedDevicePosition != null && module.trackedDevicePosition.action != null ? string.Join(", ", System.Linq.Enumerable.Select(module.trackedDevicePosition.action.controls, c => c.path)) : "-";
            return $"ui click controls: [{click}] tracked position controls: [{tracked}]";
        }

        // ------------------------------------------------------------------ buttons

        static bool Pressed(bool was, float value, float threshold) => was ? value > threshold - 0.12f : value >= threshold;

        VRAction Binding(Ctl c, bool alt)
        {
            var cfg = Plugin.Cfg;
            VRAction a;
            if (_menuContext)
            {
                // In menus the D-pad moves the focus cursor, A selects the focused element and B goes back.
                switch (c)
                {
                    case Ctl.DpadUp: case Ctl.DpadDown: case Ctl.DpadLeft: case Ctl.DpadRight: return VRAction.None;
                    case Ctl.PSecondary: return VRAction.MenuBack;
                    case Ctl.PPrimary: if (_navActive) return VRAction.LeftClick; break;
                }
            }
            if (alt)
            {
                switch (c)
                {
                    case Ctl.FrameX: a = cfg.FrameXAlt.Value; break;
                    case Ctl.FrameY: a = cfg.FrameYAlt.Value; break;
                    case Ctl.PPrimary: a = cfg.PointerPrimaryAlt.Value; break;
                    case Ctl.PSecondary: a = cfg.PointerSecondaryAlt.Value; break;
                    case Ctl.PStick: a = cfg.PointerStickClickAlt.Value; break;
                    case Ctl.OPrimary: a = cfg.OffPrimaryAlt.Value; break;
                    case Ctl.OSecondary: a = cfg.OffSecondaryAlt.Value; break;
                    case Ctl.OStick: a = cfg.OffStickClickAlt.Value; break;
                    default: a = VRAction.None; break;
                }
                if (a != VRAction.None) return a;
            }
            switch (c)
            {
                case Ctl.PTrigger: return cfg.PointerTrigger.Value;
                case Ctl.PGrip: return cfg.PointerGrip.Value;
                case Ctl.PPrimary: return cfg.PointerPrimary.Value;
                case Ctl.PSecondary: return cfg.PointerSecondary.Value;
                case Ctl.PStick: return cfg.PointerStickClick.Value;
                case Ctl.OTrigger: return cfg.OffTrigger.Value;
                case Ctl.OGrip: return cfg.OffGrip.Value;
                case Ctl.OPrimary: return cfg.OffPrimary.Value;
                case Ctl.OSecondary: return cfg.OffSecondary.Value;
                case Ctl.OStick: return cfg.OffStickClick.Value;
                case Ctl.OMenu: return cfg.OffMenu.Value;
                case Ctl.FrameX: return cfg.FrameX.Value;
                case Ctl.FrameY: return cfg.FrameY.Value;
                case Ctl.DpadUp: return cfg.DpadUp.Value;
                case Ctl.DpadDown: return cfg.DpadDown.Value;
                case Ctl.DpadLeft: return cfg.DpadLeft.Value;
                case Ctl.DpadRight: return cfg.DpadRight.Value;
                case Ctl.PBumper: return cfg.PointerBumper.Value;
                case Ctl.OBumper: return cfg.OffBumper.Value;
                default: return VRAction.None;
            }
        }

        void ReadButtons(HandState p, HandState o)
        {
            float th = Plugin.Cfg.PressThreshold.Value;
            var now = _now;
            now[(int)Ctl.PTrigger] = p.Valid && Pressed(_ctl[(int)Ctl.PTrigger], p.Trigger, th);
            now[(int)Ctl.PGrip] = p.Valid && Pressed(_ctl[(int)Ctl.PGrip], p.Grip, th);
            now[(int)Ctl.PPrimary] = p.Valid && p.Primary;
            now[(int)Ctl.PSecondary] = p.Valid && p.Secondary;
            now[(int)Ctl.PStick] = p.Valid && p.StickClick;
            now[(int)Ctl.OTrigger] = o.Valid && Pressed(_ctl[(int)Ctl.OTrigger], o.Trigger, th);
            now[(int)Ctl.OGrip] = o.Valid && Pressed(_ctl[(int)Ctl.OGrip], o.Grip, th);
            now[(int)Ctl.OPrimary] = o.Valid && o.Primary;
            now[(int)Ctl.OSecondary] = o.Valid && o.Secondary;
            now[(int)Ctl.OStick] = o.Valid && o.StickClick;
            // The menu button sits on the left controller of most sets regardless of the pointer hand.
            now[(int)Ctl.OMenu] = (o.Valid && o.Menu) || (p.Valid && p.Menu);
            // Steam Frame extras: X/Y and the D-pad sit on fixed hands, so take them from whichever hand has them.
            now[(int)Ctl.FrameX] = (p.Valid && p.ButtonX) || (o.Valid && o.ButtonX);
            now[(int)Ctl.FrameY] = (p.Valid && p.ButtonY) || (o.Valid && o.ButtonY);
            now[(int)Ctl.DpadUp] = (p.Valid && p.DpadUp) || (o.Valid && o.DpadUp);
            now[(int)Ctl.DpadDown] = (p.Valid && p.DpadDown) || (o.Valid && o.DpadDown);
            now[(int)Ctl.DpadLeft] = (p.Valid && p.DpadLeft) || (o.Valid && o.DpadLeft);
            now[(int)Ctl.DpadRight] = (p.Valid && p.DpadRight) || (o.Valid && o.DpadRight);
            now[(int)Ctl.PBumper] = p.Valid && p.Bumper;
            now[(int)Ctl.OBumper] = o.Valid && o.Bumper;

            DetectGrabToggle(now);

            bool alt = false;
            for (int i = 0; i < now.Length; i++)
                if (now[i] && Binding((Ctl)i, false) == VRAction.AltLayer) alt = true;

            Array.Copy(_act, _actPrev, ActionCount);
            Array.Clear(_act, 0, ActionCount);
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] && !_ctl[i]) _ctlAction[i] = Binding((Ctl)i, alt); // bound at press time
                _ctl[i] = now[i];
                if (now[i]) _act[(int)_ctlAction[i]] = true;
            }
            _act[(int)VRAction.None] = false;

            // scripted click of the focus cursor (B = "back" presses a visible back button)
            if (_synthClick > 0)
            {
                int n = --_synthClick;
                if (n == 3 || n == 2) _act[(int)VRAction.LeftClick] = true;
            }
        }

        // ------------------------------------------------------------------ grab mode

        float _lastBumperTap = -10f;
        bool _gHeld, _gArm, _gMoving, _gPressOnUi, _gWasPlacing, _rotHigh;
        float _gTime;
        float _gLastAng, _gAccum;
        int _gApplied, _rotQueue;

        /// <summary>Double-tap of the configured bumper switches between pointer and grab mode.</summary>
        void DetectGrabToggle(bool[] now)
        {
            var choice = Plugin.Cfg.GrabToggleBumper.Value;
            if (choice == BumperChoice.None) return;
            bool pointerIsRight = Plugin.Cfg.PointerHand.Value == Hand.Right;
            bool leftNow = pointerIsRight ? now[(int)Ctl.OBumper] : now[(int)Ctl.PBumper];
            bool leftWas = pointerIsRight ? _ctl[(int)Ctl.OBumper] : _ctl[(int)Ctl.PBumper];
            bool rightNow = pointerIsRight ? now[(int)Ctl.PBumper] : now[(int)Ctl.OBumper];
            bool rightWas = pointerIsRight ? _ctl[(int)Ctl.PBumper] : _ctl[(int)Ctl.OBumper];
            bool tap = ((choice == BumperChoice.Left || choice == BumperChoice.Both) && leftNow && !leftWas)
                    || ((choice == BumperChoice.Right || choice == BumperChoice.Both) && rightNow && !rightWas);
            if (!tap) return;
            float t = Time.unscaledTime;
            if (t - _lastBumperTap < 0.4f) { Grab.Toggle(); _lastBumperTap = -10f; }
            else _lastBumperTap = t;
        }

        static float TwistAbout(Quaternion q, Vector3 axis)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            float d = Vector3.Dot(r, axis);
            return 2f * Mathf.Atan2(d, q.w) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Grab mode behaviour on the trigger: holding it on a piece picks it up (like the game's 1-second hold, but
        /// quick), releasing sets it down. While carrying, twisting the controller turns the piece in 90 degree steps.
        /// </summary>
        void UpdateGrab(float dt)
        {
            bool placing = Game.Placing && !Game.InMenu;
            if (!Grab.Active || _navActive)
            {
                Grab.Holding = false;
                _gHeld = _gArm = _gMoving = false;
                _gWasPlacing = false;
                _rotQueue = 0;
                _rotHigh = false;
                return;
            }

            // trigger -> pick up / put down
            bool raw = _act[(int)VRAction.LeftClick];
            if (raw && !_gHeld) { _gHeld = true; _gTime = 0f; _gArm = false; _gMoving = false; _gPressOnUi = OverUi; }
            if (raw) _gTime += dt;
            if (_gHeld && raw && !placing && !_gMoving && !_gArm && !_gPressOnUi && _gTime >= Plugin.Cfg.GrabHoldSeconds.Value && !Game.InMenu)
            {
                Game.TriggerCursorHold();
                _gArm = true;
            }
            if (_gArm && placing) { _gMoving = true; _gArm = false; }
            if (_gMoving)
            {
                if (raw) MaskLeftClick();
                else
                {
                    _gMoving = false;
                    if (placing) _synthClick = 4; // the click that sets the piece down
                }
            }
            if (!raw) { _gHeld = false; _gArm = false; }
            Grab.Holding = _gMoving || placing;

            // twist the controller -> rotate the carried piece (90 degree steps, with hysteresis)
            Quaternion world = _rig.rotation * _aimRot;
            float ang = TwistAbout(world, Vector3.up);
            if (placing && !_gWasPlacing) { _gLastAng = ang; _gAccum = 0f; _gApplied = 0; }
            _gWasPlacing = placing;
            if (placing)
            {
                _gAccum += Mathf.DeltaAngle(_gLastAng, ang);
                _gLastAng = ang;
                while (_gAccum - _gApplied * 90f > 58f) { _gApplied++; _rotQueue += 1; }
                while (_gAccum - _gApplied * 90f < -58f) { _gApplied--; _rotQueue += 3; }
            }
            else _rotQueue = 0;
            if (_rotQueue > 0 && !_rotHigh) { _rotHigh = true; _rotQueue--; }
            else _rotHigh = false;
        }

        // ------------------------------------------------------------------ menu focus navigation

        void UpdateNavigation(HandState p, HandState o, float dt)
        {
            if (!_menuContext)
            {
                _navActive = false;
                _navHeldDir = Vector2.zero;
                return;
            }

            Vector2 dir = Vector2.zero;
            if (_ctl[(int)Ctl.DpadUp]) dir = Vector2.up;
            else if (_ctl[(int)Ctl.DpadDown]) dir = Vector2.down;
            else if (_ctl[(int)Ctl.DpadLeft]) dir = Vector2.left;
            else if (_ctl[(int)Ctl.DpadRight]) dir = Vector2.right;
            else if (OffStick.magnitude > 0.6f)
                dir = Mathf.Abs(OffStick.x) > Mathf.Abs(OffStick.y) ? new Vector2(Mathf.Sign(OffStick.x), 0f) : new Vector2(0f, Mathf.Sign(OffStick.y));

            bool step = false;
            if (dir == Vector2.zero) { _navHeldDir = Vector2.zero; _navRepeat = 0f; }
            else if (dir != _navHeldDir) { _navHeldDir = dir; _navRepeat = 0.45f; step = true; }
            else
            {
                _navRepeat -= dt;
                if (_navRepeat <= 0f) { _navRepeat = 0.16f; step = true; }
            }

            if (_navActive)
            {
                bool laserTakenUp = _synthClick == 0 && (_ctl[(int)Ctl.PTrigger] || (p.Valid && Quaternion.Angle(p.AimRot, _navAimRot) > 6f));
                if (laserTakenUp) _navActive = false;
                else if (_hud.IsReachable(_navTarget, out Vector2 px)) _navPixel = px;
                else
                {
                    // the focused element is gone (a dialog opened or closed): focus whatever is nearest now
                    _navTarget = _hud.FindSelectable(_navPixel, Vector2.zero, null, out px);
                    if (_navTarget != null) _navPixel = px; else _navActive = false;
                }
            }

            if (step)
            {
                if (!_navActive)
                {
                    Vector2 from = _laserOnPanel ? _laserPixel : new Vector2(_hud.Width * 0.2f, _hud.Height * 0.7f);
                    var first = _hud.FindSelectable(from, Vector2.zero, null, out Vector2 px);
                    if (first != null) { _navActive = true; _navTarget = first; _navPixel = px; }
                }
                else
                {
                    var next = _hud.FindSelectable(_navPixel, dir, _navTarget, out Vector2 px);
                    if (next != null) { _navTarget = next; _navPixel = px; }
                }
                _navAimRot = p.AimRot;
            }

            if (Down(VRAction.MenuBack))
            {
                var back = _hud.FindBackButton(out Vector2 px);
                if (back != null)
                {
                    _navActive = true;
                    _navTarget = back;
                    _navPixel = px;
                    _navAimRot = p.AimRot;
                    _synthClick = 5;
                }
                else Game.Publish(Evt.EscapeAction);
            }
        }

        static Vector2 Deadzone(Vector2 v, float dz)
        {
            float m = v.magnitude;
            if (m <= dz) return Vector2.zero;
            return v / m * Mathf.Clamp01((m - dz) / (1f - dz));
        }

        // ------------------------------------------------------------------ main update

        public void Process(float dt)
        {
            EnsureMouse();
            SilenceXrUiBindings();
            var cfg = Plugin.Cfg;
            HandState p = PointerHandState, o = OffHandState;

            _menuContext = cfg.MenuNavigation.Value && Game.InMenu;
            ReadButtons(p, o);
            PointerStick = p.Valid ? Deadzone(p.Stick, cfg.StickDeadzone.Value) : Vector2.zero;
            OffStick = o.Valid ? Deadzone(o.Stick, cfg.StickDeadzone.Value) : Vector2.zero;
            UpdateNavigation(p, o, dt);

            UpdateGrab(dt);
            _hud.SetGrabLabel(Grab.Active);

            // left trigger (default): build menu - tap shows/hides it, hold carries it on the hand
            _hud.MenuPanelButton(Held(VRAction.MenuPanel), Game.InMenu);

            bool lmb = Held(VRAction.LeftClick), rmb = Held(VRAction.RightClick), mmb = Held(VRAction.MiddleClick);
            bool anyMouse = lmb || rmb || mmb;
            bool anyMouseDown = Down(VRAction.LeftClick) || Down(VRAction.RightClick) || Down(VRAction.MiddleClick);

            Vector2 uiPos = new Vector2(-10000f, -10000f);
            PointerValid = p.Valid || _navActive;
            OverUi = false;
            OverWidget = false;
            StickYConsumed = false;

            if (_navActive)
            {
                // focus cursor: the UI pointer sits on the focused element, the laser is parked
                OverUi = true;
                uiPos = _navPixel;
                ReticleWorld = _hud.PixelToWorld(_navPixel);
                AimOriginWorld = ReticleWorld;
                VRShim.HasPointerRay = false;
                VRShim.MousePosition = new Vector3(_navPixel.x, _navPixel.y, 0f);
                _buttonLock = false;
                _laserOnPanel = false;
                _aimInitialised = false;
                ReleaseCapture();
            }
            else if (p.Valid)
            {
                UpdateAim(p, dt, anyMouse, anyMouseDown);
                Vector3 origin = _rig.TransformPoint(_aimPos);
                Vector3 dir = (_rig.rotation * (_aimRot * Vector3.forward)).normalized;
                var ray = new Ray(origin, dir);
                var localRay = new Ray(_aimPos, (_aimRot * Vector3.forward).normalized);
                AimOriginWorld = origin;
                Grab.UpdateTip(ray); // the ball stays visible on the controller while the laser is on a menu

                // the mod's own widgets (grab bar, scale corner, VR button, settings panel) come first
                if (HandleWidgets(ray, localRay, dt, out Vector3 widgetPoint))
                {
                    lmb = Held(VRAction.LeftClick);
                    anyMouse = lmb || rmb || mmb;
                    anyMouseDown = Down(VRAction.RightClick) || Down(VRAction.MiddleClick);
                    OverUi = true;
                    OverWidget = true;
                    StickYConsumed = true;
                    ReticleWorld = widgetPoint;
                    _laserOnPanel = false;
                    VRShim.PointerRay = ray;
                    VRShim.HasPointerRay = true;
                    Vector3 wm = VRShim.WorldToScreen(widgetPoint);
                    VRShim.MousePosition = new Vector3(wm.x, wm.y, 0f);
                    goto Buttons;
                }

                bool hitPanel = _hud.Raycast(ray, out Vector2 pixel, out Vector3 panelPoint, out bool inside, out bool solid);
                _laserOnPanel = hitPanel && inside;
                _laserPixel = pixel;
                bool ui;
                if (_buttonLock && anyMouse) ui = _lockedToUi && hitPanel;
                else ui = hitPanel && inside && (solid || _hud.IsOverUi(pixel)); // solid panels never let the laser through to the world

                if (anyMouseDown && !_buttonLock) { _buttonLock = true; _lockedToUi = ui; }
                if (!anyMouse) _buttonLock = false;

                Vector3 gameMouse;
                Ray gameRay = ray;
                if (ui)
                {
                    OverUi = true;
                    uiPos = pixel;
                    gameMouse = new Vector3(pixel.x, pixel.y, 0f);
                    ReticleWorld = panelPoint;
                }
                else if (Grab.Active)
                {
                    gameRay = Grab.BuildRay(ray, WorldMask, out Vector3 tip);
                    ReticleWorld = tip;
                    gameMouse = VRShim.WorldToScreen(tip);
                    gameMouse.z = 0f;
                    gameMouse.x = Mathf.Clamp(gameMouse.x, -20f * _hud.Width, 21f * _hud.Width);
                    gameMouse.y = Mathf.Clamp(gameMouse.y, -20f * _hud.Height, 21f * _hud.Height);
                }
                else
                {
                    Vector3 aim = FindWorldAimPoint(ray);
                    ReticleWorld = aim;
                    gameMouse = VRShim.WorldToScreen(aim);
                    gameMouse.z = 0f;
                    // keep the numbers sane when pointing far away from the panel
                    gameMouse.x = Mathf.Clamp(gameMouse.x, -20f * _hud.Width, 21f * _hud.Width);
                    gameMouse.y = Mathf.Clamp(gameMouse.y, -20f * _hud.Height, 21f * _hud.Height);
                }

                VRShim.PointerRay = gameRay;
                VRShim.HasPointerRay = true;
                VRShim.MousePosition = gameMouse;
            }
            else
            {
                VRShim.HasPointerRay = false;
                _aimInitialised = false;
                _buttonLock = false;
                _laserOnPanel = false;
                ReleaseCapture();
            }

        Buttons:
            AnyButtonHeld = anyMouse;

            // mouse buttons
            SetMouse(0, VRAction.LeftClick);
            SetMouse(1, VRAction.RightClick);
            SetMouse(2, VRAction.MiddleClick);

            // keys read through the legacy Input class
            SetKey(KeyCode.R, VRAction.Rotate);
            SetKey(KeyCode.LeftShift, VRAction.Shift);
            SetKey(KeyCode.Return, VRAction.RollMarbles);
            if (_rotHigh)
            {
                VRShim.KeyDown[(int)KeyCode.R] = true;
                VRShim.KeyHeld[(int)KeyCode.R] = true;
            }

            // pointer-hand stick, vertical axis: scroll lists / step the piece height / fly up-down
            float stickY = PointerStick.y;
            float uiScroll = 0f;
            bool placing = Game.Placing;
            bool up = false, down = false;
            if (OverWidget)
            {
                // the stick belongs to the widget (e.g. pushing/pulling a panel that is being moved)
            }
            else if (OverUi)
            {
                StickYConsumed = true;
                uiScroll = stickY * 15f * cfg.UiScrollSpeed.Value * dt * _scrollUnit;
            }
            else if (placing)
            {
                StickYConsumed = true;
                up = _stickUp ? stickY > 0.4f : stickY > 0.65f;
                down = _stickDown ? stickY < -0.4f : stickY < -0.65f;
            }
            bool upDown = up && !_stickUp, downDown = down && !_stickDown;
            _stickUp = up;
            _stickDown = down;
            Repeat(ref _heightUpTimer, up || Held(VRAction.HeightUp), upDown || Down(VRAction.HeightUp), Evt.HeightUpAction, dt);
            Repeat(ref _heightDownTimer, down || Held(VRAction.HeightDown), downDown || Down(VRAction.HeightDown), Evt.HeightDownAction, dt);

            // When the game's free camera is not running (marble tracking), the stick zooms like the mouse wheel.
            VRShim.ScrollWheel = (!StickYConsumed && !_manager.LocomotionActive) ? stickY * 1.5f * dt : 0f;

            // one-shot shortcuts
            Fire(VRAction.BuildMenu, Evt.ToggleBuildMenuAction);
            Fire(VRAction.ToggleSnap, Evt.ToggleSnapAction);
            Fire(VRAction.MagicDrawVertical, Evt.ToggleMagicDrawVertAction);
            Fire(VRAction.Delete, Evt.DestroyObjectsAction);
            Fire(VRAction.Undo, Evt.CtrlZAction);
            Fire(VRAction.Redo, Evt.CtrlYAction);
            Fire(VRAction.Cut, Evt.CtrlXAction);
            Fire(VRAction.Copy, Evt.CtrlCAction);
            Fire(VRAction.Paste, Evt.CtrlVAction);
            Fire(VRAction.Escape, Evt.EscapeAction);
            if (Down(VRAction.Recenter)) _manager.Recenter();

            // feed the UI (Input System) pointer
            var state = new MouseState { position = uiPos, scroll = new Vector2(0f, uiScroll) };
            state = state.WithButton(MouseButton.Left, lmb).WithButton(MouseButton.Right, rmb).WithButton(MouseButton.Middle, mmb);
            InputSystem.QueueStateEvent(_mouse, state);
        }

        // ------------------------------------------------------------------ mod widgets

        enum Capture { None, Drag, Swallow }
        Capture _capture;

        void MaskLeftClick()
        {
            _act[(int)VRAction.LeftClick] = false;
            _actPrev[(int)VRAction.LeftClick] = false;
        }

        void ReleaseCapture()
        {
            if (_capture == Capture.Drag) _hud.EndDrag();
            _capture = Capture.None;
            _hud.SetWidgetHover(HudPanel.Widget.None);
        }

        /// <summary>
        /// Grab bar / scale corner / VR button of the UI panel and the VR settings panel. Returns true while the
        /// laser is on one of them or a drag is running; the trigger press then never reaches the game.
        /// </summary>
        bool HandleWidgets(Ray ray, Ray localRay, float dt, out Vector3 point)
        {
            point = default;
            var settings = _manager.Settings;
            bool held = Held(VRAction.LeftClick), down = Down(VRAction.LeftClick);

            if (_capture == Capture.Drag)
            {
                if (held && _hud.Visible)
                {
                    _hud.UpdateDrag(localRay, _tracking.HeadPos, PointerStick.y, dt);
                    MaskLeftClick();
                    point = _rig.TransformPoint(_hud.RootPosition);
                    var plane = new Plane(_rig.TransformDirection(_hud.RootRotation * Vector3.forward), point);
                    if (plane.Raycast(ray, out float t)) point = ray.GetPoint(t);
                    return true;
                }
                ReleaseCapture();
                MaskLeftClick();
            }
            else if (_capture == Capture.Swallow)
            {
                if (held) MaskLeftClick();
                else _capture = Capture.None;
            }

            var help = _manager.Help;
            bool onSettings = settings.Hit(ray, out Vector3 sHit, out float sDist);
            bool onHelp = help.Hit(ray, out Vector3 hHit, out float hDist);
            var widget = _hud.HitWidget(ray, out Vector3 wHit, out float wDist);
            bool popup = onSettings || onHelp;
            float popupDist = Mathf.Min(onSettings ? sDist : float.MaxValue, onHelp ? hDist : float.MaxValue);
            if (popup && (widget == HudPanel.Widget.None || popupDist <= wDist))
            {
                _hud.SetWidgetHover(HudPanel.Widget.None);
                bool helpNearer = onHelp && (!onSettings || hDist <= sDist);
                point = helpNearer ? hHit : sHit;
                if (down && _capture == Capture.None)
                {
                    if (helpNearer) help.Click(); else settings.Click();
                    _capture = Capture.Swallow;
                }
                if (_capture != Capture.None) MaskLeftClick();
                return true;
            }

            _hud.SetWidgetHover(widget);
            if (widget == HudPanel.Widget.None)
            {
                // still swallowing a press that started on a widget: park the dot at panel distance
                point = ray.GetPoint(Vector3.Distance(ray.origin, _rig.TransformPoint(_hud.RootPosition)));
                return _capture != Capture.None;
            }

            point = wHit;
            if (down && _capture == Capture.None)
            {
                if (widget == HudPanel.Widget.Settings)
                {
                    if (settings.Open) settings.Close();
                    else settings.Show(_hud.RootPosition, _hud.RootRotation, _hud.PanelWidth, _hud.PanelHeight);
                    _capture = Capture.Swallow;
                }
                else if (widget == HudPanel.Widget.Grab)
                {
                    Grab.Toggle();
                    _capture = Capture.Swallow;
                }
                else if (widget == HudPanel.Widget.Info)
                {
                    if (help.Open) help.Close(); else help.Show(_tracking.HeadPos, _tracking.HeadRot);
                    _capture = Capture.Swallow;
                }
                else
                {
                    _hud.BeginDrag(widget, localRay);
                    _capture = Capture.Drag;
                }
            }
            if (_capture != Capture.None) MaskLeftClick();
            return true;
        }

        void Fire(VRAction action, Evt evt)
        {
            if (Down(action)) Game.Publish(evt);
        }

        void Repeat(ref float timer, bool held, bool pressedNow, Evt evt, float dt)
        {
            if (!held) { timer = 0f; return; }
            if (pressedNow) { timer = 0.4f; Game.Publish(evt); return; }
            timer -= dt;
            if (timer <= 0f) { timer = 0.12f; Game.Publish(evt); }
        }

        void SetMouse(int button, VRAction action)
        {
            VRShim.MouseHeld[button] = Held(action);
            VRShim.MouseDown[button] = Down(action);
            VRShim.MouseUp[button] = Up(action);
        }

        void SetKey(KeyCode key, VRAction action)
        {
            int i = (int)key;
            VRShim.KeyHeld[i] = Held(action);
            VRShim.KeyDown[i] = Down(action);
            VRShim.KeyUp[i] = Up(action);
        }

        /// <summary>Low-pass filters the aim pose and freezes it briefly while a button goes down.</summary>
        void UpdateAim(HandState p, float dt, bool anyMouse, bool anyMouseDown)
        {
            float smoothing = Plugin.Cfg.PointerSmoothing.Value;
            if (!_aimInitialised || smoothing <= 0.01f)
            {
                _aimPos = p.AimPos;
                _aimRot = p.AimRot;
                _aimInitialised = true;
            }
            else
            {
                float angSpeed = Quaternion.Angle(_aimRot, p.AimRot) / Mathf.Max(dt, 1e-4f);
                float k = Mathf.Clamp01(dt * (14f + angSpeed * 0.8f) / smoothing);
                _aimRot = Quaternion.Slerp(_aimRot, p.AimRot, k);
                _aimPos = Vector3.Lerp(_aimPos, p.AimPos, Mathf.Clamp01(dt * 25f / smoothing + k));
            }

            if (anyMouseDown && !_pressFrozen)
            {
                _pressFrozen = true;
                _pressPos = _aimPos;
                _pressRot = _aimRot;
            }
            if (_pressFrozen)
            {
                if (Quaternion.Angle(_pressRot, p.AimRot) > Plugin.Cfg.ClickDeadzoneDegrees.Value
                    || (p.AimPos - _pressPos).sqrMagnitude > 0.03f * 0.03f)
                {
                    _pressFrozen = false;
                }
                else
                {
                    // also on the release frame, so the click lands exactly where it was pressed
                    _aimPos = _pressPos;
                    _aimRot = _pressRot;
                    if (!anyMouse) _pressFrozen = false;
                }
            }
        }

        /// <summary>Where the laser ends in the world; projected onto the virtual screen it becomes the game's mouse position.</summary>
        Vector3 FindWorldAimPoint(Ray ray)
        {
            if (Game.TryGetPlacementPlane(out float planeY) && Mathf.Abs(ray.direction.y) > 1e-4f)
            {
                float t = (planeY - ray.origin.y) / ray.direction.y;
                if (t > 0f && t < 2000f) return ray.GetPoint(t);
            }
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f, WorldMask, QueryTriggerInteraction.Collide))
                return hit.point;
            if (ray.direction.y < -1e-3f)
            {
                float t = -ray.origin.y / ray.direction.y;
                if (t > 0f && t < 2000f) return ray.GetPoint(t);
            }
            return ray.GetPoint(60f);
        }
    }
}
