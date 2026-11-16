using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CozyMarblesVR
{
    /// <summary>
    /// Start window on the desktop: VR or monitor, which screen, resolution and window mode.
    /// Built like the game's own dialogs (cream paper, yellow header, game font) on an overlay canvas as soon as the
    /// game's font is loaded; the game's input modules are paused meanwhile, so clicks are hit-tested here.
    /// If the font does not show up within a few seconds, a plain IMGUI version is drawn instead.
    /// Can be switched off with a checkbox and switched on again in the game (game settings, VR settings).
    /// </summary>
    public class StartDialog : MonoBehaviour
    {
        readonly List<DisplayInfo> _displays = new List<DisplayInfo>();
        int _display;
        List<Vector2Int> _resolutions = new List<Vector2Int>();
        int _resolution;
        int _mode;                 // 0 = borderless full screen, 1 = exclusive full screen, 2 = window
        bool _dontShowAgain;
        bool _busy;
        readonly List<BaseInputModule> _blockedModules = new List<BaseInputModule>();
        public static StartDialog Instance { get; private set; }

        void Awake() { Instance = this; _startTime = Time.unscaledTime; }
        float _startTime;
        bool _fallback;
        GUIStyle _title, _label, _button, _bigButton, _toggle, _hint;

        static string[] ModeNames => new[] { Loc.T("Vollbild (Fenster)", "Fullscreen (borderless)"), Loc.T("Vollbild (exklusiv)", "Fullscreen (exclusive)"), Loc.T("Fenster", "Windowed") };
        static readonly FullScreenMode[] Modes = { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };

        void Start()
        {
            Refresh();
            _mode = Screen.fullScreenMode == FullScreenMode.Windowed || Screen.fullScreenMode == FullScreenMode.MaximizedWindow ? 2
                  : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? 1 : 0;
        }

        void Refresh()
        {
            _displays.Clear();
            Screen.GetDisplayLayout(_displays);
            DisplayInfo current = Screen.mainWindowDisplayInfo;
            _display = Mathf.Max(0, _displays.FindIndex(d => d.name == current.name && d.width == current.width && d.height == current.height));

            _resolutions = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct()
                .OrderByDescending(v => v.x * v.y).ToList();
            var native = new Vector2Int(current.width, current.height);
            if (native.x > 0 && !_resolutions.Contains(native)) _resolutions.Insert(0, native);
            if (_resolutions.Count == 0) _resolutions.Add(new Vector2Int(Screen.width, Screen.height));

            // preselect: the game's saved resolution if it fits this screen, otherwise the screen's own size
            var saved = new Vector2Int(PlayerPrefs.GetInt("resX", 0), PlayerPrefs.GetInt("resY", 0));
            _resolution = _resolutions.IndexOf(saved);
            if (_resolution < 0) _resolution = Mathf.Max(0, _resolutions.IndexOf(native));
        }

        void Update()
        {
            if (_canvas == null && !_fallback)
            {
                if (VRSettingsPanel.FindFont() != null) BuildUi();
                else if (Time.unscaledTime - _startTime > 3f) { _fallback = true; Plugin.Log.LogWarning("Start window: game font not found, using the plain window."); }
            }
            if (_canvas != null) TickUi();

            // clicks on the window must not reach the game's main menu underneath (the EventSystem itself
            // must stay on, the game's code relies on EventSystem.current)
            var es = EventSystem.current;
            if (es == null) return;
            foreach (var m in es.GetComponents<BaseInputModule>())
                if (m.enabled) { m.enabled = false; _blockedModules.Add(m); }
        }

        void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            foreach (var m in _blockedModules) if (m != null) m.enabled = true;
            if (Instance == this) Instance = null;
        }

        void Styles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = new Color(1f, 0.85f, 0.4f);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleLeft };
            _label.normal.textColor = Color.white;
            _hint = new GUIStyle(_label) { fontSize = 14, wordWrap = true };
            _hint.normal.textColor = new Color(0.8f, 0.8f, 0.85f);
            _button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            _bigButton = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 16 };
            _toggle.normal.textColor = _toggle.onNormal.textColor = Color.white;
        }

        void OnGUI()
        {
            if (!_fallback) return;
            Styles();
            const float W = 620f, H = 560f;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / (W + 40f), Screen.height / (H + 40f)), 0.6f, 3f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float sw = Screen.width / scale, sh = Screen.height / scale;

            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
            GUI.color = new Color(0.16f, 0.12f, 0.22f, 0.98f);
            var r = new Rect((sw - W) * 0.5f, (sh - H) * 0.5f, W, H);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(r.x + 24f, r.y + 16f, W - 48f, H - 32f));
            GUILayout.Label("Cozy Marbles VR", _title, GUILayout.Height(40f));
            GUILayout.Space(10f);

            GUI.enabled = !_busy;
            Row(Loc.T("Bildschirm", "Display"), _displays.Count > 0 ? $"{_displays[_display].name}  ({_displays[_display].width}×{_displays[_display].height})" : "-",
                () => ChangeDisplay(-1), () => ChangeDisplay(1), _displays.Count > 1);
            Row(Loc.T("Auflösung", "Resolution"), $"{_resolutions[_resolution].x} × {_resolutions[_resolution].y}",
                () => _resolution = (_resolution + _resolutions.Count - 1) % _resolutions.Count,
                () => _resolution = (_resolution + 1) % _resolutions.Count, _resolutions.Count > 1);
            Row(Loc.T("Anzeige", "Mode"), ModeNames[_mode], () => _mode = (_mode + 2) % 3, () => _mode = (_mode + 1) % 3, true);

            GUILayout.Label(Loc.T("Gilt nur für das Fenster am PC, nicht für die Brille.", "Only for the window on the PC, not for the headset."), _hint);
            GUILayout.Space(6f);
            var cfg = Plugin.Cfg;
            Row(Loc.T("VR pro Auge", "VR per eye"), VRManager.PresetText(cfg.EyeResolution.Value),
                () => cfg.EyeResolution.Value = VRManager.StepPreset(cfg.EyeResolution.Value, -1),
                () => cfg.EyeResolution.Value = VRManager.StepPreset(cfg.EyeResolution.Value, 1), true);
            Row(Loc.T("Zuschauer", "Spectator"), cfg.DesktopView.Value ? Loc.T("Ein", "On") : Loc.T("Aus", "Off"),
                () => cfg.DesktopView.Value = !cfg.DesktopView.Value, () => cfg.DesktopView.Value = !cfg.DesktopView.Value, true);
            GUILayout.Space(14f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("In VR spielen", "Play in VR"), _bigButton, GUILayout.Height(58f))) StartCoroutine(Apply(true));
            GUILayout.Space(16f);
            if (GUILayout.Button(Loc.T("Am Monitor spielen", "Play on monitor"), _bigButton, GUILayout.Height(58f))) StartCoroutine(Apply(false));
            GUILayout.EndHorizontal();

            GUILayout.Space(14f);
            _dontShowAgain = GUILayout.Toggle(_dontShowAgain, Loc.T("  Dieses Fenster nicht mehr anzeigen (die Wahl wird gemerkt)", "  Do not show this window again (the choice is remembered)"), _toggle);
            GUILayout.Label(Loc.T("Wieder einschalten: in den Spiel-Einstellungen („VR-Startfenster zeigen“) oder im VR-Menü. Wechsel VR ↔ Monitor jederzeit mit F9 oder über „VR %s“ unten rechts.".Replace("%s", Plugin.Version), "To re-enable: game settings (“Show VR start window”) or the VR menu. Switch VR ↔ monitor any time with F9 or via “VR %s” at the bottom right.".Replace("%s", Plugin.Version)), _hint);
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- game-style window (uGUI)

        static readonly Color Cream = new Color32(238, 233, 221, 255);
        static readonly Color Brown = new Color32(92, 58, 46, 255);
        static readonly Color BrownSoft = new Color32(125, 98, 86, 255);
        static readonly Color Field = new Color32(174, 130, 107, 255);
        static readonly Color Teal = new Color32(62, 127, 124, 255);
        static readonly Color Yellow = new Color32(250, 196, 45, 255);
        static readonly Color Tan = new Color32(232, 207, 154, 255);

        class UiButton
        {
            public RectTransform Rt;
            public Image Bg;
            public Color Color;
            public Action Click;
            public Func<bool> Enabled;
        }

        Canvas _canvas;
        TMP_FontAsset _font;
        readonly List<UiButton> _uiButtons = new List<UiButton>();
        UiButton _pressed;
        TextMeshProUGUI _displayValue, _resolutionValue, _modeValue, _renderValue, _spectatorValue, _vrHint;
        Image _check;
        CanvasGroup _group;

        static Sprite _rounded;
        static Sprite Sprite(string icon, bool sliced = false)
        {
            var tex = Icons.Get(icon);
            if (tex == null) return null;
            var border = sliced ? new Vector4(40f, 40f, 40f, 40f) : Vector4.zero;
            return UnityEngine.Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Image Img(RectTransform rt, Sprite sprite, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        TextMeshProUGUI Txt(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions align, bool bold = false)
        {
            var rt = Rect("Text", parent, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>No line breaks: the text shrinks (down to minSize) to fit its box instead.</summary>
        static TextMeshProUGUI OneLine(TextMeshProUGUI t, float minSize)
        {
            t.enableWordWrapping = false;
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = minSize;
            return t;
        }

        UiButton Btn(Transform parent, Vector2 pos, Vector2 size, Color color, Action click, Func<bool> enabled = null)
        {
            var rt = Rect("Button", parent, pos, size);
            var b = new UiButton { Rt = rt, Bg = Img(rt, _rounded, color), Color = color, Click = click, Enabled = enabled };
            _uiButtons.Add(b);
            return b;
        }

        UiButton Arrow(Transform parent, Vector2 pos, bool right, Action click, Func<bool> enabled)
        {
            var b = Btn(parent, pos, new Vector2(64f, 64f), Teal, click, enabled);
            var icon = Rect("Arrow", b.Rt, Vector2.zero, new Vector2(40f, 40f));
            Img(icon, Sprite("ui_arrow"), Color.white);
            if (!right) icon.localScale = new Vector3(-1f, 1f, 1f);
            return b;
        }

        TextMeshProUGUI SettingRow(Transform panel, float y, string label, Action prev, Action next, Func<bool> enabled)
        {
            OneLine(Txt(panel, label, new Vector2(-340f, y), new Vector2(260f, 70f), 32f, Brown, TextAlignmentOptions.Left, true), 22f);
            Arrow(panel, new Vector2(-95f, y), false, prev, enabled);
            var field = Rect("Field", panel, new Vector2(155f, y), new Vector2(420f, 64f));
            Img(field, _rounded, Field);
            var value = Txt(field, "", Vector2.zero, new Vector2(400f, 64f), 30f, Color.white, TextAlignmentOptions.Center, true);
            value.enableWordWrapping = false;
            value.overflowMode = TextOverflowModes.Ellipsis;
            Arrow(panel, new Vector2(405f, y), true, next, enabled);
            return value;
        }

        void BuildUi()
        {
            _font = VRSettingsPanel.FindFont();
            _rounded = Sprite("ui_round_rect", true);

            var go = new GameObject("CozyVR Desktop Start Window", typeof(RectTransform)) { layer = 5 };
            DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;   // the whole window always fits
            _group = go.AddComponent<CanvasGroup>();

            // dim the game behind
            var dim = Rect("Dim", go.transform, Vector2.zero, Vector2.zero);
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.sizeDelta = Vector2.zero;
            Img(dim, null, new Color(0.12f, 0.08f, 0.06f, 0.72f));

            const float W = 1040f, H = 1330f;
            // the window is taller than the 1080 reference height: scale it to fit with a margin
            var win = Rect("Window", go.transform, Vector2.zero, new Vector2(W, H));
            win.localScale = Vector3.one * Mathf.Min(1f, 1020f / H);
            var shadow = Rect("Shadow", win, new Vector2(10f, -14f), new Vector2(W, H));
            Img(shadow, _rounded, new Color(0.1f, 0.05f, 0f, 0.35f));
            var panel = Rect("Panel", win, Vector2.zero, new Vector2(W, H));
            Img(panel, _rounded, Cream);

            // header band with the title in the game's look: white letters on a brown shadow
            var header = Rect("Header", panel, new Vector2(0f, H * 0.5f - 66f), new Vector2(W, 132f));
            Img(header, Sprite("ui_header"), Color.white);
            Txt(header, "Cozy Marbles VR", new Vector2(4f, -5f), new Vector2(W - 80f, 120f), 72f, new Color32(150, 80, 30, 255), TextAlignmentOptions.Left, true).margin = new Vector4(40f, 0f, 0f, 0f);
            Txt(header, "Cozy Marbles VR", Vector2.zero, new Vector2(W - 80f, 120f), 72f, Color.white, TextAlignmentOptions.Left, true).margin = new Vector4(40f, 0f, 0f, 0f);

            float y = H * 0.5f - 132f - 60f;
            Txt(panel, Loc.T("Wie möchtest du spielen?", "How do you want to play?"), new Vector2(0f, y), new Vector2(W - 100f, 60f), 40f, Brown, TextAlignmentOptions.Left, true);

            // the two big choices
            y -= 135f;
            var vr = Btn(panel, new Vector2(-240f, y), new Vector2(440f, 170f), Yellow, () => StartCoroutine(Apply(true)));
            Img(Rect("Icon", vr.Rt, new Vector2(0f, 30f), new Vector2(96f, 96f)), Sprite("ui_headset"), Brown);
            OneLine(Txt(vr.Rt, Loc.T("In VR spielen", "Play in VR"), new Vector2(0f, -48f), new Vector2(400f, 60f), 40f, Brown, TextAlignmentOptions.Center, true), 26f);
            var mon = Btn(panel, new Vector2(240f, y), new Vector2(440f, 170f), Teal, () => StartCoroutine(Apply(false)));
            Img(Rect("Icon", mon.Rt, new Vector2(0f, 30f), new Vector2(96f, 96f)), Sprite("ui_monitor"), Color.white);
            OneLine(Txt(mon.Rt, Loc.T("Am Monitor spielen", "Play on monitor"), new Vector2(0f, -48f), new Vector2(400f, 60f), 40f, Color.white, TextAlignmentOptions.Center, true), 26f);

            // the window on the PC
            y -= 135f;
            Txt(panel, Loc.T("Fenster am PC", "Window on the PC"), new Vector2(0f, y), new Vector2(W - 100f, 56f), 36f, Brown, TextAlignmentOptions.Left, true);
            y -= 70f;
            _displayValue = SettingRow(panel, y, Loc.T("Bildschirm", "Display"), () => ChangeDisplay(-1), () => ChangeDisplay(1), () => _displays.Count > 1);
            y -= 84f;
            _resolutionValue = SettingRow(panel, y, Loc.T("Auflösung", "Resolution"),
                () => _resolution = (_resolution + _resolutions.Count - 1) % _resolutions.Count,
                () => _resolution = (_resolution + 1) % _resolutions.Count, () => _resolutions.Count > 1);
            y -= 84f;
            _modeValue = SettingRow(panel, y, Loc.T("Anzeige", "Mode"), () => _mode = (_mode + 2) % 3, () => _mode = (_mode + 1) % 3, null);
            y -= 62f;
            Txt(panel, Loc.T("Gilt nur für das Fenster am PC, nicht für die Brille.", "Only for the window on the PC, not for the headset."),
                new Vector2(0f, y), new Vector2(W - 100f, 44f), 24f, BrownSoft, TextAlignmentOptions.Left);

            // the headset
            y -= 70f;
            Txt(panel, Loc.T("VR-Brille", "VR headset"), new Vector2(0f, y), new Vector2(W - 100f, 56f), 36f, Brown, TextAlignmentOptions.Left, true);
            y -= 70f;
            var cfg = Plugin.Cfg;
            _renderValue = SettingRow(panel, y, Loc.T("Auflösung pro Auge", "Resolution per eye"),
                () => cfg.EyeResolution.Value = VRManager.StepPreset(cfg.EyeResolution.Value, -1),
                () => cfg.EyeResolution.Value = VRManager.StepPreset(cfg.EyeResolution.Value, 1), null);
            y -= 84f;
            _spectatorValue = SettingRow(panel, y, Loc.T("Zuschaueransicht", "Spectator view"),
                () => cfg.DesktopView.Value = !cfg.DesktopView.Value, () => cfg.DesktopView.Value = !cfg.DesktopView.Value, null);
            y -= 66f;
            _vrHint = Txt(panel, "", new Vector2(0f, y), new Vector2(W - 100f, 56f), 22f, BrownSoft, TextAlignmentOptions.Left);

            // "do not show again"
            y -= 86f;
            var box = Btn(panel, new Vector2(-W * 0.5f + 50f + 28f, y), new Vector2(56f, 56f), Tan, () => _dontShowAgain = !_dontShowAgain);
            _check = Img(Rect("Check", box.Rt, Vector2.zero, new Vector2(40f, 40f)), Sprite("ui_check"), Brown);
            OneLine(Txt(panel, Loc.T("Dieses Fenster nicht mehr anzeigen (die Wahl wird gemerkt)", "Do not show this window again (the choice is remembered)"),
                new Vector2(45f, y), new Vector2(W - 190f, 56f), 28f, Brown, TextAlignmentOptions.Left, true), 20f);
            y -= 84f;
            Txt(panel, Loc.T("Wieder einschalten: in den Spiel-Einstellungen („VR-Startfenster zeigen“) oder in den VR-Einstellungen. Wechsel VR ↔ Monitor jederzeit mit F9 oder über „VR %s“ unten rechts.".Replace("%s", Plugin.Version),
                    "To re-enable: game settings (“Show VR start window”) or the VR settings. Switch VR ↔ monitor any time with F9 or via “VR %s” at the bottom right.".Replace("%s", Plugin.Version)),
                new Vector2(0f, y), new Vector2(W - 100f, 90f), 22f, BrownSoft, TextAlignmentOptions.Left);
            Plugin.Log.LogInfo("Start window shown (font " + (_font != null ? _font.name : "-") + ").");
        }

        void TickUi()
        {
            // values
            _displayValue.text = _displays.Count > 0 ? $"{_displays[_display].name}  ({_displays[_display].width}×{_displays[_display].height})" : "-";
            _resolutionValue.text = $"{_resolutions[_resolution].x} × {_resolutions[_resolution].y}";
            _modeValue.text = ModeNames[_mode];
            var cfg = Plugin.Cfg;
            _renderValue.text = VRManager.PresetText(cfg.EyeResolution.Value);
            _spectatorValue.text = cfg.DesktopView.Value ? Loc.T("Ein", "On") : Loc.T("Aus (spart Leistung)", "Off (saves power)");
            var sv = VRManager.SteamVREye;
            bool high = cfg.EyeResolution.Value <= 0 && sv.x > VRManager.FramePanel * 1.5f;
            _vrHint.text = high
                ? Loc.T($"Achtung: SteamVR gibt {sv.x} × {sv.y} pro Auge vor – viel mehr, als die Steam Frame zeigt (2160 × 2160). Wähle oben eine Auflösung.",
                        $"Careful: SteamVR asks for {sv.x} × {sv.y} per eye – far more than the Steam Frame shows (2160 × 2160). Pick a resolution above.")
                : Loc.T("2160 × 2160 = Display der Steam Frame (mehr wird nicht angezeigt), darunter fünf leichtere Stufen. „SteamVR“ = Vorgabe aus SteamVR.",
                        "2160 × 2160 = the Steam Frame's display (more is never shown), five lighter steps below. “SteamVR” = SteamVR's default.");
            _vrHint.color = high ? new Color32(190, 60, 40, 255) : BrownSoft;
            _check.enabled = _dontShowAgain;
            _group.alpha = _busy ? 0.85f : 1f;

            // hover and clicks (the game's input modules are paused while this window is open)
            Vector2 mouse = Input.mousePosition;
            UiButton hover = null;
            foreach (var b in _uiButtons)
            {
                bool on = !_busy && (b.Enabled == null || b.Enabled());
                if (on && hover == null && RectTransformUtility.RectangleContainsScreenPoint(b.Rt, mouse, null)) hover = b;
                Color c = b.Color;
                if (!on) c = Color.Lerp(c, Cream, 0.6f);
                else if (b == hover) c = Color.Lerp(c, Color.white, b == _pressed ? 0.05f : 0.22f);
                b.Bg.color = c;
                float s = on && b == hover ? (b == _pressed ? 0.97f : 1.03f) : 1f;
                b.Rt.localScale = new Vector3(s, s, 1f);
            }
            if (Input.GetMouseButtonDown(0)) _pressed = hover;
            if (Input.GetMouseButtonUp(0))
            {
                var clicked = _pressed != null && _pressed == hover ? _pressed : null;
                _pressed = null;
                if (clicked != null)
                {
                    try { clicked.Click(); }
                    catch (Exception e) { Plugin.Log.LogWarning("Start window: " + e.Message); }
                }
            }
        }

        /// <summary>Diagnostics: confirm the window without a mouse.</summary>
        public void Choose(bool vr) { if (!_busy) StartCoroutine(Apply(vr)); }

        void Row(string label, string value, System.Action prev, System.Action next, bool enabled)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(40f));
            GUILayout.Label(label, _label, GUILayout.Width(130f), GUILayout.Height(40f));
            bool was = GUI.enabled;
            GUI.enabled = was && enabled;
            if (GUILayout.Button("◀", _button, GUILayout.Width(44f), GUILayout.Height(36f))) prev();
            GUILayout.Label(value, new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter }, GUILayout.Height(40f), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", _button, GUILayout.Width(44f), GUILayout.Height(36f))) next();
            GUI.enabled = was;
            GUILayout.EndHorizontal();
        }

        void ChangeDisplay(int step)
        {
            if (_displays.Count < 2) return;
            int target = (_display + step + _displays.Count) % _displays.Count;
            StartCoroutine(MoveTo(_displays[target]));
        }

        IEnumerator MoveTo(DisplayInfo display)
        {
            _busy = true;
            var op = Screen.MoveMainWindowTo(display, Vector2Int.zero);
            while (op != null && !op.isDone) yield return null;
            yield return null;
            Refresh();
            _busy = false;
        }

        IEnumerator Apply(bool vr)
        {
            _busy = true;
            Vector2Int res = _resolutions[_resolution];
            FullScreenMode mode = Modes[_mode];
            // the game re-applies its own saved settings, so store them where it looks
            PlayerPrefs.SetInt("resX", res.x);
            PlayerPrefs.SetInt("resY", res.y);
            PlayerPrefs.SetInt("fullscreen", mode == FullScreenMode.Windowed ? 0 : 1);
            PlayerPrefs.Save();
            Screen.SetResolution(res.x, res.y, mode);
            yield return null;

            var cfg = Plugin.Cfg;
            cfg.StartIn.Value = vr ? StartMode.VR : StartMode.Monitor;
            cfg.ShowStartDialog.Value = !_dontShowAgain;
            Plugin.Log.LogInfo($"Start window: {(vr ? "VR" : "monitor")}, {res.x}x{res.y} {mode}, show again: {!_dontShowAgain}");
            VRManager.Instance?.StartChosen(vr);
            Destroy(this);
        }
    }
}
