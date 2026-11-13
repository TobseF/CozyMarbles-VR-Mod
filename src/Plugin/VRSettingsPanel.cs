using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CozyMarblesVR
{
    /// <summary>
    /// The mod's own settings panel, opened with the "VR" button next to the grab bar. A world-space canvas in
    /// the play space; it has no GraphicRaycaster, so the game's EventSystem never sees it - the laser is
    /// hit-tested here directly.
    /// </summary>
    public class VRSettingsPanel
    {
        const float Px = 0.001f;           // canvas unit = 1 mm
        const float W = 640f;
        const float RowH = 52f;

        readonly Transform _rig;
        readonly HudPanel _hud;
        GameObject _go;
        RectTransform _canvasRt;
        bool _open;

        class Element
        {
            public RectTransform Rt;
            public Image Img;
            public Color Color;
            public Action Click;
            public Func<string> Text;
            public TextMeshProUGUI Label;
        }

        readonly List<Element> _buttons = new List<Element>();
        readonly List<Element> _values = new List<Element>();
        Element _hover;

        public bool Open => _open;

        public VRSettingsPanel(Transform rig, HudPanel hud)
        {
            _rig = rig;
            _hud = hud;
        }

        static TMP_FontAsset _font;

        /// <summary>The game's own UI font (TextMeshPro ships no default font in this build).</summary>
        public static TMP_FontAsset FindFont()
        {
            if (_font != null) return _font;
            foreach (var t in UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.font != null) { _font = t.font; break; }
            if (_font == null)
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all.Length > 0) _font = all[0];
            }
            return _font;
        }

        // ---------------------------------------------------------------- building

        bool _builtGerman;

        void Build()
        {
            _builtGerman = Loc.German;
            var font = FindFont();
            _go = new GameObject("CozyVR Settings", typeof(RectTransform));
            _go.layer = HudPanel.HudLayer;
            _go.transform.SetParent(_rig, false);
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _canvasRt = (RectTransform)_go.transform;

            var c = Plugin.Cfg;
            var rows = new List<Action<float>>();
            float y = 0f;

            Text(_canvasRt, Loc.T("VR-Einstellungen", "VR settings"), new Vector2(0f, y - 40f), new Vector2(W - 80f, 50f), 34f, TextAlignmentOptions.Center, font, FontStyles.Bold);
            y -= 80f;

            y = Header(Loc.T("Hauptmenü", "Main menu"), y, font);
            y = Stepper(Loc.T("Größe", "Size"), y, font, () => $"{c.MenuWidth.Value:0.0} m", () => c.MenuWidth.Value = Mathf.Clamp(Round(c.MenuWidth.Value - 0.1f), 0.4f, 5f), () => c.MenuWidth.Value = Mathf.Clamp(Round(c.MenuWidth.Value + 0.1f), 0.4f, 5f));
            y = Stepper(Loc.T("Abstand", "Distance"), y, font, () => $"{new Vector3(c.MenuX.Value, c.MenuY.Value, c.MenuZ.Value).magnitude:0.0} m", () => _hud.ChangeDistance(false, -0.1f), () => _hud.ChangeDistance(false, 0.1f));
            y = Wide(Loc.T("Hauptmenü-Position zurücksetzen", "Reset main menu position"), y, font, _hud.ResetMenuPose);

            y = Header(Loc.T("Spiel-Anzeige (Leiste oben, Hinweise)", "Game display (top bar, hints)"), y - 6f, font);
            y = Stepper(Loc.T("Größe", "Size"), y, font, () => $"{c.HudWidth.Value:0.0} m", () => c.HudWidth.Value = Mathf.Clamp(Round(c.HudWidth.Value - 0.1f), 0.4f, 5f), () => c.HudWidth.Value = Mathf.Clamp(Round(c.HudWidth.Value + 0.1f), 0.4f, 5f));
            y = Stepper(Loc.T("Abstand", "Distance"), y, font, () => $"{new Vector3(c.HudX.Value, c.HudY.Value, c.HudZ.Value).magnitude:0.0} m", () => _hud.ChangeDistance(true, -0.1f), () => _hud.ChangeDistance(true, 0.1f));
            y = Wide(Loc.T("Spiel-Anzeige zurücksetzen", "Reset game display"), y, font, _hud.ResetHudPose);

            y = Header(Loc.T("Bau-Menü", "Build menu"), y - 6f, font);
            y = Stepper(Loc.T("Größe im Raum", "Size in the room"), y, font, () => $"{c.EditScale.Value * 100f:0} %", () => c.EditScale.Value = Mathf.Clamp(Round2(c.EditScale.Value - 0.05f), 0.1f, 2f), () => c.EditScale.Value = Mathf.Clamp(Round2(c.EditScale.Value + 0.05f), 0.1f, 2f));
            y = Stepper(Loc.T("Größe an der Hand", "Size on the hand"), y, font, () => $"{c.AttachedScale.Value * 100f:0} %", () => c.AttachedScale.Value = Mathf.Clamp(Round2(c.AttachedScale.Value - 0.05f), 0.1f, 1.5f), () => c.AttachedScale.Value = Mathf.Clamp(Round2(c.AttachedScale.Value + 0.05f), 0.1f, 1.5f));
            y = Stepper(Loc.T("Objekt-Menü", "Object menu"), y, font, () => $"{c.OptionsScale.Value * 100f:0} %", () => c.OptionsScale.Value = Mathf.Clamp(Round2(c.OptionsScale.Value - 0.05f), 0.05f, 1f), () => c.OptionsScale.Value = Mathf.Clamp(Round2(c.OptionsScale.Value + 0.05f), 0.05f, 1f));
            y = Toggle(Loc.T("Immer an der Hand", "Always on hand"), y, font, () => c.AlwaysAttached.Value ? Loc.T("Ein", "On") : Loc.T("Aus", "Off"), () => c.AlwaysAttached.Value = !c.AlwaysAttached.Value);
            y = Wide(Loc.T("Bau-Menü zurücksetzen", "Reset build menu"), y, font, _hud.ResetEditPose);

            y = Header(Loc.T("Allgemein", "General"), y - 6f, font);
            y = Stepper(Loc.T("Weltmaßstab", "World scale"), y, font, () => $"1 : {c.WorldScale.Value:0}", () => c.WorldScale.Value = Mathf.Clamp(Mathf.Round(c.WorldScale.Value - 1f), c.MinWorldScale.Value, c.MaxWorldScale.Value), () => c.WorldScale.Value = Mathf.Clamp(Mathf.Round(c.WorldScale.Value + 1f), c.MinWorldScale.Value, c.MaxWorldScale.Value));
            y = Toggle(Loc.T("Greifmodus", "Grab mode"), y, font, () => c.GrabMode.Value ? Loc.T("Hand", "Hand") : Loc.T("Maus", "Mouse"), () => c.GrabMode.Value = !c.GrabMode.Value);
            y = Toggle(Loc.T("Drehen", "Turning"), y, font, () => c.Turning.Value == TurnMode.Snap ? Loc.T("Stufen", "Snap") : Loc.T("Weich", "Smooth"), () => c.Turning.Value = c.Turning.Value == TurnMode.Snap ? TurnMode.Smooth : TurnMode.Snap);
            y = Toggle(Loc.T("Startfenster beim Spielstart", "Start window on launch"), y, font, () => c.ShowStartDialog.Value ? Loc.T("Zeigen", "Show") : Loc.T("Aus", "Off"), () => c.ShowStartDialog.Value = !c.ShowStartDialog.Value);
            y = Wide(Loc.T("Am Monitor weiterspielen (F9 = zurück)", "Continue on the monitor (F9 = back)"), y - 6f, font, () => { Close(); VRManager.Instance?.SetFlat(true); });
            y = Wide(Loc.T("Schließen", "Close"), y, font, Close, true);

            float height = -y + 20f;
            _canvasRt.sizeDelta = new Vector2(W, height);
            _canvasRt.pivot = new Vector2(0.5f, 1f);
            _canvasRt.localScale = Vector3.one * Px;

            // background behind everything
            var bg = NewRect("Background", _canvasRt, new Vector2(0f, 0f), new Vector2(W, height), new Vector2(0.5f, 1f));
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.13f, 0.1f, 0.17f, 0.94f);
            bg.SetAsFirstSibling();
        }

        static float Round(float v) => Mathf.Round(v * 10f) / 10f;
        static float Round2(float v) => Mathf.Round(v * 100f) / 100f;

        internal static RectTransform NewRect(string name, RectTransform parent, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = HudPanel.HudLayer };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        internal static TextMeshProUGUI Text(RectTransform parent, string text, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align, TMP_FontAsset font, FontStyles style = FontStyles.Normal)
        {
            var rt = NewRect("Text", parent, pos, size, new Vector2(0.5f, 0.5f));
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            return t;
        }

        Element Button(string label, Vector2 pos, Vector2 size, TMP_FontAsset font, Action click, Color color)
        {
            var rt = NewRect("Button", _canvasRt, pos, size, new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var t = Text(rt, label, Vector2.zero, size, 28f, TextAlignmentOptions.Center, font);
            var rtT = t.rectTransform;
            rtT.anchorMin = rtT.anchorMax = new Vector2(0.5f, 0.5f);
            rtT.anchoredPosition = Vector2.zero;
            var e = new Element { Rt = rt, Img = img, Color = color, Click = click, Label = t };
            _buttons.Add(e);
            return e;
        }

        static readonly Color ButtonColor = new Color(0.32f, 0.27f, 0.42f, 1f);
        static readonly Color AccentColor = new Color(0.3f, 0.55f, 0.95f, 1f);

        float Header(string text, float y, TMP_FontAsset font)
        {
            Text(_canvasRt, text, new Vector2(0f, y - 22f), new Vector2(W - 80f, 40f), 26f, TextAlignmentOptions.Left, font, FontStyles.Bold).color = new Color(1f, 0.82f, 0.35f, 1f);
            return y - 46f;
        }

        float Stepper(string label, float y, TMP_FontAsset font, Func<string> value, Action minus, Action plus)
        {
            float cy = y - RowH * 0.5f;
            Text(_canvasRt, label, new Vector2(-W * 0.5f + 40f + 150f, cy), new Vector2(300f, RowH), 24f, TextAlignmentOptions.Left, font);
            Button("−", new Vector2(110f, cy), new Vector2(52f, 46f), font, minus, ButtonColor);
            var v = Text(_canvasRt, "", new Vector2(190f, cy), new Vector2(100f, RowH), 24f, TextAlignmentOptions.Center, font);
            _values.Add(new Element { Label = v, Text = value });
            Button("+", new Vector2(270f, cy), new Vector2(52f, 46f), font, plus, ButtonColor);
            return y - RowH;
        }

        float Toggle(string label, float y, TMP_FontAsset font, Func<string> value, Action toggle)
        {
            float cy = y - RowH * 0.5f;
            Text(_canvasRt, label, new Vector2(-W * 0.5f + 40f + 150f, cy), new Vector2(300f, RowH), 24f, TextAlignmentOptions.Left, font);
            var b = Button("", new Vector2(190f, cy), new Vector2(212f, 46f), font, toggle, ButtonColor);
            _values.Add(new Element { Label = b.Label, Text = value });
            return y - RowH;
        }

        float Wide(string label, float y, TMP_FontAsset font, Action click, bool accent = false)
        {
            float cy = y - RowH * 0.5f;
            Button(label, new Vector2(0f, cy), new Vector2(W - 80f, 46f), font, click, accent ? AccentColor : ButtonColor);
            return y - RowH;
        }

        // ---------------------------------------------------------------- open / close

        /// <summary>Opens next to the given panel pose (tracking space).</summary>
        public void Show(Vector3 panelPos, Quaternion panelRot, float panelWidth, float panelHeight)
        {
            if (_go != null && _builtGerman != Loc.German) { UnityEngine.Object.Destroy(_go); _go = null; _buttons.Clear(); _values.Clear(); }
            if (_go == null) Build();
            Vector3 right = panelRot * Vector3.right;
            Vector3 up = panelRot * Vector3.up;
            float h = _canvasRt.sizeDelta.y * Px;
            _go.transform.localPosition = panelPos + right * (panelWidth * 0.5f + W * Px * 0.5f + 0.05f) + up * (h * 0.5f);
            _go.transform.localRotation = panelRot;
            _go.SetActive(true);
            _open = true;
            Refresh();
        }

        public void Close()
        {
            if (_go != null) _go.SetActive(false);
            _open = false;
            _hover = null;
        }

        public void Refresh()
        {
            if (!_open) return;
            foreach (var v in _values) v.Label.text = v.Text();
            foreach (var b in _buttons) b.Img.color = b == _hover ? new Color(1f, 0.82f, 0.35f, 1f) : b.Color;
            foreach (var b in _buttons) b.Label.color = b == _hover ? new Color(0.15f, 0.1f, 0.2f, 1f) : Color.white;
        }

        // ---------------------------------------------------------------- laser

        /// <summary>Laser test: true if the ray hits the panel (hover is updated).</summary>
        public bool Hit(Ray worldRay, out Vector3 hit, out float dist)
        {
            hit = default;
            dist = 0f;
            _hover = null;
            if (!_open) return false;
            var t = _go.transform;
            var plane = new Plane(t.forward, t.position);
            if (!plane.Raycast(worldRay, out dist)) return false;
            hit = worldRay.GetPoint(dist);
            Vector2 local = _canvasRt.InverseTransformPoint(hit);
            if (!_canvasRt.rect.Contains(local)) return false;
            foreach (var b in _buttons)
            {
                Vector2 lb = b.Rt.InverseTransformPoint(hit);
                if (b.Rt.rect.Contains(lb)) { _hover = b; break; }
            }
            return true;
        }

        /// <summary>World position of the n-th button (diagnostics).</summary>
        public Vector3 DebugButtonPoint(int index)
        {
            if (!_open || index < 0 || index >= _buttons.Count) return Vector3.zero;
            var rt = _buttons[index].Rt;
            return rt.TransformPoint(rt.rect.center);
        }

        /// <summary>Trigger pressed while the laser is on the panel.</summary>
        public void Click()
        {
            var h = _hover;
            if (h == null) return;
            try { h.Click(); }
            catch (Exception e) { Plugin.Log.LogWarning("Settings action failed: " + e.Message); }
        }
    }
}
