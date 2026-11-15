using System;
using BepInEx.Configuration;

namespace CozyMarblesVR
{
    public enum TurnMode { Snap, Smooth }
    public enum StartMode { VR, Monitor }
    public enum Hand { Right, Left }
    public enum BumperChoice { None, Left, Right, Both }

    /// <summary>Things a controller button can do. Bound to buttons in the [Controls.*] config sections.</summary>
    public enum VRAction
    {
        None,
        LeftClick,      // place / select / drag / UI click
        RightClick,     // cancel current piece, cancel gizmo
        MiddleClick,
        Rotate,         // R
        Shift,          // hold: drag piece height / multi-select
        BuildMenu,      // Space
        ToggleSnap,     // T
        MagicDrawVertical, // V
        Delete,         // Del
        HeightUp,       // Arrow up
        HeightDown,     // Arrow down
        Undo, Redo, Cut, Copy, Paste,
        Escape,
        RollMarbles,    // Return
        GrabWorld,      // hold and move the hand to drag the world; both hands: scale + rotate
        Recenter,       // bring the HUD in front of you and reset the standing position
        AltLayer,       // hold to switch the face buttons to their "Alt" bindings
        MenuBack,       // used automatically for B while in menus: presses a visible back button, otherwise Escape
        MenuPanel       // build menu: tap = show/hide, hold = carry it on the hand, release = it stays there
    }

    public class VRConfig
    {
        public readonly ConfigEntry<bool> Enabled;
        public readonly ConfigEntry<float> WorldScale;
        public readonly ConfigEntry<float> RenderScale;
        public readonly ConfigEntry<bool> PostProcessing;
        public readonly ConfigEntry<bool> DepthOfField;
        public readonly ConfigEntry<bool> DesktopView;
        public readonly ConfigEntry<bool> ControllerModels;
        public readonly ConfigEntry<bool> GrabMode;
        public readonly ConfigEntry<float> GrabReach;
        public readonly ConfigEntry<float> GrabHoldSeconds;
        public readonly ConfigEntry<BumperChoice> GrabToggleBumper;
        public readonly ConfigEntry<bool> GrabGhost;
        public readonly ConfigEntry<bool> MarbleMode;
        public readonly ConfigEntry<int> ThrowLevel;
        public readonly ConfigEntry<bool> MarblePhysics;
        public readonly ConfigEntry<bool> FollowCameraYaw;
        public readonly ConfigEntry<bool> DesktopInput;

        // UI panels (positions relative to where you stood/looked at the last recenter, in metres and degrees)
        public readonly ConfigEntry<int> UiResolution;
        public readonly ConfigEntry<float> MenuWidth, MenuX, MenuY, MenuZ, MenuYaw, MenuPitch;
        public readonly ConfigEntry<float> HudWidth, HudX, HudY, HudZ, HudYaw, HudPitch;
        public readonly ConfigEntry<float> EditScale, EditX, EditY, EditZ, EditYaw, EditPitch, EditRoll;
        public readonly ConfigEntry<bool> AlwaysAttached;
        public readonly ConfigEntry<float> AttachedScale, AttachedTilt, OptionsScale;
        public readonly ConfigEntry<bool> ShowStartDialog;
        public readonly ConfigEntry<StartMode> StartIn;
        public readonly ConfigEntry<int> ConfigVersion;

        public readonly ConfigEntry<Hand> PointerHand;
        public readonly ConfigEntry<TurnMode> Turning;
        public readonly ConfigEntry<float> SnapTurnDegrees;
        public readonly ConfigEntry<float> SmoothTurnSpeed;
        public readonly ConfigEntry<float> MoveSpeed;
        public readonly ConfigEntry<float> VerticalSpeed;
        public readonly ConfigEntry<float> StickDeadzone;
        public readonly ConfigEntry<float> PressThreshold;
        public readonly ConfigEntry<float> PointerSmoothing;
        public readonly ConfigEntry<float> ClickDeadzoneDegrees;
        public readonly ConfigEntry<float> UiScrollSpeed;
        public readonly ConfigEntry<float> MinWorldScale;
        public readonly ConfigEntry<float> MaxWorldScale;

        // Button bindings. "Pointer" is the hand holding the laser, "Off" the other one.
        public readonly ConfigEntry<VRAction> PointerTrigger, PointerGrip, PointerPrimary, PointerSecondary, PointerStickClick;
        public readonly ConfigEntry<VRAction> OffTrigger, OffGrip, OffPrimary, OffSecondary, OffStickClick, OffMenu;
        public readonly ConfigEntry<VRAction> PointerPrimaryAlt, PointerSecondaryAlt, PointerStickClickAlt;
        public readonly ConfigEntry<VRAction> OffPrimaryAlt, OffSecondaryAlt, OffStickClickAlt;

        // Steam Frame controllers (native profile): right X/Y, left D-pad, bumpers.
        public readonly ConfigEntry<bool> FrameControllerProfile;
        public readonly ConfigEntry<bool> MenuNavigation;
        public readonly ConfigEntry<VRAction> FrameX, FrameY, FrameXAlt, FrameYAlt, DpadUp, DpadDown, DpadLeft, DpadRight, PointerBumper, OffBumper;

        public readonly ConfigEntry<bool> MockRuntime;
        public readonly ConfigEntry<bool> SimulateControllers;
        public readonly ConfigEntry<bool> Diagnostics;

        public VRConfig(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true, "Start the game in VR. Set to false (or start the game with -novr) to play on the monitor.");
            WorldScale = cfg.Bind("General", "WorldScale", 8f, "How many game metres one real metre covers. 1 = life size (the build area is 16 m wide), 8 = table-top model about 2 m wide. Can be changed in VR by holding both grips and moving the hands apart or together.");
            RenderScale = cfg.Bind("General", "RenderScale", 1f, new ConfigDescription("Eye texture resolution multiplier.", new AcceptableValueRange<float>(0.5f, 2f)));
            PostProcessing = cfg.Bind("General", "PostProcessing", true, "Keep the game's post-processing (bloom, colour grading, outlines) in the headset. Turn off if performance is low.");
            DepthOfField = cfg.Bind("General", "DepthOfField", false, "Keep the game's depth-of-field blur. Off by default: in a headset it only makes the scene look out of focus.");
            ControllerModels = cfg.Bind("General", "ShowControllerModels", true, "Show the 3D models of the Steam Frame controllers (taken from your SteamVR installation) instead of simple boxes.");
            DesktopView = cfg.Bind("General", "DesktopView", true, "While in VR, show a calm spectator view in the desktop window (costs some GPU time). Off: the window stays black.");
            FollowCameraYaw = cfg.Bind("General", "FollowCameraYaw", true, "Rotate the play space together with the game's camera (menu transitions, marble tracking).");
            DesktopInput = cfg.Bind("General", "DesktopInputInVR", true, "Keep mouse buttons and keyboard working while in VR.");

            ShowStartDialog = cfg.Bind("General", "ShowStartDialog", true, "Show the start window (VR or monitor, screen resolution) when the game starts.");
            StartIn = cfg.Bind("General", "StartIn", StartMode.VR, "Mode used when the start window is switched off.");
            UiResolution = cfg.Bind("HUD", "UiResolutionWidth", 2560, "Pixel width of the game UI picture (16:9). Independent of the desktop window.");

            const string m = "HUD.MainMenu";
            const string poseNote = "Set in VR with the grab bar below the panel (move) and its corner (size), or in the VR settings. Position in metres relative to your head at the last recenter (x right, y up, z forward).";
            MenuWidth = cfg.Bind(m, "Width", 1.0f, "Width of the main menu panel in metres. " + poseNote);
            MenuX = cfg.Bind(m, "X", 0f, "");
            MenuY = cfg.Bind(m, "Y", -0.08f, "");
            MenuZ = cfg.Bind(m, "Z", 1.1f, "");
            MenuYaw = cfg.Bind(m, "Yaw", 0f, "");
            MenuPitch = cfg.Bind(m, "Pitch", 4f, "");

            const string hd = "HUD.GameHud";
            HudWidth = cfg.Bind(hd, "Width", 1.2f, "Width of the in-game HUD panel (top bar, play/race buttons, piece options, hints) in metres. " + poseNote);
            HudX = cfg.Bind(hd, "X", 0f, "");
            HudY = cfg.Bind(hd, "Y", 0.05f, "");
            HudZ = cfg.Bind(hd, "Z", 1.3f, "");
            HudYaw = cfg.Bind(hd, "Yaw", 0f, "");
            HudPitch = cfg.Bind(hd, "Pitch", -2f, "");

            const string e = "HUD.BuildMenu";
            AlwaysAttached = cfg.Bind(e, "AlwaysAttachedToHand", false, "Build menu always sits on the off-hand controller; the menu button only shows/hides it. Off: tap the button to show/hide, hold it to carry the menu, release to leave it in the room.");
            AttachedScale = cfg.Bind(e, "AttachedScale", 0.22f, "Size of the build menu while it is on the hand: metres per 1000 UI pixels (the open toolbox is about 1300 pixels wide).");
            OptionsScale = cfg.Bind("HUD.ObjectOptions", "Scale", 0.5f, "Size of the options panel that appears above a selected object (metres per 1000 UI pixels at 0.8 m distance; it keeps its apparent size).");
            AttachedTilt = cfg.Bind(e, "AttachedTiltDegrees", 25f, "Tilt of the build menu on the hand (positive = top leans away).");
            EditScale = cfg.Bind(e, "Scale", 0.4f, "Size of the build menu while it lies in the room: metres per 1000 UI pixels. " + poseNote);
            EditX = cfg.Bind(e, "X", 0f, "");
            EditY = cfg.Bind(e, "Y", -0.55f, "");
            EditZ = cfg.Bind(e, "Z", 0.75f, "");
            EditYaw = cfg.Bind(e, "Yaw", 0f, "");
            EditPitch = cfg.Bind(e, "Pitch", 35f, "");
            EditRoll = cfg.Bind(e, "Roll", 0f, "");

            PointerHand = cfg.Bind("Controls", "PointerHand", Hand.Right, "Hand that holds the laser pointer.");
            Turning = cfg.Bind("Controls", "Turning", TurnMode.Snap, "Snap or smooth turning on the pointer hand's stick.");
            SnapTurnDegrees = cfg.Bind("Controls", "SnapTurnDegrees", 30f, "Angle per snap turn.");
            SmoothTurnSpeed = cfg.Bind("Controls", "SmoothTurnSpeed", 90f, "Degrees per second for smooth turning.");
            MoveSpeed = cfg.Bind("Controls", "MoveSpeed", 1.0f, "Flying speed on the off-hand stick, in real metres per second (scaled with WorldScale).");
            VerticalSpeed = cfg.Bind("Controls", "VerticalSpeed", 0.6f, "Up/down speed on the pointer hand's stick, in real metres per second.");
            StickDeadzone = cfg.Bind("Controls", "StickDeadzone", 0.25f, "Thumbstick deadzone.");
            PressThreshold = cfg.Bind("Controls", "PressThreshold", 0.5f, "How far trigger/grip must be pulled to count as pressed.");
            PointerSmoothing = cfg.Bind("Controls", "PointerSmoothing", 1f, new ConfigDescription("Laser smoothing. 0 = raw, 1 = default, 2 = very steady.", new AcceptableValueRange<float>(0f, 3f)));
            ClickDeadzoneDegrees = cfg.Bind("Controls", "ClickDeadzoneDegrees", 0.8f, "The pointer is frozen while a button is held until the hand moves further than this, so clicks do not turn into drags.");
            UiScrollSpeed = cfg.Bind("Controls", "UiScrollSpeed", 1f, "Scroll speed in lists (pointer hand stick up/down while pointing at the menu).");
            MinWorldScale = cfg.Bind("Controls", "MinWorldScale", 1f, "Smallest WorldScale reachable with the two-hand gesture.");
            MaxWorldScale = cfg.Bind("Controls", "MaxWorldScale", 30f, "Largest WorldScale reachable with the two-hand gesture.");

            const string b = "Controls.Buttons";
            string acts = "Possible values: " + string.Join(", ", Enum.GetNames(typeof(VRAction)));
            PointerTrigger = cfg.Bind(b, "PointerTrigger", VRAction.LeftClick, acts);
            PointerGrip = cfg.Bind(b, "PointerGrip", VRAction.GrabWorld, "");
            PointerPrimary = cfg.Bind(b, "PointerPrimary", VRAction.Rotate, "Lower face button (A on the right hand).");
            PointerSecondary = cfg.Bind(b, "PointerSecondary", VRAction.RightClick, "Upper face button (B on the right hand).");
            PointerStickClick = cfg.Bind(b, "PointerStickClick", VRAction.ToggleSnap, "");
            OffTrigger = cfg.Bind(b, "OffTrigger", VRAction.MenuPanel, "Default: build menu (tap = show/hide, hold = carry it on the hand).");
            OffGrip = cfg.Bind(b, "OffGrip", VRAction.GrabWorld, "");
            OffPrimary = cfg.Bind(b, "OffPrimary", VRAction.BuildMenu, "Lower face button (X on the left hand).");
            OffSecondary = cfg.Bind(b, "OffSecondary", VRAction.Undo, "Upper face button (Y on the left hand).");
            OffStickClick = cfg.Bind(b, "OffStickClick", VRAction.AltLayer, "Hold to reach the Alt bindings below.");
            OffMenu = cfg.Bind(b, "OffMenu", VRAction.Escape, "Menu / View button.");

            const string a = "Controls.Buttons.Alt";
            string altDesc = "Used while a button bound to AltLayer is held (by default: off-hand stick click).";
            PointerPrimaryAlt = cfg.Bind(a, "PointerPrimary", VRAction.Copy, altDesc);
            PointerSecondaryAlt = cfg.Bind(a, "PointerSecondary", VRAction.Paste, "");
            PointerStickClickAlt = cfg.Bind(a, "PointerStickClick", VRAction.Recenter, "");
            OffPrimaryAlt = cfg.Bind(a, "OffPrimary", VRAction.Delete, "");
            OffSecondaryAlt = cfg.Bind(a, "OffSecondary", VRAction.Redo, "");
            OffStickClickAlt = cfg.Bind(a, "OffStickClick", VRAction.None, "");

            FrameControllerProfile = cfg.Bind("Controls", "SteamFrameControllerProfile", true, "Talk to Steam Frame controllers natively (D-pad, A/B/X/Y and bumpers as separate buttons). Turn off to fall back to SteamVR's Oculus Touch emulation if buttons misbehave.");
            GrabMode = cfg.Bind("Grab", "GrabMode", false, "Grab mode instead of the mouse pointer: a small ball on the controller selects and carries pieces freely through the room, a transparent ghost shows where the piece would snap on release. Toggle in the left-hand menu or by double-tapping a bumper.");
            GrabReach = cfg.Bind("Grab", "Reach", 0.15f, new ConfigDescription("How far in front of the controller (metres, real size) pieces can be touched.", new AcceptableValueRange<float>(0.03f, 0.6f)));
            GrabHoldSeconds = cfg.Bind("Grab", "HoldSeconds", 0.12f, new ConfigDescription("How long the trigger is held on a piece before it is picked up.", new AcceptableValueRange<float>(0.0f, 1f)));
            GrabToggleBumper = cfg.Bind("Grab", "ToggleBumper", BumperChoice.Left, "Double-tap this bumper to switch between pointer and grab mode (Left = Shift key bumper, Right = roll marbles bumper).");
            GrabGhost = cfg.Bind("Grab", "ShowGhost", true, "Show a transparent preview of where the carried piece will snap.");
            MarbleMode = cfg.Bind("Grab", "MarbleMode", false, "Marble mode (hand mode only): the trigger drops a marble at the ball on the controller instead of grabbing pieces. Toggle with the marble button in the left-hand menu.");
            ThrowLevel = cfg.Bind("Grab", "ThrowLevel", 0, new ConfigDescription("Marble mode: 0 = the marble just drops, 1..6 = it is thrown in the pointing direction, 6 being the fastest (3 m/s real size). A small cone on the marble shows the direction, a dial on the controller the level. L1 / L2 change it.", new AcceptableValueRange<int>(0, 6)));
            MarblePhysics = cfg.Bind("Grab", "MarblePhysics", true, "Marble mode: the marble waiting on the controller is solid and pushes other marbles. Off = a see-through preview that touches nothing.");
            MenuNavigation = cfg.Bind("Controls", "MenuNavigation", true, "In menus (main menu, pause): move a focus cursor with the D-pad or the off-hand stick, select with A, go back with B. The laser keeps working.");

            const string f = "Controls.Buttons.SteamFrame";
            FrameX = cfg.Bind(f, "ButtonX", VRAction.BuildMenu, "Only with the native Steam Frame profile. In menus the D-pad moves the focus cursor instead of these bindings.");
            FrameY = cfg.Bind(f, "ButtonY", VRAction.Delete, "");
            DpadUp = cfg.Bind(f, "DpadUp", VRAction.HeightUp, "");
            DpadDown = cfg.Bind(f, "DpadDown", VRAction.HeightDown, "");
            DpadLeft = cfg.Bind(f, "DpadLeft", VRAction.Undo, "");
            DpadRight = cfg.Bind(f, "DpadRight", VRAction.Redo, "");
            PointerBumper = cfg.Bind(f, "PointerBumper", VRAction.RollMarbles, "Bumper on the laser hand.");
            OffBumper = cfg.Bind(f, "OffBumper", VRAction.Shift, "Bumper on the other hand. Default: Shift (hold to drag a piece's height, multi-select).");
            FrameXAlt = cfg.Bind(a, "ButtonX", VRAction.Cut, "");
            FrameYAlt = cfg.Bind(a, "ButtonY", VRAction.MagicDrawVertical, "");

            MockRuntime = cfg.Bind("Debug", "MockRuntime", false, "Development: use Unity's mock OpenXR runtime instead of the real headset.");
            SimulateControllers = cfg.Bind("Debug", "SimulateControllers", false, "Development: drive the laser with the desktop mouse when no controller is tracked.");
            Diagnostics = cfg.Bind("Debug", "Diagnostics", false, "Write extra information to BepInEx/LogOutput.log.");

            // Upgrade older config files: the left trigger becomes the build-menu button, Shift moves to the left bumper.
            ConfigVersion = cfg.Bind("General", "ConfigVersion", 0, "Internal, do not change.");
            if (ConfigVersion.Value < 2)
            {
                if (OffTrigger.Value == VRAction.Shift) OffTrigger.Value = VRAction.MenuPanel;
                if (OffBumper.Value == VRAction.AltLayer) OffBumper.Value = VRAction.Shift;
                ConfigVersion.Value = 2;
            }
            if (ConfigVersion.Value < 3)
            {
                // new panel layout: main menu closer, build menu is now only the toolbox
                foreach (var entry in new ConfigEntryBase[] { MenuWidth, MenuX, MenuY, MenuZ, MenuYaw, MenuPitch, EditX, EditY, EditZ, EditYaw, EditPitch, EditRoll })
                    entry.BoxedValue = entry.DefaultValue;
                ConfigVersion.Value = 3;
            }
            if (ConfigVersion.Value < 4)
            {
                // object options (colour picker, spawner settings) were too small in the headset
                OptionsScale.BoxedValue = OptionsScale.DefaultValue;
                ConfigVersion.Value = 4;
            }
        }
    }
}
