using UnityEngine;
using UnityEngine.XR;
using XRDevice = UnityEngine.XR.InputDevice;
using XRUsages = UnityEngine.XR.CommonUsages;

namespace CozyMarblesVR
{
    /// <summary>One controller, in tracking space (real-world metres relative to the play space origin).</summary>
    public class HandState
    {
        public bool Valid;
        public Vector3 GripPos, AimPos;
        public Quaternion GripRot = Quaternion.identity, AimRot = Quaternion.identity;
        public float Trigger, Grip;
        public Vector2 Stick;
        public bool StickClick, Primary, Secondary, Menu;

        // Only with the native Steam Frame controller profile: right hand X/Y, left hand D-pad, bumpers.
        public bool IsFrame, ButtonX, ButtonY, DpadUp, DpadDown, DpadLeft, DpadRight, Bumper;

        public void Clear()
        {
            Valid = false;
            Trigger = Grip = 0f;
            Stick = Vector2.zero;
            StickClick = Primary = Secondary = Menu = false;
            ClearExtras();
        }

        public void ClearExtras()
        {
            IsFrame = ButtonX = ButtonY = DpadUp = DpadDown = DpadLeft = DpadRight = Bumper = false;
        }
    }

    /// <summary>Reads head and controller poses and buttons from the XR input subsystem.</summary>
    public class Tracking
    {
        public Vector3 HeadPos = new Vector3(0f, 1.6f, 0f);
        public Quaternion HeadRot = Quaternion.identity;
        public bool HeadValid;
        public bool HeadWorn;
        public readonly HandState Left = new HandState();
        public readonly HandState Right = new HandState();
        public bool Simulated;
        public Transform Rig;

        static readonly InputFeatureUsage<Vector3> PointerPosition = new InputFeatureUsage<Vector3>("PointerPosition");
        static readonly InputFeatureUsage<Quaternion> PointerRotation = new InputFeatureUsage<Quaternion>("PointerRotation");
        static readonly InputFeatureUsage<bool> DeviceIsTracked = new InputFeatureUsage<bool>("DeviceIsTracked");
        static readonly InputFeatureUsage<bool> FaceTop = new InputFeatureUsage<bool>(SteamFrameControllerProfile.UsageFaceTop);
        static readonly InputFeatureUsage<bool> FaceOutside = new InputFeatureUsage<bool>(SteamFrameControllerProfile.UsageFaceOutside);
        static readonly InputFeatureUsage<bool> Shoulder = new InputFeatureUsage<bool>(SteamFrameControllerProfile.UsageShoulder);

        public void UpdateHead()
        {
            XRDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!hmd.isValid) hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            HeadValid = false;
            HeadWorn = false;
            if (!hmd.isValid) return;

            bool gotPos = hmd.TryGetFeatureValue(XRUsages.centerEyePosition, out Vector3 p) || hmd.TryGetFeatureValue(XRUsages.devicePosition, out p);
            bool gotRot = hmd.TryGetFeatureValue(XRUsages.centerEyeRotation, out Quaternion r) || hmd.TryGetFeatureValue(XRUsages.deviceRotation, out r);
            if (gotPos) HeadPos = p;
            if (gotRot && (r.x != 0f || r.y != 0f || r.z != 0f || r.w != 0f)) HeadRot = r;
            HeadValid = gotPos && gotRot;

            // "Really tracked and on the head": used to decide when to centre the player.
            bool tracked = HeadValid;
            if (hmd.TryGetFeatureValue(XRUsages.isTracked, out bool it)) tracked &= it;
            if (hmd.TryGetFeatureValue(XRUsages.trackingState, out InputTrackingState ts))
                tracked &= (ts & (InputTrackingState.Position | InputTrackingState.Rotation)) == (InputTrackingState.Position | InputTrackingState.Rotation);
            if (hmd.TryGetFeatureValue(XRUsages.userPresence, out bool present)) tracked &= present;
            HeadWorn = tracked;
        }

        public void UpdateHands()
        {
            Read(XRNode.LeftHand, Left);
            Read(XRNode.RightHand, Right);
            Simulated = false;
            if (Plugin.Cfg.SimulateControllers.Value)
                Simulate();
        }

        static void Read(XRNode node, HandState s)
        {
            XRDevice d = InputDevices.GetDeviceAtXRNode(node);
            if (!d.isValid) { s.Clear(); return; }

            // OpenXR controllers report "DeviceIsTracked"; other providers use the common "IsTracked".
            bool tracked = true;
            if (d.TryGetFeatureValue(DeviceIsTracked, out bool t) || d.TryGetFeatureValue(XRUsages.isTracked, out t)) tracked = t;
            bool hasPos = d.TryGetFeatureValue(XRUsages.devicePosition, out Vector3 gp);
            bool hasRot = d.TryGetFeatureValue(XRUsages.deviceRotation, out Quaternion gr);
            if (!tracked || !hasPos || !hasRot || (gr.x == 0f && gr.y == 0f && gr.z == 0f && gr.w == 0f))
            {
                s.Clear();
                return;
            }

            s.Valid = true;
            s.GripPos = gp;
            s.GripRot = gr;
            if (d.TryGetFeatureValue(PointerPosition, out Vector3 ap) && d.TryGetFeatureValue(PointerRotation, out Quaternion ar)
                && (ar.x != 0f || ar.y != 0f || ar.z != 0f || ar.w != 0f))
            {
                s.AimPos = ap;
                s.AimRot = ar;
            }
            else
            {
                s.AimPos = gp;
                s.AimRot = gr;
            }

            d.TryGetFeatureValue(XRUsages.trigger, out s.Trigger);
            d.TryGetFeatureValue(XRUsages.grip, out s.Grip);
            d.TryGetFeatureValue(XRUsages.primary2DAxis, out s.Stick);
            d.TryGetFeatureValue(XRUsages.primary2DAxisClick, out s.StickClick);
            d.TryGetFeatureValue(XRUsages.primaryButton, out s.Primary);
            d.TryGetFeatureValue(XRUsages.secondaryButton, out s.Secondary);
            d.TryGetFeatureValue(XRUsages.menuButton, out s.Menu);

            // Controllers that only report digital trigger/grip.
            if (s.Trigger <= 0f && d.TryGetFeatureValue(XRUsages.triggerButton, out bool tb) && tb) s.Trigger = 1f;
            if (s.Grip <= 0f && d.TryGetFeatureValue(XRUsages.gripButton, out bool gb) && gb) s.Grip = 1f;

            // Steam Frame controller (native profile): four face buttons per hand, see SteamFrameControllerProfile.
            s.ClearExtras();
            if (d.TryGetFeatureValue(FaceTop, out bool top))
            {
                s.IsFrame = true;
                d.TryGetFeatureValue(FaceOutside, out bool outside);
                d.TryGetFeatureValue(Shoulder, out s.Bumper);
                bool bottom = s.Primary, inside = s.Secondary;
                if (node == XRNode.RightHand)
                {
                    s.Primary = bottom;   // A
                    s.Secondary = outside; // B
                    s.ButtonX = inside;
                    s.ButtonY = top;
                }
                else
                {
                    s.Primary = s.Secondary = false;
                    s.DpadDown = bottom;
                    s.DpadRight = inside;
                    s.DpadUp = top;
                    s.DpadLeft = outside;
                }
            }
        }

        /// <summary>
        /// Development aid: without controllers, the desktop mouse steers a virtual right-hand laser.
        /// LMB = trigger, RMB = B, MMB = A, WASD = left stick, arrows = right stick, Q/E = grips,
        /// Space = X, Z = Y, Tab = menu, F = left trigger, C = left stick click.
        /// </summary>
        void Simulate()
        {
            Simulated = true;
            float mx = Mathf.Clamp01(Input.mousePosition.x / Mathf.Max(1, Screen.width)) - 0.5f;
            float my = Mathf.Clamp01(Input.mousePosition.y / Mathf.Max(1, Screen.height)) - 0.5f;

            Right.Clear();
            Left.Clear();
            Right.Valid = true;
            Right.GripPos = Right.AimPos = HeadPos + HeadRot * new Vector3(0.18f, -0.2f, 0.25f);
            Left.Valid = true;
            Left.GripPos = Left.AimPos = HeadPos + HeadRot * new Vector3(-0.18f, -0.2f, 0.25f);
            Left.GripRot = Left.AimRot = HeadRot;

            if (SimOverride.Enabled)
            {
                Right.GripPos = Right.AimPos = Right.GripPos + SimOverride.RightHandOffset;
                Left.GripPos = Left.AimPos = Left.GripPos + SimOverride.LeftHandOffset;
                // scripted input from the diagnostics command file
                if (SimOverride.HasAimWorld && Rig != null)
                {
                    Vector3 local = Rig.InverseTransformPoint(SimOverride.AimWorld) - Right.AimPos;
                    Right.AimRot = local.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(local.normalized) : HeadRot;
                }
                else Right.AimRot = HeadRot * Quaternion.Euler(SimOverride.AimPitch, SimOverride.AimYaw, 0f);
                Right.GripRot = Right.AimRot;
                Right.Trigger = SimOverride.Button("trigger") ? 1f : 0f;
                Right.Grip = SimOverride.Button("grip") ? 1f : 0f;
                Right.Primary = SimOverride.Button("a");
                Right.Secondary = SimOverride.Button("b");
                Right.StickClick = SimOverride.Button("stick");
                Right.Stick = SimOverride.RightStick;
                Left.Trigger = SimOverride.Button("ltrigger") ? 1f : 0f;
                Left.Grip = SimOverride.Button("lgrip") ? 1f : 0f;
                Left.Primary = SimOverride.Button("x");
                Left.Secondary = SimOverride.Button("y");
                Left.StickClick = SimOverride.Button("lstick");
                Left.Menu = SimOverride.Button("menu");
                Left.Stick = SimOverride.LeftStick;
                // Steam Frame extras
                Right.ButtonX = SimOverride.Button("fx");
                Right.ButtonY = SimOverride.Button("fy");
                Right.Bumper = SimOverride.Button("rb");
                Left.Bumper = SimOverride.Button("lb");
                Left.DpadUp = SimOverride.Button("dup");
                Left.DpadDown = SimOverride.Button("ddown");
                Left.DpadLeft = SimOverride.Button("dleft");
                Left.DpadRight = SimOverride.Button("dright");
                return;
            }

            Right.GripRot = Right.AimRot = HeadRot * Quaternion.Euler(-my * 70f, mx * 100f, 0f);
            Right.Trigger = Input.GetMouseButton(0) ? 1f : 0f;
            Right.Secondary = Input.GetMouseButton(1);
            Right.Primary = Input.GetMouseButton(2);
            Right.Grip = Input.GetKey(KeyCode.E) ? 1f : 0f;
            Right.Stick = new Vector2((Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                                      (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f));
            Right.StickClick = Input.GetKey(KeyCode.X);

            Left.Trigger = Input.GetKey(KeyCode.F) ? 1f : 0f;
            Left.Grip = Input.GetKey(KeyCode.Q) ? 1f : 0f;
            Left.Primary = Input.GetKey(KeyCode.Space);
            Left.Secondary = Input.GetKey(KeyCode.Z);
            Left.Menu = Input.GetKey(KeyCode.Tab);
            Left.StickClick = Input.GetKey(KeyCode.C);
            Left.Stick = new Vector2((Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                                     (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
        }
    }
}
