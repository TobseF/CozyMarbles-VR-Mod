using UnityEngine;

namespace CozyMarblesVR
{
    /// <summary>
    /// Drop-in replacements for the UnityEngine.Input / Camera calls the game makes.
    /// The preloader patcher rewrites the game's IL so that those calls land here. While VR is not
    /// active every method simply forwards to the engine, so the flat game behaves exactly as before.
    ///
    /// Naming convention used by the patcher:  [DeclaringType]_[Method], instance methods take the
    /// instance as first parameter.
    /// </summary>
    public static class VRShim
    {
        // ------------------------------------------------------------------ state written by the plugin

        /// <summary>True while the VR session drives input.</summary>
        public static bool Active;

        /// <summary>Virtual mouse position in screen pixels (origin bottom-left), as the game sees it.</summary>
        public static Vector3 MousePosition;

        /// <summary>World-space ray of the pointing controller; returned for screen-point queries at the mouse position.</summary>
        public static Ray PointerRay;
        public static bool HasPointerRay;

        // Virtual screen ("window") model: the HUD panel is the screen, the head is the eye.
        public static Vector3 EyePosition;
        public static Vector3 ScreenOrigin;   // world position of pixel (0,0)
        public static Vector3 ScreenRight;    // world vector for one pixel to the right
        public static Vector3 ScreenUp;       // world vector for one pixel up
        public static Vector3 ScreenNormal;   // unit vector pointing away from the eye
        public static float ScreenWidth = 1920f;
        public static float ScreenHeight = 1080f;

        /// <summary>The camera rendering to the headset and the game's original (now logical) camera.</summary>
        public static Camera VRCamera;
        public static Camera GameCamera;

        public static readonly bool[] MouseHeld = new bool[3];
        public static readonly bool[] MouseDown = new bool[3];
        public static readonly bool[] MouseUp = new bool[3];

        const int KeyCount = 512;
        public static readonly bool[] KeyHeld = new bool[KeyCount];
        public static readonly bool[] KeyDown = new bool[KeyCount];
        public static readonly bool[] KeyUp = new bool[KeyCount];

        /// <summary>Value for the legacy "Mouse ScrollWheel" axis.</summary>
        public static float ScrollWheel;

        /// <summary>When false, the physical mouse and keyboard are ignored while VR is active.</summary>
        public static bool PassThroughDesktopInput = true;

        // ------------------------------------------------------------------ helpers

        static bool IsWorldCamera(Camera cam)
        {
            return Active && (object)cam != null && (ReferenceEquals(cam, VRCamera) || ReferenceEquals(cam, GameCamera));
        }

        static bool IsMousePoint(Vector3 pos)
        {
            return HasPointerRay && Mathf.Abs(pos.x - MousePosition.x) < 0.75f && Mathf.Abs(pos.y - MousePosition.y) < 0.75f;
        }

        public static Ray ScreenPointToWorldRay(Vector3 pos)
        {
            Vector3 onScreen = ScreenOrigin + ScreenRight * pos.x + ScreenUp * pos.y;
            Vector3 dir = onScreen - EyePosition;
            float len = dir.magnitude;
            return len > 1e-6f ? new Ray(EyePosition, dir / len) : new Ray(EyePosition, ScreenNormal);
        }

        public static Vector3 WorldToScreen(Vector3 world)
        {
            Vector3 d = world - EyePosition;
            float depth = Vector3.Dot(d, ScreenNormal);
            float planeDist = Vector3.Dot(ScreenOrigin - EyePosition, ScreenNormal);
            if (Mathf.Abs(depth) < 1e-5f)
                return new Vector3(-100000f, -100000f, depth);
            Vector3 onPlane = EyePosition + d * (planeDist / depth) - ScreenOrigin;
            float x = Vector3.Dot(onPlane, ScreenRight) / ScreenRight.sqrMagnitude;
            float y = Vector3.Dot(onPlane, ScreenUp) / ScreenUp.sqrMagnitude;
            return new Vector3(x, y, depth);
        }

        static Vector3 ScreenToWorld(Vector3 pos)
        {
            Ray r = ScreenPointToWorldRay(pos);
            float along = Vector3.Dot(r.direction, ScreenNormal);
            if (Mathf.Abs(along) < 1e-5f) along = 1e-5f;
            return r.origin + r.direction * (pos.z / along);
        }

        static bool Key(bool[] table, KeyCode key)
        {
            int i = (int)key;
            return i >= 0 && i < KeyCount && table[i];
        }

        // ------------------------------------------------------------------ UnityEngine.Input

        public static bool Input_GetMouseButton(int button)
        {
            if (!Active) return Input.GetMouseButton(button);
            return (button >= 0 && button < 3 && MouseHeld[button]) || (PassThroughDesktopInput && Input.GetMouseButton(button));
        }

        public static bool Input_GetMouseButtonDown(int button)
        {
            if (!Active) return Input.GetMouseButtonDown(button);
            return (button >= 0 && button < 3 && MouseDown[button]) || (PassThroughDesktopInput && Input.GetMouseButtonDown(button));
        }

        public static bool Input_GetMouseButtonUp(int button)
        {
            if (!Active) return Input.GetMouseButtonUp(button);
            return (button >= 0 && button < 3 && MouseUp[button]) || (PassThroughDesktopInput && Input.GetMouseButtonUp(button));
        }

        public static bool Input_GetKey(KeyCode key)
        {
            if (!Active) return Input.GetKey(key);
            return Key(KeyHeld, key) || (PassThroughDesktopInput && Input.GetKey(key));
        }

        public static bool Input_GetKeyDown(KeyCode key)
        {
            if (!Active) return Input.GetKeyDown(key);
            return Key(KeyDown, key) || (PassThroughDesktopInput && Input.GetKeyDown(key));
        }

        public static bool Input_GetKeyUp(KeyCode key)
        {
            if (!Active) return Input.GetKeyUp(key);
            return Key(KeyUp, key) || (PassThroughDesktopInput && Input.GetKeyUp(key));
        }

        public static float Input_GetAxis(string axisName)
        {
            if (!Active) return Input.GetAxis(axisName);
            switch (axisName)
            {
                case "Mouse X":
                case "Mouse Y":
                    return 0f; // camera orbiting is replaced by VR locomotion
                case "Mouse ScrollWheel":
                    return ScrollWheel + (PassThroughDesktopInput ? Input.GetAxis(axisName) : 0f);
                default:
                    return PassThroughDesktopInput ? Input.GetAxis(axisName) : 0f;
            }
        }

        public static float Input_GetAxisRaw(string axisName)
        {
            if (!Active) return Input.GetAxisRaw(axisName);
            switch (axisName)
            {
                case "Mouse X":
                case "Mouse Y":
                    return 0f;
                case "Mouse ScrollWheel":
                    return ScrollWheel + (PassThroughDesktopInput ? Input.GetAxisRaw(axisName) : 0f);
                default:
                    return PassThroughDesktopInput ? Input.GetAxisRaw(axisName) : 0f;
            }
        }

        public static Vector3 Input_get_mousePosition()
        {
            return Active ? MousePosition : Input.mousePosition;
        }

        public static Vector2 Input_get_mouseScrollDelta()
        {
            if (!Active) return Input.mouseScrollDelta;
            return new Vector2(0f, ScrollWheel * 10f) + (PassThroughDesktopInput ? Input.mouseScrollDelta : Vector2.zero);
        }

        // ------------------------------------------------------------------ UnityEngine.Screen
        // In VR the game's "screen" is the UI picture (fixed size) and the virtual view frustum, not the desktop window.

        public static int Screen_get_width() => Active ? (int)ScreenWidth : Screen.width;

        public static int Screen_get_height() => Active ? (int)ScreenHeight : Screen.height;

        // ------------------------------------------------------------------ UnityEngine.Camera

        public static Ray Camera_ScreenPointToRay(Camera cam, Vector3 pos)
        {
            if (!IsWorldCamera(cam)) return cam.ScreenPointToRay(pos);
            return IsMousePoint(pos) ? PointerRay : ScreenPointToWorldRay(pos);
        }

        public static Ray Camera_ScreenPointToRay(Camera cam, Vector3 pos, Camera.MonoOrStereoscopicEye eye)
        {
            if (!IsWorldCamera(cam)) return cam.ScreenPointToRay(pos, eye);
            return IsMousePoint(pos) ? PointerRay : ScreenPointToWorldRay(pos);
        }

        public static Vector3 Camera_WorldToScreenPoint(Camera cam, Vector3 position)
        {
            return IsWorldCamera(cam) ? WorldToScreen(position) : cam.WorldToScreenPoint(position);
        }

        public static Vector3 Camera_WorldToScreenPoint(Camera cam, Vector3 position, Camera.MonoOrStereoscopicEye eye)
        {
            return IsWorldCamera(cam) ? WorldToScreen(position) : cam.WorldToScreenPoint(position, eye);
        }

        public static Vector3 Camera_ScreenToWorldPoint(Camera cam, Vector3 position)
        {
            return IsWorldCamera(cam) ? ScreenToWorld(position) : cam.ScreenToWorldPoint(position);
        }

        public static Vector3 Camera_ScreenToWorldPoint(Camera cam, Vector3 position, Camera.MonoOrStereoscopicEye eye)
        {
            return IsWorldCamera(cam) ? ScreenToWorld(position) : cam.ScreenToWorldPoint(position, eye);
        }

        public static Vector3 Camera_ScreenToViewportPoint(Camera cam, Vector3 position)
        {
            if (!IsWorldCamera(cam)) return cam.ScreenToViewportPoint(position);
            return new Vector3(position.x / ScreenWidth, position.y / ScreenHeight, position.z);
        }
    }
}
