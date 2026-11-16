using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CozyMarblesVR
{
    /// <summary>
    /// Makes the controller models part of the game:
    ///  - every physical button carries a small coloured plate with an icon of what it does right now (it changes
    ///    with the tool, in menus and on the second layer) and sinks in a little while pressed;
    ///  - the off-hand controller wears a collar with round push buttons: the three tools, prepare / delete marbles,
    ///    "open menu" while the in-game menu is folded away and "object options" while something is selected.
    ///    They are pressed with the laser or by touching them with the ball of the other hand.
    /// Everything is a child of the controller model, so it moves rigidly with it.
    /// Colours: teal = building, red = deleting, amber = marbles, purple = menus, grey = moving.
    /// </summary>
    public class ControllerDecor
    {
        public static readonly Color Teal = new Color32(64, 178, 174, 255);
        public static readonly Color Red = new Color32(226, 75, 74, 255);
        public static readonly Color Amber = new Color32(239, 159, 39, 255);
        public static readonly Color Purple = new Color32(127, 119, 221, 255);
        public static readonly Color Grey = new Color32(136, 135, 128, 255);
        public static readonly Color Yellow = new Color32(250, 196, 45, 255);
        static readonly Color Cream = new Color32(238, 233, 221, 255);
        static readonly Color InkSoft = new Color32(110, 100, 95, 255);

        readonly VRInput _input;
        readonly HudPanel _hud;

        // ---------------------------------------------------------------- one plate on a button

        class Decal
        {
            public Transform Root, Press;
            public Material Plate, Glyph;
            public string GlyphName;
            public Vector3 Normal;          // model space
            public float Pressed;           // 0..1, animated
        }

        static Material Mat(string icon, int queueOffset)
        {
            var m = Icons.Material(icon);
            m.renderQueue = (int)RenderQueue.Transparent + 20 + queueOffset;
            return m;
        }

        static Decal MakeDecal(Transform parent, string name, Vector3 pos, Vector3 normal, Vector3 up, float size)
        {
            var d = new Decal { Normal = normal };
            d.Root = new GameObject(name) { layer = HudPanel.HudLayer }.transform;
            d.Root.SetParent(parent, false);
            d.Root.localPosition = pos + normal * 0.0006f;
            // the plate faces out of the button (its +Z points into it), its top towards "up"
            d.Root.localRotation = Quaternion.LookRotation(-normal, Vector3.ProjectOnPlane(up, normal).normalized);
            d.Press = new GameObject("Press") { layer = HudPanel.HudLayer }.transform;
            d.Press.SetParent(d.Root, false);
            d.Plate = Mat("plate", 0);
            var plate = HudPanel.MakeQuad("Plate", d.Plate, d.Press);
            plate.localScale = new Vector3(size, size, 1f);
            d.Glyph = Mat("g_click", 1);
            d.GlyphName = "click";
            var glyph = HudPanel.MakeQuad("Glyph", d.Glyph, d.Press);
            glyph.localScale = new Vector3(size * 0.64f, size * 0.64f, 1f);
            glyph.localPosition = new Vector3(0f, 0f, -0.0003f);
            return d;
        }

        static void SetDecal(Decal d, string glyph, Color color, bool held, float dt)
        {
            bool show = glyph != null;
            if (d.Root.gameObject.activeSelf != show) d.Root.gameObject.SetActive(show);
            if (!show) return;
            if (glyph != d.GlyphName) { d.GlyphName = glyph; d.Glyph.mainTexture = Icons.Get("g_" + glyph); }
            d.Pressed = Mathf.MoveTowards(d.Pressed, held ? 1f : 0f, dt * 12f);
            d.Plate.color = Color.Lerp(color, Color.white, 0.25f * d.Pressed);
            // pressed: the plate sinks in with the button
            d.Press.localPosition = new Vector3(0f, 0f, 0.0012f * d.Pressed);
        }

        // ---------------------------------------------------------------- one controller

        class Side
        {
            public bool Right;
            public Transform Hand;
            public bool Built;
            public readonly Dictionary<VRInput.Part, Decal> Decals = new Dictionary<VRInput.Part, Decal>();
            public Decal StickTop;
            public Vector3 FaceCenter, HeadCenter, N, F, R;   // model space
            public float FaceRadius;
        }

        readonly Side _left = new Side { Right = false }, _right = new Side { Right = true };

        public ControllerDecor(VRInput input, HudPanel hud)
        {
            _input = input;
            _hud = hud;
        }

        static string PartFile(bool right, VRInput.Part p)
        {
            switch (p)
            {
                case VRInput.Part.Trigger: return "trigger";
                case VRInput.Part.Grip: return "grip_button";
                case VRInput.Part.Bumper: return "bumper";
                case VRInput.Part.Stick: return "thumbstick";
                case VRInput.Part.A: return "a_button";
                case VRInput.Part.B: return "b_button";
                case VRInput.Part.X: return "x_button";
                case VRInput.Part.Y: return "y_button";
                case VRInput.Part.Menu: return right ? "menu_button" : "view_button";
                default: return "dpad";
            }
        }

        const float PlateSize = 0.0097f, DpadOffset = 0.0086f;

        /// <summary>Dark grey plus below the four D-pad labels, so they read as one D-pad.</summary>
        static void DpadCross(Side s, Vector3 surface)
        {
            var m = Mat("dpad_cross", -1);
            m.color = new Color32(44, 44, 46, 255);
            var root = new GameObject("D-pad cross") { layer = HudPanel.HudLayer }.transform;
            root.SetParent(s.Hand, false);
            root.localPosition = surface + s.N * 0.0003f;
            root.localRotation = Quaternion.LookRotation(-s.N, s.F);
            var q = HudPanel.MakeQuad("Cross", m, root);
            q.localScale = new Vector3(DpadOffset * 2f + PlateSize * 1.15f, DpadOffset * 2f + PlateSize * 1.15f, 1f);
        }

        bool Build(Side s)
        {
            var parts = ControllerModels.Parts(!s.Right);
            if (parts.Count == 0 || s.Hand == null) return false;

            // the top face: the plane of the face buttons (right) or of the D-pad (left)
            Vector3 n = Vector3.zero, c = Vector3.zero;
            int k = 0;
            foreach (var name in s.Right ? new[] { "a_button", "b_button", "x_button", "y_button" } : new[] { "dpad" })
                if (parts.TryGetValue(name, out var g)) { n += g.Normal; c += g.Center; k++; }
            if (k == 0) return false;
            s.N = n.normalized;
            if (Vector3.Dot(s.N, Vector3.up) < 0f) s.N = -s.N;
            s.FaceCenter = c / k;
            s.F = Vector3.ProjectOnPlane(Vector3.forward, s.N).normalized;
            s.R = Vector3.Cross(s.N, s.F);
            s.FaceCenter += s.N * (parts.TryGetValue(s.Right ? "a_button" : "dpad", out var top) ? top.Top(s.N) : 0f);

            // the round head of the controller: middle and radius of its top rim (bounding box of the upper body
            // in the face plane). The collar is centred on it, not on the buttons.
            s.HeadCenter = s.FaceCenter;
            float radius = 0.03f;
            if (parts.TryGetValue("body", out var body))
            {
                float f0 = float.MaxValue, f1 = float.MinValue, r0 = float.MaxValue, r1 = float.MinValue;
                foreach (var v in body.Vertices)
                {
                    Vector3 d = v - s.FaceCenter;
                    if (Vector3.Dot(d, s.N) < -0.007f) continue;   // only the head's top, not the handle below
                    float df = Vector3.Dot(d, s.F), dr = Vector3.Dot(d, s.R);
                    f0 = Mathf.Min(f0, df); f1 = Mathf.Max(f1, df); r0 = Mathf.Min(r0, dr); r1 = Mathf.Max(r1, dr);
                }
                if (f1 > f0)
                {
                    s.HeadCenter = s.FaceCenter + s.F * ((f0 + f1) * 0.5f) + s.R * ((r0 + r1) * 0.5f);
                    radius = Mathf.Max(f1 - f0, r1 - r0) * 0.5f;
                }
            }
            s.FaceRadius = Mathf.Clamp(radius, 0.02f, 0.045f);

            // plates on the buttons
            foreach (VRInput.Part p in Enum.GetValues(typeof(VRInput.Part)))
            {
                bool rightOnly = p == VRInput.Part.A || p == VRInput.Part.B || p == VRInput.Part.X || p == VRInput.Part.Y;
                bool dpad = p >= VRInput.Part.DpadUp;
                if ((rightOnly && !s.Right) || (dpad && s.Right)) continue;
                if (!parts.TryGetValue(PartFile(s.Right, p), out var g)) continue;
                Vector3 surface = g.Center + s.N * g.Top(s.N);
                switch (p)
                {
                    case VRInput.Part.DpadUp:
                        DpadCross(s, surface);
                        s.Decals[p] = MakeDecal(s.Hand, "Label D-pad up", surface + s.F * DpadOffset, s.N, s.F, PlateSize); break;
                    case VRInput.Part.DpadDown: s.Decals[p] = MakeDecal(s.Hand, "Label D-pad down", surface - s.F * DpadOffset, s.N, s.F, PlateSize); break;
                    case VRInput.Part.DpadLeft: s.Decals[p] = MakeDecal(s.Hand, "Label D-pad left", surface - s.R * DpadOffset, s.N, s.F, PlateSize); break;
                    case VRInput.Part.DpadRight: s.Decals[p] = MakeDecal(s.Hand, "Label D-pad right", surface + s.R * DpadOffset, s.N, s.F, PlateSize); break;
                    case VRInput.Part.Stick:
                    {
                        Vector3 sn = g.Normal;
                        s.Decals[p] = MakeDecal(s.Hand, "Label stick", g.Center + sn * g.Top(sn), sn, s.F, 0.012f);
                        break;
                    }
                    case VRInput.Part.Menu: s.Decals[p] = MakeDecal(s.Hand, "Label menu", g.Center + s.N * g.Top(s.N), s.N, s.F, PlateSize); break;
                    case VRInput.Part.Trigger:
                    case VRInput.Part.Bumper:
                    {
                        // little tags in front of the trigger and the bumper, tilted up towards the player
                        float side = p == VRInput.Part.Bumper ? 1f : -1f;
                        Vector3 nn = (s.F * 0.75f + s.N * 0.65f).normalized;
                        Vector3 pos = g.Center + s.F * (g.Top(s.F) + 0.004f) + s.R * (side * 0.008f) + s.N * 0.004f;
                        s.Decals[p] = MakeDecal(s.Hand, "Label " + p, pos, nn, s.N, PlateSize);
                        break;
                    }
                    case VRInput.Part.Grip:
                    {
                        Vector3 outward = Vector3.Dot(g.Center - s.FaceCenter, s.R) >= 0f ? s.R : -s.R;
                        Vector3 nn = (outward + s.N * 0.5f).normalized;
                        s.Decals[p] = MakeDecal(s.Hand, "Label grip", g.Center + outward * (g.Top(outward) + 0.002f), nn, s.F, PlateSize);
                        break;
                    }
                    default: s.Decals[p] = MakeDecal(s.Hand, "Label " + p, surface, s.N, s.F, PlateSize); break;
                }
            }
            s.Built = true;
            Plugin.Log.LogInfo($"Controller labels ({(s.Right ? "right" : "left")}): {s.Decals.Count}, head radius {s.FaceRadius * 100f:0.0} cm, head centre {(s.HeadCenter - s.FaceCenter) * 100f} cm from the buttons");
            return true;
        }

        // ---------------------------------------------------------------- labels

        static readonly Dictionary<VRAction, (string glyph, Color color)> Glyphs = new Dictionary<VRAction, (string, Color)>
        {
            { VRAction.LeftClick, ("click", Teal) }, { VRAction.RightClick, ("back", Purple) }, { VRAction.MiddleClick, ("click", Grey) },
            { VRAction.Rotate, ("rotate", Teal) }, { VRAction.Shift, ("shift", Teal) }, { VRAction.BuildMenu, ("toolbox", Teal) },
            { VRAction.ToggleSnap, ("magnet", Teal) }, { VRAction.MagicDrawVertical, ("magic", Teal) }, { VRAction.Delete, ("trash", Red) },
            { VRAction.HeightUp, ("up", Teal) }, { VRAction.HeightDown, ("down", Teal) }, { VRAction.Undo, ("undo", Purple) },
            { VRAction.Redo, ("redo", Purple) }, { VRAction.Cut, ("cut", Teal) }, { VRAction.Copy, ("copy", Teal) },
            { VRAction.Paste, ("paste", Teal) }, { VRAction.Escape, ("menu", Purple) }, { VRAction.RollMarbles, ("play", Amber) },
            { VRAction.GrabWorld, ("world", Grey) }, { VRAction.Recenter, ("recenter", Grey) }, { VRAction.AltLayer, ("layers", Grey) },
            { VRAction.MenuBack, ("back", Purple) }, { VRAction.MenuPanel, ("toolbox", Purple) }, { VRAction.CycleTool, ("cycle", Purple) },
            { VRAction.MenuCarry, ("carry", Purple) }, { VRAction.ToggleIngameMenu, ("toolbox", Teal) },
            { VRAction.VRSettings, ("gear", Purple) }, { VRAction.ResetMenu, ("menu_center", Grey) },
        };

        static readonly string[] ToolGlyph = { "click", "hand", "marble" };

        void Label(Side s, VRInput.Part p, Decal d, float dt)
        {
            VRAction a = _input.PartAction(s.Right, p);
            bool held = _input.PartHeld(s.Right, p);
            string glyph = null;
            Color color = Grey;
            bool pointerSide = s.Right == (Plugin.Cfg.PointerHand.Value == Hand.Right);
            bool menus = _input.MenuContext;

            if (p >= VRInput.Part.DpadUp && menus)
            {
                // in menus the D-pad moves the focus cursor
                glyph = p == VRInput.Part.DpadUp ? "up" : p == VRInput.Part.DpadDown ? "down" : p == VRInput.Part.DpadLeft ? "left" : "right";
                color = Purple;
            }
            else if (p == VRInput.Part.Stick)
            {
                // the stick itself moves (left) or turns (right); its click is shown while it works as second layer
                glyph = pointerSide ? "turn" : (_input.AltHeld ? "layers" : "fly");
                color = _input.AltHeld && !pointerSide ? Purple : Grey;
            }
            else if (_input.Grab.MarbleActive && !menus && !_input.AltHeld && (p == VRInput.Part.A || p == VRInput.Part.Y))
            {
                glyph = p == VRInput.Part.A ? "plus" : "minus";
                color = Amber;
            }
            else if (menus && p == VRInput.Part.Trigger && pointerSide)
            {
                // in menus: on the menu it clicks, pointing into empty space it puts you back at the start
                glyph = _input.OverUi ? "click" : "recenter";
                color = _input.OverUi ? Purple : Grey;
            }
            else if (a == VRAction.LeftClick && p == VRInput.Part.Trigger && pointerSide)
            {
                int tool = _input.Grab.Tool;
                glyph = ToolGlyph[tool];
                color = tool == 2 ? Amber : Teal;
            }
            else if (a == VRAction.LeftClick && menus)
            {
                glyph = "check";
                color = Purple;
            }
            else if (a == VRAction.MenuCarry && _input.WorldGrabbed)
            {
                glyph = "world";
                color = Grey;
            }
            else if (Glyphs.TryGetValue(a, out var g)) { glyph = g.glyph; color = g.color; }
            SetDecal(d, glyph, color, held, dt);
        }

        // ---------------------------------------------------------------- the collar with push buttons

        class RingButton
        {
            public string Glyph;
            public Color Color;
            public Action Click;
            public Func<bool> Visible, Lit;
            public float Angle;                 // degrees from "forward" towards the inner side
            public Transform Root, Body, Top;
            public Material BodyMat, PlateMat, GlyphMat;
            public float Shown, Pressed, Poke;
            public bool Armed = true;
        }

        readonly List<RingButton> _ring = new List<RingButton>();
        Side _ringSide;
        Transform _collar;
        int _hover = -1;
        const float ButtonRadius = 0.0098f, ButtonHeight = 0.0075f;

        void BuildRing(Side s)
        {
            _ringSide = s;
            var gm = _input.Grab;
            void Add(string glyph, Color color, Action click, Func<bool> visible = null, Func<bool> lit = null)
                => _ring.Add(new RingButton { Glyph = glyph, Color = color, Click = click, Visible = visible, Lit = lit });
            Add("pointer", Teal, () => gm.SetTool(0), null, () => gm.Tool == 0);
            Add("hand", Teal, () => gm.SetTool(1), null, () => gm.Tool == 1);
            Add("marble", Amber, () => gm.SetTool(2), null, () => gm.Tool == 2);
            Add("play", Amber, PrepareMarbles);
            Add("trash_marbles", Red, DeleteMarbles);
            Add("toolbox", Teal, _hud.ToggleIngameMenu, null, () => _hud.BuildShown);
            Add("flag", Teal, () => _hud.ClickTile("racing"), null, () => _hud.TileToggle("racing") ?? true);
            Add("camera", Grey, () => _hud.ClickTile("camreset"));
            Add("manual", Purple, ToggleHelp, null, () => VRManager.Instance != null && VRManager.Instance.Help.Open);
            Add("props", Yellow, _hud.ToggleOptions, () => _hud.HasSelection);
            // tools | marbles | build menu, racing, camera, help | object options
            float[] angles = { -6f, 21f, 48f, 84f, 113f, 148f, 175f, 202f, 229f, 262f };

            float inner = s.Right ? -1f : 1f;   // the side that faces the other hand
            float ringR = s.FaceRadius + ButtonRadius + 0.004f;
            float baseH = -0.0045f;
            for (int i = 0; i < _ring.Count; i++)
            {
                var b = _ring[i];
                b.Angle = angles[i];
                float a = b.Angle * Mathf.Deg2Rad;
                Vector3 dir = s.F * Mathf.Cos(a) + s.R * (inner * Mathf.Sin(a));
                b.Root = new GameObject("Ring button " + b.Glyph) { layer = HudPanel.HudLayer }.transform;
                b.Root.SetParent(s.Hand, false);
                b.Root.localPosition = s.HeadCenter + dir * ringR + s.N * baseH;
                b.Root.localRotation = Quaternion.LookRotation(s.F, s.N);   // local +Y = out of the controller's face

                b.BodyMat = Solid(Color.white);
                b.Body = Child("Body", b.Root, Cylinder(), b.BodyMat);
                b.Top = new GameObject("Top") { layer = HudPanel.HudLayer }.transform;
                b.Top.SetParent(b.Root, false);
                b.PlateMat = Mat("plate", 2);
                var plate = HudPanel.MakeQuad("Plate", b.PlateMat, b.Top);
                plate.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lying flat, facing +Y
                plate.localScale = new Vector3(ButtonRadius * 2.05f, ButtonRadius * 2.05f, 1f);
                b.GlyphMat = Mat("g_" + b.Glyph, 3);
                var glyph = HudPanel.MakeQuad("Glyph", b.GlyphMat, b.Top);
                glyph.localRotation = Quaternion.Euler(90f, 0f, 0f);
                glyph.localPosition = new Vector3(0f, 0.0003f, 0f);
                glyph.localScale = new Vector3(ButtonRadius * 1.3f, ButtonRadius * 1.3f, 1f);
            }

            // the collar the buttons sit on, hugging the controller's head
            _collar = Child("Collar", s.Hand, Collar(s.FaceRadius - 0.003f, ringR + ButtonRadius + 0.0035f, angles[0] - 16f, angles[angles.Length - 1] + 16f, 0.004f), Solid(Cream));
            _collar.localPosition = s.HeadCenter + s.N * (baseH - 0.0005f);
            _collar.localRotation = Quaternion.LookRotation(s.F, s.N);
            if (inner < 0f) _collar.localScale = new Vector3(-1f, 1f, 1f);
            // a common raised base under the three tools: they belong together, only one is active
            var baseMat = Solid(new Color32(120, 196, 192, 255));    // soft teal: the tool group
            var toolBase = Child("Tool base", _collar, Collar(ringR - ButtonRadius - 0.003f, ringR + ButtonRadius + 0.003f, angles[0] - 15f, angles[2] + 15f, 0.0035f), baseMat);
            toolBase.localPosition = Vector3.up * 0.0034f;
        }

        /// <summary>
        /// Where the build menu is mounted on the off-hand controller, in tracking space and computed from the pose
        /// itself (the model transforms are only moved later in the frame): just beyond the collar at the controller's
        /// head, with the controller's forward and face-normal directions. False without controller models.
        /// </summary>
        public bool TryGetMount(HandState hand, out Vector3 pos, out Vector3 forward, out Vector3 normal)
        {
            pos = forward = normal = default;
            var s = _ringSide;
            if (s == null || !s.Built || hand == null || !hand.Valid) return false;
            ControllerModels.ModelOffset(!s.Right, out Vector3 op, out Quaternion orr);
            Quaternion r = hand.GripRot * orr;
            Vector3 origin = hand.GripPos + hand.GripRot * op;
            float outer = s.FaceRadius + ButtonRadius * 2f + 0.012f;      // past the round buttons
            pos = origin + r * (s.HeadCenter + s.F * outer + s.N * 0.004f);
            forward = (r * s.F).normalized;
            normal = (r * s.N).normalized;
            return true;
        }

        static Texture2D _ramp;

        /// <summary>
        /// Opaque material for the collar, the tool base and the button bodies: they write depth like every other
        /// object, so the depth buffer sorts them (transparent ones were sorted by distance per object, and the collar
        /// sometimes covered the buttons on its far side). The shading of the meshes sits in their UV.x and is read
        /// from a grey ramp, since the pipeline's unlit shader ignores vertex colours.
        /// </summary>
        static Material Solid(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return Vertex(c);
            if (_ramp == null)
            {
                _ramp = new Texture2D(256, 1, TextureFormat.RGBA32, false, true) { name = "CozyVR Shade Ramp", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[256];
                for (int i = 0; i < 256; i++) px[i] = new Color32((byte)i, (byte)i, (byte)i, 255);
                _ramp.SetPixels32(px);
                _ramp.Apply(false, true);
            }
            var m = new Material(sh);
            m.SetTexture("_BaseMap", _ramp);
            m.mainTexture = _ramp;
            m.color = c;                                  // _BaseColor
            m.SetFloat("_Cull", (float)CullMode.Off);     // the collar is an open shell, seen from both sides
            m.renderQueue = (int)RenderQueue.Geometry;
            return m;
        }

        static Material Vertex(Color c)
        {
            var m = new Material(Shader.Find("Sprites/Default")) { color = c };
            m.renderQueue = (int)RenderQueue.Transparent + 18;
            return m;
        }

        static Transform Child(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name) { layer = HudPanel.HudLayer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        static Mesh _cylinder;

        /// <summary>The vertex shade as a position on the grey ramp of <see cref="Solid"/>.</summary>
        static List<Vector2> ShadeUv(List<Color> colors)
        {
            var uv = new List<Vector2>(colors.Count);
            foreach (var col in colors) uv.Add(new Vector2(Mathf.Clamp(col.r, 0.002f, 0.998f), 0.5f));
            return uv;
        }

        /// <summary>Unit button body: radius 1, height 1 along +Y, side darker than the rim for a bit of depth.</summary>
        static Mesh Cylinder()
        {
            if (_cylinder != null) return _cylinder;
            const int n = 24;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float x = Mathf.Cos(a), z = Mathf.Sin(a);
                float shade = 0.62f + 0.18f * Mathf.Max(0f, -x);   // lit from one side
                v.Add(new Vector3(x, 0f, z)); c.Add(new Color(shade * 0.85f, shade * 0.85f, shade * 0.85f, 1f));
                v.Add(new Vector3(x, 1f, z)); c.Add(new Color(shade, shade, shade, 1f));
            }
            for (int i = 0; i < n; i++)
            {
                int a0 = i * 2;
                t.AddRange(new[] { a0, a0 + 1, a0 + 2, a0 + 1, a0 + 3, a0 + 2 });
            }
            _cylinder = new Mesh { name = "CozyVR Button" };
            _cylinder.SetVertices(v); _cylinder.SetColors(c); _cylinder.SetUVs(0, ShadeUv(c)); _cylinder.SetTriangles(t, 0);
            _cylinder.RecalculateBounds();
            return _cylinder;
        }

        /// <summary>Flat ring sector in the XZ plane (angle 0 = +Z, growing towards +X) with a short outer wall.</summary>
        static Mesh Collar(float r0, float r1, float a0, float a1, float wall)
        {
            const int n = 40;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(d * r0); c.Add(new Color(0.86f, 0.84f, 0.8f, 1f));
                v.Add(d * r1); c.Add(Color.white);
                v.Add(d * r1 - Vector3.up * wall); c.Add(new Color(0.72f, 0.68f, 0.62f, 1f));
            }
            for (int i = 0; i < n; i++)
            {
                int k = i * 3, m = k + 3;
                t.AddRange(new[] { k, m, k + 1, k + 1, m, m + 1 });          // top
                t.AddRange(new[] { k + 1, m + 1, k + 2, k + 2, m + 1, m + 2 }); // outer wall
            }
            var mesh = new Mesh { name = "CozyVR Collar" };
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, ShadeUv(c)); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void TickRing(float dt, Vector3 tipWorld, bool poke, bool visible)
        {
            if (_collar == null) return;
            if (_collar.gameObject.activeSelf != visible) _collar.gameObject.SetActive(visible);
            float scale = Mathf.Max(1e-4f, _ringSide.Hand.lossyScale.x);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            for (int i = 0; i < _ring.Count; i++)
            {
                var b = _ring[i];
                bool want = visible && (b.Visible == null || b.Visible());
                b.Shown = Mathf.MoveTowards(b.Shown, want ? 1f : 0f, dt * 6f);
                bool on = b.Shown > 0.01f;
                if (b.Root.gameObject.activeSelf != on) b.Root.gameObject.SetActive(on);
                if (!on) continue;

                // poking it with the ball of the other hand presses it like a real button
                // only from above onto the button face counts, so brushing past it sideways does nothing
                Vector3 top = b.Root.TransformPoint(Vector3.up * ButtonHeight);
                Vector3 d = (tipWorld - top) / scale;
                float above = Vector3.Dot(d, b.Root.up), lateral = (d - b.Root.up * above).magnitude;
                bool touching = poke && lateral < ButtonRadius && above < 0.003f && above > -ButtonHeight;
                bool near = poke && lateral < ButtonRadius + 0.01f && above < 0.015f && above > -ButtonHeight - 0.01f;
                if (b.Armed && touching) { b.Armed = false; Plugin.Log.LogInfo($"Controller button (touch): {b.Glyph} lateral={lateral * 100f:0.0} cm above={above * 100f:0.0} cm"); Activate(i); }
                if (!b.Armed && !near) b.Armed = true;

                b.Pressed = Mathf.MoveTowards(b.Pressed, 0f, dt * 6f);
                bool lit = b.Lit == null || b.Lit();
                bool hover = i == _hover || !b.Armed;
                Color plate = lit ? b.Color : Cream;
                if (b.Glyph == "menu_open") plate = Color.Lerp(b.Color, Color.white, 0.35f * pulse);   // asks for attention
                if (hover) plate = Color.Lerp(plate, Color.white, 0.3f);
                b.PlateMat.color = plate;
                b.GlyphMat.color = lit ? Color.white : InkSoft;
                b.BodyMat.color = Color.Lerp(lit ? b.Color : Cream, Color.black, 0.18f);
                float h = ButtonHeight * (1f - 0.45f * b.Pressed);
                b.Body.localScale = new Vector3(ButtonRadius, h, ButtonRadius);
                b.Top.localPosition = Vector3.up * (h + 0.0002f);
                b.Root.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, b.Shown);
            }
        }

        /// <summary>Laser test against the tops of the round buttons. Returns the button index or -1.</summary>
        public int HitRing(Ray ray, out Vector3 hit, out float dist)
        {
            hit = default; dist = float.MaxValue;
            int best = -1;
            if (_collar == null || !_collar.gameObject.activeInHierarchy) return -1;
            float scale = Mathf.Max(1e-4f, _ringSide.Hand.lossyScale.x);
            float bestMiss = float.MaxValue;
            for (int i = 0; i < _ring.Count; i++)
            {
                var b = _ring[i];
                if (!b.Root.gameObject.activeInHierarchy || b.Shown < 0.9f) continue;
                // a sphere around the whole button, not just its flat top: the laser of the other hand usually
                // comes in at a slant from the side and slipped past the top disc
                Vector3 top = b.Root.TransformPoint(Vector3.up * ButtonHeight);
                Vector3 center = b.Root.TransformPoint(Vector3.up * (ButtonHeight * 0.5f));
                float r = ButtonRadius * 1.45f * scale;
                float along = Vector3.Dot(center - ray.origin, ray.direction);
                if (along <= 0f) continue;
                float miss = (ray.GetPoint(along) - center).magnitude;
                if (miss > r) continue;
                float t = along - Mathf.Sqrt(Mathf.Max(0f, r * r - miss * miss));   // where the ray enters the sphere
                // the dot sits on the button's top where the ray crosses it, otherwise at the entry point
                var plane = new Plane(b.Root.up, top);
                Vector3 p = plane.Raycast(ray, out float tp) && tp > 0f && (ray.GetPoint(tp) - top).magnitude <= ButtonRadius * 1.1f * scale
                    ? ray.GetPoint(tp) : ray.GetPoint(Mathf.Max(0f, t));
                // neighbouring spheres overlap: the button whose centre the laser passes closest wins
                if (miss < bestMiss) { bestMiss = miss; dist = t; hit = p; best = i; }
            }
            return best;
        }

        public void SetHover(int index) { _hover = index; }

        public void Activate(int index)
        {
            if (index < 0 || index >= _ring.Count) return;
            var b = _ring[index];
            b.Pressed = 1f;
            if (Plugin.Cfg.Diagnostics.Value) Plugin.Log.LogInfo("Controller button: " + b.Glyph);
            try { b.Click(); }
            catch (Exception e) { Plugin.Log.LogWarning("Controller button failed: " + e.Message); }
        }

        static void ToggleHelp()
        {
            var vm = VRManager.Instance;
            if (vm == null) return;
            if (vm.Help.Open) vm.Help.Close();
            else vm.Help.Show(vm.Tracked.HeadPos, vm.Tracked.HeadRot);
        }

        static readonly System.Reflection.FieldInfo RollField = AccessTools.Field(typeof(TimR.UI.RollMarbleUi), "_rollMarbleButton");
        static readonly System.Reflection.FieldInfo DestroyField = AccessTools.Field(typeof(TimR.GameJamMarbles.DestroyAllMarbles), "_button");

        /// <summary>Same as the game's "Prepare marbles / Roll marbles" button.</summary>
        static void PrepareMarbles()
        {
            var ui = UnityEngine.Object.FindFirstObjectByType<TimR.UI.RollMarbleUi>();
            (ui != null ? RollField?.GetValue(ui) as Button : null)?.onClick.Invoke();
        }

        /// <summary>Same as the game's "delete all marbles" button next to it.</summary>
        static void DeleteMarbles()
        {
            var d = UnityEngine.Object.FindFirstObjectByType<TimR.GameJamMarbles.DestroyAllMarbles>();
            (d != null ? DestroyField?.GetValue(d) as Button : null)?.onClick.Invoke();
        }

        // ---------------------------------------------------------------- per frame

        /// <param name="left">left controller model (null when only simple shapes are shown)</param>
        /// <param name="tipWorld">ball of the pointer hand (pokes the ring buttons in hand / marble mode)</param>
        public void Tick(Transform left, Transform right, bool active, bool inMenu, Vector3 tipWorld, bool poke, float dt)
        {
            bool pointerRight = Plugin.Cfg.PointerHand.Value == Hand.Right;
            foreach (var s in new[] { _left, _right })
            {
                var hand = s.Right ? right : left;
                if (hand != s.Hand) { s.Hand = hand; s.Built = false; }
                if (!active || s.Hand == null) continue;
                if (!s.Built && !Build(s)) { s.Hand = null; continue; }
                if (_ringSide == null && s.Right != pointerRight) BuildRing(s);
                foreach (var kv in s.Decals) Label(s, kv.Key, kv.Value, dt);
            }
            if (_ringSide != null) TickRing(dt, tipWorld, poke && !inMenu, active && !inMenu && _ringSide.Hand != null && _ringSide.Hand.gameObject.activeInHierarchy);
        }

        public string DebugState()
        {
            string ring = _ringSide == null ? "-" : string.Join(",", _ring.ConvertAll(b => b.Glyph + (b.Shown > 0.5f ? "+" : "-") + ((b.Lit == null || b.Lit()) ? "*" : "")));
            return $"decor: left={_left.Built}({_left.Decals.Count}) right={_right.Built}({_right.Decals.Count}) ring={ring} hover={_hover}";
        }

        /// <summary>World position of a ring button's top (diagnostics), by glyph name.</summary>
        public Vector3 DebugRingPoint(string glyph)
        {
            foreach (var b in _ring) if (b.Glyph == glyph) return b.Root.TransformPoint(Vector3.up * ButtonHeight);
            return Vector3.zero;
        }
    }
}
