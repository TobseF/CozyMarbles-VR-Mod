using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace CozyMarblesVR
{
    /// <summary>
    /// Small head-locked readout below the centre of view: frame rate against the headset's refresh rate, frame time
    /// and the resolution per eye that is really rendered. Switched in the VR settings ([General] ShowFps).
    /// </summary>
    public class FpsCounter
    {
        GameObject _go;
        TextMeshProUGUI _text;
        int _frames;
        float _since = -1f;

        public void Tick(Transform head, bool active)
        {
            bool show = active && head != null && Plugin.Cfg.ShowFps.Value;
            if (!show)
            {
                if (_go != null && _go.activeSelf) _go.SetActive(false);
                _since = -1f;
                return;
            }
            if (_go == null && !Build(head)) return;
            if (!_go.activeSelf) _go.SetActive(true);

            float now = Time.unscaledTime;
            if (_since < 0f) { _since = now; _frames = 0; }
            _frames++;
            float span = now - _since;
            if (span < 0.5f) return;
            float fps = _frames / span;
            _since = now; _frames = 0;

            float hz = 0f;
            var display = XRBootstrap.Display;
            if (display != null) display.TryGetDisplayRefreshRate(out hz);
            int w = XRSettings.eyeTextureWidth, h = XRSettings.eyeTextureHeight;
            string color = hz <= 0f || fps >= hz * 0.95f ? "#7CFC9A" : fps >= hz * 0.75f ? "#FFD34D" : "#FF6B5B";
            _text.text = $"<color={color}>{fps:0} FPS</color> / {(hz > 0f ? hz.ToString("0") : "?")} Hz   {1000f / Mathf.Max(1f, fps):0.0} ms   "
                       + Loc.T($"{w} × {h} pro Auge", $"{w} × {h} per eye");
        }

        bool Build(Transform head)
        {
            var font = VRSettingsPanel.FindFont();
            if (font == null) return false;
            _go = new GameObject("CozyVR FPS", typeof(RectTransform)) { layer = HudPanel.HudLayer };
            var rt = (RectTransform)_go.transform;
            rt.SetParent(head, false);
            // a little below the centre of view, close enough not to be covered by the scenery
            rt.localPosition = new Vector3(0f, -0.13f, 0.6f);
            rt.localRotation = Quaternion.Euler(12f, 0f, 0f);
            rt.sizeDelta = new Vector2(900f, 64f);
            rt.localScale = Vector3.one * 0.00035f;
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var bg = VRSettingsPanel.NewRect("Background", rt, new Vector2(0f, 0f), new Vector2(900f, 64f), new Vector2(0.5f, 1f));
            bg.anchoredPosition = Vector2.zero;
            var img = bg.gameObject.AddComponent<Image>();
            img.sprite = VRSettingsPanel.UiSprite("ui_round_rect", true);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 2.6f;
            img.color = new Color(0.12f, 0.08f, 0.06f, 0.78f);
            img.raycastTarget = false;

            _text = VRSettingsPanel.Text(rt, "", new Vector2(0f, 0f), new Vector2(860f, 64f), 34f, TextAlignmentOptions.Center, font, FontStyles.Bold);
            var trt = _text.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            _text.richText = true;
            foreach (var t in _go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = HudPanel.HudLayer;
            return true;
        }
    }
}
