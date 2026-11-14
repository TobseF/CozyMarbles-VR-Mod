using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CozyMarblesVR
{
    /// <summary>
    /// Start window on the desktop: VR or monitor, which screen, resolution and window mode.
    /// Drawn with IMGUI so it works before the game's own UI is ready. Can be switched off with a checkbox
    /// and switched on again in the game (button next to the version number, VR settings).
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

        void Awake() => Instance = this;
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
            // clicks on the window must not reach the game's main menu underneath (the EventSystem itself
            // must stay on, the game's code relies on EventSystem.current)
            var es = EventSystem.current;
            if (es == null) return;
            foreach (var m in es.GetComponents<BaseInputModule>())
                if (m.enabled) { m.enabled = false; _blockedModules.Add(m); }
        }

        void OnDestroy()
        {
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
            Styles();
            const float W = 620f, H = 470f;
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

            GUILayout.Space(8f);
            GUILayout.Label(Loc.T("Gilt für das Fenster am PC. Im VR-Modus bestimmt die Brille die Auflösung im Headset.", "Applies to the window on the PC. In VR mode the headset decides the resolution in the headset."), _hint);
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
