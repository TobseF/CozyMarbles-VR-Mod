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
        const float Px = 0.0007f;          // canvas unit = 0.7 mm (two columns: about 0.9 m wide)
        const float W = 640f;               // one column
        const float ColGap = 30f;
        const float TotalW = 2f * W + ColGap; // two columns side by side (one long column reached into the table)
        float _colX;                        // centre of the column being built
        const float RowH = 52f;
        const float HandleH = 44f, HandleGap = 14f;

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
            public Color TextColor = Color.white;
        }

        readonly List<Element> _buttons = new List<Element>();
        readonly List<Element> _values = new List<Element>();
        Element _hover;
        RectTransform _handle;
        Image _handleImg;
        bool _handleHover, _dragging;
        float _dragDist;
        Vector3 _dragOffset;

        /// <summary>The laser is on the grip handle (checked by the last <see cref="Hit"/>).</summary>
        public bool HandleHover => _handleHover;

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

            var header = NewRect("Header", _canvasRt, new Vector2(0f, 0f), new Vector2(TotalW, HeaderH), new Vector2(0.5f, 1f));
            var hImg = header.gameObject.AddComponent<Image>();
            hImg.sprite = UiSprite("ui_header", false);
            hImg.raycastTarget = false;
            Text(_canvasRt, Loc.T("VR-Einstellungen", "VR settings"), new Vector2(3f, -HeaderH * 0.5f - 4f), new Vector2(TotalW - 70f, HeaderH), 44f, TextAlignmentOptions.Left, font, FontStyles.Bold).color = TitleShadow;
            Text(_canvasRt, Loc.T("VR-Einstellungen", "VR settings"), new Vector2(0f, -HeaderH * 0.5f), new Vector2(TotalW - 70f, HeaderH), 44f, TextAlignmentOptions.Left, font, FontStyles.Bold);
            y -= HeaderH + 14f;
            float top = y;
            _colX = -(W + ColGap) * 0.5f;
            // switch to the game's own settings (the VR settings close, the game opens its settings window)
            y = Wide(Loc.T("Spiel-Einstellungen öffnen", "Open game settings"), y, font, () =>
            {
                Close();
                try { TimR.Events.EventBus._?.PublishEvent(TimR.Events.Evt.OpenSettings); }
                catch (Exception e) { Plugin.Log.LogWarning("Opening the game settings failed: " + e.Message); }
            }, true) - 6f;

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

            float leftBottom = y;
            y = top;
            _colX = (W + ColGap) * 0.5f;
            y = Header(Loc.T("Allgemein", "General"), y, font);
            y = Stepper(Loc.T("Weltmaßstab", "World scale"), y, font, () => $"1 : {c.WorldScale.Value:0}", () => c.WorldScale.Value = Mathf.Clamp(Mathf.Round(c.WorldScale.Value - 1f), c.MinWorldScale.Value, c.MaxWorldScale.Value), () => c.WorldScale.Value = Mathf.Clamp(Mathf.Round(c.WorldScale.Value + 1f), c.MinWorldScale.Value, c.MaxWorldScale.Value));
            y = Toggle(Loc.T("Greifmodus", "Grab mode"), y, font, () => c.GrabMode.Value ? Loc.T("Hand", "Hand") : Loc.T("Maus", "Mouse"), () => c.GrabMode.Value = !c.GrabMode.Value);
            y = Toggle(Loc.T("Drehen", "Turning"), y, font, () => c.Turning.Value == TurnMode.Snap ? Loc.T("Stufen", "Snap") : Loc.T("Weich", "Smooth"), () => c.Turning.Value = c.Turning.Value == TurnMode.Snap ? TurnMode.Smooth : TurnMode.Snap);
            y = Header(Loc.T("Murmeln (Handmodus)", "Marbles (hand mode)"), y - 6f, font);
            y = Toggle(Loc.T("Murmel-Modus (Trigger)", "Marble mode (trigger)"), y, font, () => c.MarbleMode.Value ? Loc.T("Ein", "On") : Loc.T("Aus", "Off"), () => VRManager.Instance?.VrInput?.Grab.ToggleMarbles());
            y = Stepper(Loc.T("Wurf-Tempo (A − / Y +)", "Throw speed (A − / Y +)"), y, font, () => c.ThrowLevel.Value <= 0 ? Loc.T("Fallen", "Drop") : $"{c.ThrowLevel.Value} / {MarbleLauncher.MaxLevel}", () => c.ThrowLevel.Value = Mathf.Clamp(c.ThrowLevel.Value - 1, 0, MarbleLauncher.MaxLevel), () => c.ThrowLevel.Value = Mathf.Clamp(c.ThrowLevel.Value + 1, 0, MarbleLauncher.MaxLevel));
            y = Toggle(Loc.T("Murmel schubst (Physik)", "Marble pushes (physics)"), y, font, () => c.MarblePhysics.Value ? Loc.T("Ein", "On") : Loc.T("Aus", "Off"), () => c.MarblePhysics.Value = !c.MarblePhysics.Value);

            y = Header(Loc.T("Bild in der Brille", "Headset picture"), y - 6f, font);
            // multiplies the resolution SteamVR asks for (its own per-app setting); pixels per eye in brackets
            // pixels per eye: the Steam Frame panel and steps below it, or SteamVR's own recommendation
            y = WideStepper(Loc.T("Auflösung pro Auge", "Resolution per eye"), y, font, () => VRManager.PresetText(c.EyeResolution.Value, true),
                () => c.EyeResolution.Value = VRManager.StepPreset(c.EyeResolution.Value, -1),
                () => c.EyeResolution.Value = VRManager.StepPreset(c.EyeResolution.Value, 1));
            y = Info(y, font, () =>
            {
                int w = UnityEngine.XR.XRSettings.eyeTextureWidth, h = UnityEngine.XR.XRSettings.eyeTextureHeight;
                var rec = VRManager.SteamVREye;
                return Loc.T($"läuft: {w} × {h} pro Auge · Steam Frame 2160 · SteamVR {rec.x}",
                             $"running: {w} × {h} per eye · Steam Frame 2160 · SteamVR {rec.x}");
            });
            y = Toggle(Loc.T("FPS-Anzeige", "FPS readout"), y, font, () => c.ShowFps.Value ? Loc.T("Ein", "On") : Loc.T("Aus", "Off"), () => c.ShowFps.Value = !c.ShowFps.Value);

            y = Header(Loc.T("Bildschirm am PC", "PC screen"), y - 6f, font);
            y = Toggle(Loc.T("Zuschaueransicht (F11)", "Spectator view (F11)"), y, font, () => c.DesktopView.Value ? Loc.T("Ein", "On") : Loc.T("Aus (spart Leistung)", "Off (saves power)"), () => c.DesktopView.Value = !c.DesktopView.Value);
            y = Stepper(Loc.T("Zuschauer-Auflösung", "Spectator resolution"), y, font, () => $"{DesktopScreen.Scale * 100f:0} %",
                () => c.DesktopViewScale.Value = Mathf.Clamp(Mathf.Round((c.DesktopViewScale.Value - 0.25f) * 4f) / 4f, 0.25f, 1f),
                () => c.DesktopViewScale.Value = Mathf.Clamp(Mathf.Round((c.DesktopViewScale.Value + 0.25f) * 4f) / 4f, 0.25f, 1f));
            y = Toggle(Loc.T("Hinweis-Leiste (F10)", "Info banner (F10)"), y, font, () => c.DesktopHint.Value ? Loc.T("Ein", "On") : Loc.T("Aus", "Off"), () => c.DesktopHint.Value = !c.DesktopHint.Value);

            y = Header(Loc.T("Sonstiges", "Other"), y - 6f, font);
            y = Toggle(Loc.T("Startfenster beim Spielstart", "Start window on launch"), y, font, () => c.ShowStartDialog.Value ? Loc.T("Zeigen", "Show") : Loc.T("Aus", "Off"), () => c.ShowStartDialog.Value = !c.ShowStartDialog.Value);
            y = Wide(Loc.T("Am Monitor weiterspielen (F9 = zurück)", "Continue on the monitor (F9 = back)"), y - 6f, font, () => { Close(); VRManager.Instance?.SetFlat(true); });
            y = Wide(Loc.T("Schließen", "Close"), y, font, Close, true);
            y = Mathf.Min(y, leftBottom);
            _colX = 0f;

            float bgHeight = -y + 20f;
            float height = bgHeight + HandleGap + HandleH;   // the grip handle hangs below the panel
            _canvasRt.sizeDelta = new Vector2(TotalW, height);
            _canvasRt.pivot = new Vector2(0.5f, 1f);
            _canvasRt.localScale = Vector3.one * Px;

            // grip handle (same look as under the other windows): drag it to move the panel
            var hrt = NewRect("Handle", _canvasRt, new Vector2(0f, -bgHeight - HandleGap - HandleH * 0.5f), new Vector2(HandleH * 5f, HandleH), new Vector2(0.5f, 0.5f));
            _handleImg = hrt.gameObject.AddComponent<Image>();
            var tex = Icons.Get("grab_bar");
            if (tex != null) _handleImg.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _handleImg.raycastTarget = false;
            _handle = hrt;

            // background behind everything
            var bg = NewRect("Background", _canvasRt, new Vector2(0f, 0f), new Vector2(TotalW, bgHeight), new Vector2(0.5f, 1f));
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = UiSprite("ui_round_rect", true);
            bgImg.type = Image.Type.Sliced;
            bgImg.pixelsPerUnitMultiplier = 1.4f;
            bgImg.color = Cream;
            bgImg.raycastTarget = false;
            bg.SetAsFirstSibling();
            var shadow = NewRect("Shadow", _canvasRt, new Vector2(8f, -10f), new Vector2(TotalW, bgHeight), new Vector2(0.5f, 1f));
            var sImg = shadow.gameObject.AddComponent<Image>();
            sImg.sprite = bgImg.sprite;
            sImg.type = Image.Type.Sliced;
            sImg.pixelsPerUnitMultiplier = 1.4f;
            sImg.color = new Color(0.1f, 0.05f, 0f, 0.35f);
            sImg.raycastTarget = false;
            shadow.SetAsFirstSibling();
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

        Element Button(string label, Vector2 pos, Vector2 size, TMP_FontAsset font, Action click, Color color, Color? textColor = null)
        {
            var rt = NewRect("Button", _canvasRt, new Vector2(pos.x + _colX, pos.y), size, new Vector2(0.5f, 0.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiSprite("ui_round_rect", true);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2.6f;   // small corner radius on small buttons
            img.color = color;
            img.raycastTarget = false;
            var t = Text(rt, label, Vector2.zero, new Vector2(size.x - 16f, size.y), 26f, TextAlignmentOptions.Center, font, FontStyles.Bold);
            t.enableAutoSizing = true; t.fontSizeMax = 26f; t.fontSizeMin = 14f;   // long labels shrink instead of spilling over
            var rtT = t.rectTransform;
            rtT.anchorMin = rtT.anchorMax = new Vector2(0.5f, 0.5f);
            rtT.anchoredPosition = Vector2.zero;
            var e = new Element { Rt = rt, Img = img, Color = color, Click = click, Label = t, TextColor = textColor ?? Color.white };
            t.color = e.TextColor;
            _buttons.Add(e);
            return e;
        }

        // colours of the game's own dialogs
        static readonly Color Cream = new Color32(238, 233, 221, 255);
        static readonly Color Brown = new Color32(92, 58, 46, 255);
        static readonly Color TitleShadow = new Color32(150, 80, 30, 255);
        static readonly Color Teal = new Color32(62, 127, 124, 255);
        static readonly Color FieldBrown = new Color32(174, 130, 107, 255);
        static readonly Color Yellow = new Color32(250, 196, 45, 255);
        static readonly Color ButtonColor = Teal;
        const float HeaderH = 90f;

        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        /// <summary>Sprite from the mod's artwork; sliced = 40 px corners for 9-slicing.</summary>
        internal static Sprite UiSprite(string icon, bool sliced)
        {
            string key = icon + (sliced ? "#9" : "");
            if (Sprites.TryGetValue(key, out var s) && s != null) return s;
            var tex = Icons.Get(icon);
            if (tex == null) return null;
            s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                sliced ? new Vector4(40f, 40f, 40f, 40f) : Vector4.zero);
            Sprites[key] = s;
            return s;
        }

        float Header(string text, float y, TMP_FontAsset font)
        {
            Text(_canvasRt, text, new Vector2(_colX, y - 24f), new Vector2(W - 80f, 40f), 28f, TextAlignmentOptions.Left, font, FontStyles.Bold).color = Brown;
            return y - 48f;
        }

        float Stepper(string label, float y, TMP_FontAsset font, Func<string> value, Action minus, Action plus)
        {
            float cy = y - RowH * 0.5f;
            Label(label, cy, font, 300f);
            Button("−", new Vector2(110f, cy), new Vector2(52f, 46f), font, minus, ButtonColor);
            var field = NewRect("Field", _canvasRt, new Vector2(_colX + 190f, cy), new Vector2(100f, 46f), new Vector2(0.5f, 0.5f));
            var fImg = field.gameObject.AddComponent<Image>();
            fImg.sprite = UiSprite("ui_round_rect", true);
            fImg.type = Image.Type.Sliced;
            fImg.pixelsPerUnitMultiplier = 2.6f;
            fImg.color = FieldBrown;
            fImg.raycastTarget = false;
            var v = Text(_canvasRt, "", new Vector2(_colX + 190f, cy), new Vector2(100f, RowH), 22f, TextAlignmentOptions.Center, font, FontStyles.Bold);
            v.enableAutoSizing = true; v.fontSizeMax = 22f; v.fontSizeMin = 14f;
            _values.Add(new Element { Label = v, Text = value });
            Button("+", new Vector2(270f, cy), new Vector2(52f, 46f), font, plus, ButtonColor);
            return y - RowH;
        }

        /// <summary>Row label on the left of a column; shrinks to fit the room left of its controls.</summary>
        void Label(string label, float cy, TMP_FontAsset font, float width)
        {
            var t = Text(_canvasRt, label, new Vector2(_colX - W * 0.5f + 40f + width * 0.5f, cy), new Vector2(width, RowH), 24f, TextAlignmentOptions.Left, font, FontStyles.Bold);
            t.color = Brown;
            t.enableAutoSizing = true; t.fontSizeMax = 24f; t.fontSizeMin = 15f;
        }

        /// <summary>A stepper with a wide value field (long values such as "2160 × 2160 (Steam Frame)").</summary>
        float WideStepper(string label, float y, TMP_FontAsset font, Func<string> value, Action minus, Action plus)
        {
            float cy = y - RowH * 0.5f;
            Label(label, cy, font, 200f);
            Button("−", new Vector2(-10f, cy), new Vector2(52f, 46f), font, minus, ButtonColor);
            var field = NewRect("Field", _canvasRt, new Vector2(_colX + 130f, cy), new Vector2(216f, 46f), new Vector2(0.5f, 0.5f));
            var fImg = field.gameObject.AddComponent<Image>();
            fImg.sprite = UiSprite("ui_round_rect", true);
            fImg.type = Image.Type.Sliced;
            fImg.pixelsPerUnitMultiplier = 2.6f;
            fImg.color = FieldBrown;
            fImg.raycastTarget = false;
            var v = Text(_canvasRt, "", new Vector2(_colX + 130f, cy), new Vector2(206f, RowH), 22f, TextAlignmentOptions.Center, font, FontStyles.Bold);
            v.enableAutoSizing = true; v.fontSizeMax = 22f; v.fontSizeMin = 12f;
            _values.Add(new Element { Label = v, Text = value });
            Button("+", new Vector2(270f, cy), new Vector2(52f, 46f), font, plus, ButtonColor);
            return y - RowH;
        }

        float Toggle(string label, float y, TMP_FontAsset font, Func<string> value, Action toggle)
        {
            float cy = y - RowH * 0.5f;
            Label(label, cy, font, 264f);
            var b = Button("", new Vector2(190f, cy), new Vector2(212f, 46f), font, toggle, FieldBrown);
            _values.Add(new Element { Label = b.Label, Text = value });
            return y - RowH;
        }

        /// <summary>A small line of text that is refreshed like the values (e.g. the resulting pixel size).</summary>
        float Info(float y, TMP_FontAsset font, Func<string> text)
        {
            var v = Text(_canvasRt, "", new Vector2(_colX, y - 16f), new Vector2(W - 80f, 30f), 19f, TextAlignmentOptions.Left, font);
            v.color = Brown;
            _values.Add(new Element { Label = v, Text = text });
            return y - 34f;
        }

        float Wide(string label, float y, TMP_FontAsset font, Action click, bool accent = false)
        {
            float cy = y - RowH * 0.5f;
            Button(label, new Vector2(0f, cy), new Vector2(W - 80f, 46f), font, click, accent ? Yellow : ButtonColor, accent ? Brown : (Color?)null);
            return y - RowH;
        }

        // ---------------------------------------------------------------- open / close

        /// <summary>Opens next to the given panel pose (tracking space), turned towards the viewer.</summary>
        public void Show(Vector3 panelPos, Quaternion panelRot, float panelWidth, float panelHeight, Vector3 headPos)
        {
            if (_go != null && _builtGerman != Loc.German) { UnityEngine.Object.Destroy(_go); _go = null; _buttons.Clear(); _values.Clear(); }
            if (_go == null) Build();
            Vector3 right = panelRot * Vector3.right;
            float h = _canvasRt.sizeDelta.y * Px;
            Vector3 center = panelPos + right * (panelWidth * 0.5f + TotalW * Px * 0.5f + 0.05f);
            // next to a menu held on the hand that would be right in your face: keep a comfortable reading distance
            Vector3 away = center - headPos;
            const float minDist = 0.85f;
            if (away.magnitude < minDist)
                center = headPos + (away.sqrMagnitude > 1e-4f ? away.normalized : panelRot * Vector3.forward) * minDist;
            _lastHead = headPos;
            Quaternion rot = FaceViewer(center, headPos);
            _go.transform.localRotation = rot;
            _go.transform.localPosition = center + rot * Vector3.up * (h * 0.5f);
            _go.SetActive(true);
            _open = true;
            _dragging = false;
            Refresh();
        }

        /// <summary>Opens centred on the given point (tracking space), turned towards the viewer.</summary>
        public void ShowAt(Vector3 center, Vector3 headPos)
        {
            if (_go != null && _builtGerman != Loc.German) { UnityEngine.Object.Destroy(_go); _go = null; _buttons.Clear(); _values.Clear(); }
            if (_go == null) Build();
            float h = _canvasRt.sizeDelta.y * Px;
            _lastHead = headPos;
            Quaternion rot = FaceViewer(center, headPos);
            _go.transform.localRotation = rot;
            _go.transform.localPosition = center + rot * Vector3.up * (h * 0.5f);
            _go.SetActive(true);
            _open = true;
            _dragging = false;
            Refresh();
        }

        /// <summary>Turned straight towards the head position, kept upright (the canvas faces along +Z, away from the viewer).</summary>
        static Quaternion FaceViewer(Vector3 center, Vector3 headPos)
        {
            Vector3 look = center - headPos;
            return look.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity;
        }

        // ---------------------------------------------------------------- moving with the grip handle (tracking space)

        public bool Dragging => _dragging;

        public void BeginDrag(Ray localRay)
        {
            if (!_open) return;
            var t = _go.transform;
            var plane = new Plane(t.localRotation * Vector3.forward, t.localPosition);
            if (!plane.Raycast(localRay, out float d)) d = Vector3.Distance(localRay.origin, t.localPosition);
            Vector3 hit = localRay.GetPoint(d);
            _dragDist = d;
            _dragOffset = Quaternion.Inverse(t.localRotation) * (t.localPosition - hit);
            _dragging = true;
        }

        /// <summary>Follows the laser at the grab distance (stick = further/closer) and keeps facing the viewer.</summary>
        public void UpdateDrag(Ray localRay, Vector3 headPos, float stickY, float dt)
        {
            if (!_dragging || !_open) return;
            _lastHead = headPos;
            if (Mathf.Abs(stickY) > 0.2f) _dragDist = Mathf.Clamp(_dragDist + stickY * 1.2f * dt, 0.25f, 8f);
            var t = _go.transform;
            Vector3 hit = localRay.GetPoint(_dragDist);
            Quaternion rot = t.localRotation;
            float h = _canvasRt.sizeDelta.y * Px;
            for (int i = 0; i < 2; i++)
            {
                Vector3 pos = hit + rot * _dragOffset;
                rot = FaceViewer(pos - rot * Vector3.up * (h * 0.5f), headPos);
            }
            t.localRotation = rot;
            t.localPosition = hit + rot * _dragOffset;
            Refresh();
        }

        public void EndDrag() { _dragging = false; Refresh(); }

        public void Close()
        {
            if (_go != null) _go.SetActive(false);
            _open = false;
            _dragging = false;
            _hover = null;
        }

        public void Refresh()
        {
            if (!_open) return;
            foreach (var v in _values) v.Label.text = v.Text();
            foreach (var b in _buttons)
            {
                b.Img.color = b == _hover ? Color.Lerp(b.Color, Color.white, 0.3f) : b.Color;
                b.Label.color = b.TextColor;
                b.Rt.localScale = b == _hover ? new Vector3(1.04f, 1.04f, 1f) : Vector3.one;
            }
            if (_handleImg != null) _handleImg.color = (_handleHover || _dragging) ? new Color(1f, 0.86f, 0.5f, 1f) : Color.white;
        }

        // ---------------------------------------------------------------- laser

        /// <summary>Laser test: true if the ray hits the panel (hover is updated).</summary>
        public bool Hit(Ray worldRay, out Vector3 hit, out float dist)
        {
            hit = default;
            dist = 0f;
            _hover = null;
            _handleHover = false;
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
            if (_handle != null)
            {
                Vector2 lh = _handle.InverseTransformPoint(hit);
                Rect r = _handle.rect;
                r.xMin -= 10f; r.xMax += 10f; r.yMin -= 10f; r.yMax += 10f; // a little extra margin
                _handleHover = r.Contains(lh);
            }
            return true;
        }

        /// <summary>Diagnostics: pose of the panel in tracking space.</summary>
        public string DebugState()
        {
            if (_go == null) return "-";
            var tr = _go.transform;
            Vector3 center = tr.localPosition - tr.localRotation * Vector3.up * (_canvasRt.sizeDelta.y * Px * 0.5f);
            float angle = Vector3.Angle(tr.localRotation * Vector3.forward, center - _lastHead);
            return $"pos={tr.localPosition} fwd={tr.localRotation * Vector3.forward} facingError={angle:F1}deg dragging={_dragging} handleHover={_handleHover}";
        }

        Vector3 _lastHead;

        /// <summary>World position of the grip handle (diagnostics).</summary>
        public Vector3 DebugHandlePoint() => _open && _handle != null ? _handle.TransformPoint(_handle.rect.center) : Vector3.zero;

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
