using System.Collections.Generic;
using UnityEngine;

namespace CozyMarblesVR
{
    /// <summary>
    /// Grab mode: instead of the laser/mouse pointer, a small ball at the tip of the controller touches, selects and
    /// carries pieces. While a piece is carried it follows the hand freely (the game's own preview), and a transparent
    /// ghost shows the grid cell it would snap into on release.
    /// </summary>
    public class GrabMode
    {
        readonly Transform _rig;
        public bool Touching { get; private set; }
        public bool Holding { get; set; }
        public Vector3 Tip { get; private set; }

        public bool Active => Plugin.Cfg.GrabMode.Value;

        public GrabMode(Transform rig) { _rig = rig; }

        public void Toggle()
        {
            Plugin.Cfg.GrabMode.Value = !Plugin.Cfg.GrabMode.Value;
            Plugin.Log.LogInfo("Grab mode " + (Active ? "on" : "off"));
        }

        /// <summary>Position of the ball at the controller tip (world space), also while the laser is on a menu.</summary>
        public Vector3 UpdateTip(Ray aim)
        {
            float s = Mathf.Max(0.0001f, _rig.lossyScale.x);
            Tip = aim.origin + aim.direction * (0.03f * s);
            return Tip;
        }

        /// <summary>
        /// The ray handed to the game as "mouse ray". Placing a piece: a vertical ray onto the game's placement plane
        /// right below the ball (the plane height follows the hand). Otherwise: a short probe along the controller that
        /// only reaches pieces near the ball; elsewhere it points into the sky so clicks hit nothing.
        /// </summary>
        public Ray BuildRay(Ray aim, int mask, out Vector3 tip)
        {
            float s = Mathf.Max(0.0001f, _rig.lossyScale.x);
            tip = UpdateTip(aim);
            if (Game.Placing)
            {
                Touching = false;
                // decorations sit freely on the surface right below the ball (no grid, no placement plane)
                if (Game.PlacingDecoOnly)
                    return new Ray(tip + Vector3.up * (0.03f * s), Vector3.down);
                Game.TrackHeight(tip.y);
                if (Game.TryGetPlacementPlane(out float py))
                    return new Ray(new Vector3(tip.x, py + 5f, tip.z), Vector3.down);
            }
            Vector3 d = aim.direction;
            var probe = new Ray(tip - d * (0.03f * s), d);
            float reach = 0.03f * s + Plugin.Cfg.GrabReach.Value * s;
            Touching = Physics.Raycast(probe, out RaycastHit _, reach, mask, QueryTriggerInteraction.Collide);
            return Touching ? probe : new Ray(tip + Vector3.up * 1000f, Vector3.up);
        }

        // ------------------------------------------------------------------ ghost

        class Ghost
        {
            public Transform Root;
            public Transform Source;
            public int Key;
        }

        readonly List<Ghost> _ghosts = new List<Ghost>();
        readonly List<Transform> _roots = new List<Transform>();
        Material _ghostMat;

        /// <summary>Transparent copy of the carried piece at the snapped grid position.</summary>
        public void UpdateGhost()
        {
            if (!Active || !Plugin.Cfg.GrabGhost.Value || !Game.Placing || Game.InMenu) { HideGhosts(0); return; }
            Game.CollectPreviewRoots(_roots);
            if (_roots.Count == 0 || !Game.TryGetGhostDelta(out Vector3 delta)) { HideGhosts(0); return; }
            if (_ghostMat == null)
            {
                _ghostMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.45f, 0.85f, 1f, 0.35f) };
            }
            for (int i = 0; i < _roots.Count; i++)
            {
                if (_ghosts.Count <= i) _ghosts.Add(new Ghost());
                var g = _ghosts[i];
                Transform src = _roots[i];
                int key = src.GetInstanceID() ^ (src.GetComponentsInChildren<MeshFilter>().Length << 20);
                if (g.Root == null || g.Source != src || g.Key != key) Build(g, src, key);
                if (!g.Root.gameObject.activeSelf) g.Root.gameObject.SetActive(true);
                g.Root.SetPositionAndRotation(src.position + delta, src.rotation);
            }
            HideGhosts(_roots.Count);
        }

        void Build(Ghost g, Transform src, int key)
        {
            if (g.Root != null) Object.Destroy(g.Root.gameObject);
            var go = new GameObject("CozyVR Grab Ghost");
            g.Root = go.transform;
            g.Source = src;
            g.Key = key;
            // pieces carry several level-of-detail copies of the same mesh and helper quads (hit boxes, markers):
            // only the most detailed copy of the real geometry belongs in the ghost
            var skip = new HashSet<Renderer>();
            foreach (var lg in src.GetComponentsInChildren<LODGroup>())
            {
                var lods = lg.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers) if (r != null) skip.Add(r);
            }
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || !mr.enabled || !mf.gameObject.activeInHierarchy) continue;
                if (skip.Contains(mr)) continue;
                string n = mf.name.ToLowerInvariant();
                if (n.Contains("lod (") || n.Contains("hitbox") || n.Contains("collider") || n.Contains("trigger")) continue;
                bool helper = false;
                foreach (var pm in mr.sharedMaterials)
                    if (pm != null && (pm.shader.name.StartsWith("Shapes/"))) helper = true;
                if (helper) continue;
                if (false) Plugin.Log.LogInfo($"[ghost] part {mf.name} mesh={mf.sharedMesh.name} size={mr.bounds.size} mats={string.Join(",", System.Array.ConvertAll(mr.sharedMaterials, m => m ? m.name + "/" + m.shader.name : "-"))} layer={mf.gameObject.layer}");
                var child = new GameObject("part");
                child.transform.SetParent(go.transform, false);
                Matrix4x4 m = src.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                child.transform.localPosition = m.GetColumn(3);
                child.transform.localRotation = m.rotation;
                child.transform.localScale = m.lossyScale;
                child.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var cr = child.AddComponent<MeshRenderer>();
                var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMat;
                cr.sharedMaterials = mats;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cr.receiveShadows = false;
            }
        }

        void HideGhosts(int from)
        {
            for (int i = from; i < _ghosts.Count; i++)
                if (_ghosts[i].Root != null && _ghosts[i].Root.gameObject.activeSelf) _ghosts[i].Root.gameObject.SetActive(false);
        }

        public void Dispose() { HideGhosts(0); }
    }
}
