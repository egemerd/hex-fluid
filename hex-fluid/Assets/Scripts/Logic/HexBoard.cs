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
    ///
    /// The board is the one owner of the current level: every other script reads
    /// it from Level and listens to LevelLoaded to reset itself. Switch levels at
    /// runtime with LoadLevel() (a LevelManager does this for you).
    /// </summary>
    public class HexBoard : MonoBehaviour
    {
        [Header("Level")]
        [Tooltip("Level built on Start. A LevelManager sets this for you; switch at runtime with LoadLevel().")]
        public LevelData level;

        [Tooltip("Per-type models: each resource / role gets its own hex model. Optional.")]
        public TileModelSet modelSet;

        [Header("Optional visuals")]
        [Tooltip("Prefab shown on a flooded tile (e.g. a water plane). Optional.")]
        public GameObject waterOverlayPrefab;

        [Tooltip("Build the level as soon as the game starts. A MainMenu turns this off and builds it when Play is clicked.")]
        public bool buildOnStart = true;

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

        [Header("Level change animation")]
        [Tooltip("When switching levels, fly the old tiles up and away before the new ones drop in (Play mode only).")]
        public bool riseOut = true;

        [Tooltip("How high the old tiles fly before they disappear.")]
        public float riseHeight = 12f;

        [Tooltip("How long each old tile takes to fly up.")]
        public float riseDuration = 0.45f;

        [Tooltip("Max random delay before an old tile starts rising (0 = all together).")]
        public float riseStagger = 0.5f;

        [Tooltip("Played once as the old board starts flying away.")]
        public SoundSO clearSound;

        [Header("Fit to table")]
        [Tooltip("Centre each level on the table area below, and shrink it evenly if it's too big to fit (Play mode only).")]
        public bool fitToTable = true;

        [Tooltip("Middle of the free space on the table (world X / Z). The board's height is kept. Select the board to see the area.")]
        public Vector2 tableAreaCenter = new Vector2(1f, -6f);

        [Tooltip("Size of the free space on the table (world X / Z).")]
        public Vector2 tableAreaSize = new Vector2(10f, 10f);

        [Tooltip("Largest size a level is shown at (1 = its normal size, so small levels aren't blown up).")]
        [Min(0.01f)] public float maxFitScale = 1f;

        // coord -> spawned tile root
        private readonly Dictionary<HexCoord, GameObject> _tileObjects = new();
        // coord -> spawned water overlay (if any)
        private readonly Dictionary<HexCoord, GameObject> _waterObjects = new();
        // tiles currently holding water
        private readonly HashSet<HexCoord> _flooded = new();

        public bool Won { get; private set; }
        public bool Lost { get; private set; }

        public IReadOnlyCollection<HexCoord> FloodedTiles => _flooded;

        /// <summary>
        /// The level currently on the board. In Play mode this is a copy of 'level',
        /// so barricades placed while playing never edit the asset. Null until built.
        /// </summary>
        public LevelData Level { get; private set; }

        /// <summary>Fires after the board is (re)built from a level. Scripts reset their per-level state here.</summary>
        public event System.Action<LevelData> LevelLoaded;

        /// <summary>Fires when the old level starts leaving (its tiles fly away). Level is already null.</summary>
        public event System.Action LevelUnloading;

        /// <summary>True while the old tiles are flying away and the next level hasn't been built yet.</summary>
        public bool IsChangingLevel { get; private set; }

        private bool _ownsLevel; // Level is a runtime copy we made (destroy it on unload)
        private readonly List<GameObject> _leaving = new(); // old tiles still flying away

        // The board's placement as set up in the scene; FitToTable() moves / scales from here.
        private Vector3 _homePosition, _homeScale;
        private bool _homeSaved;
        private float _fitScale = 1f;

        void Start()
        {
            if (buildOnStart && level != null && Level == null) Build();
        }

        /// <summary>
        /// Despawns the current level and builds the given one in its place. In Play
        /// mode the old tiles fly up first, then the new ones drop in. Calling it again
        /// mid-change just swaps which level gets built.
        /// </summary>
        public void LoadLevel(LevelData newLevel)
        {
            level = newLevel;
            if (IsChangingLevel) return;                 // the running change builds 'level' when it's done

            if (riseOut && Application.isPlaying && _tileObjects.Count > 0)
                StartCoroutine(ChangeLevel());
            else
                Build();
        }

        private IEnumerator ChangeLevel()
        {
            IsChangingLevel = true;
            float wait = RiseOut();
            LevelUnloading?.Invoke();
            if (wait > 0f) yield return new WaitForSeconds(wait);
            IsChangingLevel = false;
            Build();
        }

        /// <summary>
        /// Sends every tile flying up (they destroy themselves at the top) and unloads
        /// the level. Returns how long until the last one is gone.
        /// </summary>
        private float RiseOut()
        {
            Sfx.Play(clearSound);

            float longest = 0f;
            foreach (var go in _tileObjects.Values)
            {
                if (!go) continue;
                var drop = go.GetComponent<TileDropAnimator>();
                if (drop) DestroyImmediate(drop);        // still landing: take off from where it is

                float delay = Random.Range(0f, Mathf.Max(0f, riseStagger));
                go.AddComponent<TileRiseAnimator>().Play(riseHeight / _fitScale, delay, riseDuration);  // same height on screen at any fit
                longest = Mathf.Max(longest, delay + riseDuration);
                _leaving.Add(go);
            }
            _tileObjects.Clear();                        // they're on their way out; Clear() won't touch them
            Clear();
            return longest;
        }

        /// <summary>Clears and rebuilds the whole board from the level asset.</summary>
        public void Build()
        {
            foreach (var go in _leaving) if (go) DestroySafe(go);   // any old tile that hasn't flown off yet
            _leaving.Clear();
            Clear();
            if (level == null) return;

            _ownsLevel = Application.isPlaying;
            Level = _ownsLevel ? Instantiate(level) : level;
            Level.name = level.name;
            Level.BuildLookups();

            if (dropIn && Application.isPlaying && buildSound != null)
                StartCoroutine(PlayWhenLanding(buildSound, LandingTime() + buildSoundOffset));

            foreach (var tile in Level.tiles)
            {
                var prefab = ResolveModel(tile);
                GameObject go = prefab != null
                    ? Instantiate(prefab, transform)
                    : new GameObject($"Tile {tile.coord}");
                if (prefab == null) go.transform.SetParent(transform);

                go.name = $"Tile {tile.coord} [{tile.role}]";
                go.transform.localPosition = tile.coord.ToWorld(Level.hexSize);
                _tileObjects[tile.coord] = go;
            }

            // Centre (and if needed shrink) the board on the table, measured with the tiles at rest.
            if (Application.isPlaying) FitToTable();

            // Drop each tile in from above with a random delay (Play mode only).
            if (dropIn && Application.isPlaying)
            {
                foreach (var go in _tileObjects.Values)
                {
                    Vector3 target = go.transform.localPosition;
                    var anim = go.AddComponent<TileDropAnimator>();
                    anim.Play(target, dropHeight / _fitScale, Random.Range(0f, maxStagger), fallDuration, dropBounce);
                }
            }

            // Seed water at every source.
            _flooded.Clear();
            foreach (var src in Level.TilesWithRole(TileRole.Source))
            {
                _flooded.Add(src.coord);
                ShowWater(src.coord, true);
            }

            Won = Lost = false;
            EvaluateEndState();

            LevelLoaded?.Invoke(Level);
        }

        /// <summary>
        /// Moves the board so the level's tiles sit in the middle of the table area,
        /// and shrinks it (every tile evenly) when the level is too big to fit. Always
        /// starts from the board's scene placement, so each level is fitted on its own.
        /// Everything else on the board (water, items, picking) follows, since it all
        /// works in the board's own space.
        /// </summary>
        private void FitToTable()
        {
            if (!_homeSaved)
            {
                _homePosition = transform.position;
                _homeScale = transform.localScale;
                _homeSaved = true;
            }
            transform.position = _homePosition;
            transform.localScale = _homeScale;
            _fitScale = 1f;
            if (!fitToTable) return;

            // The tiles' footprint in board space: their models, not just the hex centres.
            bool any = false;
            var local = new Bounds();
            foreach (var go in _tileObjects.Values)
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    Bounds b = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                                 (i & 2) == 0 ? b.min.y : b.max.y,
                                                 (i & 4) == 0 ? b.min.z : b.max.z);
                        Vector3 p = transform.InverseTransformPoint(corner);
                        if (any) local.Encapsulate(p);
                        else { local = new Bounds(p, Vector3.zero); any = true; }
                    }
                }
            }
            if (!any) return;

            // Shrink to fit (never past maxFitScale).
            Vector3 size = Vector3.Scale(local.size, _homeScale);
            float fit = maxFitScale;
            if (size.x > 0f) fit = Mathf.Min(fit, tableAreaSize.x / size.x);
            if (size.z > 0f) fit = Mathf.Min(fit, tableAreaSize.y / size.z);
            _fitScale = fit;
            transform.localScale = _homeScale * fit;

            // Slide it so the footprint's middle lands on the area's middle; the height stays.
            Vector3 middle = transform.TransformPoint(local.center);
            transform.position += new Vector3(tableAreaCenter.x - middle.x, 0f, tableAreaCenter.y - middle.z);
        }

        private void OnDrawGizmosSelected()
        {
            if (!fitToTable) return;
            float y = _homeSaved ? _homePosition.y : transform.position.y;
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(tableAreaCenter.x, y, tableAreaCenter.y), new Vector3(tableAreaSize.x, 0f, tableAreaSize.y));
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
            if (tile.modelIndex >= 0 && tile.modelIndex < Level.tileModels.Count
                && Level.tileModels[tile.modelIndex] != null)
                return Level.tileModels[tile.modelIndex];

            // 2. By type, from the shared model set.
            if (modelSet != null)
            {
                var byType = modelSet.GetModel(tile);
                if (byType != null) return byType;
            }

            // 3. Fallback.
            return Level.defaultTileModel;
        }

        public void Clear()
        {
            foreach (var go in _tileObjects.Values) if (go) DestroySafe(go);
            foreach (var go in _waterObjects.Values) if (go) DestroySafe(go);
            _tileObjects.Clear();
            _waterObjects.Clear();
            _flooded.Clear();

            if (_ownsLevel && Level != null) DestroySafe(Level);
            Level = null;
            _ownsLevel = false;
        }

        /// <summary>
        /// Advance the water one turn. Expands waterStepsPerTurn rings outward.
        /// Water enters a neighbor only if the shared edge is Open and a tile
        /// exists there. Flooding a City loses; reaching a Goal can win.
        /// </summary>
        public void StepWater()
        {
            if (Won || Lost) return;

            for (int step = 0; step < Mathf.Max(1, Level.waterStepsPerTurn); step++)
            {
                var frontier = new List<HexCoord>(_flooded);
                var newlyFlooded = new List<HexCoord>();

                foreach (var cur in frontier)
                {
                    for (int d = 0; d < 6; d++)
                    {
                        var n = cur.Neighbor(d);
                        if (!Level.HasTile(n)) continue;          // off-board
                        if (_flooded.Contains(n)) continue;        // already wet
                        if (!Level.IsEdgeOpen(cur, n)) continue;   // barricade/wall blocks
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
            foreach (var city in Level.TilesWithRole(TileRole.City))
                if (_flooded.Contains(city.coord)) { Lost = true; }

            // Win if every goal is flooded (and we haven't lost).
            bool allGoals = true;
            bool anyGoal = false;
            foreach (var goal in Level.TilesWithRole(TileRole.Goal))
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
            if (Level.GetEdge(a, b) == EdgeState.Wall) return; // can't build on map walls
            Level.SetEdge(a, b, EdgeState.Barricade);
        }

        public void RemoveBarricade(HexCoord a, HexCoord b)
        {
            if (Level.GetEdge(a, b) == EdgeState.Barricade)
                Level.SetEdge(a, b, EdgeState.Open);
        }

        // ---- Visuals ----

        private void ShowWater(HexCoord c, bool on)
        {
            if (waterOverlayPrefab == null) return;
            if (on)
            {
                if (_waterObjects.ContainsKey(c)) return;
                var w = Instantiate(waterOverlayPrefab, transform);
                w.transform.localPosition = c.ToWorld(Level.hexSize, 0.05f);
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