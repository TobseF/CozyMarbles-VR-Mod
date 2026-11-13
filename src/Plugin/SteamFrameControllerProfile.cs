using System.Collections.Generic;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace CozyMarblesVR
{
    /// <summary>
    /// Native OpenXR bindings for the Steam Frame controllers (extension XR_VALVE_frame_controller_interaction).
    /// Without it SteamVR presents them as Oculus Touch controllers, which hides the D-pad and two of the
    /// four face buttons. Paths follow Valve's published profile (ValveSoftware/Unity, openxr.utils).
    ///
    /// Each action carries exactly one usage; <see cref="Tracking"/> reads them through UnityEngine.XR.InputDevices:
    ///   PrimaryButton      right A      / left D-pad down
    ///   SecondaryButton    right X      / left D-pad right
    ///   FaceButtonTop      right Y      / left D-pad up
    ///   FaceButtonOutside  right B      / left D-pad left
    ///   ShoulderButton     bumper,  MenuButton  right Menu / left View
    /// </summary>
    public class SteamFrameControllerProfile : OpenXRInteractionFeature
    {
        public const string Extension = "XR_VALVE_frame_controller_interaction";
        public const string Profile = "/interaction_profiles/valve/frame_controller_valve";
        public const string UsageFaceTop = "FaceButtonTop";
        public const string UsageFaceOutside = "FaceButtonOutside";
        public const string UsageShoulder = "ShoulderButton";
        const string DeviceName = "Steam Frame Controller OpenXR";

        // If the runtime does not know the controllers, this profile switches itself off and the Touch bindings apply.
        protected internal override bool OnInstanceCreate(ulong xrInstance) => OpenXRRuntime.IsExtensionEnabled(Extension);

        static ActionConfig Action(string name, ActionType type, string usage, string path)
        {
            return new ActionConfig
            {
                name = name,
                localizedName = name,
                type = type,
                usages = new List<string> { usage },
                bindings = new List<ActionBinding> { new ActionBinding { interactionPath = path, interactionProfileName = Profile } }
            };
        }

        static ActionConfig PerHand(string name, string usage, string rightPath, string leftPath)
        {
            return new ActionConfig
            {
                name = name,
                localizedName = name,
                type = ActionType.Binary,
                usages = new List<string> { usage },
                bindings = new List<ActionBinding>
                {
                    new ActionBinding { interactionPath = rightPath, interactionProfileName = Profile, userPaths = new List<string> { UserPaths.rightHand } },
                    new ActionBinding { interactionPath = leftPath, interactionProfileName = Profile, userPaths = new List<string> { UserPaths.leftHand } },
                }
            };
        }

        protected override void RegisterActionMapsWithRuntime()
        {
            const InputDeviceCharacteristics hand = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Controller;
            AddActionMap(new ActionMapConfig
            {
                name = "steamframecontroller",
                localizedName = DeviceName,
                desiredInteractionProfile = Profile,
                manufacturer = "Valve",
                serialNumber = "",
                deviceInfos = new List<DeviceConfig>
                {
                    new DeviceConfig { characteristics = hand | InputDeviceCharacteristics.Left, userPath = UserPaths.leftHand },
                    new DeviceConfig { characteristics = hand | InputDeviceCharacteristics.Right, userPath = UserPaths.rightHand },
                },
                actions = new List<ActionConfig>
                {
                    Action("devicePose", ActionType.Pose, "Device", "/input/grip/pose"),
                    Action("pointer", ActionType.Pose, "Pointer", "/input/aim/pose"),
                    Action("thumbstick", ActionType.Axis2D, "Primary2DAxis", "/input/thumbstick"),
                    Action("thumbstickClicked", ActionType.Binary, "Primary2DAxisClick", "/input/thumbstick/click"),
                    Action("trigger", ActionType.Axis1D, "Trigger", "/input/trigger/value"),
                    Action("triggerPressed", ActionType.Binary, "TriggerButton", "/input/trigger/click"),
                    Action("grip", ActionType.Axis1D, "Grip", "/input/squeeze/value"),
                    Action("gripPressed", ActionType.Binary, "GripButton", "/input/squeeze/click"),
                    Action("shoulder", ActionType.Binary, UsageShoulder, "/input/shoulder/click"),
                    PerHand("menu", "MenuButton", "/input/menu/click", "/input/view/click"),
                    PerHand("faceButtonBottom", "PrimaryButton", "/input/a/click", "/input/dpad_down/click"),
                    PerHand("faceButtonInside", "SecondaryButton", "/input/x/click", "/input/dpad_right/click"),
                    PerHand("faceButtonTop", UsageFaceTop, "/input/y/click", "/input/dpad_up/click"),
                    PerHand("faceButtonOutside", UsageFaceOutside, "/input/b/click", "/input/dpad_left/click"),
                    Action("haptic", ActionType.Vibrate, "Haptic", "/output/haptic"),
                }
            });
        }
    }
}
