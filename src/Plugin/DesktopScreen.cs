using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace CozyMarblesVR
{
    /// <summary>
    /// What the desktop window shows while the game runs in the headset:
    ///  - spectator view on: the spectator camera, optionally rendered at a lower resolution than the window
    ///    (DesktopViewScale) and stretched to fill it - the headset image is not affected;
    ///  - spectator view off: no third picture of the world at all, just a cheap screen in the game's dialog style
    ///    with a few buttons (spectator view on, play on the monitor, quit) and the keyboard shortcuts.
    /// The buttons are hit-tested with the real desktop mouse (the game's UI is driven by the VR pointer meanwhile).
    /// </summary>
    public class DesktopScreen
    {
        Camera _blank;              // clears the window when the spectator camera does not draw into it
        RenderTexture _rt;
        GameObject _canvasGo;
        RawImage _picture;
        GameObject _offPanel;
        readonly List<Btn> _buttons = new List<Btn>();
        Btn _pressed;
        TextMeshProUGUI _quitLabel;
        float _quitArmedUntil;

        class Btn { public RectTransform Rt; public Image Bg; public Color Color; public Action Click; }

        static readonly Color Cream = new Color32(238, 233, 221, 255);
        static readonly Color Brown = new Color32(92, 58, 46, 255);
        static readonly Color BrownSoft = new Color32(125, 98, 86, 255);
        static readonly Color Teal = new Color32(62, 127, 124, 255);
        static readonly Color Yellow = new Color32(250, 196, 45, 255);
        static readonly Color Red = new Color32(200, 70, 60, 255);

        public static float Scale => Mathf.Clamp(Plugin.Cfg.DesktopViewScale.Value, 0.25f, 1f);

        /// <param name="desk">the spectator camera (null when there is none)</param>
        /// <param name="active">VR is running</param>
        public void Tick(Camera desk, bool active)
        {
            bool on = active && Plugin.Cfg.DesktopView.Value && desk != null;
            bool off = active && !on;
            // URP's render scale also carries the headset's render resolution (VRManager.ApplyRenderScale) and would
            // make the spectator view just as expensive: the texture shrinks by the same factor
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            float scale = Mathf.Min(1f, Scale / Mathf.Max(0.1f, urp != null ? urp.renderScale : 1f));
            bool scaled = on && scale < 0.999f;

            if (desk != null && desk.enabled != on) desk.enabled = on;
            EnsureUi(active);
            if (_canvasGo != null && _canvasGo.activeSelf != active) _canvasGo.SetActive(active);

            // reduced resolution: the spectator camera renders into a smaller texture shown full-window
            if (scaled)
            {
                int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * scale)), h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * scale));
                if (_rt == null || _rt.width != w || _rt.height != h)
                {
                    if (desk.targetTexture == _rt) desk.targetTexture = null;
                    if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
                    // no alpha channel: post-processing leaves alpha below 1, which would blend the picture with the
                    // dark background when it is shown on the window
                    var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGB32;
                    _rt = new RenderTexture(w, h, 24, fmt, RenderTextureReadWrite.Linear) { name = "CozyVR Spectator", filterMode = FilterMode.Bilinear };
                    _rt.Create();
                    Plugin.Log.LogInfo($"Spectator view: {w}x{h} texture ({Scale * 100f:0}% of the window after render scale)");
                }
                if (desk.targetTexture != _rt) desk.targetTexture = _rt;
            }
            else
            {
                if (desk != null && desk.targetTexture != null) desk.targetTexture = null;
                if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            }

            if (_picture != null)
            {
                bool show = scaled && _rt != null;
                if (_picture.gameObject.activeSelf != show) _picture.gameObject.SetActive(show);
                if (show && _picture.texture != _rt) _picture.texture = _rt;
            }
            if (_offPanel != null && _offPanel.activeSelf != off) _offPanel.SetActive(off);
            EnsureBlank(active && (off || scaled));
            if (off) TickButtons();
        }

        /// <summary>A camera that only clears the window (draws nothing) - next to free.</summary>
        void EnsureBlank(bool want)
        {
            if (want && _blank == null)
            {
                var go = new GameObject("CozyVR Desktop Clear");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _blank = go.AddComponent<Camera>();
                _blank.cullingMask = 0;
                _blank.clearFlags = CameraClearFlags.SolidColor;
                _blank.backgroundColor = new Color32(36, 30, 27, 255);
                _blank.depth = 9f;
                _blank.stereoTargetEye = StereoTargetEyeMask.None;
                _blank.allowHDR = false;
                _blank.allowMSAA = false;
                var data = _blank.GetUniversalAdditionalCameraData();
                data.allowXRRendering = false;
                data.renderPostProcessing = false;
                data.renderShadows = false;
            }
            if (_blank != null && _blank.enabled != want) _blank.enabled = want;
        }

        // ---------------------------------------------------------------- UI

        void EnsureUi(bool active)
        {
            if (_canvasGo != null || !active) return;
            var font = VRSettingsPanel.FindFont();
            if (font == null) return;

            _canvasGo = new GameObject("CozyVR Desktop Screen", typeof(RectTransform)) { layer = 5 };
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4990;   // below the info banner
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            // the spectator picture at reduced resolution, stretched over the window
            var pic = Rect("Spectator", _canvasGo.transform, Vector2.zero, Vector2.zero);
            pic.anchorMin = Vector2.zero; pic.anchorMax = Vector2.one;
            _picture = pic.gameObject.AddComponent<RawImage>();
            _picture.raycastTarget = false;
            pic.gameObject.SetActive(false);

            // the screen while the spectator view is off
            var rounded = VRSettingsPanel.UiSprite("ui_round_rect", true);
            _offPanel = new GameObject("Off", typeof(RectTransform)) { layer = 5 };
            var offRt = (RectTransform)_offPanel.transform;
            offRt.SetParent(_canvasGo.transform, false);
            offRt.anchorMin = Vector2.zero; offRt.anchorMax = Vector2.one; offRt.sizeDelta = Vector2.zero;
            const float W = 900f, H = 620f;
            var panel = Rect("Panel", offRt, Vector2.zero, new Vector2(W, H));
            Img(panel, rounded, Cream, 1.2f);
            var header = Rect("Header", panel, new Vector2(0f, H * 0.5f - 60f), new Vector2(W, 120f));
            Img(header, VRSettingsPanel.UiSprite("ui_header", false), Color.white, 1f);
            Txt(header, "Cozy Marbles VR", new Vector2(3f, -4f), new Vector2(W - 80f, 110f), 64f, new Color32(150, 80, 30, 255), TextAlignmentOptions.Left, font, true);
            Txt(header, "Cozy Marbles VR", Vector2.zero, new Vector2(W - 80f, 110f), 64f, Color.white, TextAlignmentOptions.Left, font, true);

            float y = H * 0.5f - 160f;
            Txt(panel, Loc.T("Du spielst im Headset. Die Zuschaueransicht ist aus und spart Grafikleistung.", "You are playing in the headset. The spectator view is off and saves graphics power."),
                new Vector2(0f, y), new Vector2(W - 100f, 70f), 30f, Brown, TextAlignmentOptions.Left, font, true);
            y -= 100f;
            Button(panel, new Vector2(0f, y), Teal, Color.white, Loc.T("Zuschaueransicht einschalten  (F11)", "Show the spectator view  (F11)"), font,
                () => Plugin.Cfg.DesktopView.Value = true);
            y -= 90f;
            Button(panel, new Vector2(0f, y), Yellow, Brown, Loc.T("Am Monitor spielen  (F9)", "Play on the monitor  (F9)"), font,
                () => VRManager.Instance?.SetFlat(true));
            y -= 90f;
            _quitLabel = Button(panel, new Vector2(0f, y), Red, Color.white, "", font, Quit);
            y -= 80f;
            Txt(panel, Loc.T("F9: Monitor ↔ VR   ·   F10: Hinweis-Leiste   ·   F11: Zuschaueransicht", "F9: monitor ↔ VR   ·   F10: info banner   ·   F11: spectator view"),
                new Vector2(0f, y), new Vector2(W - 100f, 50f), 24f, BrownSoft, TextAlignmentOptions.Center, font);
            _offPanel.SetActive(false);
        }

        void Quit()
        {
            // two clicks: the first one asks
            if (Time.unscaledTime < _quitArmedUntil) { Plugin.Log.LogInfo("Quit from the desktop screen."); Application.Quit(); return; }
            _quitArmedUntil = Time.unscaledTime + 3f;
        }

        void TickButtons()
        {
            if (_quitLabel != null)
                _quitLabel.text = Time.unscaledTime < _quitArmedUntil ? Loc.T("Wirklich beenden? Nochmal klicken", "Really quit? Click again") : Loc.T("Spiel beenden", "Quit the game");
            Vector2 mouse = Input.mousePosition;
            Btn hover = null;
            foreach (var b in _buttons)
            {
                bool over = RectTransformUtility.RectangleContainsScreenPoint(b.Rt, mouse, null);
                if (over && hover == null) hover = b;
                b.Bg.color = b == hover ? Color.Lerp(b.Color, Color.white, 0.22f) : b.Color;
                b.Rt.localScale = b == hover ? new Vector3(1.02f, 1.02f, 1f) : Vector3.one;
            }
            if (Input.GetMouseButtonDown(0)) _pressed = hover;
            if (Input.GetMouseButtonUp(0))
            {
                var click = _pressed != null && _pressed == hover ? _pressed : null;
                _pressed = null;
                if (click != null)
                {
                    try { click.Click(); }
                    catch (Exception e) { Plugin.Log.LogWarning("Desktop screen: " + e.Message); }
                }
            }
        }

        static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image Img(RectTransform rt, Sprite sprite, Color color, float ppuMul)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = ppuMul;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI Txt(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions align, TMP_FontAsset font, bool bold = false)
        {
            var t = Rect("Text", parent, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false;
            return t;
        }

        TextMeshProUGUI Button(RectTransform panel, Vector2 pos, Color color, Color textColor, string label, TMP_FontAsset font, Action click)
        {
            var rt = Rect("Button", panel, pos, new Vector2(700f, 72f));
            var b = new Btn { Rt = rt, Bg = Img(rt, VRSettingsPanel.UiSprite("ui_round_rect", true), color, 1.6f), Color = color, Click = click };
            _buttons.Add(b);
            var t = Txt(rt, label, Vector2.zero, new Vector2(680f, 72f), 32f, textColor, TextAlignmentOptions.Center, font, true);
            t.enableWordWrapping = false;
            return t;
        }
    }
}
