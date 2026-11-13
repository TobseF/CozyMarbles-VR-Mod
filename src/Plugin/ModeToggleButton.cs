using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CozyMarblesVR
{
    /// <summary>
    /// Small button next to the game's version number (bottom right): shows whether the game runs in VR or on the
    /// monitor and switches to the other mode. It is an ordinary overlay canvas, so on the monitor it is clicked with
    /// the mouse and in VR it appears on the menu panel and is clicked with the laser.
    /// </summary>
    public class ModeToggleButton
    {
        GameObject _go;
        TextMeshProUGUI _label, _dialogLabel;
        Image _bg;
        string _lastText;

        public void Tick(VRManager manager)
        {
            if (_go == null && !Build()) return;
            bool show = !manager.WaitingForStart;
            if (_go.activeSelf != show) _go.SetActive(show);
            if (!show) return;
            _dialogLabel.text = Plugin.Cfg.ShowStartDialog.Value ? Loc.T("Startfenster: an", "Start window: on") : Loc.T("Startfenster: aus", "Start window: off");
            string text;
            Color color;
            switch (manager.Mode)
            {
                case VRManager.ModeState.VR:
                    text = Loc.T("VR-Modus  ·  zum Monitor wechseln", "VR mode  ·  switch to monitor");
                    color = new Color(0.3f, 0.55f, 0.95f, 0.95f);
                    break;
                case VRManager.ModeState.VRNotShown:
                    text = Loc.T("VR-Modus (wartet auf Anzeige im Headset)  ·  zum Monitor wechseln", "VR mode (waiting for the headset view)  ·  switch to monitor");
                    color = new Color(0.3f, 0.55f, 0.95f, 0.95f);
                    break;
                case VRManager.ModeState.Connecting:
                    text = Loc.T("Warte auf VR-Headset …  ·  abbrechen", "Waiting for VR headset …  ·  cancel");
                    color = new Color(0.55f, 0.45f, 0.2f, 0.95f);
                    break;
                case VRManager.ModeState.NotShown:
                    text = Loc.T("Monitor-Modus (SteamVR zeigt das Spiel nicht)  ·  VR erneut versuchen", "Monitor mode (SteamVR does not show the game)  ·  retry VR");
                    color = new Color(0.35f, 0.3f, 0.42f, 0.95f);
                    break;
                default:
                    text = Loc.T("Monitor-Modus  ·  zu VR wechseln", "Monitor mode  ·  switch to VR");
                    color = new Color(0.35f, 0.3f, 0.42f, 0.95f);
                    break;
            }
            if (text != _lastText)
            {
                _lastText = text;
                _label.text = text;
                _bg.color = color;
            }
            UpdateDesktopHint(manager.Mode);
        }

        // ------------------------------------------------------------ desktop-only hint while in VR
        // The button above moves onto the VR panel in VR mode; this line stays on the PC window (spectator view).

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
                Object.DontDestroyOnLoad(_hintGo);
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

        bool Build()
        {
            var font = VRSettingsPanel.FindFont();
            if (font == null) return false;

            _go = new GameObject("CozyVR Mode Toggle", typeof(RectTransform)) { layer = 5 }; // UI layer, picked up by the VR panel
            Object.DontDestroyOnLoad(_go);
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5001; // just above the game's version number canvas
            var scaler = _go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            _go.AddComponent<GraphicRaycaster>();

            var btnGo = new GameObject("Button", typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)btnGo.transform;
            rt.SetParent(_go.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-110f, 10f);   // left of the version number
            rt.sizeDelta = new Vector2(560f, 34f);
            _bg = btnGo.AddComponent<Image>();
            var button = btnGo.AddComponent<Button>();
            button.targetGraphic = _bg;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => VRManager.Instance?.ToggleMode());

            _label = Label(rt, font, TextAlignmentOptions.Right);

            // second button: show the start window again at the next game start
            var dGo = new GameObject("Start Window Button", typeof(RectTransform)) { layer = 5 };
            var drt = (RectTransform)dGo.transform;
            drt.SetParent(_go.transform, false);
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1f, 0f);
            drt.anchoredPosition = new Vector2(-110f - 560f - 10f, 10f);
            drt.sizeDelta = new Vector2(200f, 34f);
            var dImg = dGo.AddComponent<Image>();
            dImg.color = new Color(0.35f, 0.3f, 0.42f, 0.95f);
            var dButton = dGo.AddComponent<Button>();
            dButton.targetGraphic = dImg;
            dButton.colors = colors;
            dButton.onClick.AddListener(() => Plugin.Cfg.ShowStartDialog.Value = !Plugin.Cfg.ShowStartDialog.Value);
            _dialogLabel = Label(drt, font, TextAlignmentOptions.Center);
            return true;
        }

        static TextMeshProUGUI Label(RectTransform parent, TMP_FontAsset font, TextAlignmentOptions align)
        {
            var textGo = new GameObject("Label", typeof(RectTransform)) { layer = 5 };
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(parent, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 0f);
            trt.offsetMax = new Vector2(-10f, 0f);
            var label = textGo.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = 20f;
            label.alignment = align;
            label.color = Color.white;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            return label;
        }
    }
}
