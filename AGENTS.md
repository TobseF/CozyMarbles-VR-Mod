# AGENTS.md – working on Cozy Marbles VR

Orientation for coding agents (and humans) starting a new session. The player-facing documentation is
[README.md](README.md); this file is about how the mod is built, tested and debugged.

The maintainer writes German; answer in German. Code, comments, commit messages and the README stay English.
Texts shown in the game go through `Loc.T("Deutsch", "English")`.

## 1. What this is

A BepInEx 5 mod that makes **Cozy Marbles** (Unity 6000.3.14f1, Mono, URP, Steam app 2836460) playable in VR
through OpenXR/SteamVR, made for the Steam Frame. The game's screen-space UI is rendered into textures and shown
as floating panels; a laser or a "grab ball" on the controller replaces the mouse.

- The repository lives **inside the game folder**: `...\steamapps\common\Cozy Marbles\CozyMarblesVR`.
  The game, BepInEx, its config and logs are one level up and are not part of the repository.
- GitHub: https://github.com/TobseF/CozyMarbles-VR-Mod (MIT).

## 2. Layout and entry points

Three assemblies:

| Part | Source | Installed to | Role |
|---|---|---|---|
| Patcher | `src/Patcher/Patcher.cs` | `BepInEx/patchers` | Preloader: rewrites the game's IL calls to `UnityEngine.Input` / `Camera` / `Screen` so they go through the shim (in memory, on every start) |
| Shim | `src/Shim/VRShim.cs` | `BepInEx/plugins/CozyMarblesVR` | Static replacements for those calls. Forwards to Unity while VR is off; in VR it returns the virtual mouse (`MousePosition`, `PointerRay`) |
| Plugin | `src/Plugin/*.cs` | `BepInEx/plugins/CozyMarblesVR` | Everything else |

Plugin start: `Plugin.Awake` → Harmony patches (`GameHooks.cs`) → `XRBootstrap.Initialize` (OpenXR set up in code,
no Unity editor assets) → `VRManager` (one GameObject, `DontDestroyOnLoad`) → optional `StartDialog`.

| File | Responsibility |
|---|---|
| `VRManager.cs` | Rig that follows the game camera, eye/HUD cameras, laser, ball, throw cone, locomotion, VR ↔ monitor (F9, `SetFlat`) |
| `VRInput.cs` | Controller → mouse/keys/events; laser vs. panel vs. world; mod widgets (`HandleWidgets`); grab and marble mode on the trigger; D-pad menu navigation |
| `HudPanel.cs` | Game canvases → render textures → floating panels (main menu/HUD, build toolbox, object options, world labels, top-bar tiles); left-hand menu layout (`LayoutBar`) |
| `GrabMode.cs` | Hand mode: ball tip, ray handed to the game, snap ghost |
| `MarbleDropper.cs`, `MarbleLauncher.cs` | Marble mode: creates the game's own marbles; the waiting marble at the tip (kinematic, `FixedTick` moves it so it can push), direction cone, speed dial |
| `GameHooks.cs` | `Game` facade over game state (placing, placement plane, decorations) + Harmony patches |
| `VRSettingsPanel.cs`, `VRHelpPanel.cs` | The mod's own world-space panels (hit-tested directly, no `GraphicRaycaster`) |
| `ModeToggleButton.cs` | "VR x.y.z" label, main-menu entry, settings row, desktop hint – clones of the game's own UI |
| `ControllerDecor.cs` | Icon plates on every physical button (mode-dependent, from `VRInput.PartAction`), the collar with round push buttons on the off-hand controller (tools, prepare/delete marbles, open menu, object options; laser or ball touch) |
| `Tracking.cs`, `SteamFrameControllerProfile.cs`, `ControllerModels.cs` | Poses and buttons, native Steam Frame bindings, controller models from the user's SteamVR install |
| `VRConfig.cs` | All BepInEx config entries (`BepInEx/config/tobse.cozymarbles.vr.cfg`), incl. `ConfigVersion` migrations |
| `Icons.cs` + `Icons/*.png` | Menu artwork, embedded as resources; drawn by `build/make-icons.py` |
| `Diagnostics.cs` | Debug command interface (section 5) |

## 3. Build and install

```powershell
.\build\build.ps1          # compiles Shim, Patcher, Plugin (VS 2022 Roslyn csc) and installs into ..\BepInEx
.\build\make-dist.ps1      # release zips into dist\ (mod only / with BepInEx)
python build\make-icons.py # only after changing the artwork; rewrites src\Plugin\Icons\*.png
```

- No .csproj: `build/common.ps1` calls `csc` with the game's `Managed` assemblies as references.
  New `.cs` files under `src/Plugin` are picked up automatically, PNGs in `src/Plugin/Icons` are embedded automatically.
- **The build fails while the game runs** (the plugin DLL is locked). The maintainer often has the game running
  with the headset – ask before closing it, and close it gracefully (see section 5), never kill it.
- `lib/` is tracked on purpose (XR assemblies built by `build/build-xr-deps.ps1`, native OpenXR loader, Unity's mock runtime).
- Compiler warnings are muted in `common.ps1`; check errors only.

## 4. Reading the game's code (decompiling)

The game's code is the main reference – most features start with finding the right game class.

```powershell
.\build\decompile.ps1                                 # all of Assembly-CSharp -> %TEMP%\CozyMarblesVR_decompiled\Assembly-CSharp (~860 files, ~1 min)
.\build\decompile.ps1 -Type TimR.Snap.SnapManager     # one type to the console
.\build\decompile.ps1 -Assembly TimR.Events.dll       # other assemblies from Cozy Marbles_Data\Managed
```

Uses the ILSpy engine bundled with Visual Studio 2022. Run it with PowerShell 7 (`pwsh`, the agent's PowerShell tool) – Windows PowerShell 5 cannot load the decompiler. **Never commit decompiled code** (it is the game's code;
keep it in `%TEMP%`). Then grep the output.

Game conventions:

- Namespaces `TimR.*`. Singletons are `X._` (e.g. `MarbleManager._`, `InputManager._`, `GameModeManager._`).
- Events: `EventBus._.PublishEvent(Evt.X, new EventData.X(...))`; keyboard shortcuts go through `InputManager.PublishInputAction`
  (`Game.Publish(Evt)` wraps it and respects text focus/tutorial locks).
- UniRx `ReactiveProperty` everywhere (`.Value`). Private `[SerializeField]` fields are reached with `HarmonyLib.AccessTools.Field`.
- Assembly-CSharp is referenced at compile time, so public game types can be used directly (`typeof(TimR.CtrlZ.UndoManager)`).

Game classes the mod already relies on (good starting points):

| Topic | Classes |
|---|---|
| Placing structures (always on the voxel grid) | `StructureSpawner`, `ActiveVoxelSelector` (placement plane, `ChangeHeightByVoxels`), `StructureMover`, `StructureSpawnPreview` |
| Decorations / "Pins" (free on surfaces, raycast from the mouse) | `DecoSpawner` (`RaycastForDeco`, `DecoPos`) |
| Magnet (snap to other pieces, not the grid) | `TimR.Snap.SnapManager` (`Evt.ToggleSnapAction`) |
| Marbles | `MarbleManager`, `Marble`, `CustomMarblesSO` (`InstantiateMarble`, `GetRandomDefaultAllowedMarbleId`), `MarbleSpawner3` |
| Top bar | `RollMarbleUi` (`_rollMarbleButton`), `UndoManager` (`_undoButton`, `_redoButton`); canvas `Canvas_RollMarble` |
| Menus / modes | `MainMenuUi`, `PauseManager`, `GameModeManager` (`ModeIsGameOrRace`), `VideoSettingsUi` |
| Camera | `CamMovementManager` (`TickMode0` = free camera), `CamMouseFollow` |

## 5. Debug mode and local testing

### Config switches (`BepInEx/config/tobse.cozymarbles.vr.cfg`, section `[Debug]`)

| Key | Effect |
|---|---|
| `MockRuntime = true` | Unity's mock OpenXR runtime (`lib/mock`) instead of SteamVR – VR without headset |
| `SimulateControllers = true` | Simulated controllers (desktop mouse, or scripted by diagnostics commands) |
| `Diagnostics = true` | Enables the command file interface below and extra logging |

**Back up the config before a test and restore it afterwards** – tests change settings (grab mode, panel poses …).
New config entries are written into the file on the next start.

### Starting the game

- Launch the exe directly with a temporary `steam_appid.txt` (`2836460`) in the game folder; **delete it afterwards**.
  Do not launch through Steam for tests.
- Monitor mode: launch option `-novr`, `[General] Enabled = false`, or **F9** at runtime (also `flat on/off` command).
  `StartIn` / `ShowStartDialog` decide what happens at launch (start window = IMGUI `StartDialog`, `start vr|monitor` confirms it).
- Close the game with the `quit` command or the window's close button. Never kill the process – repeated force-kills
  led to Steam "Unable to Sync" cloud warnings.
- Logs: `BepInEx/LogOutput.log` (fresh each start). Wait for `UI panel texture` before sending commands in VR mode.

### Diagnostics commands

Commands are lines in `BepInEx/cozyvr-cmd.txt`; the mod polls, executes and deletes the file. Use the helper,
which refuses to run when `Diagnostics` is off, appends a marker and waits until everything ran. Check first whether
the game is already running (`tasklist`) – it may be the maintainer's own session with the headset:

```powershell
.\build\diag.ps1 'wait 5', 'aimpx 170 837', 'tap trigger', 'wait 3', 'uishot play'
.\build\diag.ps1 dump -Grep 'panels:|grab:|marbles:'
powershell -NoProfile -File build/diag.ps1 'grab on; wait 1; dump' -Grep 'launcher:'   # from bash: one string, ; separated
```

The full list is in `Diagnostics.Execute`. The important ones:

- Controller buttons: `aimring <glyph>` aims the laser at a collar button (`pointer`, `hand`, `marble`, `play`, `trash_marbles`, `menu_open`, `props`); `pokering <glyph> <above>` puts the ball of the right hand onto it – the ball sits 0.03 × world scale below the grip, so `above` depends on `[General] WorldScale` (the maintainer uses ~22, not 8).
- Pointing: `aimpx x y` (UI texture pixel, origin bottom-left; main UI x 0..W, build toolbox W..2W, options 2W..3W,
  top bar 3W..4W with W = `UiResolutionWidth`, default 2560), `aimwidget bar|corner|vr|bbar|bcorner|info|mvr|mgrab|mmarble|bctl0|bctl1|tile<name> [dx dy]` (dx dy: metres along the widget's right / up),
  `aimsettings n`, `aimsethandle`, `aimworld x y z`, `aim yaw pitch`, `aimtag <tag> [i]`.
- Buttons: `tap|down|up trigger|grip|a|b|x|y|ltrigger|lgrip|lb|rb|dup|...` (names in `Tracking.Simulate`), `stick lx ly rx ry`.
- Hands/head: `handr|handl dx dy dz` (offset from the default pose), `handlrot pitch yaw roll` (turn the simulated
  left controller, e.g. `handlrot -100 15 0` = button side facing you, tip up), `aimfwd m` (simulated aim pose in front of the grip,
  like a real controller), `head x y z yaw pitch` (mock headset), `recenter`.
- State: `tool 0|1|2` (pointer / hand / marble), `grab on|off`, `marble on|off`, `throw 0..6` (throw level), `physics on|off` (marble physics), `flat on|off`,
  `spectator on|off`, `specscale x`, `eyeres <width>` (headset pixels per eye, 0 = SteamVR), `fps on|off`,
  `lockmouse` (sets the game's mouse lock, to test the stale-lock guard).
- Output (`BepInEx/cozyvr-out/*.png`): `eyeshot` (headset view), `uishot` (main UI texture), `buildshot` (panel atlas),
  `overlayshot`, `handshot name l|r|b|m yaw pitch` (b = grab ball, m = newest marble with the whole scene), `shot` (desktop).
- `dump` logs XR/session state, cameras, panels, tiles, raycast under the mouse, grab/deco/marble state;
  `tree <canvasName> [depth]` dumps a canvas hierarchy.

Headless recipe that gets into a level (UI width 2560): `aimpx 170 837` + `tap trigger` (main menu "Play"),
then `uishot` and click the first **example** level – its row moves down with every level the maintainer saves
(with two saved levels: `aimpx 1071 588`). Never click the maintainer's saved levels. `wait 12` for loading.
Undo anything you place. The game language follows the maintainer's setting (currently German).

### Pitfalls when testing

- Desktop screen capture of the game window is black (GDI); use the in-game shot commands (or PrintWindow with flag 2).
- `shot` (ScreenCapture) leaves out screen-space overlay canvases such as the spectator info banner. To see exactly
  what the desktop window shows, capture it with `user32!PrintWindow` (flag 3, after `SetProcessDPIAware`).
- `DesktopScreen` owns the desktop window in VR: spectator camera on/off (F11), rendered into a smaller texture for
  `DesktopViewScale` < 1 (format without alpha – post-processing leaves alpha < 1 and the picture turned dark/flat),
  and the "spectator view off" screen whose buttons are hit-tested with the real mouse.
- Headset and spectator view: a base camera renders the world, an overlay camera the HUD layer (menus, laser,
  hands, decals) without post-processing. The overlay camera keeps the world's depth (`m_ClearDepth = false`), and
  the panel material writes depth with alpha clipping – menus are ordinary objects in the room. The spectator view
  (PrintWindow) shows the same depth behaviour as the headset, so use it to check occlusion.
- `eyeshot` renders world and HUD layer with one camera; good for content, not for judging depth.
- In simulation the aim pose equals the grip pose, so the grab ball and throw cone sit inside the controller model –
  use `aimfwd 0.12` before judging their visibility.
- World scale 8: a marble or pin is only a few pixels from head distance; use `handshot … m`/`… b` or the `dump` numbers.
- The start window (`StartDialog`) is a uGUI overlay in the game's dialog style (sprites `ui_*` from `make-icons.py`,
  game font). The game's input modules are paused while it is open, so it hit-tests the real mouse itself. Test it
  with `[General] ShowStartDialog = true`, `shot`, and real mouse input via `user32` (`SetProcessDPIAware`,
  `SetCursorPos`, `mouse_event`) – Windows display scaling applies. Ticking "do not show again" in a test saves
  `ShowStartDialog = false` (restore the config afterwards); `start vr|monitor` confirms it without a mouse.
- `handshot` cameras look from a fixed yaw; billboards that face the head (speed dial) appear mirrored from behind.
- The game removes marbles outside the level bounds after 15 s (`MarbleDestroyer` trigger zones) – drop test marbles
  near the middle of the board, and do not mistake that for a mod bug.
- `handabove x y z above` puts the simulated hand above a world point aiming down (tip = point when `above` = 0.03 × world scale);
  sweeping it in small steps (`wait 0.03` between) is how marble pushing was tested.
- If the build toolbox stands free in the room it may be out of view; `down ltrigger` carries it on the hand.
  With "always on hand", tapping `ltrigger` hides/shows it.
- Things that need the real headset (comfort, sizes, controller models, button feel) cannot be verified headless –
  say so instead of claiming them.

## 6. Real headset / SteamVR notes

- Stereo must be MultiPass; `XRSRPSettings.useVisibilityMesh` must be false; the HUD overlay camera uses the base
  camera's renderer index.
- Launching from the in-headset library opens the game in SteamVR's "VR theater": the OpenXR session stays
  SYNCHRONIZED and never becomes visible. Launch from desktop Steam. There is no per-game "desktop theater" option.
- Never gate XR rendering on session VISIBLE (SteamVR only makes a session visible after frames are submitted), and
  never end a hidden session automatically – the headset is often connected after the game started.
- "Game not shown in headset": check `Steam\logs` first. `vrserver.txt` "entering standby" ~5 s after "leaving
  standby" plus `vrcompositor.txt` "WaitForAcquire timed out" means the Steam Frame is not in PC-VR streaming
  (restart SteamVR, reconnect in VR mode) – not a mod problem.
- During streaming the game window may sit on a portrait display; the mod draws its own desktop spectator view.

## 7. Lessons learned

- Read the game's code before guessing: e.g. the magnet only controls piece-to-piece snapping; structures are always
  on the voxel grid, only decorations move freely.
- The game reads the mouse through `InputManager.MousePos` and `Camera.ScreenPointToRay`; in VR the shim returns
  `VRShim.PointerRay` for the mouse position. Hand mode works by handing the game a different ray (`GrabMode.BuildRay`).
- `MaskLeftClick()` clears the click for one frame only; while a button is held it reappears as a new "down" every
  frame. Keep your own edge detection (see the marble trigger in `VRInput.Process`).
- Clone the game's own UI (buttons, toggles) for anything that should look native; the mod's own panels use the
  icon PNGs and the warm-yellow hover tint (`HotTint`).
- Panels: all top-bar tiles are scaled to the height of the "Prepare/Roll marbles" button; the mod's buttons
  (`MiniButton`) share that height. World-space panels should open facing the viewer at a comfortable distance.
- Python heredocs with many quotes break in the agent's bash tool; write patch scripts to a scratch file and run them.
- Python `str.replace` replaces every occurrence – assert the count when patching code with scripts.
- Kinematic bodies only push others when moved with `Rigidbody.MovePosition` in `FixedUpdate`; teleporting the
  transform does not. Big jumps per physics step tunnel through.
- In marble mode L1/L2 are taken over before the bindings are applied (`ReadButtons`): L1 = faster, L2 tap = slower,
  L2 held > 0.35 s is passed on as the build-menu button (`MenuPanelButton(..., alreadyHeld)`). An earlier version
  swallowed L2 completely and the left menu could no longer be opened – never take a button away without a way back.
- The game locks the mouse while the right button (B) turns the camera and only unlocks on the release frame; a missed
  frame leaves `InputManager.MouseLocked` set and the build menu ignores piece clicks (tabs still work).
  `VRInput.ReleaseStaleMouseLock` clears such a lock; check `game: mouseLocked=` in `dump` when clicks "do nothing".
- Controller layout (ConfigVersion 6): L2 = next tool (in menus: menu back in front), both grips = world (move / scale),
  X = build menu on/off, A/Y = throw level in marble mode, L1 = Shift, right Menu = main menu/pause (`PointerMenu`),
  left View = VR settings (`OffMenu`), R2 into empty space in menus = position reset. The game's top bar and the mod's
  menu buttons are gone from the panels (`LayoutBar` shows nothing): their functions are ring buttons in
  `ControllerDecor` that press the game's own controls (`HudPanel.ClickTile`, `RollMarbleUi`, `DestroyAllMarbles`).
  SteamVR ships every button as its own OBJ next to the controller model (same coordinate space) – `ControllerModels.Parts`
  reads them for the button labels.
- The main menu panel is cropped to its content (`ComputeCrop`, small gadgets at the right screen edge excluded) and
  centred on it; the build menu is mounted rigidly on the off-hand controller (`ControllerDecor.TryGetMount`: past
  the collar, in the plane of the controller's face, computed from the grip pose and the model's own axes `F`/`N`, not
  from the grip axes – those turned it sideways once). The model's `F` points from the handle to the tip; held
  pointing forward the face looks up, so the menu is a tray then and a tablet when the button side faces you.
  Without controller models it falls back to floating above the hand, billboarded. Shown build menu = open game toolbox (`KeepToolboxInSync`).
  `PlaceBuildControls` puts two `MiniButton` handles (resize at the corner, tilt above it; 2 cm regardless of the menu
  scale) right of the toolbox's lower right corner, only while attached and the toolbox is open. Held, they run through
  `BeginDrag`/`UpdateDrag`/`EndDrag` as `HandleDrag` (not `_dragPanel`, so the menu keeps following the hand): resize
  scales about the lower edge centre so the held corner stays under the laser; tilt adds the change of the laser's
  angle in the controller's tilt plane (`HandAngle`, basis `_mountFwd`/`-_mountFace` from the last `PlaceBuild`). Live
  values sit in `_liveScale`/`_liveTilt` and go into the config only on release (no file write per frame).
  Tilt `[HUD.BuildMenu] AttachedTiltTowardsYou` −75..75, 0 = flush with the ring, positive towards the viewer (the placement
  formula uses the opposite sign); `SnapTilt` holds 0 within ±5° and catches up over the next 5° without a jump.
  (Renamed from `AttachedTiltDegrees`, whose sign and zero differed – old values are not carried over.) While a handle is held the laser end comes
  from `HandleDragPoint` (the generic drag used the main panel's plane, so the laser jumped to the screen centre);
  while tilting, laser and reticle are hidden (`TiltDragging`).
  Test the tilt with `aim yaw pitch` while `down trigger` is held: moving the aim point (`aimwidget bctl1 0 dy`) barely
  turns a laser that comes from below, almost parallel to the menu.
- Start: the player and the main menu are recentred as soon as the head pose is valid (not only when the headset
  reports "worn" – that never happened for some sessions and left the player somewhere in the world).
- Never draw menus "always on top" in VR. It looked fine on the monitor, but in the headset a panel painted over a
  closer object gets occlusion cues ("in front") that contradict its stereo disparity ("behind"): both eyes disagree,
  labels seem to sit beside their object, controller buttons poke through the build menu. The same goes for one
  flat label layer at a fixed distance. Fixes in place: shared depth buffer, depth-writing panels, every world label
  cut out of the label texture and placed at the world point the game projected it from (`VRShim.ProjectedWorld`,
  recorded in `Camera_WorldToScreenPoint`), the menu viewpoint (`VRManager.UpdateMenuViewpoint`: step back/up while
  a menu is blocked, chosen once per visit – the main menu's showcase run has moving parts) and
  `HudPanel.KeepInFrontOfScenery` (pull the menu closer and shrink it as the last resort). Obstacle tests ignore
  moving bodies (marbles) and volumes you are inside of (`LevelBounds`).
- Headset resolution: URP copies its asset's `renderScale` into the XR display every frame, so
  `XRSettings.eyeTextureResolutionScale` alone is silently reset to 1. `VRManager.ApplyRenderScale` first measures
  SteamVR's recommended size (while the scale is still the game's own; the session may only start later, eye size
  0 until then), then turns the chosen per-eye width (`EyeResolution`, presets in `VRManager.EyePresets`) into the URP
  asset's `renderScale`; everything is restored when VR ends. `DesktopScreen` shrinks the spectator texture by the
  same factor. `dump` prints `XR: eye=` and `urp:`. The game's resolution setting only sizes the desktop window.
- The game's URP asset has the GPU Resident Drawer with GPU occlusion culling on. In VR (multi-pass, changed render
  scale) parts of the scenery went missing in one eye only – most likely this culling (not confirmed in the headset
  yet); `ApplyRenderScale` switches it off while VR runs.
- The VR settings panel has two columns (one column was ~1.6 m tall and reached into the table once menus got depth).
- Opaque things must be opaque: the collar, the tool base and the round button bodies use URP/Unlit (opaque, depth
  writes, shading baked into UV.x and read from a grey ramp – the unlit shader ignores vertex colours). Only the flat
  plates and icons are transparent (`Sprites/Default`). Transparent objects write no depth and are sorted per object by
  distance: the collar used to cover the buttons on its far side, and the laser dot (drawn before the icons) was painted
  over – laser and dot now render after the icons (queue Transparent + 30).
- `ControllerDecor.HitRing` tests a sphere around each round button (the other hand's laser comes in at a slant and
  slipped past the flat top); overlapping spheres are resolved by the closest centre. It runs in `HandleWidgets` before
  the tools, so in hand mode the laser appears and the trigger clicks when it points at a round button.
- Keep the README in sync with player-visible changes (controls, menus, settings).
