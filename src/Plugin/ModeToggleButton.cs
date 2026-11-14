using System;
using System.Reflection;
using HarmonyLib;
using TimR.Events;
using TimR.MainMenu;
using TimR.Services;
using TimR.Settings;
using TimR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CozyMarblesVR
{
    /// <summary>
    /// Everything the mod adds to the game's own menus for switching between VR and the monitor:
    ///  - a small "VR 0.1.0" label below the game's version number (bottom right): hover = tooltip, click = confirmation,
    ///  - an entry in the main menu ("Play in VR" / "Play on monitor"),
    ///  - a "show VR start window" switch in the game's settings.
    /// All of it is ordinary game UI (overlay canvas / clones of the game's own buttons), so on the monitor it is
    /// clicked with the mouse and in VR it appears on the menu panel and is clicked with the laser.
    /// </summary>
    public class ModeToggleButton
    {
        // ------------------------------------------------------------ texts

        static string Tooltip(VRManager.ModeState m)
        {
            switch (m)
            {
                case VRManager.ModeState.VR:
                case VRManager.ModeState.VRNotShown: return Loc.T("In den Monitor-Modus wechseln", "Change to monitor mode");
                case VRManager.ModeState.Connecting: return Loc.T("Nicht mehr auf das VR-Headset warten", "Stop waiting for the VR headset");
                case VRManager.ModeState.NotShown: return Loc.T("VR-Modus erneut versuchen", "Retry VR mode");
                default: return Loc.T("In den VR-Modus wechseln", "Change to VR mode");
            }
        }

        static string Question(VRManager.ModeState m)
        {
            switch (m)
            {
                case VRManager.ModeState.VR:
                case VRManager.ModeState.VRNotShown: return Loc.T("Zum Monitor-Modus wechseln?", "Switch to monitor mode?");
                case VRManager.ModeState.Connecting: return Loc.T("Nicht mehr auf das VR-Headset warten und am Monitor spielen?", "Stop waiting for the VR headset and play on the monitor?");
                case VRManager.ModeState.NotShown: return Loc.T("VR-Modus erneut versuchen? (SteamVR zeigt das Spiel derzeit nicht an.)", "Retry VR mode? (SteamVR is not showing the game right now.)");
                default: return Loc.T("In den VR-Modus wechseln?", "Switch to VR mode?");
            }
        }

        static string MenuText(VRManager.ModeState m)
        {
            switch (m)
            {
                case VRManager.ModeState.VR:
                case VRManager.ModeState.VRNotShown: return Loc.T("Am Monitor spielen", "Play on monitor");
                case VRManager.ModeState.Connecting: return Loc.T("VR-Suche abbrechen", "Cancel VR search");
                case VRManager.ModeState.NotShown: return Loc.T("VR erneut versuchen", "Retry VR");
                default: return Loc.T("In VR spielen", "Play in VR");
            }
        }

        static Color LabelColor(VRManager.ModeState m)
        {
            switch (m)
            {
                case VRManager.ModeState.VR: return new Color(0.55f, 0.78f, 1f, 1f);
                case VRManager.ModeState.VRNotShown: return new Color(0.55f, 0.78f, 1f, 0.8f);
                case VRManager.ModeState.Connecting: return new Color(1f, 0.82f, 0.4f, 0.95f);
                default: return new Color(1f, 1f, 1f, 0.7f);
            }
        }

        // ------------------------------------------------------------ tick

        public void Tick(VRManager manager)
        {
            bool show = !manager.WaitingForStart;
            UpdateLabel(manager, show);
            if (show)
            {
                UpdateMenuEntry(manager);
                UpdateSettingsRow();
            }
            UpdateDesktopHint(show ? manager.Mode : VRManager.ModeState.Monitor);
        }

        static void Publish(Evt evt, object data)
        {
            try { EventBus._?.PublishEvent(evt, data); }
            catch (Exception e) { Plugin.Log.LogWarning("Publishing " + evt + " failed: " + e.Message); }
        }

        /// <summary>The click on the label: asks, then switches.</summary>
        static void AskSwitch()
        {
            var vm = VRManager.Instance;
            if (vm == null) return;
            Publish(Evt.OpenConfirmUi, new EventData.OpenConfirmUi(ok => { if (ok) VRManager.Instance?.ToggleMode(); }, Question(vm.Mode), "General_No"));
        }

        // ------------------------------------------------------------ "VR 0.1.0" below the version number

        GameObject _go;
        RectTransform _canvasRt, _labelRt;
        TextMeshProUGUI _label;
        bool _hover;
        readonly Guid _tipGuid = Guid.NewGuid();
        string _lastTip;

        TextMeshProUGUI _gameVersion;
        Vector2 _gameBase, _gameSet;
        bool _haveBase;
        static readonly FieldInfo VersionTextField = AccessTools.Field(typeof(VersionNumber), "_versionNumberText");

        class LabelEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public Action Enter, Exit, Click;
            public void OnPointerEnter(PointerEventData e) { Enter?.Invoke(); }
            public void OnPointerExit(PointerEventData e) { Exit?.Invoke(); }
            public void OnPointerClick(PointerEventData e) { Click?.Invoke(); }
            void OnDisable() { Exit?.Invoke(); }
        }

        TextMeshProUGUI FindGameVersion()
        {
            if (_gameVersion != null) return _gameVersion;
            _haveBase = false;
            var vn = UnityEngine.Object.FindFirstObjectByType<VersionNumber>(FindObjectsInactive.Include);
            if (vn == null || VersionTextField == null) return null;
            _gameVersion = VersionTextField.GetValue(vn) as TextMeshProUGUI;
            return _gameVersion;
        }

        bool BuildLabel()
        {
            var font = VRSettingsPanel.FindFont();
            if (font == null) return false;

            _go = new GameObject("CozyVR Version Label", typeof(RectTransform)) { layer = 5 }; // UI layer, picked up by the VR panel
            UnityEngine.Object.DontDestroyOnLoad(_go);
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5001; // just above the game's version number canvas
            _canvasRt = (RectTransform)_go.transform;
            var scaler = _go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            _go.AddComponent<GraphicRaycaster>();

            var btnGo = new GameObject("Label", typeof(RectTransform)) { layer = 5 };
            _labelRt = (RectTransform)btnGo.transform;
            _labelRt.SetParent(_go.transform, false);
            _labelRt.anchorMin = _labelRt.anchorMax = _labelRt.pivot = new Vector2(1f, 0f);
            _labelRt.anchoredPosition = new Vector2(0f, 4f);
            _labelRt.sizeDelta = new Vector2(80f, 20f);
            var hit = btnGo.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.02f); // practically invisible, but catches the pointer
            hit.canvasRenderer.cullTransparentMesh = false; // otherwise the raycaster skips it
            var ev = btnGo.AddComponent<LabelEvents>();
            ev.Enter = () => { _hover = true; ShowTip(true); };
            ev.Exit = () => { _hover = false; ShowTip(false); };
            ev.Click = () => { ShowTip(false); AskSwitch(); };

            var textGo = new GameObject("Text", typeof(RectTransform)) { layer = 5 };
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(_labelRt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            _label = textGo.AddComponent<TextMeshProUGUI>();
            _label.font = font;
            _label.fontSize = 16f;
            _label.alignment = TextAlignmentOptions.BottomRight;
            _label.color = Color.white;
            _label.raycastTarget = false;
            _label.enableWordWrapping = false;
            _label.text = "VR " + Plugin.Version;
            return true;
        }

        void ShowTip(bool on)
        {
            var vm = VRManager.Instance;
            if (on && vm != null) { _lastTip = Tooltip(vm.Mode); Publish(Evt.ShowTooltip, new EventData.ShowTooltip(_tipGuid, _lastTip)); }
            else Publish(Evt.HideTooltip, new EventData.HideTooltip(_tipGuid));
        }

        void UpdateLabel(VRManager manager, bool show)
        {
            if (_go == null && !BuildLabel()) return;
            var gt = FindGameVersion();
            bool visible = show && (gt == null || gt.gameObject.activeInHierarchy);
            if (_go.activeSelf != visible) _go.SetActive(visible);
            if (!visible) { RestoreGameVersion(); return; }

            var mode = manager.Mode;
            _label.color = _hover ? new Color(1f, 0.82f, 0.35f, 1f) : LabelColor(mode);
            if (_hover && Tooltip(mode) != _lastTip) ShowTip(true); // mode changed while hovering

            // The label sits right below the game's version number: the game's text moves up by one line and the
            // label takes its old place, same right edge and same text size. Both canvases span the screen, so their
            // heights give the scale between them. (The game's text has an inner margin; its glyphs sit inside it.)
            float mh = _canvasRt.rect.height;
            if (gt == null)
            {
                _label.fontSize = 16f;
                _labelRt.anchoredPosition = new Vector2(-10f, 6f);
                _labelRt.sizeDelta = new Vector2(_label.preferredWidth + 2f, _label.preferredHeight);
                return;
            }
            var grt = gt.rectTransform;
            var gcanvas = gt.canvas != null ? gt.canvas.rootCanvas : null;
            float gh = gcanvas != null ? ((RectTransform)gcanvas.transform).rect.height : 0f;
            if (gh < 1f || mh < 1f) return;
            float ratio = mh / gh; // my canvas units per game canvas unit

            _label.fontSize = Mathf.Max(8f, gt.fontSize * ratio);
            if (!_haveBase || (grt.anchoredPosition - _gameSet).sqrMagnitude > 0.0001f) { _gameBase = grt.anchoredPosition; _haveBase = true; }
            Vector2 size = grt.rect.size, pivot = grt.pivot;
            Vector4 margin = gt.margin;                          // left, top, right, bottom
            float xMax = _gameBase.x + (1f - pivot.x) * size.x - margin.z; // glyphs' right edge relative to the screen's right edge
            float yMin = _gameBase.y - pivot.y * size.y + margin.w;        // glyphs' bottom edge relative to the screen's bottom edge
            float h = _label.preferredHeight;
            _labelRt.anchoredPosition = new Vector2(xMax * ratio, yMin * ratio);
            _labelRt.sizeDelta = new Vector2(_label.preferredWidth + 2f, h);
            _gameSet = _gameBase + new Vector2(0f, h / ratio);
            grt.anchoredPosition = _gameSet;
        }

        void RestoreGameVersion()
        {
            if (_gameVersion != null && _haveBase) { _gameVersion.rectTransform.anchoredPosition = _gameBase; _gameSet = _gameBase; }
        }

        // ------------------------------------------------------------ entry in the game's main menu

        GameObject _menuEntry;
        TextMeshProUGUI _menuText;
        string _menuLast;
        float _nextMenuSearch;

        void UpdateMenuEntry(VRManager manager)
        {
            if (_menuEntry == null)
            {
                if (Time.unscaledTime < _nextMenuSearch) return;
                _nextMenuSearch = Time.unscaledTime + 1f;
                BuildMenuEntry();
                if (_menuEntry == null) return;
            }
            string text = MenuText(manager.Mode);
            if (text != _menuLast) { _menuLast = text; _menuText.text = text; }
        }

        void BuildMenuEntry()
        {
            var ui = UnityEngine.Object.FindFirstObjectByType<MainMenuUi>(FindObjectsInactive.Include);
            if (ui == null) return;
            var template = AccessTools.Field(typeof(MainMenuUi), "_creditsButton")?.GetValue(ui) as Button;
            if (template == null) return;

            // clone of the game's own menu button: same look, same hover animation
            var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            clone.name = "btn vr mode";
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            foreach (var l in clone.GetComponentsInChildren<LocaText>(true)) UnityEngine.Object.DestroyImmediate(l);
            var btn = clone.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => VRManager.Instance?.ToggleMode());

            _menuText = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            // no icon of the original: "VR" in its place
            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "img_icon") continue;
                t.gameObject.SetActive(false);
                var go = new GameObject("vr icon text", typeof(RectTransform)) { layer = t.gameObject.layer };
                var rt = (RectTransform)go.transform;
                rt.SetParent(t.parent, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var tx = go.AddComponent<TextMeshProUGUI>();
                tx.font = _menuText.font;
                tx.fontSize = _menuText.fontSize * 0.85f;
                tx.fontStyle = FontStyles.Bold;
                tx.alignment = TextAlignmentOptions.Center;
                tx.color = _menuText.color;
                tx.raycastTarget = false;
                tx.enableWordWrapping = false;
                tx.text = "VR";
                break;
            }
            _menuEntry = clone;
            _menuLast = null;
        }

        // ------------------------------------------------------------ "show VR start window" in the game's settings

        GameObject _settingsRow;
        Toggle _settingsToggle;
        TextMeshProUGUI _settingsText;
        string _settingsLast;
        float _nextSettingsSearch;

        void UpdateSettingsRow()
        {
            if (_settingsRow == null)
            {
                if (Time.unscaledTime < _nextSettingsSearch) return;
                _nextSettingsSearch = Time.unscaledTime + 1f;
                BuildSettingsRow();
                if (_settingsRow == null) return;
            }
            string text = Loc.T("VR-Startfenster zeigen", "Show VR start window");
            if (text != _settingsLast) { _settingsLast = text; _settingsText.text = text; }
            bool on = Plugin.Cfg.ShowStartDialog.Value;
            if (_settingsToggle.isOn != on) _settingsToggle.SetIsOnWithoutNotify(on);
        }

        void BuildSettingsRow()
        {
            var ui = UnityEngine.Object.FindFirstObjectByType<VideoSettingsUi>(FindObjectsInactive.Include);
            if (ui == null) return;
            var template = AccessTools.Field(typeof(VideoSettingsUi), "_vsyncToggle")?.GetValue(ui) as Toggle;
            if (template == null) return;
            var row = template.transform.parent;

            var clone = UnityEngine.Object.Instantiate(row.gameObject, row.parent);
            clone.name = "vr start window";
            clone.transform.SetAsLastSibling();
            foreach (var l in clone.GetComponentsInChildren<LocaText>(true)) UnityEngine.Object.DestroyImmediate(l);
            _settingsText = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            _settingsToggle = clone.GetComponentInChildren<Toggle>(true);
            _settingsToggle.onValueChanged.RemoveAllListeners();
            _settingsToggle.SetIsOnWithoutNotify(Plugin.Cfg.ShowStartDialog.Value);
            _settingsToggle.onValueChanged.AddListener(v => Plugin.Cfg.ShowStartDialog.Value = v);
            _settingsRow = clone;
            _settingsLast = null;
        }

        // ------------------------------------------------------------ desktop-only hint while in VR
        // The game UI above moves onto the VR panel in VR mode; this line stays on the PC window (spectator view).

        GameObject _hintGo;
        TextMeshProUGUI _hint;
        string _lastHint;

        void UpdateDesktopHint(VRManager.ModeState mode)
        {
            bool show = mode == VRManager.ModeState.VR || mode == VRManager.ModeState.VRNotShown;
            if (_hintGo == null)
            {
                if (!show) return;
                var font = VRSettingsPanel.FindFont();
                if (font == null) return;
                _hintGo = new GameObject("CozyVR Desktop Hint", typeof(RectTransform)) { layer = 5 };
                UnityEngine.Object.DontDestroyOnLoad(_hintGo);
                var canvas = _hintGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5002;
                var scaler = _hintGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0f;

                var bgGo = new GameObject("Background", typeof(RectTransform)) { layer = 5 };
                var rt = (RectTransform)bgGo.transform;
                rt.SetParent(_hintGo.transform, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 20f);
                rt.sizeDelta = new Vector2(1500f, 56f);
                var img = bgGo.AddComponent<Image>();
                img.color = new Color(0.1f, 0.08f, 0.14f, 0.8f);
                img.raycastTarget = false;

                var tGo = new GameObject("Text", typeof(RectTransform)) { layer = 5 };
                var trt = (RectTransform)tGo.transform;
                trt.SetParent(rt, false);
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = trt.offsetMax = Vector2.zero;
                _hint = tGo.AddComponent<TextMeshProUGUI>();
                _hint.font = font;
                _hint.fontSize = 30f;
                _hint.alignment = TextAlignmentOptions.Center;
                _hint.color = Color.white;
                _hint.raycastTarget = false;
                _hint.enableWordWrapping = false;
            }
            if (_hintGo.activeSelf != show) _hintGo.SetActive(show);
            if (!show) return;
            string text = mode == VRManager.ModeState.VR
                ? Loc.T("VR-Modus – Zuschaueransicht   ·   F9: am Monitor spielen", "VR mode – spectator view   ·   F9: play on monitor")
                : Loc.T("VR-Modus – warte, bis SteamVR das Spiel im Headset zeigt   ·   F9: am Monitor spielen", "VR mode – waiting for SteamVR to show the game in the headset   ·   F9: play on monitor");
            if (text != _lastHint) { _lastHint = text; _hint.text = text; }
        }
    }
}
