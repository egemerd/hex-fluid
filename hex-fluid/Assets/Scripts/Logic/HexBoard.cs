using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Runtime board. Reads a LevelData, spawns a model per tile, and runs
    /// the water simulation as turn-based flood-fill. Barricades live on
    /// edges, so blocking is symmetric automatically.
    ///
    /// Drop this on an empty GameObject, assign a LevelData, press Play.
    /// Call StepWater() each turn (from input, a button, or a tick).
    /// </summary>
    public class HexBoard : MonoBehaviour
    {
        [Header("Level")]
        public LevelData level;

        [Tooltip("Per-type models: each resource / role gets its own hex model. Optional.")]
        public TileModelSet modelSet;

        [Header("Optional visuals")]
        [Tooltip("Prefab shown on a flooded tile (e.g. a water plane). Optional.")]
        public GameObject waterOverlayPrefab;

        [Header("Spawn animation")]
        [Tooltip("Drop tiles in from above when the board builds (Play mode only).")]
        public bool dropIn = true;

        [Tooltip("How high above their final spot tiles start.")]
        public float dropHeight = 12f;

        [Tooltip("How long each tile takes to fall.")]
        public float fallDuration = 0.5f;

        [Tooltip("Max random delay before a tile starts falling (0 = all together).")]
        public float maxStagger = 0.6f;

        [Tooltip("Bounce on landing instead of a smooth stop.")]
        public bool dropBounce = true;

        [Tooltip("Played once as the dropping board lands.")]
        public SoundSO buildSound;

        [Tooltip("Extra seconds to shift the sound by (negative = earlier, e.g. if the clip has a slow start).")]
        public float buildSoundOffset = 0f;

        // coord -> spawned tile root
        private readonly Dictionary<HexCoord, GameObject> _tileObjects = new();
        // coord -> spawned water overlay (if any)
        private readonly Dictionary<HexCoord, GameObject> _waterObjects = new();
        // tiles currently holding water
        private readonly HashSet<HexCoord> _flooded = new();

        public bool Won { get; private set; }
        public bool Lost { get; private set; }

        public IReadOnlyCollection<HexCoord> FloodedTiles => _flooded;

        void Start()
        {
            if (level != null) Build();
        }

        /// <summary>Clears and rebuilds the whole board from the level asset.</summary>
        public void Build()
        {
            Clear();
            level.BuildLookups();

            if (dropIn && Application.isPlaying && buildSound != null)
                StartCoroutine(PlayWhenLanding(buildSound, LandingTime() + buildSoundOffset));

            foreach (var tile in level.tiles)
            {
                var prefab = ResolveModel(tile);
                GameObject go = prefab != null
                    ? Instantiate(prefab, transform)
                    : new GameObject($"Tile {tile.coord}");
                if (prefab == null) go.transform.SetParent(transform);

                go.name = $"Tile {tile.coord} [{tile.role}]";
                go.transform.localPosition = tile.coord.ToWorld(level.hexSize);
                _tileObjects[tile.coord] = go;

                // Drop it in from above with a random delay (Play mode only).
                if (dropIn && Application.isPlaying)
                {
                    Vector3 target = go.transform.localPosition;
                    var anim = go.AddComponent<TileDropAnimator>();
                    anim.Play(target, dropHeight, Random.Range(0f, maxStagger), fallDuration, dropBounce);
                }
            }

            // Seed water at every source.
            _flooded.Clear();
            foreach (var src in level.TilesWithRole(TileRole.Source))
            {
                _flooded.Add(src.coord);
                ShowWater(src.coord, true);
            }

            Won = Lost = false;
            EvaluateEndState();
        }

        /// <summary>
        /// Seconds after the drop starts until the board hits the table: the average
        /// tile's stagger plus its fall (with a bounce, the first touch down comes at
        /// about 36% of the fall).
        /// </summary>
        private float LandingTime()
        {
            float fall = Mathf.Max(0.01f, fallDuration) * (dropBounce ? 1f / 2.75f : 1f);
            return Mathf.Max(0f, maxStagger) * 0.5f + fall;
        }

        private IEnumerator PlayWhenLanding(SoundSO sound, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Sfx.Play(sound);
        }

        /// <summary>
        /// Picks the model for a tile. Priority:
        ///   1. explicit per-tile modelIndex override (if set)
        ///   2. the TileModelSet's resource / role model
        ///   3. the board's defaultTileModel
        /// </summary>
        private GameObject ResolveModel(HexTileData tile)
        {
            // 1. Explicit per-tile override wins (for one-off special tiles).
            if (tile.modelIndex >= 0 && tile.modelIndex < level.tileModels.Count
                && level.tileModels[tile.modelIndex] != null)
                return level.tileModels[tile.modelIndex];

            // 2. By type, from the shared model set.
            if (modelSet != null)
            {
                var byType = modelSet.GetModel(tile);
                if (byType != null) return byType;
            }

            // 3. Fallback.
            return level.defaultTileModel;
        }

        public void Clear()
        {
            foreach (var go in _tileObjects.Values) if (go) DestroySafe(go);
            foreach (var go in _waterObjects.Values) if (go) DestroySafe(go);
            _tileObjects.Clear();
            _waterObjects.Clear();
            _flooded.Clear();
        }

        /// <summary>
        /// Advance the water one turn. Expands waterStepsPerTurn rings outward.
        /// Water enters a neighbor only if the shared edge is Open and a tile
        /// exists there. Flooding a City loses; reaching a Goal can win.
        /// </summary>
        public void StepWater()
        {
            if (Won || Lost) return;

            for (int step = 0; step < Mathf.Max(1, level.waterStepsPerTurn); step++)
            {
                var frontier = new List<HexCoord>(_flooded);
                var newlyFlooded = new List<HexCoord>();

                foreach (var cur in frontier)
                {
                    for (int d = 0; d < 6; d++)
                    {
                        var n = cur.Neighbor(d);
                        if (!level.HasTile(n)) continue;          // off-board
                        if (_flooded.Contains(n)) continue;        // already wet
                        if (!level.IsEdgeOpen(cur, n)) continue;   // barricade/wall blocks
                        newlyFlooded.Add(n);
                    }
                }

                if (newlyFlooded.Count == 0) break; // water settled

                foreach (var c in newlyFlooded)
                {
                    _flooded.Add(c);
                    ShowWater(c, true);
                }
            }

            EvaluateEndState();
        }

        private void EvaluateEndState()
        {
            // Lose if any city is flooded.
            foreach (var city in level.TilesWithRole(TileRole.City))
                if (_flooded.Contains(city.coord)) { Lost = true; }

            // Win if every goal is flooded (and we haven't lost).
            bool allGoals = true;
            bool anyGoal = false;
            foreach (var goal in level.TilesWithRole(TileRole.Goal))
            {
                anyGoal = true;
                if (!_flooded.Contains(goal.coord)) allGoals = false;
            }
            if (!Lost && anyGoal && allGoals) Won = true;
        }

        // ---- Player actions ----

        /// <summary>Place a barricade on the edge between two tiles (consumes resources elsewhere).</summary>
        public void PlaceBarricade(HexCoord a, HexCoord b)
        {
            if (level.GetEdge(a, b) == EdgeState.Wall) return; // can't build on map walls
            level.SetEdge(a, b, EdgeState.Barricade);
        }

        public void RemoveBarricade(HexCoord a, HexCoord b)
        {
            if (level.GetEdge(a, b) == EdgeState.Barricade)
                level.SetEdge(a, b, EdgeState.Open);
        }

        // ---- Visuals ----

        private void ShowWater(HexCoord c, bool on)
        {
            if (waterOverlayPrefab == null) return;
            if (on)
            {
                if (_waterObjects.ContainsKey(c)) return;
                var w = Instantiate(waterOverlayPrefab, transform);
                w.transform.localPosition = c.ToWorld(level.hexSize, 0.05f);
                w.name = $"Water {c}";
                _waterObjects[c] = w;
            }
            else if (_waterObjects.TryGetValue(c, out var w))
            {
                DestroySafe(w);
                _waterObjects.Remove(c);
            }
        }

        public GameObject GetTileObject(HexCoord c) =>
            _tileObjects.TryGetValue(c, out var go) ? go : null;

        private static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}