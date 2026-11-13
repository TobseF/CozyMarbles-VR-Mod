using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CozyMarblesVR
{
    /// <summary>
    /// "i" window: explains the VR controls. The text is generated from the current button bindings, so it stays
    /// correct when bindings are changed in the config. Like the settings panel it is a world-space canvas that is
    /// hit-tested with the laser directly.
    /// </summary>
    public class VRHelpPanel
    {
        const float Px = 0.001f;
        const float W = 1180f;

        readonly Transform _rig;
        GameObject _go;
        RectTransform _canvasRt, _closeRt;
        Image _closeImg;
        bool _open, _hoverClose;

        public bool Open => _open;

        public VRHelpPanel(Transform rig) { _rig = rig; }

        static string Name(VRAction a)
        {
            switch (a)
            {
                case VRAction.None: return "–";
                case VRAction.LeftClick: return Loc.T("Klick: platzieren, auswählen, Menü", "Click: place, select, menu");
                case VRAction.RightClick: return Loc.T("Abbrechen", "Cancel");
                case VRAction.MiddleClick: return Loc.T("Mittlere Maustaste", "Middle mouse button");
                case VRAction.Rotate: return Loc.T("Teil drehen", "Rotate piece");
                case VRAction.Shift: return Loc.T("Umschalt halten (Höhe ziehen, Mehrfachauswahl)", "Hold Shift (drag height, multi-select)");
                case VRAction.BuildMenu: return Loc.T("Bau-Menü öffnen/schließen", "Open/close build menu");
                case VRAction.ToggleSnap: return Loc.T("Magnet-Einrasten an/aus", "Magnet snapping on/off");
                case VRAction.MagicDrawVertical: return Loc.T("Magic Draw vertikal", "Magic Draw vertical");
                case VRAction.Delete: return Loc.T("Löschen", "Delete");
                case VRAction.HeightUp: return Loc.T("Höhe + (eine Stufe)", "Height + (one step)");
                case VRAction.HeightDown: return Loc.T("Höhe − (eine Stufe)", "Height − (one step)");
                case VRAction.Undo: return Loc.T("Rückgängig", "Undo");
                case VRAction.Redo: return Loc.T("Wiederholen", "Redo");
                case VRAction.Cut: return Loc.T("Ausschneiden", "Cut");
                case VRAction.Copy: return Loc.T("Kopieren", "Copy");
                case VRAction.Paste: return Loc.T("Einfügen", "Paste");
                case VRAction.Escape: return Loc.T("Pause / Zurück", "Pause / Back");
                case VRAction.RollMarbles: return Loc.T("Murmeln losrollen", "Release marbles");
                case VRAction.GrabWorld: return Loc.T("Welt greifen und verschieben", "Grab and move the world");
                case VRAction.Recenter: return Loc.T("Ansicht zentrieren", "Recenter view");
                case VRAction.AltLayer: return Loc.T("Zweitbelegung (halten)", "Alternate layer (hold)");
                case VRAction.MenuBack: return Loc.T("Zurück im Menü", "Back in menus");
                case VRAction.MenuPanel: return Loc.T("Bau-Fenster: antippen = auf/zu, halten = an die Hand", "Build window: tap = open/close, hold = take on hand");
                default: return a.ToString();
            }
        }

        void Build()
        {
            var font = VRSettingsPanel.FindFont();
            var c = Plugin.Cfg;
            _go = new GameObject("CozyVR Help", typeof(RectTransform)) { layer = HudPanel.HudLayer };
            _go.transform.SetParent(_rig, false);
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _canvasRt = (RectTransform)_go.transform;

            bool right = c.PointerHand.Value == Hand.Right;
            // physical sides: rows for the right and the left controller
            var rightRows = new List<KeyValuePair<string, string>>
            {
                Row("Trigger", right ? c.PointerTrigger.Value : c.OffTrigger.Value),
                Row("Grip", right ? c.PointerGrip.Value : c.OffGrip.Value),
                Row("A", right ? c.PointerPrimary.Value : c.OffPrimary.Value),
                Row("B", right ? c.PointerSecondary.Value : c.OffSecondary.Value),
                Row("X", c.FrameX.Value),
                Row("Y", c.FrameY.Value),
                Row("Bumper", right ? c.PointerBumper.Value : c.OffBumper.Value),
                Row(Loc.T("Stick-Klick", "Stick click"), right ? c.PointerStickClick.Value : c.OffStickClick.Value),
                new KeyValuePair<string, string>("Stick", right ? Loc.T("Drehen  ·  ↑↓ Bauhöhe / scrollen / fliegen", "Turn  ·  ↑↓ build height / scroll / fly") : Loc.T("Fliegen", "Fly")),
            };
            var leftRows = new List<KeyValuePair<string, string>>
            {
                Row("Trigger", right ? c.OffTrigger.Value : c.PointerTrigger.Value),
                Row("Grip", right ? c.OffGrip.Value : c.PointerGrip.Value),
                Row("Bumper", right ? c.OffBumper.Value : c.PointerBumper.Value),
                Row(Loc.T("Steuerkreuz", "D-pad") + " ↑", c.DpadUp.Value),
                Row(Loc.T("Steuerkreuz", "D-pad") + " ↓", c.DpadDown.Value),
                Row(Loc.T("Steuerkreuz", "D-pad") + " ←", c.DpadLeft.Value),
                Row(Loc.T("Steuerkreuz", "D-pad") + " →", c.DpadRight.Value),
                Row(Loc.T("Stick-Klick", "Stick click"), right ? c.OffStickClick.Value : c.PointerStickClick.Value),
                new KeyValuePair<string, string>("Stick", right ? Loc.T("Fliegen", "Fly") : Loc.T("Drehen  ·  ↑↓ Bauhöhe / scrollen / fliegen", "Turn  ·  ↑↓ build height / scroll / fly")),
                Row(Loc.T("View-Taste", "View button"), c.OffMenu.Value),
            };

            float y = 0f;
            VRSettingsPanel.Text(_canvasRt, Loc.T("VR-Steuerung", "VR controls"), new Vector2(0f, y - 36f), new Vector2(W - 80f, 52f), 38f, TextAlignmentOptions.Center, font, FontStyles.Bold);
            y -= 80f;
            float top = y;
            float colW = (W - 100f) * 0.5f;
            float yr = Column(rightRows, -W * 0.25f - 10f, top, colW, Loc.T("Rechter Controller", "Right controller") + (right ? Loc.T("  (Laser)", "  (laser)") : ""), font);
            float yl = Column(leftRows, W * 0.25f + 10f, top, colW, Loc.T("Linker Controller", "Left controller") + (right ? "" : Loc.T("  (Laser)", "  (laser)")), font);
            y = Mathf.Min(yr, yl) - 14f;

            string[] notes =
            {
                Loc.T("Beide Grips halten und die Hände auseinander- oder zusammenziehen: Welt verschieben, drehen und skalieren.", "Hold both grips and move your hands apart or together: move, rotate and scale the world."),
                Loc.T("Menü ohne Laser: linkes Steuerkreuz (oder linker Stick) bewegt den Fokus, A wählt aus, B geht zurück.", "Menus without laser: left D-pad (or left stick) moves the focus, A selects, B goes back."),
                Loc.T("Fenster verschieben: Leiste unter dem Fenster greifen (rechter Stick ↑↓: näher / weiter weg). Ecke unten rechts: Größe.", "Move windows: grab the bar below the window (right stick ↑↓: closer / farther). Bottom right corner: size."),
                Loc.T("Greifmodus (Knopf „Maus/Hand“ im linken Menü oder linken Bumper zweimal antippen): Kugel am Controller statt Laser.", "Grab mode (button “Mouse/Hand” in the left menu, or double-tap the left bumper): a ball on the controller instead of the laser."),
                Loc.T("Trigger halten = Teil greifen und frei bewegen, Controller drehen = Teil drehen, loslassen = einrasten (Vorschau zeigt wohin).", "Hold trigger = grab and move a piece freely, twist controller = rotate it, release = snap in (preview shows where)."),
                Loc.T("Knopf „VR“ neben der Menüleiste: VR-Einstellungen (Größen, Abstände, Bau-Fenster an der Hand).", "Button “VR” next to the menu bar: VR settings (sizes, distances, build window on the hand)."),
            };
            foreach (var n in notes)
            {
                var t = VRSettingsPanel.Text(_canvasRt, "•  " + n, new Vector2(0f, y - 18f), new Vector2(W - 90f, 34f), 21f, TextAlignmentOptions.Left, font);
                t.color = new Color(0.85f, 0.85f, 0.92f);
                y -= 38f;
            }

            y -= 10f;
            _closeRt = VRSettingsPanel.NewRect("Close", _canvasRt, new Vector2(0f, y - 28f), new Vector2(360f, 52f), new Vector2(0.5f, 0.5f));
            _closeImg = _closeRt.gameObject.AddComponent<Image>();
            _closeImg.color = new Color(0.3f, 0.55f, 0.95f, 1f);
            _closeImg.raycastTarget = false;
            var label = VRSettingsPanel.Text(_closeRt, Loc.T("Schließen", "Close"), Vector2.zero, new Vector2(360f, 52f), 28f, TextAlignmentOptions.Center, font);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            y -= 70f;

            float height = -y + 14f;
            _canvasRt.sizeDelta = new Vector2(W, height);
            _canvasRt.pivot = new Vector2(0.5f, 1f);
            _canvasRt.localScale = Vector3.one * Px;
            var bg = VRSettingsPanel.NewRect("Background", _canvasRt, Vector2.zero, new Vector2(W, height), new Vector2(0.5f, 1f));
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.13f, 0.1f, 0.17f, 0.95f);
            bgImg.raycastTarget = false;
            bg.SetAsFirstSibling();
        }

        static KeyValuePair<string, string> Row(string key, VRAction a) => new KeyValuePair<string, string>(key, Name(a));

        float Column(List<KeyValuePair<string, string>> rows, float centerX, float top, float width, string title, TMP_FontAsset font)
        {
            float y = top;
            var head = VRSettingsPanel.Text(_canvasRt, title, new Vector2(centerX, y - 20f), new Vector2(width, 38f), 26f, TextAlignmentOptions.Left, font, FontStyles.Bold);
            head.color = new Color(1f, 0.82f, 0.35f, 1f);
            y -= 46f;
            foreach (var r in rows)
            {
                VRSettingsPanel.Text(_canvasRt, r.Key, new Vector2(centerX - width * 0.5f + 100f, y - 17f), new Vector2(200f, 32f), 21f, TextAlignmentOptions.Left, font)
                    .color = new Color(0.7f, 0.85f, 1f);
                VRSettingsPanel.Text(_canvasRt, r.Value, new Vector2(centerX + 100f, y - 17f), new Vector2(width - 215f, 32f), 21f, TextAlignmentOptions.Left, font);
                y -= 36f;
            }
            return y;
        }

        public void Show(Vector3 headPos, Quaternion headRot)
        {
            if (_go != null) Object.Destroy(_go); // rebuild: bindings may have changed
            _go = null;
            Build();
            Vector3 fwd = headRot * Vector3.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
            float h = _canvasRt.sizeDelta.y * Px;
            float distance = 1.2f;
            Vector3 pos = headPos + fwd * distance + Vector3.up * (h * 0.5f - 0.15f);
            _go.transform.localPosition = pos;
            _go.transform.localRotation = Quaternion.LookRotation(pos - headPos, Vector3.up);
            _go.SetActive(true);
            _open = true;
        }

        public void Close()
        {
            if (_go != null) _go.SetActive(false);
            _open = false;
            _hoverClose = false;
        }

        public bool Hit(Ray worldRay, out Vector3 hit, out float dist)
        {
            hit = default; dist = 0f; _hoverClose = false;
            if (!_open || _go == null) return false;
            var t = _go.transform;
            var plane = new Plane(t.forward, t.position);
            if (!plane.Raycast(worldRay, out dist)) return false;
            hit = worldRay.GetPoint(dist);
            Vector2 local = _canvasRt.InverseTransformPoint(hit);
            if (!_canvasRt.rect.Contains(local)) return false;
            Vector2 lc = _closeRt.InverseTransformPoint(hit);
            _hoverClose = _closeRt.rect.Contains(lc);
            if (_closeImg != null) _closeImg.color = _hoverClose ? new Color(1f, 0.82f, 0.35f, 1f) : new Color(0.3f, 0.55f, 0.95f, 1f);
            return true;
        }

        public void Click()
        {
            if (_hoverClose) Close();
        }
    }
}
