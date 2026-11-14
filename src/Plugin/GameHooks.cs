using System;
using System.Reflection;
using HarmonyLib;
using TimR.Cam;
using TimR.Deco;
using TimR.Events;
using TimR.GameJamMarbles;
using System.Collections.Generic;
using TimR.Cam;
using TimR.InputStuff;
using TimR.StructureOptions;
using UnityEngine;

namespace CozyMarblesVR
{
    /// <summary>Typed access to the bits of game state the VR layer needs.</summary>
    public static class Game
    {
        static StructureSpawner _spawner;
        static DecoSpawner _deco;
        static ActiveVoxelSelector _voxel;
        static CamMouseFollow _mouseFollow;
        static StructureSpawnPreview _spawnPreview;
        static CursorHold _cursorHold;
        static float _nextLookup;
        static readonly FieldInfo Movers = AccessTools.Field(typeof(StructureSpawner), "_structureMovers");

        static readonly FieldInfo PlaneHeight = AccessTools.Field(typeof(ActiveVoxelSelector), "_planeHeight");
        static readonly MethodInfo PublishInputAction = AccessTools.Method(typeof(InputManager), "PublishInputAction");

        public static void Refresh()
        {
            if (Time.unscaledTime < _nextLookup) return;
            _nextLookup = Time.unscaledTime + 2f;
            if (_spawner == null) _spawner = UnityEngine.Object.FindFirstObjectByType<StructureSpawner>();
            if (_deco == null) _deco = UnityEngine.Object.FindFirstObjectByType<DecoSpawner>();
            if (_voxel == null) _voxel = UnityEngine.Object.FindFirstObjectByType<ActiveVoxelSelector>();
            if (_spawnPreview == null) _spawnPreview = UnityEngine.Object.FindFirstObjectByType<StructureSpawnPreview>(FindObjectsInactive.Include);
            if (_cursorHold == null) _cursorHold = UnityEngine.Object.FindFirstObjectByType<CursorHold>(FindObjectsInactive.Include);
            if (_mouseFollow == null) _mouseFollow = UnityEngine.Object.FindFirstObjectByType<CamMouseFollow>(FindObjectsInactive.Include);
            // The menu camera sways with the mouse position; in VR that would rock the whole world.
            if (_mouseFollow != null && _mouseFollow.enabled && VRShim.Active) _mouseFollow.enabled = false;
        }

        public static void RestoreFlatBehaviour()
        {
            if (_mouseFollow != null) _mouseFollow.enabled = true;
        }

        /// <summary>Main menu, marble editor or pause menu: no building going on, the UI has the stage.</summary>
        public static bool InMenu
        {
            get
            {
                try
                {
                    var mode = TimR.MainMenu.GameModeManager._;
                    if (mode == null) return true;
                    if (!mode.ModeIsGameOrRace) return true;
                    var pause = TimR.MainMenu.PauseManager._;
                    return pause != null && pause.IsPaused;
                }
                catch { return false; }
            }
        }

        /// <summary>A structure or decoration is attached to the pointer and waiting to be placed.</summary>
        public static bool Placing
        {
            get
            {
                try
                {
                    return (_spawner != null && _spawner.IsInSpawnMoveOrDuplicateMode) || (_deco != null && _deco.IsInSpawnMoveOrDuplicateMode);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Only a decoration is being placed or moved (no structure). Decorations are not bound to the grid: the game
        /// puts them on whatever surface the pointer ray hits (with the magnet off they never snap anywhere).
        /// </summary>
        public static bool PlacingDecoOnly
        {
            get
            {
                try
                {
                    return _deco != null && _deco.IsInSpawnMoveOrDuplicateMode && (_spawner == null || !_spawner.IsInSpawnMoveOrDuplicateMode);
                }
                catch { return false; }
            }
        }

        /// <summary>Diagnostics: where the decoration preview sits and what the pointer ray hits for it.</summary>
        public static string DebugDeco()
        {
            try
            {
                if (_deco == null) return "deco: -";
                bool hit = _deco.RaycastForDeco(VRShim.PointerRay, out RaycastHit h);
                return $"deco: placing={_deco.IsInSpawnMoveOrDuplicateMode} pos={_deco.DecoPos.Value} rayHit={hit} at={(hit ? h.point.ToString() + " " + h.collider.name : "-")}";
            }
            catch (Exception e) { return "deco: " + e.Message; }
        }

        /// <summary>Height of the horizontal plane the game intersects the pointer with while placing structures.</summary>
        public static bool TryGetPlacementPlane(out float height)
        {
            height = 0f;
            try
            {
                if (_spawner == null || _voxel == null || PlaneHeight == null) return false;
                if (!_spawner.IsInSpawnMoveOrDuplicateMode || !_voxel.IsEnabled) return false;
                height = (float)PlaneHeight.GetValue(_voxel);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Moves the placement plane (and with it the piece) in voxel steps until it matches the given world height.</summary>
        public static void TrackHeight(float worldY)
        {
            try
            {
                if (_voxel == null || !TryGetPlacementPlane(out float h)) return;
                float diff = worldY - h;
                if (Mathf.Abs(diff) < 0.12f) return; // voxels are 0.2 high, hysteresis of 0.6 voxel
                int steps = Mathf.Clamp(Mathf.RoundToInt(diff / 0.2f), -3, 3);
                if (steps != 0) _voxel.ChangeHeightByVoxels(steps);
            }
            catch { }
        }

        /// <summary>The game's own live previews of the piece(s) being placed or moved.</summary>
        public static void CollectPreviewRoots(List<Transform> roots)
        {
            roots.Clear();
            try
            {
                if (_spawner == null) return;
                if (_spawner.IsInSpawnMode)
                {
                    var p = _spawnPreview != null ? _spawnPreview.Preview : null;
                    if (p != null) roots.Add(p.transform);
                }
                else if (_spawner.IsInMoveOrDuplicateMode && Movers != null && Movers.GetValue(_spawner) is List<StructureMover> list)
                {
                    foreach (var m in list)
                    {
                        if (m == null) continue;
                        try { var t = m.Preview; if (t != null) roots.Add(t); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>Horizontal shift from the freely moving preview to the grid cell the piece would snap into.</summary>
        public static bool TryGetGhostDelta(out Vector3 delta)
        {
            delta = default;
            try
            {
                if (_voxel == null || !_voxel.IsEnabled) return false;
                Vector3 snapped = _voxel.PosVoxelWithSnapOffsets.Value;
                Vector3 free = _voxel.WorldMousePos.Value;
                if (float.IsInfinity(free.x) || float.IsInfinity(free.z)) return false;
                delta = new Vector3(snapped.x - free.x, 0f, snapped.z - free.z);
                if (delta.sqrMagnitude > 0.5f * 0.5f) delta = Vector3.zero; // sanity: never further than a voxel or so
                return true;
            }
            catch { return false; }
        }

        /// <summary>Same as holding the left mouse button on a selected piece for a second: starts moving it.</summary>
        public static void TriggerCursorHold()
        {
            try { if (_cursorHold != null) _cursorHold.TriggerSuccess(); } catch { }
        }

        /// <summary>Raises one of the game's keyboard-shortcut events (respects text-field focus and tutorial locks).</summary>
        public static void Publish(Evt evt)
        {
            try
            {
                var input = InputManager._;
                if (input != null && PublishInputAction != null)
                    PublishInputAction.Invoke(input, new object[] { evt });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Publishing " + evt + " failed: " + (e.InnerException ?? e).Message);
            }
        }
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.MousePos), MethodType.Getter)]
    static class InputManager_MousePos
    {
        static bool Prefix(ref Vector3 __result)
        {
            if (!VRShim.Active) return true;
            __result = VRShim.MousePosition;
            return false;
        }
    }

    // The game warps the OS cursor to keep it on a piece; a laser pointer cannot be warped.
    [HarmonyPatch(typeof(InputManager), nameof(InputManager.WarpMouse))]
    static class InputManager_WarpMouse
    {
        static bool Prefix() => !VRShim.Active;
    }

    // Runs only while the game allows free camera movement, which is exactly when VR locomotion should work.
    [HarmonyPatch(typeof(CamMovementManager), "TickMode0")]
    static class CamMovementManager_TickMode0
    {
        static void Prefix(CamMovementManager __instance)
        {
            if (VRShim.Active && VRManager.Instance != null)
                VRManager.Instance.Locomotion(__instance);
        }
    }
}
