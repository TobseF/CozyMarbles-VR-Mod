using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CozyMarblesVR
{
    /// <summary>
    /// The game's screen-space UI in VR, split into two independent floating panels:
    ///
    ///  - Main panel: every canvas except the build toolbox (main menu, pause, dialogs, in-game HUD).
    ///      In menus it floats at the "menu" pose, during play at the "game HUD" pose. Always drawn on top.
    ///  - Build panel: only the game's build toolbox (Canvas_StructureSelect), rendered by its own UI camera.
    ///      Lies in the room where it was put or sits on the off-hand controller.
    ///
    /// Both UI cameras render into textures with the same layout size W x H. The build camera draws into the right
    /// half of a 2W wide texture, so its pixels (x = W..2W) never overlap the main UI's pixels (x = 0..W): one virtual
    /// mouse can address both and each GraphicRaycaster only reacts to its own half.
    /// </summary>
    public class HudPanel
    {
        public const int HudLayer = 31;
        const string BuildCanvasName = "Canvas_StructureSelect";
        const string OptionsCanvasPrefix = "Canvas_StructureOptions";

        // canvases the game positions over world objects (via Camera.WorldToScreenPoint): shown on a see-through
        // layer that matches the game's virtual screen, so each label appears right in front of its object
        static readonly HashSet<string> OverlayCanvasNames = new HashSet<string>
        {
            "Canvas_Marble_Names", "Canvas_Spawner_Info", "Canvas_Checkpoints_Info", "Canvas_Checkpoints_MarblePassedUi",
            "Canvas_Portal_ConnectorUi", "Canvas_BoxSelect", "Canvas_MagicDraw_Controls"
        };

        // Texture atlas for the "slotted" sub panels: slot 0 unused (main UI has its own texture),
        // 1 = build toolbox, 2 = object options, 3 = top bar (undo/play/delete/race/camera/pause) as separate tiles
        const int AtlasSlots = 4;

        public enum Placement { Menu, GameHud, BuildFree, BuildAttached, Options, Overlay, Hidden }
        public enum Widget { None, Bar, Corner, Settings, Info, Grab, Marble, BuildResize, BuildTilt }
        enum Drag { None, Move, Scale }

        // ---------------------------------------------------------------- one floating panel

        class PanelView
        {
            public readonly Transform Root, Quad, Bar, Corner, Gear;
            public readonly Material Mat, BarMat, CornerMat, GearMat;
            public Rect Crop;               // shown part of the texture, in texture (= mouse) pixels
            public Vector2 Anchor;          // texture pixel that sits at the panel's pose position
            public float MetersPerPixel = 0.0005f;
            public Vector3 Pos;
            public Quaternion Rot = Quaternion.identity;
            public Placement Placement = Placement.Hidden;
            public bool Solid;              // the whole rectangle blocks the laser (no pointing "through" it)
            public bool HasWidgets;
            public bool ShowGear = true;
            public float WidgetScale = 1f;  // the panel was pulled closer (and smaller): its handles shrink with it
            public RenderTexture Tex;

            public PanelView(string name, Transform parent)
            {
                Root = new GameObject(name).transform;
                Root.SetParent(parent, false);
                Mat = CreateMaterial();
                Quad = MakeQuad(name + " Picture", Mat, Root);
                BarMat = Icons.Material("grab_bar");
                CornerMat = Icons.Material("resize");
                GearMat = Icons.Material("btn_vr");
                Bar = MakeQuad(name + " Grab Bar", BarMat, Root);
                Corner = MakeQuad(name + " Scale Corner", CornerMat, Root);
                Gear = MakeQuad(name + " VR Button", GearMat, Root);
                Root.gameObject.SetActive(false);
            }

            public bool Visible => Placement != Placement.Hidden && Tex != null;
            public float WidthM => Crop.width * MetersPerPixel;
            public float HeightM => Crop.height * MetersPerPixel;

            public void SetTexture(RenderTexture tex)
            {
                Tex = tex;
                Mat.mainTexture = tex;
                if (Mat.HasProperty("_BaseMap")) Mat.SetTexture("_BaseMap", tex);
            }

            public void Apply()
            {
                bool visible = Visible && Crop.width > 1f && Crop.height > 1f;
                if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
                if (!visible) return;

                Root.localPosition = Pos;
                Root.localRotation = Rot;
                float s = MetersPerPixel;
                Quad.localPosition = new Vector3((Crop.center.x - Anchor.x) * s, (Crop.center.y - Anchor.y) * s, 0f);
                Quad.localScale = new Vector3(Crop.width * s, Crop.height * s, 1f);

                var scale = new Vector2(Crop.width / Tex.width, Crop.height / Tex.height);
                var offset = new Vector2(Crop.xMin / Tex.width, Crop.yMin / Tex.height);
                if (Mat.HasProperty("_BaseMap")) { Mat.SetTextureScale("_BaseMap", scale); Mat.SetTextureOffset("_BaseMap", offset); }
                Mat.mainTextureScale = scale;
                Mat.mainTextureOffset = offset;

                // below the panel: round VR button, grip bar in the middle; small resize button at the lower right corner
                float w = Crop.width * s, k = WidgetScale;
                float cx = (Crop.center.x - Anchor.x) * s, bottom = (Crop.yMin - Anchor.y) * s;
                float barW = Mathf.Clamp(w * 0.2f, 0.14f * k, 0.32f * k), barH = barW / 5f, y = bottom - 0.012f * k - barH * 0.5f;
                float gear = Mathf.Max(0.042f * k, barH * 1.4f);
                SetWidget(Bar, HasWidgets, new Vector3(cx, y, 0f), new Vector3(barW, barH, 1f));
                SetWidget(Gear, HasWidgets && ShowGear, new Vector3(cx - barW * 0.5f - 0.012f * k - gear * 0.5f, y, 0f), new Vector3(gear, gear, 1f));
                SetWidget(Corner, HasWidgets, new Vector3(cx + w * 0.5f + 0.016f * k, bottom - 0.016f * k, 0f), new Vector3(0.03f * k, 0.03f * k, 1f));
            }

            /// <summary>Laser hit on the picture: pixel in texture (= mouse) coordinates.</summary>
            public bool Raycast(Ray ray, out Vector2 pixel, out Vector3 point, out bool inside, out float dist)
            {
                pixel = default; point = default; inside = false; dist = float.MaxValue;
                if (!Root.gameObject.activeInHierarchy) return false;
                Vector3 n = Quad.forward;
                float denom = Vector3.Dot(ray.direction, n);
                if (denom < 1e-4f) return false;
                float t = Vector3.Dot(Quad.position - ray.origin, n) / denom;
                if (t <= 0f) return false;
                dist = t;
                point = ray.origin + ray.direction * t;
                Vector3 local = Quad.InverseTransformPoint(point);
                pixel = new Vector2(Crop.xMin + (local.x + 0.5f) * Crop.width, Crop.yMin + (local.y + 0.5f) * Crop.height);
                inside = Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f;
                return true;
            }

            public bool Contains(Vector2 pixel) => Crop.Contains(pixel);

            public Vector3 PixelToWorld(Vector2 pixel)
            {
                return Quad.TransformPoint(new Vector3((pixel.x - Crop.xMin) / Mathf.Max(1f, Crop.width) - 0.5f, (pixel.y - Crop.yMin) / Mathf.Max(1f, Crop.height) - 0.5f, 0f));
            }

            public Widget HitWidget(Ray ray, out Vector3 hit, out float dist)
            {
                hit = default; dist = float.MaxValue;
                Widget best = Widget.None;
                if (!HasWidgets || !Root.gameObject.activeInHierarchy) return best;
                if (HitQuad(Bar, ray, out Vector3 h1, out float d1) && d1 < dist) { best = Widget.Bar; hit = h1; dist = d1; }
                if (HitQuad(Corner, ray, out Vector3 h2, out float d2) && d2 < dist) { best = Widget.Corner; hit = h2; dist = d2; }
                if (HitQuad(Gear, ray, out Vector3 h3, out float d3) && d3 < dist) { best = Widget.Settings; hit = h3; dist = d3; }
                return best;
            }

            public void SetHover(Widget w, bool moving, bool scaling)
            {
                BarMat.color = (w == Widget.Bar || moving) ? HotTint : Color.white;
                CornerMat.color = (w == Widget.Corner || scaling) ? HotTint : Color.white;
                GearMat.color = w == Widget.Settings ? HotTint : Color.white;
            }
        }

        /// <summary>A button of the mod itself, drawn from an icon (round VR / manual buttons, mouse-hand toggle).</summary>
        class MiniButton
        {
            public readonly Transform Quad;
            public readonly Material Mat;
            public float Aspect = 1f;   // width / height
            string _icon;

            public MiniButton(string name, string icon, Transform parent)
            {
                _icon = icon;
                Mat = Icons.Material(icon);
                Quad = MakeQuad(name, Mat, parent);
                Quad.gameObject.SetActive(false);
            }

            public void Place(bool show, Vector3 pos, Quaternion rot, float height)
            {
                if (Quad.gameObject.activeSelf != show) Quad.gameObject.SetActive(show);
                if (!show) return;
                Quad.localPosition = pos;
                Quad.localRotation = rot;
                Quad.localScale = new Vector3(height * Aspect, height, 1f);
            }

            public void SetIcon(string icon)
            {
                if (icon == _icon) return;
                _icon = icon;
                Mat.mainTexture = Icons.Get(icon);
            }

            public void SetHover(bool hover) { Mat.color = hover ? HotTint : Color.white; }
        }

        /// <summary>One part of the game's top bar, shown as its own small panel above the left-hand menu.</summary>
        class Tile
        {
            public string Name;
            public float Scale = 1f;
            public readonly List<RectTransform> Srcs = new List<RectTransform>();
            public PanelView View;
            public bool Found;
        }

        static void SetWidget(Transform t, bool show, Vector3 pos, Vector3 scale)
        {
            if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
            if (!show) return;
            t.localPosition = pos;
            t.localScale = scale;
        }

        static readonly Color HotTint = new Color(1f, 0.86f, 0.5f, 1f); // hover: icons turn warm yellow

        // ---------------------------------------------------------------- state

        readonly Transform _rig;
        readonly PanelView _main, _build, _options, _overlay;
        readonly List<Tile> _tiles = new List<Tile>();
        readonly MiniButton _miniInfo, _miniVr, _miniGrab, _miniMarble;
        readonly MiniButton[] _buildCtl;   // resize and tilt handles at the lower right corner of the build menu
        static readonly Widget[] BuildCtlWidgets = { Widget.BuildResize, Widget.BuildTilt };
        MiniButton _hoverMini;
        RenderTexture _mainRt, _atlasRt, _overlayRt;

        Camera _uiCam, _buildCam, _optionsCam, _overlayCam, _barCam;
        readonly List<Canvas> _barCanvases = new List<Canvas>();
        readonly List<Canvas> _mainCanvases = new List<Canvas>();
        readonly List<Graphic> _disabledGraphics = new List<Graphic>();
        readonly List<GameObject> _hiddenObjects = new List<GameObject>();
        Canvas _pauseCanvas;
        Rect _mainGameCrop;
        readonly List<PanelView> _solidViews = new List<PanelView>();
        readonly List<Canvas> _optionsCanvases = new List<Canvas>();
        readonly List<Canvas> _overlayCanvases = new List<Canvas>();
        readonly List<GraphicRaycaster> _overlayRaycasters = new List<GraphicRaycaster>();
        readonly List<GraphicRaycaster> _mainRaycasters = new List<GraphicRaycaster>();
        bool _overlayTarget;
        Component _optionsUi; // the game's StructureOptionsUi
        UniversalAdditionalCameraData _uiData;
        Camera _stackOwner;
        CameraClearFlags _uiClearFlags;
        Color _uiBackground;
        float _uiDepth;
        bool _uiHdr, _uiMsaa;
        Canvas _buildCanvas;
        Component _buildUi;  // the game's StructureTypeSelectUi

        struct ConvertedCanvas { public Canvas Canvas; public int Order; public int SortingLayer; }
        readonly List<ConvertedCanvas> _converted = new List<ConvertedCanvas>();
        float _nextCanvasScan, _nextCropUpdate;

        // reference frame for stored poses: head position and heading at the last recenter
        Vector3 _frameOrigin = new Vector3(0f, 1.6f, 0f);
        Quaternion _frameRot = Quaternion.identity;

        // build-menu button state
        bool _menuHidden, _holding, _pendingTap, _pendingHide, _buttonPrev;
        float _buttonDownTime;

        // dragging
        Drag _drag;
        PanelView _dragPanel, _focus;
        float _dragDist, _dragD0, _dragMpp0;
        Vector3 _dragOffset;

        PointerEventData _ped;
        readonly List<RaycastResult> _hits = new List<RaycastResult>();

        public int Width => TextureWidth;
        public int Height => TextureHeight;
        public Camera UiCamera => _uiCam;
        public RenderTexture BuildTexture => _atlasRt;
        public RenderTexture OverlayTexture => _overlayRt;
        public bool Active => _uiCam != null && _mainRt != null;
        public bool Visible => Active && (_main.Visible || _build.Visible);
        public bool Dragging => _drag != Drag.None;

        // pose of the panel whose widgets were used last (settings panel placement, drag feedback)
        PanelView Focus => _focus ?? _main;
        public Vector3 RootPosition => Focus.Root.localPosition + Focus.Root.localRotation * Focus.Quad.localPosition;
        public Quaternion RootRotation => Focus.Rot;
        public float PanelWidth => Focus.WidthM;
        public float PanelHeight => Focus.HeightM;

        /// <summary>Makes the main menu / HUD panel the one the VR settings open next to.</summary>
        public void FocusMain() { _focus = _main; }

        static int TextureWidth => Mathf.Clamp(Plugin.Cfg.UiResolution.Value, 1280, 3840);
        static int TextureHeight => TextureWidth * 9 / 16;

        public HudPanel(Transform rig)
        {
            _rig = rig;
            _main = new PanelView("CozyVR Main Panel", rig) { Solid = false };
            _build = new PanelView("CozyVR Build Panel", rig) { Solid = true, ShowGear = false };
            _options = new PanelView("CozyVR Options Panel", rig) { Solid = true };
            _overlay = new PanelView("CozyVR World Labels", rig) { Solid = false };
            // top bar parts, left to right: main menu, undo/redo, prepare/roll marbles, delete, race, camera reset.
            // "center" (undo..delete in one piece) is only used when the single buttons cannot be found.
            foreach (var name in new[] { "pause", "undo", "play", "delete", "center", "racing", "camreset" })
                _tiles.Add(new Tile { Name = name, View = new PanelView("CozyVR Bar " + name, rig) { Solid = true } });
            _miniInfo = new MiniButton("CozyVR Info Button", "btn_manual", rig);
            _miniVr = new MiniButton("CozyVR VR Button", "btn_vr", rig);
            _miniGrab = new MiniButton("CozyVR Grab Button", "toggle_mouse", rig) { Aspect = 2f };
            _miniMarble = new MiniButton("CozyVR Marble Button", "btn_marble", rig);
            _buildCtl = new[]
            {
                new MiniButton("CozyVR Build Resize", "resize", rig),
                new MiniButton("CozyVR Build Tilt", "build_tilt", rig),
            };
        }

        /// <summary>Shows the current mode on the toggle: knob left on the mouse pointer, or right on the hand.</summary>
        public void SetGrabLabel(bool grab) => _miniGrab.SetIcon(grab ? "toggle_hand" : "toggle_mouse");

        /// <summary>Marble button: lit while marble mode is on.</summary>
        public void SetMarbleLabel(bool on) => _miniMarble.SetIcon(on ? "btn_marble_on" : "btn_marble");

        static Mesh _quadMesh;

        internal static Transform MakeQuad(string name, Material mat, Transform parent)
        {
            if (_quadMesh == null)
            {
                _quadMesh = new Mesh { name = "CozyVR Quad" };
                _quadMesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
                _quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                _quadMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                _quadMesh.RecalculateBounds();
            }
            var go = new GameObject(name) { layer = HudLayer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
            mr.sharedMaterial = mat;
            return go.transform;
        }

        static Material WidgetMaterial(Color c)
        {
            var m = new Material(Shader.Find("Sprites/Default")) { color = c };
            m.renderQueue = (int)RenderQueue.Transparent + 5;
            return m;
        }

        static Material CreateMaterial()
        {
            // The UI texture holds premultiplied colour, so it must be blended One/OneMinusSrcAlpha
            // without multiplying by alpha again. URP/Unlit does exactly that in its base variant.
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            Material m;
            if (sh != null)
            {
                m = new Material(sh);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 1f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                // Panels write depth like any other object (so controller buttons behind a menu stay hidden behind
                // it). Alpha clipping keeps the see-through parts of the picture from writing depth.
                m.SetFloat("_ZWrite", 1f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.04f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                Plugin.Log.LogWarning("URP Unlit shader not found, falling back to Sprites/Default for the HUD.");
                m = new Material(Shader.Find("Sprites/Default"));
            }
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        static RenderTexture NewTexture(string name, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name,
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
                // the panels are seen at a slant and a little smaller than the texture: prefer the sharper mip level
                mipMapBias = -0.6f,
                antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }

        // ---------------------------------------------------------------- UI camera redirection

        public void Attach(Camera uiCam, Camera stackOwner)
        {
            if (uiCam == null) return;
            _uiCam = uiCam;
            _stackOwner = stackOwner;
            _uiData = uiCam.GetUniversalAdditionalCameraData();
            _uiClearFlags = uiCam.clearFlags;
            _uiBackground = uiCam.backgroundColor;
            _uiDepth = uiCam.depth;
            _uiHdr = uiCam.allowHDR;
            _uiMsaa = uiCam.allowMSAA;

            if (stackOwner != null)
                stackOwner.GetUniversalAdditionalCameraData().cameraStack.Remove(uiCam);

            _uiData.renderType = CameraRenderType.Base;
            _uiData.allowXRRendering = false;
            _uiData.renderPostProcessing = false;
            uiCam.clearFlags = CameraClearFlags.SolidColor;
            uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            uiCam.allowHDR = false;
            uiCam.allowMSAA = false;
            uiCam.depth = -50f; // render before the eye cameras so the panels show this frame's UI
            EnsureTextures();
            _nextCanvasScan = 0f;
        }

        public void Detach()
        {
            _main.Placement = _build.Placement = Placement.Hidden;
            _main.Root.gameObject.SetActive(false);
            _build.Root.gameObject.SetActive(false);
            foreach (var c in _converted)
            {
                if (c.Canvas == null) continue;
                c.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                c.Canvas.sortingOrder = c.Order;
                c.Canvas.sortingLayerID = c.SortingLayer;
            }
            _converted.Clear();
            _options.Placement = _overlay.Placement = Placement.Hidden;
            _options.Root.gameObject.SetActive(false);
            _overlay.Root.gameObject.SetActive(false);
            SetOverlayTarget(false, force: true);
            if (_uiCam != null)
            {
                if (_buildCanvas != null) _buildCanvas.worldCamera = _uiCam;
                foreach (var c in _optionsCanvases) if (c != null) c.worldCamera = _uiCam;
                foreach (var c in _overlayCanvases) if (c != null) c.worldCamera = _uiCam;
            }
            foreach (var r in _overlayRaycasters) if (r != null) r.enabled = true;
            _buildCanvas = null;
            _optionsCanvases.Clear();
            _overlayCanvases.Clear();
            _overlayRaycasters.Clear();
            _mainRaycasters.Clear();
            foreach (var cam in new[] { _buildCam, _optionsCam, _overlayCam, _barCam })
                if (cam != null) Object.Destroy(cam.gameObject);
            _buildCam = _optionsCam = _overlayCam = _barCam = null;

            // give the game's own UI back its pieces
            if (_uiCam != null)
            {
                foreach (var c in _barCanvases) if (c != null) c.worldCamera = _uiCam;
                if (_pauseCanvas != null) _pauseCanvas.worldCamera = _uiCam;
            }
            foreach (var g in _disabledGraphics) if (g != null) g.enabled = true;
            foreach (var o in _hiddenObjects) if (o != null) o.SetActive(true);
            _disabledGraphics.Clear();
            _hiddenObjects.Clear();
            _barCanvases.Clear();
            _mainCanvases.Clear();
            _pauseCanvas = null;
            foreach (var t in _tiles) { t.Srcs.Clear(); t.Found = false; t.View.Placement = Placement.Hidden; t.View.Root.gameObject.SetActive(false); }
            _miniInfo.Place(false, default, Quaternion.identity, 0f);
            _miniVr.Place(false, default, Quaternion.identity, 0f);
            _miniGrab.Place(false, default, Quaternion.identity, 0f);
            _miniMarble.Place(false, default, Quaternion.identity, 0f);
            foreach (var b in _buildCtl) b.Place(false, default, Quaternion.identity, 0f);

            if (_uiCam != null)
            {
                _uiCam.targetTexture = null;
                _uiCam.clearFlags = _uiClearFlags;
                _uiCam.backgroundColor = _uiBackground;
                _uiCam.depth = _uiDepth;
                _uiCam.allowHDR = _uiHdr;
                _uiCam.allowMSAA = _uiMsaa;
                _uiData.renderType = CameraRenderType.Overlay;
                _uiData.allowXRRendering = false; // on the monitor the UI never goes to the headset
                if (_stackOwner != null)
                {
                    var stack = _stackOwner.GetUniversalAdditionalCameraData().cameraStack;
                    if (!stack.Contains(_uiCam)) stack.Add(_uiCam);
                }
            }
            _uiCam = null;
            if (_mainRt != null) { _mainRt.Release(); Object.Destroy(_mainRt); _mainRt = null; }
            if (_atlasRt != null) { _atlasRt.Release(); Object.Destroy(_atlasRt); _atlasRt = null; }
            if (_overlayRt != null) { _overlayRt.Release(); Object.Destroy(_overlayRt); _overlayRt = null; }
        }

        /// <summary>
        /// Fixed 16:9 layout size, independent of the desktop window (SteamVR streaming can resize or even rotate
        /// the game window, which would otherwise re-layout the whole UI).
        /// </summary>
        void EnsureTextures()
        {
            int w = TextureWidth, h = TextureHeight;
            if (_mainRt == null || _mainRt.width != w || _mainRt.height != h)
            {
                if (_mainRt != null) { _uiCam.targetTexture = null; _mainRt.Release(); Object.Destroy(_mainRt); }
                _mainRt = NewTexture("CozyVR UI", w, h);
                _main.SetTexture(_mainRt);
                if (EventSystem.current != null) EventSystem.current.pixelDragThreshold = Mathf.Max(10, w / 110);
                Plugin.Log.LogInfo($"UI panel texture {w}x{h}");
            }
            if (_uiCam.targetTexture != _mainRt) _uiCam.targetTexture = _mainRt;

            if (_atlasRt == null || _atlasRt.width != AtlasSlots * w || _atlasRt.height != h)
            {
                foreach (var cam in new[] { _buildCam, _optionsCam, _barCam }) if (cam != null) cam.targetTexture = null;
                if (_atlasRt != null) { _atlasRt.Release(); Object.Destroy(_atlasRt); }
                _atlasRt = NewTexture("CozyVR Panel Atlas", AtlasSlots * w, h);
                _build.SetTexture(_atlasRt);
                _options.SetTexture(_atlasRt);
                foreach (var t in _tiles) t.View.SetTexture(_atlasRt);
            }
            if (_overlayRt == null || _overlayRt.width != w || _overlayRt.height != h)
            {
                if (_overlayCam != null) _overlayCam.targetTexture = null;
                if (_overlayRt != null) { _overlayRt.Release(); Object.Destroy(_overlayRt); }
                _overlayRt = NewTexture("CozyVR World Labels", w, h);
                _overlay.SetTexture(_overlayRt);
            }
            EnsureSubCamera(ref _buildCam, "CozyVR Build UI Camera", 5000f, _atlasRt, new Rect(1f / AtlasSlots, 0f, 1f / AtlasSlots, 1f), -49f);
            EnsureSubCamera(ref _optionsCam, "CozyVR Options UI Camera", 10000f, _atlasRt, new Rect(2f / AtlasSlots, 0f, 1f / AtlasSlots, 1f), -48f);
            EnsureSubCamera(ref _barCam, "CozyVR Bar UI Camera", 20000f, _atlasRt, new Rect(3f / AtlasSlots, 0f, 1f / AtlasSlots, 1f), -46f);
            // the world-label camera keeps the normal pixel origin: the game maps virtual-screen pixels through it
            EnsureSubCamera(ref _overlayCam, "CozyVR World Label Camera", 15000f, _overlayRt, new Rect(0f, 0f, 1f, 1f), -47f);
        }

        /// <summary>
        /// An extra UI camera far away from the normal one (so neither sees the other's canvases), rendering into
        /// its own slot of a texture. Each slot has its own pixel range, so one virtual mouse can address all panels.
        /// </summary>
        void EnsureSubCamera(ref Camera cam, string name, float offset, RenderTexture target, Rect rect, float depth)
        {
            if (cam == null)
            {
                var go = new GameObject(name);
                Object.DontDestroyOnLoad(go);
                cam = go.AddComponent<Camera>();
                cam.CopyFrom(_uiCam);
                go.transform.SetPositionAndRotation(_uiCam.transform.position + new Vector3(offset, 0f, 0f), _uiCam.transform.rotation);
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderType = CameraRenderType.Base;
                data.allowXRRendering = false;
                data.renderPostProcessing = false;
                data.SetRenderer(RendererIndexOf(_uiData));
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.depth = depth;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
            }
            if (cam.targetTexture != target) cam.targetTexture = target;
            if (cam.rect != rect) cam.rect = rect;
        }

        static int RendererIndexOf(UniversalAdditionalCameraData data)
        {
            var f = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return f != null ? (int)f.GetValue(data) : -1;
        }

        /// <summary>Screen-space-overlay canvases never reach the headset; move them onto the UI camera. The build toolbox gets its own camera.</summary>
        void ConvertCanvases()
        {
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                string n = canvas.name;
                if (n == "Canvas_Pause_Menu" && canvas.renderMode == RenderMode.ScreenSpaceCamera && _barCam != null)
                {
                    // its menu button joins the left-hand menu; while the game is paused the whole menu shows on the main panel
                    if (_pauseCanvas != canvas) { _pauseCanvas = canvas; Plugin.Log.LogInfo("Pause button moved to the left-hand menu."); }
                    continue;
                }
                if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == _uiCam)
                {
                    if (n == "Canvas_RollMarble" && _barCam != null)
                    {
                        _barCanvases.Add(canvas);
                        canvas.worldCamera = _barCam;
                        Plugin.Log.LogInfo("Top bar moved to the left-hand menu.");
                        continue;
                    }
                    if (n == BuildCanvasName && _buildCam != null)
                    {
                        _buildCanvas = canvas;
                        canvas.worldCamera = _buildCam;
                        Plugin.Log.LogInfo("Build toolbox moved to its own panel.");
                        continue;
                    }
                    if (n.StartsWith(OptionsCanvasPrefix) && _optionsCam != null)
                    {
                        _optionsCanvases.Add(canvas);
                        canvas.worldCamera = _optionsCam;
                        Plugin.Log.LogInfo("Object options moved to their own panel.");
                        continue;
                    }
                    if (OverlayCanvasNames.Contains(n) && _overlayCam != null)
                    {
                        _overlayCanvases.Add(canvas);
                        canvas.worldCamera = _overlayCam;
                        var r = canvas.GetComponent<GraphicRaycaster>();
                        if (r != null) _overlayRaycasters.Add(r);
                        if (Plugin.Cfg.Diagnostics.Value) Plugin.Log.LogInfo("World labels: " + n);
                        continue;
                    }
                }
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if ((_uiCam.cullingMask & (1 << canvas.gameObject.layer)) == 0) continue;
                if (n.StartsWith("CozyVR Desktop")) continue; // the mod's own desktop-only hint
                // Fake mouse cursors position themselves in raw pixels and are replaced by the laser dot anyway.
                if (n.IndexOf("Cursor", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("FPS", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                _converted.Add(new ConvertedCanvas { Canvas = canvas, Order = canvas.sortingOrder, SortingLayer = canvas.sortingLayerID });
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _uiCam;
                canvas.planeDistance = 5f;
                canvas.sortingLayerName = "Ui";
                canvas.sortingOrder = 10000 + canvas.sortingOrder; // overlay canvases draw on top of camera canvases
                if (Plugin.Cfg.Diagnostics.Value) Plugin.Log.LogInfo("Converted overlay canvas: " + n);
            }
        }

        /// <summary>Main-UI canvases and raycasters (everything still drawn by the normal UI camera).</summary>
        void ScanMainRaycasters()
        {
            _mainRaycasters.Clear();
            _mainCanvases.Clear();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceCamera || c.worldCamera != _uiCam) continue;
                var r = c.GetComponent<GraphicRaycaster>();
                if (r != null) _mainRaycasters.Add(r);
                // permanent status bits that must not keep the in-game panel open
                if (c.name.IndexOf("VersionNumber", System.StringComparison.OrdinalIgnoreCase) >= 0 || c.name.StartsWith("CozyVR")) continue;
                _mainCanvases.Add(c);
            }
            FindBarSources();
            HideDistractions();
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform c in root)
            {
                if (c.name == name) return c;
                var d = FindDeep(c, name);
                if (d != null) return d;
            }
            return null;
        }

        static readonly System.Reflection.FieldInfo UndoField = HarmonyLib.AccessTools.Field(typeof(TimR.CtrlZ.UndoManager), "_undoButton");
        static readonly System.Reflection.FieldInfo RedoField = HarmonyLib.AccessTools.Field(typeof(TimR.CtrlZ.UndoManager), "_redoButton");
        static readonly System.Reflection.FieldInfo RollField = HarmonyLib.AccessTools.Field(typeof(TimR.UI.RollMarbleUi), "_rollMarbleButton");

        void FindBarSources()
        {
            foreach (var t in _tiles) t.Srcs.Clear();
            foreach (var c in _barCanvases)
            {
                if (c == null) continue;
                var center = FindDeep(c.transform, "center parent") as RectTransform;
                if (center != null && !SplitCenter(center)) AddSource("center", center);
                AddSource("racing", FindDeep(c.transform, "racing parent"));
                AddSource("camreset", FindDeep(c.transform, "cam reset parent"));
            }
            if (_pauseCanvas != null) AddSource("pause", FindDeep(_pauseCanvas.transform, "btn_open_menu"));
        }

        /// <summary>
        /// Splits the game's middle group into undo/redo, the big marble button and the rest (delete), so each can be
        /// shown at the same height. False if the buttons are not where they are expected.
        /// </summary>
        bool SplitCenter(RectTransform center)
        {
            try
            {
                var undoMgr = Object.FindFirstObjectByType<TimR.CtrlZ.UndoManager>(FindObjectsInactive.Include);
                var rollUi = Object.FindFirstObjectByType<TimR.UI.RollMarbleUi>(FindObjectsInactive.Include);
                var undo = undoMgr != null ? UndoField?.GetValue(undoMgr) as Button : null;
                var redo = undoMgr != null ? RedoField?.GetValue(undoMgr) as Button : null;
                var play = rollUi != null ? RollField?.GetValue(rollUi) as Button : null;
                if (undo == null || play == null || !undo.transform.IsChildOf(center) || !play.transform.IsChildOf(center)) return false;
                var own = new List<Transform> { undo.transform, play.transform };
                AddSource("undo", undo.transform);
                if (redo != null && redo.transform.IsChildOf(center)) { own.Add(redo.transform); AddSource("undo", redo.transform); }
                AddSource("play", play.transform);
                foreach (var b in center.GetComponentsInChildren<Button>(true))
                {
                    bool related = false;
                    foreach (var o in own) if (b.transform.IsChildOf(o) || o.IsChildOf(b.transform)) { related = true; break; }
                    if (!related) AddSource("delete", b.transform);
                }
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Top bar split failed: " + e.Message);
                return false;
            }
        }

        void AddSource(string name, Transform t)
        {
            if (!(t is RectTransform rt)) return;
            foreach (var tile in _tiles) if (tile.Name == name && !tile.Srcs.Contains(rt)) tile.Srcs.Add(rt);
        }

        /// <summary>The controls cheat sheet and the Twitch bar are replaced by the mod's own info button; the top bar's backdrop gradient would stretch the panel over the whole width.</summary>
        void HideDistractions()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // full-screen "blur" backdrops need the game's blur camera and come out black in the panel
                if (c.name == "Canvas_Pause_Menu" || c.name == "Canvas_Help_Menu" || c.name == "Canvas_Settings_Menu" || c.name == "Canvas_QuitGame" || c.name == "Canvas_ConfirmUi")
                {
                    foreach (var g in c.GetComponentsInChildren<Graphic>(true))
                    {
                        if (!g.enabled || g.GetType().Name != "TranslucentImage") continue;
                        var r = g.rectTransform;
                        if (r.anchorMin == Vector2.zero && r.anchorMax == Vector2.one) { g.enabled = false; _disabledGraphics.Add(g); }
                    }
                }
                if (c.name != "Canvas_Help_Menu") continue;
                foreach (var n in new[] { "controls_info", "twitch_info" })
                {
                    var t = c.transform.Find(n);
                    if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); _hiddenObjects.Add(t.gameObject); }
                }
            }
            foreach (var c in _barCanvases)
            {
                if (c == null) continue;
                foreach (var g in c.GetComponentsInChildren<Graphic>(true))
                    if (g.enabled && g.name.IndexOf("gradient", System.StringComparison.OrdinalIgnoreCase) >= 0) { g.enabled = false; _disabledGraphics.Add(g); }
            }
        }

        void RoutePauseCanvas(bool inMenu)
        {
            if (_pauseCanvas == null || _barCam == null || _uiCam == null) return;
            Camera want = inMenu ? _uiCam : _barCam;
            if (_pauseCanvas.worldCamera != want) _pauseCanvas.worldCamera = want;
        }

        /// <summary>Rectangle (atlas pixels) around the visible ones of a tile's elements, or false when none is visible.</summary>
        bool TileRect(List<RectTransform> srcs, out Rect rect)
        {
            rect = default;
            if (_barCam == null) return false;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            var corners = new Vector3[4];
            foreach (var src in srcs)
            {
                if (src == null || !src.gameObject.activeInHierarchy) continue;
                var canvas = src.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.enabled) continue;
                bool any = false;
                foreach (var g in src.GetComponentsInChildren<Graphic>(false))
                    if (g.enabled && g.color.a >= 0.02f && !g.canvasRenderer.cull && g.canvasRenderer.GetInheritedAlpha() >= 0.02f) { any = true; break; }
                if (!any) continue;
                src.GetWorldCorners(corners);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 p = RectTransformUtility.WorldToScreenPoint(_barCam, corners[i]);
                    x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
                }
            }
            if (x1 <= x0) return false;
            int w = TextureWidth, h = TextureHeight;
            const float pad = 4f;
            x0 = Mathf.Clamp(x0 - pad, 3f * w, 4f * w); x1 = Mathf.Clamp(x1 + pad, 3f * w, 4f * w);
            y0 = Mathf.Clamp(y0 - pad, 0f, h); y1 = Mathf.Clamp(y1 + pad, 0f, h);
            if (x1 - x0 < 4f || y1 - y0 < 4f) return false;
            rect = Rect.MinMaxRect(Mathf.Floor(x0), Mathf.Floor(y0), Mathf.Ceil(x1), Mathf.Ceil(y1));
            return true;
        }

        void UpdateTiles()
        {
            foreach (var t in _tiles)
            {
                t.Found = TileRect(t.Srcs, out Rect r);
                if (t.Found) { t.View.Crop = r; t.View.Anchor = r.center; }
            }
        }

        PanelView TileAt(Vector2 pixel)
        {
            foreach (var t in _tiles) if (t.View.Visible && t.View.Contains(pixel)) return t.View;
            return null;
        }

        Tile FindTile(string name)
        {
            foreach (var t in _tiles) if (t.Name == name) return t;
            return null;
        }

        /// <summary>
        /// The menu around the left-hand toolbox. One row above it, everything at the height of the game's big marble
        /// button: undo/redo, marble button, delete, race, camera reset, mouse/hand toggle. The main menu button sits
        /// in front of the row, left of the toolbox; the round VR and manual buttons hang below it as a column.
        /// </summary>
        void LayoutBar()
        {
            bool show = false;   // the top bar and the mod buttons moved onto the controllers (ControllerDecor)
            if (!show)
            {
                foreach (var t in _tiles) { t.View.Placement = Placement.Hidden; t.View.Apply(); }
                _miniInfo.Place(false, default, Quaternion.identity, 0f);
                _miniVr.Place(false, default, Quaternion.identity, 0f);
                _miniGrab.Place(false, default, Quaternion.identity, 0f);
                _miniMarble.Place(false, default, Quaternion.identity, 0f);
                return;
            }

            float mpp = _build.MetersPerPixel;
            const float gap = 16f;
            var play = FindTile("play");
            var center = FindTile("center");
            float rowH = play != null && play.Found ? play.View.Crop.height : center != null && center.Found ? center.View.Crop.height : 70f;
            rowH = Mathf.Max(rowH, 40f);

            float left = _build.Crop.xMin - _build.Anchor.x;          // toolbox's left edge, in pixels from its anchor
            float colX = left - gap - rowH * 0.5f;
            float rowY = (_build.Crop.yMax - _build.Anchor.y) + 30f + rowH * 0.5f;
            System.Func<float, float, Vector3> at = (px, py) => _build.Pos + _build.Rot * new Vector3(px * mpp, py * mpp, 0f);

            float x = left;
            foreach (var t in _tiles)
            {
                if (!t.Found) { t.View.Placement = Placement.Hidden; t.View.Apply(); continue; }
                t.Scale = rowH / Mathf.Max(1f, t.View.Crop.height);
                float tw = t.View.Crop.width * t.Scale;
                float cxp;
                if (t.Name == "pause") cxp = colX;
                else { cxp = x + tw * 0.5f; x += tw + gap; }
                t.View.Placement = _build.Placement;
                t.View.Pos = at(cxp, rowY);
                t.View.Rot = _build.Rot;
                t.View.MetersPerPixel = mpp * t.Scale;
                t.View.Apply();
            }
            float size = rowH * mpp;
            _miniGrab.Place(true, at(x + rowH, rowY), _build.Rot, size);
            _miniMarble.Place(true, at(x + 2f * rowH + gap + rowH * 0.5f, rowY), _build.Rot, size);
            _miniVr.Place(true, at(colX, rowY - (rowH + gap)), _build.Rot, size);
            _miniInfo.Place(true, at(colX, rowY - 2f * (rowH + gap)), _build.Rot, size);
        }

        /// <summary>
        /// Two handles just right of the lower right corner of the build menu on the hand: resize (at the corner) and
        /// tilt (above it). Held with the trigger, the menu follows the laser: resize scales it about its lower edge,
        /// tilt follows the turn of the pointing hand. They keep their size when the menu is scaled.
        /// </summary>
        void PlaceBuildControls()
        {
            bool show = _build.Placement == Placement.BuildAttached && (_handleDrag != HandleDrag.None || (ToolboxOpen() ?? _build.Crop.width > 220f));
            if (_build.Placement != Placement.BuildAttached) EndHandleDrag();
            if (!show)
            {
                foreach (var b in _buildCtl) b.Place(false, default, Quaternion.identity, 0f);
                ShowReadout(false, default, default, null);
                return;
            }
            const float size = 0.02f, gap = 0.005f;
            float mpp = _build.MetersPerPixel;
            float x = (_build.Crop.xMax - _build.Anchor.x) * mpp + gap + size * 0.5f;
            float bottom = (_build.Crop.yMin - _build.Anchor.y) * mpp;
            for (int i = 0; i < _buildCtl.Length; i++)
            {
                float y = bottom + size * 0.5f + i * (size + gap);   // 0 = resize at the corner, 1 = tilt above it
                _buildCtl[i].Place(true, _build.Pos + _build.Rot * new Vector3(x, y, 0f), _build.Rot, size);
            }
            if (_handleDrag == HandleDrag.None) { ShowReadout(false, default, default, null); return; }
            string text = _handleDrag == HandleDrag.Tilt ? (Mathf.Abs(_liveTilt) < 0.5f ? "0°" : $"{_liveTilt:+0;-0}°") : $"{_liveScale * 100f:0} %";
            float ry = bottom + 2f * (size + gap) + 0.008f;
            ShowReadout(true, _build.Pos + _build.Rot * new Vector3(x, ry, -0.002f), _build.Rot, text);
        }

        // ---------------------------------------------------------------- resize / tilt handles of the build menu

        enum HandleDrag { None, Resize, Tilt }
        HandleDrag _handleDrag;
        float _liveScale, _liveTilt;            // values while a handle is held; written to the config on release
        float _rawTilt;                         // tilt before the snap to 0
        float _dragScale0, _dragTilt0, _dragAngle0, _lastHandAngle;
        Vector3 _dragCorner0;                   // laser hit at the start, in the menu's frame (relative to its pose)
        Vector3 _mountFwd, _mountFace;          // controller axes of the last placement (tilt 0 = up along _mountFwd)
        bool _mountValid;
        public const float MinAttachedScale = 0.12f, MaxAttachedScale = 0.6f, MaxAttachedTilt = 75f, TiltSnap = 5f;
        /// <summary>
        /// Soft snap to 0: within ±TiltSnap the menu stays flush with the ring, the next TiltSnap degrees catch up
        /// with the hand (no jump), beyond that it follows 1:1.
        /// </summary>
        static float SnapTilt(float raw)
        {
            float a = Mathf.Abs(raw);
            if (a <= TiltSnap) return 0f;
            if (a < 2f * TiltSnap) return Mathf.Sign(raw) * (a - TiltSnap) * 2f;
            return raw;
        }
        public bool HandleDragging => _handleDrag != HandleDrag.None;
        public bool TiltDragging => _handleDrag == HandleDrag.Tilt;

        /// <summary>Where the laser meets the build menu's plane while a handle is held (tracking space).</summary>
        public Vector3 HandleDragPoint(Ray localRay)
        {
            var plane = new Plane(_build.Rot * Vector3.forward, _build.Pos);
            return plane.Raycast(localRay, out float t) ? localRay.GetPoint(t) : localRay.GetPoint(0.3f);
        }
        GameObject _readout;
        TMPro.TextMeshProUGUI _readoutText;

        /// <summary>Direction of the laser as an angle in the controller's tilt plane (0 = along the tilt-0 "up").</summary>
        float HandAngle(Vector3 dir) => Mathf.Atan2(Vector3.Dot(dir, -_mountFace), Vector3.Dot(dir, _mountFwd)) * Mathf.Rad2Deg;

        bool BeginHandleDrag(Widget w, Ray localRay)
        {
            if (w != Widget.BuildResize && w != Widget.BuildTilt) return false;
            var c = Plugin.Cfg;
            _liveScale = _dragScale0 = Mathf.Clamp(c.AttachedScale.Value, MinAttachedScale, MaxAttachedScale);
            _liveTilt = _rawTilt = _dragTilt0 = Mathf.Clamp(c.AttachedTilt.Value, -MaxAttachedTilt, MaxAttachedTilt);
            if (w == Widget.BuildResize)
            {
                var plane = new Plane(_build.Rot * Vector3.forward, _build.Pos);
                if (!plane.Raycast(localRay, out float t)) return true;
                _dragCorner0 = Quaternion.Inverse(_build.Rot) * (localRay.GetPoint(t) - _build.Pos);
                if (_dragCorner0.sqrMagnitude < 1e-4f) return true;
                _handleDrag = HandleDrag.Resize;
            }
            else
            {
                if (!_mountValid) return true;
                _dragAngle0 = HandAngle(localRay.direction);
                _handleDrag = HandleDrag.Tilt;
            }
            return true;
        }

        void UpdateHandleDrag(Ray localRay)
        {
            if (_handleDrag == HandleDrag.Resize)
            {
                var plane = new Plane(_build.Rot * Vector3.forward, _build.Pos);
                if (!plane.Raycast(localRay, out float t)) return;
                Vector3 local = Quaternion.Inverse(_build.Rot) * (localRay.GetPoint(t) - _build.Pos);
                // the menu grows about its lower edge centre, so the held corner stays under the laser
                float k = Vector3.Dot(local, _dragCorner0) / _dragCorner0.sqrMagnitude;
                _liveScale = Mathf.Clamp(_dragScale0 * k, MinAttachedScale, MaxAttachedScale);
            }
            else if (_handleDrag == HandleDrag.Tilt && _mountValid)
            {
                // the menu turns like the pointing hand: tip the hand back and the menu's top leans back as much
                // (HandAngle grows when it leans back, the setting grows towards the viewer)
                _lastHandAngle = HandAngle(localRay.direction);
                float delta = Mathf.DeltaAngle(_dragAngle0, _lastHandAngle);
                _rawTilt = Mathf.Clamp(_dragTilt0 - delta, -MaxAttachedTilt, MaxAttachedTilt);
                _liveTilt = SnapTilt(_rawTilt);
            }
        }

        void EndHandleDrag()
        {
            if (_handleDrag == HandleDrag.None) return;
            var c = Plugin.Cfg;
            if (_handleDrag == HandleDrag.Resize) c.AttachedScale.Value = Mathf.Round(_liveScale * 100f) / 100f;
            else c.AttachedTilt.Value = Mathf.Round(_liveTilt);
            _handleDrag = HandleDrag.None;
        }

        void ShowReadout(bool show, Vector3 pos, Quaternion rot, string text)
        {
            if (!show) { if (_readout != null && _readout.activeSelf) _readout.SetActive(false); return; }
            if (_readout == null)
            {
                var font = VRSettingsPanel.FindFont();
                if (font == null) return;
                _readout = new GameObject("CozyVR Build Readout", typeof(RectTransform)) { layer = HudLayer };
                var rt = (RectTransform)_readout.transform;
                rt.SetParent(_build.Root.parent, false);
                rt.sizeDelta = new Vector2(220f, 80f);
                rt.localScale = Vector3.one * 0.00012f;
                _readout.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var bg = VRSettingsPanel.NewRect("Background", rt, Vector2.zero, new Vector2(220f, 80f), new Vector2(0.5f, 0.5f));
                bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0.5f);
                bg.anchoredPosition = Vector2.zero;
                var img = bg.gameObject.AddComponent<Image>();
                img.sprite = VRSettingsPanel.UiSprite("ui_round_rect", true);
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 2.6f;
                img.color = new Color(0.12f, 0.08f, 0.06f, 0.85f);
                img.raycastTarget = false;
                _readoutText = VRSettingsPanel.Text(rt, "", Vector2.zero, new Vector2(210f, 80f), 52f, TMPro.TextAlignmentOptions.Center, font, TMPro.FontStyles.Bold);
                var trt = _readoutText.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                trt.anchoredPosition = Vector2.zero;
                _readoutText.color = new Color(1f, 0.9f, 0.7f, 1f);
                foreach (var tr in _readout.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = HudLayer;
            }
            if (!_readout.activeSelf) _readout.SetActive(true);
            _readout.transform.localPosition = pos;
            _readout.transform.localRotation = rot;
            _readoutText.text = text;
        }

        /// <summary>
        /// World labels share their pixel range with the main UI. Only one of the two groups may answer the
        /// virtual mouse at a time: the one the laser is on.
        /// </summary>
        void SetOverlayTarget(bool overlay, bool force = false)
        {
            if (overlay == _overlayTarget && !force) return;
            _overlayTarget = overlay;
            foreach (var r in _overlayRaycasters) if (r != null) r.enabled = overlay;
            foreach (var r in _mainRaycasters) if (r != null) r.enabled = !overlay;
        }

        Rect ComputeBuildCrop() => ComputeCrop(_buildCanvas != null ? new List<Canvas> { _buildCanvas } : null, _buildCam, 1);

        /// <summary>The visible part of some canvases rendered into an atlas slot, in atlas pixels.</summary>
        Rect ComputeCrop(List<Canvas> canvases, Camera cam, int slot)
        {
            int w = TextureWidth, h = TextureHeight;
            float x0 = slot * w, x1 = (slot + 1) * w;
            var empty = new Rect(x0, 0f, 0f, 0f);
            if (canvases == null || cam == null) return empty;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            var corners = new Vector3[4];
            foreach (var canvas in canvases)
            foreach (var g in (canvas != null && canvas.gameObject.activeInHierarchy && canvas.enabled) ? canvas.GetComponentsInChildren<Graphic>(false) : new Graphic[0])
            {
                if (!g.enabled || g.color.a < 0.02f || g.canvasRenderer.cull || g.canvasRenderer.GetInheritedAlpha() < 0.02f) continue;
                g.rectTransform.GetWorldCorners(corners);
                float gx0 = float.MaxValue, gy0 = float.MaxValue, gx1 = float.MinValue, gy1 = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                    gx0 = Mathf.Min(gx0, p.x); gy0 = Mathf.Min(gy0, p.y); gx1 = Mathf.Max(gx1, p.x); gy1 = Mathf.Max(gy1, p.y);
                }
                if (gx1 - gx0 > w * 0.95f && gy1 - gy0 > h * 0.9f) continue; // full-screen backgrounds
                // small gadgets tucked into the right screen edge (the main menu's news/Twitch flap) would stretch the
                // main panel over the whole width; they are left out of the picture
                if (slot == 0 && gx1 - gx0 < 140f && gy1 - gy0 < 140f && gx1 > x1 - 140f) continue;
                if (gx1 < x0 || gx0 > x1 || gy1 < 0f || gy0 > h) continue;   // off screen (slid away)
                xMin = Mathf.Min(xMin, gx0); yMin = Mathf.Min(yMin, gy0); xMax = Mathf.Max(xMax, gx1); yMax = Mathf.Max(yMax, gy1);
            }
            if (xMax <= xMin) return empty;
            const float pad = 8f;
            xMin = Mathf.Clamp(xMin - pad, x0, x1); xMax = Mathf.Clamp(xMax + pad, x0, x1);
            yMin = Mathf.Clamp(yMin - pad, 0f, h); yMax = Mathf.Clamp(yMax + pad, 0f, h);
            return Rect.MinMaxRect(Mathf.Floor(xMin), Mathf.Floor(yMin), Mathf.Ceil(xMax), Mathf.Ceil(yMax));
        }

        // ---------------------------------------------------------------- the game's toolbox state

        static readonly System.Type BuildUiType = System.Type.GetType("TimR.UI.StructureTypeSelectUi, Assembly-CSharp");
        static readonly System.Reflection.FieldInfo BuildUiField = BuildUiType?.GetField("_ui", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>Is the game's build toolbox opened (pulled out)?</summary>
        bool? ToolboxOpen()
        {
            try
            {
                if (_buildUi == null && BuildUiType != null) _buildUi = Object.FindFirstObjectByType(BuildUiType) as Component;
                if (_buildUi == null || BuildUiField == null) return null;
                var pullout = BuildUiField.GetValue(_buildUi);
                var isOpen = pullout?.GetType().GetField("IsOpen")?.GetValue(pullout) ?? pullout?.GetType().GetProperty("IsOpen")?.GetValue(pullout);
                var value = isOpen?.GetType().GetProperty("Value")?.GetValue(isOpen);
                return value is bool b ? b : (bool?)null;
            }
            catch { return null; }
        }

        void SyncToolbox(bool wantOpen)
        {
            bool? open = ToolboxOpen();
            if (open.HasValue && open.Value != wantOpen) Game.Publish(TimR.Events.Evt.ToggleBuildMenuAction);
        }

        // ---------------------------------------------------------------- poses

        /// <summary>New reference frame after a recenter: stored panel positions are relative to it.</summary>
        public void SetFrame(Vector3 headPos, Quaternion headRot)
        {
            Vector3 f = headRot * Vector3.forward;
            _frameOrigin = headPos;
            _frameRot = Quaternion.Euler(0f, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 0f);
        }

        struct Pose3 { public Vector3 Pos; public Quaternion Rot; }

        Pose3 FramePose(Vector3 rel, float yaw, float pitch, float roll)
        {
            return new Pose3 { Pos = _frameOrigin + _frameRot * rel, Rot = _frameRot * Quaternion.Euler(pitch, yaw, roll) };
        }

        void StoreFramePose(Placement p, Vector3 pos, Quaternion rot, float metersPerPixel)
        {
            var c = Plugin.Cfg;
            Vector3 rel = Quaternion.Inverse(_frameRot) * (pos - _frameOrigin);
            Vector3 e = (Quaternion.Inverse(_frameRot) * rot).eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, e.x), yaw = Mathf.DeltaAngle(0f, e.y), roll = Mathf.DeltaAngle(0f, e.z);
            switch (p)
            {
                case Placement.Menu:
                    c.MenuX.Value = rel.x; c.MenuY.Value = rel.y; c.MenuZ.Value = rel.z;
                    c.MenuYaw.Value = yaw; c.MenuPitch.Value = pitch; c.MenuWidth.Value = metersPerPixel * TextureWidth;
                    break;
                case Placement.GameHud:
                    c.HudX.Value = rel.x; c.HudY.Value = rel.y; c.HudZ.Value = rel.z;
                    c.HudYaw.Value = yaw; c.HudPitch.Value = pitch; c.HudWidth.Value = metersPerPixel * TextureWidth;
                    break;
                case Placement.BuildFree:
                    c.EditX.Value = rel.x; c.EditY.Value = rel.y; c.EditZ.Value = rel.z;
                    c.EditYaw.Value = yaw; c.EditPitch.Value = pitch; c.EditRoll.Value = roll; c.EditScale.Value = metersPerPixel * 1000f;
                    break;
                case Placement.BuildAttached:
                    c.AttachedScale.Value = metersPerPixel * 1000f;
                    break;
            }
        }

        void PlaceMain(Placement p)
        {
            var c = Plugin.Cfg;
            Pose3 pose = p == Placement.Menu
                ? FramePose(new Vector3(c.MenuX.Value, c.MenuY.Value, c.MenuZ.Value), c.MenuYaw.Value, c.MenuPitch.Value, 0f)
                : FramePose(new Vector3(c.HudX.Value, c.HudY.Value, c.HudZ.Value), c.HudYaw.Value, c.HudPitch.Value, 0f);
            float width = Mathf.Clamp(p == Placement.Menu ? c.MenuWidth.Value : c.HudWidth.Value, 0.3f, 6f);
            _main.Pos = pose.Pos;
            _main.Rot = pose.Rot;
            _main.MetersPerPixel = width / TextureWidth;
        }

        float _mainPull = 1f, _lastNearest = 1f;
        string _lastHitName = "-";
        Vector3 _lastHead;

        /// <summary>
        /// Panels are ordinary objects in the room now, so scenery between you and the main menu (the marble run in
        /// the main menu's room, a tall tower in a level) would cover it. The panel is pulled towards you until it
        /// stands in front of it; it shrinks by the same factor, so it keeps its apparent size.
        /// </summary>
        void KeepInFrontOfScenery(PanelView v, Vector3 head)
        {
            _lastHead = head;
            _mainBaseValid = v.Visible && v.Crop.width >= 2f && v.Crop.height >= 2f;
            if (!_mainBaseValid) { _mainPull = 1f; v.WidgetScale = 1f; return; }
            _mainBasePos = v.Pos; _mainBaseMpp = v.MetersPerPixel;
            float nearest = NearestObstacle(head, Vector3.zero, 0f);
            _lastNearest = nearest;
            // a gap in front of the obstacle; never closer than 40 % of the way
            float want = nearest < 1f ? Mathf.Max(0.4f, nearest * 0.9f) : 1f;
            // move in at once, back out slowly (no pumping when something passes by)
            _mainPull = want < _mainPull ? want : Mathf.MoveTowards(_mainPull, want, Time.unscaledDeltaTime * 0.5f);
            v.WidgetScale = Mathf.Min(1f, _mainPull);
            if (_mainPull >= 0.999f) return;
            v.Pos = head + (v.Pos - head) * _mainPull;
            v.MetersPerPixel = _mainBaseMpp * _mainPull;
        }

        Vector3 _mainBasePos;
        float _mainBaseMpp;
        bool _mainBaseValid;

        /// <summary>The main menu stands as a menu in front of you (the case the menu viewpoint cares about).</summary>
        public Vector3 MainBasePos => _mainBasePos;
        public bool MainIsMenu => _mainBaseValid && _main.Placement == Placement.Menu && _dragPanel != _main;

        /// <summary>Horizontal direction from the head to the main menu (tracking space).</summary>
        public Vector3 MainDirection(Vector3 head)
        {
            Vector3 d = _mainBasePos - head; d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>
        /// How far along the view rays to the (unpulled) main menu the nearest obstacle is: 1 = free.
        /// <paramref name="shift"/> moves eye and panel together (tracking space) to test another viewpoint;
        /// <paramref name="behind"/> also wants that much free space behind the panel (metres).
        /// </summary>
        public float NearestObstacle(Vector3 head, Vector3 shift, float behind)
        {
            if (!_mainBaseValid) return 1f;
            var v = _main;
            float s = _mainBaseMpp;
            Vector3 center = _mainBasePos + v.Rot * new Vector3((v.Crop.center.x - v.Anchor.x) * s, (v.Crop.center.y - v.Anchor.y) * s, 0f);
            Vector3 right = v.Rot * Vector3.right * (v.Crop.width * s * 0.5f);
            Vector3 up = v.Rot * Vector3.up * (v.Crop.height * s * 0.5f);
            // the bar and buttons underneath belong to the panel too
            center -= v.Rot * Vector3.up * 0.03f;
            up += v.Rot * Vector3.up * 0.03f;
            Vector3 eye = _rig.TransformPoint(head + shift);
            // thin rays slip past pipes and posts: sweep a small sphere (2 cm at the panel, in world units)
            float radius = 0.02f * _rig.lossyScale.x;
            float nearest = 1f;
            for (int ix = -2; ix <= 2; ix++)
            for (int iy = -2; iy <= 2; iy++)
            {
                Vector3 target = _rig.TransformPoint(center + shift + right * (ix * 0.5f) + up * (iy * 0.5f));
                Vector3 d = target - eye;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                float reach = len * (1f + behind / Mathf.Max(0.05f, (center - head).magnitude));
                if (StaticHit(eye, radius, d / len, reach, out float dist, out string what) && dist / len < nearest)
                {
                    // a hit just behind the panel (within the wanted gap) still counts as "not free"
                    nearest = Mathf.Min(dist / len, 0.999f);
                    _lastHitName = what;
                }
            }
            return nearest;
        }

        /// <summary>Can you get from the head to the shifted head without passing through anything?</summary>
        public bool PathFree(Vector3 head, Vector3 shift)
        {
            Vector3 a = _rig.TransformPoint(head), b = _rig.TransformPoint(head + shift);
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return true;
            float r = 0.1f * _rig.lossyScale.x;
            // both ways: one-sided mesh colliders (walls) are only hit from their front
            return !StaticHit(a, r, d / len, len + r, out _, out _) && !StaticHit(b, r, -d / len, len + r, out _, out _);
        }

        readonly RaycastHit[] _sceneryHits = new RaycastHit[32];

        /// <summary>Nearest hit of a sphere sweep against fixed scenery; rolling marbles and other moving bodies do not count.</summary>
        bool StaticHit(Vector3 origin, float radius, Vector3 dir, float reach, out float dist, out string what)
        {
            dist = float.MaxValue; what = null;
            int n = Physics.SphereCastNonAlloc(origin, radius, dir, _sceneryHits, reach, SceneryMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _sceneryHits[i];
                if (h.distance <= 0f && h.point == Vector3.zero) continue;   // already overlapping at the start
                if (h.collider.bounds.Contains(origin)) continue;            // volumes you are inside of (level bounds)
                var body = h.collider.attachedRigidbody;
                if (body != null && !body.isKinematic) continue;
                if (h.distance < dist) { dist = h.distance; what = h.collider.name; }
            }
            return what != null;
        }

        static int SceneryMask => ~((1 << HudLayer) | (1 << 2));

        void PlaceBuild(Placement p, HandState hand, Vector3 head)
        {
            var c = Plugin.Cfg;
            var decor = VRManager.Instance != null ? VRManager.Instance.Decor : null;
            if (p == Placement.BuildAttached && decor != null && decor.TryGetMount(hand, out Vector3 mount, out Vector3 fwd, out Vector3 face))
            {
                // fixed to the controller like a tablet whose handle is the controller: it moves and turns with it.
                // Its lower edge sits just beyond the round buttons, it continues the controller's face (the side with
                // the buttons faces you); [HUD.BuildMenu] AttachedTiltTowardsYou turns it about its lower edge (positive = towards you).
                bool live = _handleDrag != HandleDrag.None;
                _build.MetersPerPixel = (live ? _liveScale : Mathf.Clamp(c.AttachedScale.Value, 0.05f, 2f)) / 1000f;
                float tilt = -(live ? _liveTilt : Mathf.Clamp(c.AttachedTilt.Value, -MaxAttachedTilt, MaxAttachedTilt)) * Mathf.Deg2Rad;   // the formula below: positive = top leans back
                _mountFwd = fwd; _mountFace = face; _mountValid = true;
                Vector3 up = fwd * Mathf.Cos(tilt) - face * Mathf.Sin(tilt);
                Vector3 towardsViewer = face * Mathf.Cos(tilt) + fwd * Mathf.Sin(tilt);
                Quaternion rot = Quaternion.LookRotation(-towardsViewer, up);
                float lift = (_build.Anchor.y - _build.Crop.yMin) * _build.MetersPerPixel;
                _build.Pos = mount + rot * new Vector3(0f, lift, 0f);
                _build.Rot = rot;
            }
            else if (p == Placement.BuildAttached)
            {
                // the toolbox floats a few centimetres above the controller, upright and turned towards you
                // (independent of how the grip pose is oriented, so it can never end up turned sideways)
                _build.MetersPerPixel = Mathf.Clamp(c.AttachedScale.Value, 0.05f, 2f) / 1000f;
                Vector3 basePos = hand.GripPos + Vector3.up * 0.075f;
                Vector3 look = basePos - head;
                look.y = Mathf.Min(look.y, 0f) * 0.5f;                       // a little tilted back when below the eyes
                Quaternion rot = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
                rot *= Quaternion.Euler(Mathf.Clamp(c.AttachedTilt.Value, -45f, 45f) * 0.5f, 0f, 0f);
                float lift = (_build.Anchor.y - _build.Crop.yMin) * _build.MetersPerPixel;
                _build.Pos = basePos + rot * new Vector3(0f, lift, 0f);
                _build.Rot = rot;
            }
            else
            {
                Pose3 pose = FramePose(new Vector3(c.EditX.Value, c.EditY.Value, c.EditZ.Value), c.EditYaw.Value, c.EditPitch.Value, c.EditRoll.Value);
                _build.Pos = pose.Pos;
                _build.Rot = pose.Rot;
                _build.MetersPerPixel = Mathf.Clamp(c.EditScale.Value, 0.05f, 3f) / 1000f;
            }
        }

        /// <summary>
        /// The off-hand "menu panel" button. Free mode: tap opens/closes the toolbox, holding carries the whole left-hand
        /// menu on the controller and releasing leaves it there. "Always on hand" mode: tap shows/hides the whole menu.
        /// </summary>
        /// <param name="alreadyHeld">the press has already been held for a while (marble mode passes L2 on late): carry the menu at once</param>
        public void MenuPanelButton(bool held, bool inMenu, bool alreadyHeld = false)
        {
            if (inMenu) held = false;
            float now = Time.unscaledTime;
            bool attachedMode = Plugin.Cfg.AlwaysAttached.Value;
            if (held && !_buttonPrev)
            {
                _buttonDownTime = alreadyHeld ? now - 1f : now;
                if (attachedMode) _pendingHide = true; else _pendingTap = true;
            }
            if (held && _pendingTap && now - _buttonDownTime > 0.3f)
            {
                _pendingTap = false;
                _holding = true;
                SyncToolbox(true);
            }
            if (!held && _buttonPrev)
            {
                if (_pendingHide) { _menuHidden = !_menuHidden; SyncToolbox(!_menuHidden); }
                else if (_pendingTap) SyncToolbox(!(ToolboxOpen() ?? false));
                else if (_holding && !attachedMode) StoreFramePose(Placement.BuildFree, _build.Pos, _build.Rot, _build.MetersPerPixel); // dropped: stays where it is
                _holding = _pendingTap = _pendingHide = false;
            }
            _buttonPrev = held;
        }

        bool _optionsHidden, _carryPrev;
        bool _toolboxWasOpen;
        float _toolboxWait;

        /// <summary>
        /// Shown build menu = open toolbox. Opens the game's toolbox when the build menu is shown; closing it with
        /// its own arrow button hides the build menu (X or the ring button bring it back).
        /// </summary>
        void KeepToolboxInSync(bool inMenu)
        {
            if (inMenu || Time.unscaledTime < _toolboxWait) return;
            bool? open = ToolboxOpen();
            if (!open.HasValue) return;
            if (!_menuHidden && !open.Value)
            {
                if (_toolboxWasOpen) _menuHidden = true;          // closed by the player
                else { SyncToolbox(true); _toolboxWait = Time.unscaledTime + 0.8f; }
            }
            _toolboxWasOpen = open.Value;
        }

        /// <summary>Something is selected (its object options exist).</summary>
        public bool HasSelection { get; private set; }

        /// <summary>The in-game menu (top bar, build menu, mod buttons) is folded away.</summary>
        public bool MenuHidden => _menuHidden;

        public void ToggleIngameMenu() { _menuHidden = !_menuHidden; if (!_menuHidden) SyncToolbox(true); }
        public void ShowIngameMenu() { _menuHidden = false; SyncToolbox(true); }

        /// <summary>Hides the main menu panel (the VR settings take its place).</summary>
        public bool HideMain;

        /// <summary>
        /// Presses the game's own control in one of the former top-bar tiles ("racing", "camreset", ...): buttons are
        /// clicked, toggles flipped. False if it is not there.
        /// </summary>
        public bool ClickTile(string name)
        {
            foreach (var tile in _tiles)
            {
                if (tile.Name != name) continue;
                foreach (var src in tile.Srcs)
                {
                    if (src == null) continue;
                    var tg = src.GetComponentInChildren<Toggle>(true);
                    if (tg != null && tg.interactable) { tg.isOn = !tg.isOn; return true; }
                    var bt = src.GetComponentInChildren<Button>(true);
                    if (bt != null && bt.interactable) { bt.onClick.Invoke(); return true; }
                }
            }
            return false;
        }

        /// <summary>On/off state of a toggle in a former top-bar tile, or null.</summary>
        public bool? TileToggle(string name)
        {
            foreach (var tile in _tiles)
                if (tile.Name == name)
                    foreach (var src in tile.Srcs)
                    {
                        var tg = src != null ? src.GetComponentInChildren<Toggle>(true) : null;
                        if (tg != null) return tg.isOn;
                    }
            return null;
        }

        /// <summary>The build menu is shown (X / ring button).</summary>
        public bool BuildShown => !_menuHidden;

        /// <summary>Centre of the main menu panel (tracking space) and its size, for the VR settings that replace it.</summary>
        public Vector3 MainCenter => _main.Root.localPosition + _main.Root.localRotation * _main.Quad.localPosition;

        /// <summary>Shows / hides the options panel of the selected object.</summary>
        public void ToggleOptions() { _optionsHidden = !_optionsHidden; }

        /// <summary>
        /// Off-hand grip: while held, the in-game menu follows the hand (it is shown if it was folded away);
        /// on release it stays where it is.
        /// </summary>
        public void CarryMenu(bool held, bool inMenu)
        {
            if (inMenu) held = false;
            if (held && !_carryPrev) { _menuHidden = false; _holding = true; }
            if (!held && _carryPrev)
            {
                if (_holding && !Plugin.Cfg.AlwaysAttached.Value) StoreFramePose(Placement.BuildFree, _build.Pos, _build.Rot, _build.MetersPerPixel);
                _holding = false;
            }
            _carryPrev = held;
        }

        public void Tick(Tracking tracking, HandState offHand, bool inMenu)
        {
            if (!Active) return;
            EnsureTextures();
            if (Time.unscaledTime >= _nextCanvasScan)
            {
                _nextCanvasScan = Time.unscaledTime + 1f;
                ConvertCanvases();
                ScanMainRaycasters();
                SetOverlayTarget(_overlayTarget, force: true);
            }
            RoutePauseCanvas(inMenu);

            int w = TextureWidth, h = TextureHeight;
            _main.Anchor = new Vector2(w * 0.5f, h * 0.5f);
            if (Time.unscaledTime >= _nextCropUpdate)
            {
                _nextCropUpdate = Time.unscaledTime + 0.1f;
                Rect crop = ComputeBuildCrop();
                // with the toolbox closed a small invisible strip keeps the window (and its handles) in place
                if (crop.width < 2f) crop = new Rect(w * 1.5f - 100f, 0f, 200f, 24f);
                // the toolbox is anchored at the bottom centre of its (virtual) screen
                _build.Crop = crop;
                _build.Anchor = new Vector2(w * 1.5f, 0f);
                _options.Crop = ComputeCrop(_optionsCanvases, _optionsCam, 2);
                // the options panel hangs from its lower edge centre, which sits above the selected object
                _options.Anchor = new Vector2(_options.Crop.center.x, _options.Crop.yMin);
                _mainGameCrop = ComputeCrop(_mainCanvases, _uiCam, 0);
                KeepToolboxInSync(inMenu);
                UpdateTiles();
            }
            // only what is really on screen: in menus the menu itself (plus an open dialog), during play tutorial
            // popups, race list ... The panel is centred on that content, not on the whole virtual desktop.
            Rect content = _mainGameCrop;
            if (inMenu && (content.width < 40f || content.height < 40f)) content = new Rect(0f, 0f, w, h);
            _main.Crop = content;
            if (inMenu) _main.Anchor = content.center;

            Placement main = inMenu ? (HideMain ? Placement.Hidden : Placement.Menu) : Placement.GameHud;
            // the build menu floats above the off-hand controller
            Placement build;
            if (inMenu || _menuHidden) build = Placement.Hidden;
            else if (offHand.Valid) build = Placement.BuildAttached;
            else build = Placement.BuildFree;

            if ((_dragPanel == _main && main != _main.Placement) || (_dragPanel == _build && build != _build.Placement)) EndDrag(save: false);
            _main.Placement = main;
            _build.Placement = build;
            _main.HasWidgets = true;
            _build.HasWidgets = false;

            if (_dragPanel != _main) { PlaceMain(main); KeepInFrontOfScenery(_main, tracking.HeadPos); }
            if (_dragPanel != _build && build != Placement.Hidden) PlaceBuild(build, offHand, tracking.HeadPos);
            // object options: above the selected object(s), always turned towards you
            Vector3 top = default;
            bool selection = !inMenu && TrySelectionTop(out top, out float objectSize);
            if (!selection) _optionsHidden = false;     // a new selection shows its options again
            HasSelection = selection;
            _options.Placement = selection && !_optionsHidden ? Placement.Options : Placement.Hidden;
            _options.HasWidgets = false;
            if (_options.Placement == Placement.Options)
            {
                Vector3 head = tracking.HeadPos;
                Vector3 toHead = head - top;
                float dist = toHead.magnitude;
                // a little towards the viewer and just above the object, so the panel does not cover it
                Vector3 pos = top + Vector3.up * 0.02f + (dist > 1e-3f ? toHead / dist : Vector3.zero) * Mathf.Min(0.08f, dist * 0.2f);
                Vector3 look = pos - head;
                _options.Rot = look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
                _options.Pos = pos;
                // constant apparent size: scales with the distance to the viewer
                _options.MetersPerPixel = Mathf.Clamp(Plugin.Cfg.OptionsScale.Value, 0.05f, 2f) / 1000f * Mathf.Clamp(dist / 0.8f, 0.4f, 3f);
            }

            _overlay.Placement = inMenu ? Placement.Hidden : Placement.Overlay;
            _overlay.HasWidgets = false;
            _overlay.Crop = new Rect(0f, 0f, w, h);
            _overlay.Anchor = new Vector2(w * 0.5f, h * 0.5f);
            PlaceOverlay();

            _main.Apply();
            _build.Apply();
            PlaceBuildControls();
            LayoutBar();
            _options.Apply();
        }

        // ---------------------------------------------------------------- world labels

        /// <summary>One label (or a cluster of touching labels) cut out of the label texture, shown at its object.</summary>
        class LabelPiece
        {
            public readonly Transform Quad;
            public readonly Material Mat;
            public Rect Crop;

            public LabelPiece(Transform parent)
            {
                Mat = CreateMaterial();
                Quad = MakeQuad("CozyVR Label", Mat, parent);
            }

            public void Show(RenderTexture tex, Rect crop, Vector3 pos, Quaternion rot, float metersPerPixel)
            {
                Crop = crop;
                if (Mat.mainTexture != tex) { Mat.mainTexture = tex; if (Mat.HasProperty("_BaseMap")) Mat.SetTexture("_BaseMap", tex); }
                var scale = new Vector2(crop.width / tex.width, crop.height / tex.height);
                var offset = new Vector2(crop.xMin / tex.width, crop.yMin / tex.height);
                if (Mat.HasProperty("_BaseMap")) { Mat.SetTextureScale("_BaseMap", scale); Mat.SetTextureOffset("_BaseMap", offset); }
                Mat.mainTextureScale = scale;
                Mat.mainTextureOffset = offset;
                Quad.localPosition = pos;
                Quad.localRotation = rot;
                Quad.localScale = new Vector3(crop.width * metersPerPixel, crop.height * metersPerPixel, 1f);
                if (!Quad.gameObject.activeSelf) Quad.gameObject.SetActive(true);
            }

            public bool Raycast(Ray ray, out Vector2 pixel, out Vector3 point, out float dist)
            {
                pixel = default; point = default; dist = float.MaxValue;
                if (!Quad.gameObject.activeInHierarchy) return false;
                Vector3 n = Quad.forward;
                float denom = Vector3.Dot(ray.direction, n);
                if (Mathf.Abs(denom) < 1e-4f) return false;
                float t = Vector3.Dot(Quad.position - ray.origin, n) / denom;
                if (t <= 0f) return false;
                point = ray.origin + ray.direction * t;
                Vector3 local = Quad.InverseTransformPoint(point);
                if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f) return false;
                dist = t;
                pixel = new Vector2(Crop.xMin + (local.x + 0.5f) * Crop.width, Crop.yMin + (local.y + 0.5f) * Crop.height);
                return true;
            }
        }

        readonly List<LabelPiece> _labelPieces = new List<LabelPiece>();
        readonly List<Rect> _labelRects = new List<Rect>();
        readonly List<Graphic> _graphicBuffer = new List<Graphic>();
        readonly Vector3[] _cornerBuffer = new Vector3[4];
        readonly Vector3[] _projWorld = new Vector3[VRShim.ProjectionCapacity];
        readonly Vector3[] _projScreen = new Vector3[VRShim.ProjectionCapacity];
        int _projCount, _projFrame = -1;
        int _labelsShown;
        public int LabelCount => _labelsShown;

        /// <summary>
        /// The game draws its labels on the (virtual) screen at the projection of a world point. In VR each label
        /// is cut out of that picture and put back into the room at the depth of its object, so both eyes see it
        /// where the object is. The world point comes from the game's own WorldToScreenPoint call (VRShim records
        /// them); labels without one are placed on whatever the view ray through them hits.
        /// </summary>
        public void PlaceOverlay()
        {
            if (Time.frameCount != _projFrame)
            {
                _projFrame = Time.frameCount;
                _projCount = VRShim.ProjectedCount;
                System.Array.Copy(VRShim.ProjectedWorld, _projWorld, _projCount);
                System.Array.Copy(VRShim.ProjectedScreen, _projScreen, _projCount);
                VRShim.ProjectedCount = 0;
            }

            // the picture of the old single layer is never shown; its root holds the pieces
            var root = _overlay.Root;
            bool show = _overlay.Placement == Placement.Overlay && _overlayRt != null && VRShim.ScreenWidth >= 1f;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            _labelsShown = 0;
            if (show)
            {
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
                foreach (var t in new[] { _overlay.Quad, _overlay.Bar, _overlay.Corner, _overlay.Gear })
                    if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
                LayoutLabels(root);
            }
            for (int i = _labelsShown; i < _labelPieces.Count; i++)
                if (_labelPieces[i].Quad.gameObject.activeSelf) _labelPieces[i].Quad.gameObject.SetActive(false);
        }

        void CollectLabelRects()
        {
            _labelRects.Clear();
            if (_overlayCam == null) return;
            int w = TextureWidth, h = TextureHeight;
            foreach (var canvas in _overlayCanvases)
            {
                if (canvas == null || !canvas.enabled || !canvas.gameObject.activeInHierarchy) continue;
                canvas.GetComponentsInChildren(false, _graphicBuffer);
                foreach (var g in _graphicBuffer)
                {
                    if (!g.enabled || g.color.a < 0.02f || g.canvasRenderer.cull || g.canvasRenderer.GetInheritedAlpha() < 0.02f) continue;
                    g.rectTransform.GetWorldCorners(_cornerBuffer);
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 p = RectTransformUtility.WorldToScreenPoint(_overlayCam, _cornerBuffer[i]);
                        x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
                    }
                    if (x1 - x0 < 1f || y1 - y0 < 1f) continue;
                    if (x1 - x0 > w * 0.95f && y1 - y0 > h * 0.9f) continue;   // full-screen backgrounds
                    if (x1 < 0f || x0 > w || y1 < 0f || y0 > h) continue;      // off screen
                    // a little margin: the label may move a bit between this layout and the texture's next render
                    const float pad = 10f;
                    var r = Rect.MinMaxRect(Mathf.Max(0f, Mathf.Floor(x0 - pad)), Mathf.Max(0f, Mathf.Floor(y0 - pad)),
                                            Mathf.Min(w, Mathf.Ceil(x1 + pad)), Mathf.Min(h, Mathf.Ceil(y1 + pad)));
                    // merge with every rectangle it touches (a label's text, frame and buttons become one piece)
                    for (int i = _labelRects.Count - 1; i >= 0; i--)
                    {
                        if (!_labelRects[i].Overlaps(r)) continue;
                        var o = _labelRects[i];
                        r = Rect.MinMaxRect(Mathf.Min(r.xMin, o.xMin), Mathf.Min(r.yMin, o.yMin), Mathf.Max(r.xMax, o.xMax), Mathf.Max(r.yMax, o.yMax));
                        _labelRects.RemoveAt(i);
                        i = _labelRects.Count;   // the grown rectangle may now touch others: start over
                    }
                    _labelRects.Add(r);
                }
            }
        }

        void LayoutLabels(Transform parent)
        {
            CollectLabelRects();
            if (_labelRects.Count == 0) return;
            Vector3 eye = _rig.InverseTransformPoint(VRShim.EyePosition);
            Vector3 n = _rig.InverseTransformDirection(VRShim.ScreenNormal).normalized;
            Vector3 up = _rig.InverseTransformDirection(VRShim.ScreenUp).normalized;
            if (n.sqrMagnitude < 0.5f || up.sqrMagnitude < 0.5f) return;
            Quaternion rot = Quaternion.LookRotation(n, up);
            // width of the virtual screen at 1 unit of depth
            float widthAtOne = VRShim.ScreenRight.magnitude * VRShim.ScreenWidth;
            float rigScale = Mathf.Max(1e-4f, _rig.lossyScale.x);
            int mask = ~(1 << HudLayer);

            foreach (var r in _labelRects)
            {
                // which projected world point belongs to this label: the nearest one in or just around it
                float margin = Mathf.Max(24f, r.height * 0.6f);
                var near = new Rect(r.xMin - margin, r.yMin - margin, r.width + 2f * margin, r.height + 2f * margin);
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < _projCount; i++)
                {
                    Vector2 s = _projScreen[i];
                    if (!near.Contains(s)) continue;
                    float d = (s - r.center).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }

                float depth;   // metres along the screen normal
                if (best >= 0)
                    depth = Vector3.Dot(_rig.InverseTransformPoint(_projWorld[best]) - eye, n);
                else if (Physics.Raycast(VRShim.ScreenPointToWorldRay(r.center), out RaycastHit hit, 5000f, mask, QueryTriggerInteraction.Ignore))
                    depth = Vector3.Dot(_rig.InverseTransformPoint(hit.point) - eye, n);
                else
                    depth = 0.9f;
                // a touch towards the viewer, so the label does not sink into its own object
                depth = Mathf.Max(0.2f, depth - Mathf.Min(0.04f, depth * 0.04f));

                Vector3 onScreen = VRShim.ScreenOrigin + VRShim.ScreenRight * r.center.x + VRShim.ScreenUp * r.center.y;
                Vector3 dir = _rig.InverseTransformDirection(onScreen - VRShim.EyePosition) / rigScale;
                float along = Vector3.Dot(dir, n);
                if (along < 1e-4f) continue;
                Vector3 pos = eye + dir * (depth / along);

                if (_labelsShown >= _labelPieces.Count) _labelPieces.Add(new LabelPiece(parent));
                _labelPieces[_labelsShown++].Show(_overlayRt, r, pos, rot, widthAtOne * depth / VRShim.ScreenWidth);
            }
        }

        bool RaycastLabels(Ray ray, out Vector2 pixel, out Vector3 point, out float dist)
        {
            pixel = default; point = default; dist = float.MaxValue;
            bool any = false;
            for (int i = 0; i < _labelsShown; i++)
            {
                if (_labelPieces[i].Raycast(ray, out Vector2 p, out Vector3 w, out float d) && d < dist)
                { pixel = p; point = w; dist = d; any = true; }
            }
            return any;
        }

        static readonly System.Type OptionsUiType = System.Type.GetType("TimR.StructureOptions.StructureOptionsUi, Assembly-CSharp");

        /// <summary>Top centre (tracking space) of the currently selected objects.</summary>
        bool TrySelectionTop(out Vector3 top, out float size)
        {
            top = default; size = 0f;
            if (_options.Crop.width < 2f) return false;
            try
            {
                if (_optionsUi == null && OptionsUiType != null) _optionsUi = Object.FindFirstObjectByType(OptionsUiType) as Component;
                var list = _optionsUi != null ? OptionsUiType.GetProperty("SelectedPlaceables")?.GetValue(_optionsUi) as System.Collections.IList : null;
                if (list == null || list.Count == 0) return false;
                bool any = false;
                var bounds = new Bounds();
                foreach (var item in list)
                {
                    var comp = item as Component;
                    if (comp == null) continue;
                    foreach (var r in comp.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!r.enabled || r is ParticleSystemRenderer) continue;
                        if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                    }
                    if (!any) { bounds = new Bounds(comp.transform.position, Vector3.one * 0.1f); any = true; }
                }
                if (!any) return false;
                top = _rig.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
                size = bounds.size.magnitude / Mathf.Max(1e-3f, _rig.lossyScale.x);
                return true;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- widgets

        static bool HitQuad(Transform t, Ray worldRay, out Vector3 hit, out float dist)
        {
            hit = default;
            dist = 0f;
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            var plane = new Plane(t.forward, t.position);
            if (!plane.Raycast(worldRay, out dist)) return false;
            hit = worldRay.GetPoint(dist);
            Vector3 local = t.InverseTransformPoint(hit);
            // 1 cm extra margin makes the small handles easier to hit without letting them overlap
            Vector3 s = t.localScale;
            return Mathf.Abs(local.x) <= 0.5f + 0.01f / Mathf.Max(1e-4f, s.x) && Mathf.Abs(local.y) <= 0.5f + 0.01f / Mathf.Max(1e-4f, s.y);
        }

        PanelView _hoverPanel;

        public Widget HitWidget(Ray worldRay, out Vector3 hit, out float dist)
        {
            var w1 = _main.HitWidget(worldRay, out Vector3 h1, out float d1);
            var w2 = _build.HitWidget(worldRay, out Vector3 h2, out float d2);
            PanelView panel = null;
            Widget best = Widget.None;
            hit = default; dist = float.MaxValue;
            if (w1 != Widget.None) { best = w1; hit = h1; dist = d1; panel = _main; }
            if (w2 != Widget.None && d2 < dist) { best = w2; hit = h2; dist = d2; panel = _build; }

            // the mod's own small buttons next to the toolbox
            MiniButton mini = null;
            if (_build.Placement != Placement.Hidden)
            {
                if (HitQuad(_miniInfo.Quad, worldRay, out Vector3 h3, out float d3) && d3 < dist) { best = Widget.Info; hit = h3; dist = d3; panel = null; mini = _miniInfo; }
                if (HitQuad(_miniVr.Quad, worldRay, out Vector3 h4, out float d4) && d4 < dist) { best = Widget.Settings; hit = h4; dist = d4; panel = null; mini = _miniVr; }
                if (HitQuad(_miniGrab.Quad, worldRay, out Vector3 h5, out float d5) && d5 < dist) { best = Widget.Grab; hit = h5; dist = d5; panel = null; mini = _miniGrab; }
                if (HitQuad(_miniMarble.Quad, worldRay, out Vector3 h6, out float d6) && d6 < dist) { best = Widget.Marble; hit = h6; dist = d6; panel = null; mini = _miniMarble; }
                for (int i = 0; i < _buildCtl.Length; i++)
                    if (HitQuad(_buildCtl[i].Quad, worldRay, out Vector3 h7, out float d7) && d7 < dist) { best = BuildCtlWidgets[i]; hit = h7; dist = d7; panel = null; mini = _buildCtl[i]; }
            }
            _hoverPanel = panel;
            _hoverMini = mini;
            if (mini != null) _focus = _build;
            return best;
        }

        public void SetWidgetHover(Widget w)
        {
            _main.SetHover(_hoverPanel == _main ? w : Widget.None, _drag == Drag.Move && _dragPanel == _main, _drag == Drag.Scale && _dragPanel == _main);
            _build.SetHover(_hoverPanel == _build ? w : Widget.None, _drag == Drag.Move && _dragPanel == _build, _drag == Drag.Scale && _dragPanel == _build);
            _miniInfo.SetHover(_hoverMini == _miniInfo && w == Widget.Info);
            _miniVr.SetHover(_hoverMini == _miniVr && w == Widget.Settings);
            _miniGrab.SetHover(_hoverMini == _miniGrab && w == Widget.Grab);
            _miniMarble.SetHover(_hoverMini == _miniMarble && w == Widget.Marble);
            for (int i = 0; i < _buildCtl.Length; i++)
                _buildCtl[i].SetHover((_hoverMini == _buildCtl[i] && w == BuildCtlWidgets[i]) || (int)_handleDrag == i + 1);
            if (w != Widget.None && _hoverPanel != null) _focus = _hoverPanel;
        }

        /// <summary>Starts moving or scaling the hovered panel with the laser (local = tracking-space ray).</summary>
        public void BeginDrag(Widget w, Ray localRay)
        {
            if (BeginHandleDrag(w, localRay)) return;
            var p = _hoverPanel ?? _main;
            _focus = p;
            Vector3 center = p.Pos + p.Rot * p.Quad.localPosition;
            var plane = new Plane(p.Rot * Vector3.forward, center);
            if (!plane.Raycast(localRay, out float t)) t = Vector3.Distance(localRay.origin, center);
            Vector3 hit = localRay.GetPoint(t);
            _dragPanel = p;
            if (w == Widget.Bar)
            {
                _drag = Drag.Move;
                _dragDist = t;
                _dragOffset = Quaternion.Inverse(p.Rot) * (p.Pos - hit);
            }
            else if (w == Widget.Corner)
            {
                _drag = Drag.Scale;
                _dragD0 = Mathf.Max(0.05f, Vector3.Distance(hit, center));
                _dragMpp0 = p.MetersPerPixel;
            }
        }

        public void UpdateDrag(Ray localRay, Vector3 headPos, float stickY, float dt)
        {
            if (_handleDrag != HandleDrag.None) { UpdateHandleDrag(localRay); return; }
            var p = _dragPanel;
            if (p == null) return;
            if (_drag == Drag.Move)
            {
                // the stick pushes the panel away or pulls it closer while it is held
                if (Mathf.Abs(stickY) > 0.2f) _dragDist = Mathf.Clamp(_dragDist + stickY * 1.2f * dt, 0.25f, 8f);
                Vector3 hit = localRay.GetPoint(_dragDist);
                Vector3 pos = hit + p.Rot * _dragOffset;
                Vector3 look = pos - headPos;
                if (look.sqrMagnitude > 1e-4f) p.Rot = Quaternion.LookRotation(look.normalized, Vector3.up);
                p.Pos = hit + p.Rot * _dragOffset;
            }
            else if (_drag == Drag.Scale)
            {
                Vector3 center = p.Pos + p.Rot * p.Quad.localPosition;
                var plane = new Plane(p.Rot * Vector3.forward, center);
                if (plane.Raycast(localRay, out float t))
                {
                    float d = Vector3.Distance(localRay.GetPoint(t), center);
                    float mpp = _dragMpp0 * d / _dragD0;
                    float maxWidth = p == _main ? 5f : 3f;
                    p.MetersPerPixel = Mathf.Clamp(mpp, 0.2f / Mathf.Max(1f, p.Crop.width), maxWidth / Mathf.Max(1f, p.Crop.width));
                }
            }
            p.Apply();
        }

        public void EndDrag(bool save = true)
        {
            EndHandleDrag();
            if (_drag != Drag.None && save && _dragPanel != null) StoreFramePose(_dragPanel.Placement, _dragPanel.Pos, _dragPanel.Rot, _dragPanel.MetersPerPixel);
            _drag = Drag.None;
            _dragPanel = null;
        }

        // ---------------------------------------------------------------- settings actions

        public void ResetMenuPose()
        {
            var c = Plugin.Cfg;
            foreach (var e in new BepInEx.Configuration.ConfigEntryBase[] { c.MenuX, c.MenuY, c.MenuZ, c.MenuYaw, c.MenuPitch, c.MenuWidth })
                e.BoxedValue = e.DefaultValue;
        }

        public void ResetHudPose()
        {
            var c = Plugin.Cfg;
            foreach (var e in new BepInEx.Configuration.ConfigEntryBase[] { c.HudX, c.HudY, c.HudZ, c.HudYaw, c.HudPitch, c.HudWidth })
                e.BoxedValue = e.DefaultValue;
        }

        public void ResetEditPose()
        {
            var c = Plugin.Cfg;
            foreach (var e in new BepInEx.Configuration.ConfigEntryBase[] { c.EditX, c.EditY, c.EditZ, c.EditYaw, c.EditPitch, c.EditRoll, c.EditScale, c.AttachedScale })
                e.BoxedValue = e.DefaultValue;
            _menuHidden = false;
        }

        /// <summary>Moves the main menu (or in-game HUD) closer or further away along its current direction.</summary>
        public void ChangeDistance(bool hud, float delta)
        {
            var c = Plugin.Cfg;
            var x = hud ? c.HudX : c.MenuX; var y = hud ? c.HudY : c.MenuY; var z = hud ? c.HudZ : c.MenuZ;
            var rel = new Vector3(x.Value, y.Value, z.Value);
            float d = rel.magnitude;
            if (d < 0.01f) return;
            float nd = Mathf.Clamp(d + delta, 0.4f, 8f);
            rel *= nd / d;
            x.Value = rel.x; y.Value = rel.y; z.Value = rel.z;
        }

        // ---------------------------------------------------------------- hit testing of the UI pictures

        /// <summary>
        /// Nearest panel hit by a world ray. pixel is in mouse coordinates (main UI: x 0..W, build toolbox: x W..2W).
        /// solid: the panel blocks the laser everywhere inside its rectangle (build panel, or any panel while in menus).
        /// </summary>
        public bool Raycast(Ray ray, out Vector2 pixel, out Vector3 worldPoint, out bool inside, out bool solid)
        {
            // 1) solid panels (build window, object options): anything inside their rectangle is theirs
            PanelView bestSolid = null;
            Vector2 bp = default; Vector3 bw = default; float bd = float.MaxValue;
            _solidViews.Clear();
            _solidViews.Add(_build);
            _solidViews.Add(_options);
            foreach (var t in _tiles) _solidViews.Add(t.View);
            foreach (var view in _solidViews)
            {
                if (view.Raycast(ray, out Vector2 vp, out Vector3 vw, out bool vi, out float vd) && vi && vd < bd)
                { bestSolid = view; bp = vp; bw = vw; bd = vd; }
            }
            bool hMain = _main.Raycast(ray, out Vector2 mp, out Vector3 mw, out bool mi, out float md);
            if (bestSolid != null && (!hMain || !mi || bd <= md || _main.Placement != Placement.Menu))
            {
                SetOverlayTarget(false);
                pixel = bp; worldPoint = bw; inside = true; solid = true;
                return true;
            }

            // 2) world labels: only where an actual label/button is
            if (RaycastLabels(ray, out Vector2 op, out Vector3 ow, out float od) && (!hMain || !mi || od < md))
            {
                SetOverlayTarget(true);
                if (IsOverUi(op))
                {
                    pixel = op; worldPoint = ow; inside = true; solid = true;
                    return true;
                }
            }

            // 3) main menu / in-game HUD
            SetOverlayTarget(false);
            pixel = mp; worldPoint = mw; inside = mi;
            solid = _main.Placement == Placement.Menu;
            return hMain;
        }

        public Vector3 PixelToWorld(Vector2 pixel)
        {
            if (pixel.x >= 3 * TextureWidth) { var tile = TileAt(pixel); return tile != null ? tile.PixelToWorld(pixel) : _build.PixelToWorld(pixel); }
            return pixel.x >= 2 * TextureWidth ? _options.PixelToWorld(pixel) : pixel.x >= TextureWidth ? _build.PixelToWorld(pixel) : _main.PixelToWorld(pixel);
        }

        /// <summary>True if a clickable UI element is under the given pixel.</summary>
        public bool IsOverUi(Vector2 pixel) => TopUiObjectAt(pixel) != null;

        // ---------------------------------------------------------------- focus navigation (D-pad / stick)

        /// <summary>The topmost clickable UI object under a pixel, or null.</summary>
        public GameObject TopUiObjectAt(Vector2 pixel)
        {
            var es = EventSystem.current;
            if (es == null) return null;
            if (_ped == null) _ped = new PointerEventData(es);
            _ped.Reset();
            _ped.position = pixel;
            _hits.Clear();
            es.RaycastAll(_ped, _hits);
            for (int i = 0; i < _hits.Count; i++)
                if (_hits[i].module is GraphicRaycaster)
                    return _hits[i].gameObject;
            return null;
        }

        /// <summary>Centre of a UI element in mouse pixels (only for elements on a visible panel).</summary>
        public bool TryGetPixel(Selectable s, out Vector2 pixel)
        {
            pixel = default;
            if (s == null || _uiCam == null) return false;
            var rt = s.transform as RectTransform;
            if (rt == null) return false;
            var parentCanvas = s.GetComponentInParent<Canvas>();
            var canvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            Camera cam = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != null ? canvas.worldCamera : _uiCam;
            pixel = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
            if (cam == _buildCam) return _build.Visible && _build.Contains(pixel);
            if (cam == _optionsCam) return _options.Visible && _options.Contains(pixel);
            if (cam == _barCam) return TileAt(pixel) != null;
            if (cam == _overlayCam) return false;
            return _main.Visible && pixel.x >= 0f && pixel.y >= 0f && pixel.x <= Width && pixel.y <= Height;
        }

        /// <summary>Usable right now: active, interactable, on a visible panel and not covered by another element.</summary>
        public bool IsReachable(Selectable s, out Vector2 pixel)
        {
            pixel = default;
            if (s == null || !s.IsActive() || !s.IsInteractable()) return false;
            if (!TryGetPixel(s, out pixel)) return false;
            GameObject top = TopUiObjectAt(pixel);
            return top != null && top.transform.IsChildOf(s.transform);
        }

        /// <summary>
        /// Finds the UI element to focus next. With a direction: the best neighbour of <paramref name="current"/> that way
        /// (null if there is none). Without: the element nearest to <paramref name="from"/>.
        /// </summary>
        public Selectable FindSelectable(Vector2 from, Vector2 direction, Selectable current, out Vector2 pixel)
        {
            pixel = default;
            Selectable best = null;
            float bestScore = float.NegativeInfinity;
            bool directional = direction != Vector2.zero;
            var all = Selectable.allSelectablesArray;
            for (int i = 0; i < all.Length; i++)
            {
                Selectable s = all[i];
                if (s == current || !s.IsActive() || !s.IsInteractable()) continue;
                if (!TryGetPixel(s, out Vector2 p)) continue;
                Vector2 v = p - from;
                float dist = v.magnitude;
                float score;
                if (directional)
                {
                    if (dist < 1f) continue;
                    float along = Vector2.Dot(v / dist, direction);
                    if (along < 0.35f) continue;           // not in that direction
                    score = along * along / dist;           // straight ahead and close wins
                }
                else score = -dist;
                if (score <= bestScore) continue;
                GameObject top = TopUiObjectAt(p);          // only now the (more expensive) visibility test
                if (top == null || !top.transform.IsChildOf(s.transform)) continue;
                best = s;
                bestScore = score;
                pixel = p;
            }
            return best;
        }

        /// <summary>A visible "back / close" style button, for dialogs that do not react to Escape.</summary>
        public Selectable FindBackButton(out Vector2 pixel)
        {
            pixel = default;
            var all = Selectable.allSelectablesArray;
            for (int i = 0; i < all.Length; i++)
            {
                Selectable s = all[i];
                if (!(s is Button) || !s.IsActive() || !s.IsInteractable()) continue;
                var label = s.GetComponentInChildren<TMPro.TMP_Text>();
                string text = ((label != null ? label.text : "") + " " + s.name).ToLowerInvariant();
                bool back = false;
                for (int k = 0; k < BackWords.Length && !back; k++) back = text.Contains(BackWords[k]);
                if (back && IsReachable(s, out pixel)) return s;
            }
            return null;
        }

        static readonly string[] BackWords =
        {
            "zurück", "back", "retour", "atrás", "volver", "indietro", "voltar", "terug", "wstecz", "назад", "geri", "戻る", "返回", "뒤로", "schließen", "close"
        };

        /// <summary>World position of a point on a panel's widgets (diagnostics): bar, corner, vr; build panel with "b" prefix.</summary>
        public Transform RigTransform => _build.Root.parent;

        /// <summary>World position of a widget and its rotation relative to the rig (diagnostics).</summary>
        public Vector3 DebugWidgetPoint(string name, out Quaternion rigRot)
        {
            var t = DebugWidget(name);
            var rig = RigTransform;
            rigRot = rig != null ? Quaternion.Inverse(rig.rotation) * t.rotation : t.rotation;
            return t.position;
        }

        Transform DebugWidget(string name)
        {
            if (name == "info") return _miniInfo.Quad;
            for (int i = 0; i < _buildCtl.Length; i++) if (name == "bctl" + i) return _buildCtl[i].Quad;
            if (name == "mvr") return _miniVr.Quad;
            if (name == "mgrab") return _miniGrab.Quad;
            if (name == "mmarble") return _miniMarble.Quad;
            foreach (var tile in _tiles) if (name == "tile" + tile.Name) return tile.View.Quad;
            var p = name.StartsWith("b") ? _build : _main;
            string n = name.StartsWith("b") ? name.Substring(1) : name;
            return n == "bar" ? p.Bar : n == "corner" ? p.Corner : p.Gear;
        }

        /// <summary>Diagnostics: graphics of the main UI right of the given x (texture pixels) that widen the crop.</summary>
        public string DebugMainGraphics(float minX)
        {
            var sb = new System.Text.StringBuilder("[diag] main graphics right of " + minX + ":\n");
            var corners = new Vector3[4];
            int w = TextureWidth, h = TextureHeight;
            foreach (var canvas in _mainCanvases)
            foreach (var g in (canvas != null && canvas.gameObject.activeInHierarchy && canvas.enabled) ? canvas.GetComponentsInChildren<Graphic>(false) : new Graphic[0])
            {
                if (!g.enabled || g.color.a < 0.02f || g.canvasRenderer.cull || g.canvasRenderer.GetInheritedAlpha() < 0.02f) continue;
                g.rectTransform.GetWorldCorners(corners);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                for (int i = 0; i < 4; i++) { Vector2 p = RectTransformUtility.WorldToScreenPoint(_uiCam, corners[i]); x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
                if (x1 - x0 > w * 0.95f && y1 - y0 > h * 0.9f) continue;
                if (x1 < minX) continue;
                sb.AppendLine($"  {canvas.name}/{g.name} ({g.GetType().Name}) x={x0:0}..{x1:0} y={y0:0}..{y1:0} a={g.color.a:0.00}");
            }
            return sb.ToString();
        }

        public string DebugState()
        {
            return $"panels: options={_options.Placement} crop={_options.Crop} pos={_options.Pos} overlay={_overlay.Placement} labels={_labelsShown} mainPull={_mainPull.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} nearest={_lastNearest.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} hit={_lastHitName} mainPos={_main.Pos} head={_lastHead} target={_overlayTarget} | main={_main.Placement} | build={_build.Placement} buildPos={_build.Pos} buildFwd={_build.Rot * Vector3.forward} buildUp={_build.Rot * Vector3.up} mpp={_build.MetersPerPixel} crop={_build.Crop} hidden={_menuHidden} tiles={string.Join(",", _tiles.ConvertAll(t => t.Name + (t.Found ? "+" : "-")))} mainCrop={_main.Crop} toolboxOpen={ToolboxOpen()} canvas={(_buildCanvas != null)} handle={_handleDrag} live={_liveScale:0.000}/{_liveTilt:0.0} angle0={_dragAngle0:0.0} angle={_lastHandAngle:0.0} mountF={_mountFwd} mountN={_mountFace}";
        }
    }
}
