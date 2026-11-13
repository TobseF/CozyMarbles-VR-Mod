using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace CozyMarblesVR
{
    /// <summary>
    /// Shows the real Steam Frame controller models (from the user's own SteamVR installation, not redistributed)
    /// instead of simple boxes. The models are Wavefront OBJ files in OpenVR's right-handed coordinate system,
    /// positioned like the controller's grip pose.
    /// </summary>
    public static class ControllerModels
    {
        static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static string _root;
        static bool _searched;

        /// <summary>...\SteamVR\drivers\frame_controller\resources\rendermodels, or null.</summary>
        static string FindRoot()
        {
            if (_searched) return _root;
            _searched = true;
            var candidates = new List<string>();
            try
            {
                // the active OpenXR runtime (SteamVR) lives in the SteamVR folder
                var rt = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime", null) as string;
                if (!string.IsNullOrEmpty(rt)) candidates.Add(Path.GetDirectoryName(rt));
            }
            catch { }
            try
            {
                var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(steam)) candidates.Add(Path.Combine(steam, "steamapps", "common", "SteamVR"));
            }
            catch { }
            candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\SteamVR");
            // the game's own library may hold SteamVR too
            candidates.Add(Path.Combine(Paths.GameRootPath, "..", "SteamVR"));

            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c)) continue;
                string dir = Path.Combine(c, "drivers", "frame_controller", "resources", "rendermodels");
                if (Directory.Exists(dir)) { _root = dir; break; }
            }
            if (_root == null) Plugin.Log.LogInfo("Steam Frame controller models not found in the SteamVR folder, using simple shapes.");
            return _root;
        }

        /// <summary>
        /// The models are authored around the controller's raw pose. OpenXR reports the grip pose, which is
        /// offset (values from the SteamVR render model json, converted to Unity's left-handed axes), so the model
        /// has to be moved by the inverse of that offset to sit where the real controller is.
        /// </summary>
        public static void ModelOffset(bool left, out Vector3 pos, out Quaternion rot)
        {
            var gripPos = new Vector3(left ? -0.003117f : 0.003117f, -0.004277f, -0.099501f);
            var gripRot = Quaternion.Euler(-2.8091f, 0f, 0f);
            rot = Quaternion.Inverse(gripRot);
            pos = -(rot * gripPos);
        }

        /// <summary>Replaces the placeholder shape of a hand with the real controller model. Returns true on success.</summary>
        public static bool TryApply(Transform hand, bool left)
        {
            try
            {
                string side = left ? "left" : "right";
                string root = FindRoot();
                if (root == null) return false;
                string dir = Path.Combine(root, "frame_controller_" + side);
                string obj = Path.Combine(dir, "frame_controller_" + side + ".obj");
                if (!File.Exists(obj)) return false;

                if (!Meshes.TryGetValue(side, out Mesh mesh))
                {
                    mesh = LoadObj(obj, "frame_controller_" + side);
                    Meshes[side] = mesh;
                }
                if (!Materials.TryGetValue(side, out Material mat))
                {
                    mat = MakeMaterial(dir, side);
                    Materials[side] = mat;
                }
                hand.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = hand.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                hand.localScale = Vector3.one;
                Plugin.Log.LogInfo($"Steam Frame controller model ({side}): {mesh.vertexCount} vertices from {dir}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Loading the controller model failed, using simple shapes: " + e.Message);
                return false;
            }
        }

        static Material MakeMaterial(string dir, string side)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            Texture2D color = LoadPng(Path.Combine(dir, "frame_controller_" + side + "_color.png"));
            Texture2D ao = LoadPng(Path.Combine(dir, "frame_controller_" + side + "_ao.png"));
            if (color != null)
            {
                // bake the ambient-occlusion map into the colour: the unlit shader has no lighting of its own
                if (ao != null && ao.width == color.width && ao.height == color.height)
                {
                    var c = color.GetPixels32();
                    var a = ao.GetPixels32();
                    for (int i = 0; i < c.Length; i++)
                    {
                        float k = 0.45f + 0.55f * (a[i].r / 255f);
                        c[i] = new Color32((byte)(c[i].r * k), (byte)(c[i].g * k), (byte)(c[i].b * k), 255);
                    }
                    color.SetPixels32(c);
                    color.Apply(true);
                }
                mat.mainTexture = color;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", color);
            }
            else if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.85f));
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 2f);
            return mat;
        }

        static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            return ImageConversion.LoadImage(tex, File.ReadAllBytes(path), false) ? tex : null;
        }

        /// <summary>Minimal OBJ reader: v / vt / vn / f (any polygon), OpenVR (right-handed) to Unity (left-handed).</summary>
        static Mesh LoadObj(string path, string name)
        {
            var pos = new List<Vector3>();
            var uv = new List<Vector2>();
            var nor = new List<Vector3>();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            var map = new Dictionary<long, int>();
            var face = new List<int>();
            var ic = CultureInfo.InvariantCulture;

            foreach (string raw in File.ReadLines(path))
            {
                if (raw.Length < 3) continue;
                if (raw[0] == 'v' && raw[1] == ' ')
                {
                    var t = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    pos.Add(new Vector3(float.Parse(t[1], ic), float.Parse(t[2], ic), -float.Parse(t[3], ic)));
                }
                else if (raw[0] == 'v' && raw[1] == 't')
                {
                    var t = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    uv.Add(new Vector2(float.Parse(t[1], ic), float.Parse(t[2], ic)));
                }
                else if (raw[0] == 'v' && raw[1] == 'n')
                {
                    var t = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    nor.Add(new Vector3(float.Parse(t[1], ic), float.Parse(t[2], ic), -float.Parse(t[3], ic)));
                }
                else if (raw[0] == 'f' && raw[1] == ' ')
                {
                    var t = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    face.Clear();
                    for (int i = 1; i < t.Length; i++)
                    {
                        string[] p = t[i].Split('/');
                        int vi = Index(p[0], pos.Count);
                        int ti = p.Length > 1 && p[1].Length > 0 ? Index(p[1], uv.Count) : -1;
                        int ni = p.Length > 2 && p[2].Length > 0 ? Index(p[2], nor.Count) : -1;
                        long key = ((long)vi << 42) ^ ((long)(ti + 1) << 21) ^ (long)(ni + 1);
                        if (!map.TryGetValue(key, out int idx))
                        {
                            idx = verts.Count;
                            map[key] = idx;
                            verts.Add(pos[vi]);
                            uvs.Add(ti >= 0 ? uv[ti] : Vector2.zero);
                            normals.Add(ni >= 0 ? nor[ni] : Vector3.up);
                        }
                        face.Add(idx);
                    }
                    // the z flip mirrors the model, so the winding order has to be reversed
                    for (int i = 1; i + 1 < face.Count; i++)
                    {
                        tris.Add(face[0]);
                        tris.Add(face[i + 1]);
                        tris.Add(face[i]);
                    }
                }
            }

            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Index(string s, int count)
        {
            int i = int.Parse(s, CultureInfo.InvariantCulture);
            return i > 0 ? i - 1 : count + i;
        }
    }
}
