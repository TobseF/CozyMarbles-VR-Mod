using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CozyMarblesVR
{
    /// <summary>The mod's own menu artwork (PNG files from src/Plugin/Icons, embedded by build.ps1, drawn by build/make-icons.py).</summary>
    public static class Icons
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            if (Cache.TryGetValue(name, out var tex) && tex != null) return tex;
            tex = null;
            try
            {
                using (var s = typeof(Icons).Assembly.GetManifestResourceStream("CozyMarblesVR.Icons." + name + ".png"))
                {
                    if (s != null)
                    {
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "CozyVR " + name };
                        ImageConversion.LoadImage(tex, ms.ToArray(), false);
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Trilinear;
                        tex.anisoLevel = 4;
                        Object.DontDestroyOnLoad(tex);
                    }
                    else Plugin.Log.LogWarning("Icon missing: " + name);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Icon " + name + " failed: " + e.Message); }
            Cache[name] = tex;
            return tex;
        }

        /// <summary>Unlit, alpha-blended material showing one icon; its colour tints it (hover).</summary>
        public static Material Material(string name)
        {
            var m = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
            m.mainTexture = Get(name);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 5;
            return m;
        }
    }
}
