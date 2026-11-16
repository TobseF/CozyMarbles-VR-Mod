using System.Collections.Generic;
using TimR.Marbles;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace CozyMarblesVR
{
    /// <summary>
    /// Marble mode on the controller: the next marble sits at the tip of the controller, ready to go. The trigger
    /// releases it (drops it, or throws it with the set speed) and the following one appears right away.
    ///
    /// With "marble physics" on, the waiting marble is a kinematic body that is moved with the hand, so it pushes
    /// other marbles; otherwise it is a see-through preview that touches nothing.
    /// A small cone in front of it shows the launch direction (straight down when the speed is 0) and a dial on the
    /// cone shows the speed.
    /// </summary>
    public class MarbleLauncher
    {
        public const int MaxLevel = 6;
        public const float SpeedPerLevel = 0.5f;   // real metres per second: level 6 = 3 m/s

        public static int Level => Mathf.Clamp(Plugin.Cfg.ThrowLevel.Value, 0, MaxLevel);
        /// <summary>Throw speed in real metres per second.</summary>
        public static float Speed => Level * SpeedPerLevel;

        readonly Transform _rig;
        Marble _next;
        Rigidbody _rb;
        float _radius;                      // world units
        bool _shown, _physics, _dithered = true;
        Vector3 _target;

        // the marble just released must not be hit by the next one appearing at the same spot
        Marble _released;
        float _releasedAt;

        Transform _cone, _gauge;
        Material _lcd;
        readonly List<Material> _leds = new List<Material>();
        int _shownLevel = -1;

        public MarbleLauncher(Transform rig)
        {
            _rig = rig;
        }

        /// <summary>World radius of the waiting marble (0 while none is shown).</summary>
        public float Radius => _shown && _next != null ? _radius : 0f;

        // ---------------------------------------------------------------- per frame

        /// <param name="show">marble mode is usable right now (hand mode, in a level, nothing being placed)</param>
        /// <param name="tip">world position of the controller tip</param>
        /// <param name="aimDir">world pointing direction</param>
        /// <param name="head">world head position</param>
        /// <param name="hand">the pointer hand's controller model (the dial lies on its top)</param>
        /// <param name="gripRot">world rotation of the pointer hand's grip pose</param>
        public void Tick(bool show, Vector3 tip, Vector3 aimDir, Vector3 head, Transform hand, Quaternion gripRot)
        {
            if (!show) { Hide(); return; }
            float scale = Mathf.Max(1e-4f, _rig.lossyScale.x);
            bool physics = Plugin.Cfg.MarblePhysics.Value;

            if (_next == null && !CreateNext()) { HideIndicators(); return; }
            if (!_shown) { SetMarbleVisible(true); _shown = true; }

            _target = tip;
            if (physics != _physics) { _physics = physics; _rb.position = tip; }
            var interp = physics ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (_rb.interpolation != interp) _rb.interpolation = interp;
            bool collide = physics && ReleasedIsClear();
            if (_rb.detectCollisions != collide) _rb.detectCollisions = collide;
            // solid when it can push, see-through when it is only a preview
            bool dither = !physics;
            if (dither != _dithered) { _dithered = dither; _next.SetIsPreview(dither); }
            if (!physics) { _next.Position = tip; _rb.position = tip; }

            UpdateIndicators(tip, aimDir, head, scale, hand, gripRot);
        }

        /// <summary>Moves the waiting marble with the physics step, so a kinematic marble really pushes others.</summary>
        public void FixedTick()
        {
            if (_shown && _next != null && _rb != null && _physics) _rb.MovePosition(_target);
        }

        public static Vector3 LaunchDirection(Vector3 aimDir) =>
            Level > 0 ? aimDir.normalized : Vector3.down;

        /// <summary>Lets the waiting marble go with the given world velocity. False if there was none.</summary>
        public bool Release(Vector3 velocity)
        {
            if (_next == null || !_shown) return false;
            var m = _next;
            _next = null;
            var rb = _rb;
            _rb = null;
            _shown = false;
            m.SetIsPreview(false);
            m.Position = _target;
            rb.position = _target;
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.linearVelocity = velocity;
            rb.WakeUp();
            MarbleDropper.Announce(m);
            _released = m;
            _releasedAt = Time.unscaledTime;
            return true;
        }

        public void Hide()
        {
            if (_shown) { SetMarbleVisible(false); _shown = false; }
            HideIndicators();
        }

        public void Dispose()
        {
            Hide();
            if (_next != null) Object.Destroy(_next.gameObject);
            _next = null;
            _rb = null;
        }

        public string DebugState() =>
            $"launcher: next={(_next != null)} shown={_shown} pos={(_next != null ? _next.Position.ToString() : "-")} r={_radius:F3} physics={_physics} collide={(_rb != null && _rb.detectCollisions)} kinematic={(_rb != null && _rb.isKinematic)} cone={(_cone != null && _cone.gameObject.activeSelf)}";

        // ---------------------------------------------------------------- the waiting marble

        bool CreateNext()
        {
            var m = MarbleDropper.Create();
            if (m == null) return false;
            _next = m;
            _rb = m.Rigidbody;
            _rb.isKinematic = true;
            _rb.detectCollisions = false;
            _radius = 0.05f;
            var col = _rb.GetComponentInChildren<Collider>();
            if (col != null) _radius = Mathf.Max(0.005f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.y));
            _dithered = !Plugin.Cfg.MarblePhysics.Value;
            _physics = Plugin.Cfg.MarblePhysics.Value;
            m.SetIsPreview(_dithered);
            _shown = false;
            SetMarbleVisible(false);
            return true;
        }

        bool ReleasedIsClear()
        {
            if (_released == null) return true;
            if (Time.unscaledTime - _releasedAt > 1.5f || (_released.Position - _target).magnitude > _radius * 3f) { _released = null; return true; }
            return false;
        }

        void SetMarbleVisible(bool on)
        {
            if (_next == null) return;
            if (_next.gameObject.activeSelf != on) _next.gameObject.SetActive(on);
            if (_rb != null && _rb.gameObject.activeSelf != on) _rb.gameObject.SetActive(on);
        }

        // ---------------------------------------------------------------- direction cone and speed dial

        void UpdateIndicators(Vector3 center, Vector3 aimDir, Vector3 head, float scale, Transform hand, Quaternion gripRot)
        {
            EnsureIndicators();
            int level = Level;
            float t = level / (float)MaxLevel;
            Vector3 dir = LaunchDirection(aimDir);

            // cone in front of the marble, a little longer the faster
            float len = 0.014f + 0.016f * t;                          // real metres
            float gap = _radius + 0.004f * scale;                     // world
            Vector3 basePos = center + dir * gap;
            if (!_cone.gameObject.activeSelf) _cone.gameObject.SetActive(true);
            _cone.position = basePos;
            _cone.rotation = Quaternion.LookRotation(dir, Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? _rig.forward : Vector3.up);
            _cone.localScale = new Vector3(0.011f, 0.011f, len);

            // speed module set into the top of the controller: a ring of LEDs around a small 7-segment display
            const float size = 0.03f;                                 // real metres
            Vector3 up = gripRot * Vector3.up, fwd = gripRot * Vector3.forward;
            if (!_gauge.gameObject.activeSelf) _gauge.gameObject.SetActive(true);
            // the dial faces up out of the controller (its +Z points into it), its top towards the tip
            Quaternion rot = Quaternion.LookRotation(-up, fwd);
            if (DialOnModel(hand, up, rot, scale, out Vector3 onPos, out Quaternion onRot))
            {
                _gauge.position = onPos;
                _gauge.rotation = onRot;
            }
            else
            {
                _gauge.position = center - fwd * (0.06f * scale) + up * (0.03f * scale);
                _gauge.rotation = rot;
            }
            _gauge.localScale = Vector3.one * size;
            if (level != _shownLevel)
            {
                _shownLevel = level;
                _lcd.mainTexture = Icons.Get("lcd_" + level);
                for (int i = 0; i < _leds.Count; i++) _leds[i].color = i < level ? LedColor(i) : new Color(0.24f, 0.21f, 0.19f, 1f);
            }
        }

        // where the dial sits on the controller model, relative to the model (found once from its vertices)
        Transform _dialHand;
        Mesh _dialMesh;
        Vector3 _dialLocalPos;
        Quaternion _dialLocalRot;
        bool _dialKnown;

        /// <summary>
        /// Dial pose on the top of the controller model: above the middle of the model, lying on the highest part of
        /// the surface within the dial's footprint (a few millimetres above it). Computed once per model from its
        /// vertices and then kept relative to the model, so it moves rigidly with the controller.
        /// </summary>
        bool DialOnModel(Transform hand, Vector3 up, Quaternion rot, float scale, out Vector3 pos, out Quaternion worldRot)
        {
            pos = default; worldRot = rot;
            if (hand == null || !hand.gameObject.activeInHierarchy) return false;
            var mf = hand.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null) return false;
            if (!_dialKnown || _dialHand != hand || _dialMesh != mesh)
            {
                Vector3[] verts;
                try { verts = mesh.vertices; } catch { return false; }   // mesh not readable
                if (verts.Length == 0) return false;
                Vector3 c = hand.TransformPoint(mesh.bounds.center);
                float footprint = 0.016f * scale, top = float.MinValue;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 d = hand.TransformPoint(verts[i]) - c;
                    float h = Vector3.Dot(d, up);
                    if ((d - up * h).sqrMagnitude <= footprint * footprint && h > top) top = h;
                }
                if (top == float.MinValue) return false;
                Vector3 world = c + up * (top + 0.003f * scale);
                _dialLocalPos = hand.InverseTransformPoint(world);
                _dialLocalRot = Quaternion.Inverse(hand.rotation) * rot;
                _dialHand = hand;
                _dialMesh = mesh;
                _dialKnown = true;
            }
            pos = hand.TransformPoint(_dialLocalPos);
            worldRot = hand.rotation * _dialLocalRot;
            return true;
        }

        void HideIndicators()
        {
            if (_cone != null && _cone.gameObject.activeSelf) _cone.gameObject.SetActive(false);
            if (_gauge != null && _gauge.gameObject.activeSelf) _gauge.gameObject.SetActive(false);
        }

        void EnsureIndicators()
        {
            if (_cone != null) return;
            _cone = Pyramid("CozyVR Throw Direction", _rig, Unlit(new Color(1f, 0.6f, 0.15f, 0.95f)));

            // the speed module: a 1 x 1 unit root scaled to its size. Dark round bezel, six LEDs on an arc (slow = teal,
            // fast = red), the level as a 7-segment digit in the middle
            _gauge = new GameObject("CozyVR Speed Module") { layer = HudPanel.HudLayer }.transform;
            _gauge.SetParent(_rig, false);
            var bezel = Icons.Material("plate");
            bezel.color = new Color32(46, 40, 36, 255);
            bezel.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 20;
            HudPanel.MakeQuad("Bezel", bezel, _gauge).localScale = new Vector3(1.12f, 1.12f, 1f);
            for (int i = 0; i < MaxLevel; i++)
            {
                var m = Icons.Material("ui_round_rect");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 21;
                _leds.Add(m);
                float a = Mathf.Lerp(155f, 25f, i / (float)(MaxLevel - 1)) * Mathf.Deg2Rad;
                var led = HudPanel.MakeQuad("LED " + (i + 1), m, _gauge);
                led.localPosition = new Vector3(Mathf.Cos(a) * 0.4f, Mathf.Sin(a) * 0.4f - 0.06f, -0.002f);
                led.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f);
                led.localScale = new Vector3(0.17f, 0.085f, 1f);
            }
            _lcd = Icons.Material("lcd_0");
            _lcd.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 22;
            var lcd = HudPanel.MakeQuad("Display", _lcd, _gauge);
            lcd.localPosition = new Vector3(0f, -0.12f, -0.003f);
            lcd.localScale = new Vector3(0.33f, 0.44f, 1f);
            _cone.gameObject.SetActive(false);
            _gauge.gameObject.SetActive(false);
        }

        static Color LedColor(int i)
        {
            Color[] stops = { new Color32(64, 178, 174, 255), new Color32(120, 200, 110, 255), new Color32(250, 205, 70, 255), new Color32(240, 146, 58, 255), new Color32(226, 75, 74, 255) };
            float f = i / (float)(MaxLevel - 1) * (stops.Length - 1);
            int k = Mathf.Min((int)f, stops.Length - 2);
            return Color.Lerp(stops[k], stops[k + 1], f - k);
        }

        static Material Unlit(Color c)
        {
            var m = new Material(Shader.Find("Sprites/Default")) { color = c };
            m.renderQueue = (int)RenderQueue.Transparent + 10;
            return m;
        }

        /// <summary>Four-sided pyramid pointing along +Z (base 1 x 1 at z = 0, tip at z = 1), faces shaded for depth.</summary>
        static Transform Pyramid(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name) { layer = HudPanel.HudLayer };
            go.transform.SetParent(parent, false);
            Vector3 tip = new Vector3(0f, 0f, 1f);
            Vector3[] b = { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            float[] shade = { 0.7f, 1f, 0.85f, 0.95f };
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                int k = verts.Count;
                verts.Add(b[i]); verts.Add(b[(i + 1) % 4]); verts.Add(tip);
                for (int j = 0; j < 3; j++) cols.Add(new Color(shade[i], shade[i], shade[i], 1f));
                tris.Add(k); tris.Add(k + 2); tris.Add(k + 1);
            }
            int bi = verts.Count;
            verts.AddRange(b);
            for (int j = 0; j < 4; j++) cols.Add(new Color(0.55f, 0.55f, 0.55f, 1f));
            tris.AddRange(new[] { bi, bi + 1, bi + 2, bi, bi + 2, bi + 3 });
            var mesh = new Mesh { name = "CozyVR Pyramid" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }
    }
}
