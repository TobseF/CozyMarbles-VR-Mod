using System;
using TimR.CustomMarbles;
using TimR.Events;
using TimR.Marbles;
using UnityEngine;

namespace CozyMarblesVR
{
    /// <summary>
    /// Creates the game's own marbles for marble mode. A created marble is first only a waiting marble on the
    /// controller (see <see cref="MarbleLauncher"/>); once released it is announced like a marble from a spawner, so
    /// the game counts it, removes the oldest ones over its limit and clears it together with the others.
    /// </summary>
    public static class MarbleDropper
    {
        static CustomMarblesSO _marbles;
        static Transform _parent;

        /// <summary>A random marble, kinematic and not yet known to the game. Null if the game data is missing.</summary>
        public static Marble Create()
        {
            try
            {
                if (_marbles == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<CustomMarblesSO>();
                    if (all.Length > 0) _marbles = all[0];
                }
                if (_marbles == null || MarbleManager._ == null) return null;

                MarbleData data = _marbles.GetMarbleData(_marbles.GetRandomDefaultAllowedMarbleId());
                if (data == null) return null;
                Marble marble = _marbles.InstantiateMarble(data, data.Name);
                if (_parent == null)
                {
                    var p = GameObject.FindWithTag("MarblesParent");
                    if (p != null) _parent = p.transform;
                }
                if (_parent != null) marble.transform.SetParent(_parent, worldPositionStays: true);
                marble.Rotation = UnityEngine.Random.rotation;
                marble.Rigidbody.isKinematic = true;
                return marble;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Creating a marble failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Hands a released marble over to the game.</summary>
        public static void Announce(Marble marble)
        {
            try { EventBus._?.PublishEvent(Evt.MarbleSpawned, new EventData.MarbleSpawned(marble)); }
            catch (Exception e) { Plugin.Log.LogWarning("Announcing a marble failed: " + e.Message); }
        }

        /// <summary>Creates a marble and lets it go right away (fallback when no waiting marble is shown).</summary>
        public static bool Drop(Vector3 position, Vector3 velocity)
        {
            var marble = Create();
            if (marble == null) return false;
            marble.Position = position;
            var rb = marble.Rigidbody;
            rb.position = position;
            rb.isKinematic = false;
            rb.linearVelocity = velocity;
            rb.WakeUp();
            Announce(marble);
            return true;
        }
    }
}
