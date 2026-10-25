using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The water flow. Starts at Source tiles and spreads DOWNWARD only.
    ///
    /// Rules (all enforced here):
    ///   - Water starts at every Source tile.
    ///   - It only enters a neighbor that is lower down the board (the flow direction).
    ///   - What an item does to an edge comes from the item itself (GetEdgeFlow):
    ///     Block stops it, Force funnels it across even if not downhill. A Force edge
    ///     only pushes water AWAY from the side it arrived on, never back over itself.
    ///     A fixed map wall always blocks (see ItemPlacementSystem).
    ///   - It can't enter a resource tile.
    ///   - Entering a City (house) = LOSE, straight away.
    ///   - Water keeps spreading until no tile can take more. Then: if it reached every
    ///     Goal tile (and no city got flooded) = WIN, otherwise = LOSE (goal not reached).
    ///     A level with no Goal tile wins just by keeping the cities dry.
    ///
    /// How it plays: StartSimulation() works the whole flood out up front with the
    /// rules above (which tile gets water on which step, and from where), then plays
    /// it back on one continuous clock, WaterTime, measured in tiles. The water
    /// front sweeps smoothly across the board like a baked fluid simulation instead
    /// of jumping one ring of tiles at a time. LOSE fires when the visible water
    /// reaches a city; WIN (or LOSE, goal not reached) when it has settled.
    ///
    /// Look: every tile the water will reach gets a FloodedTile, which sinks it just
    /// ahead of the front. The water is one continuous 3D surface (WaterBody) over the
    /// whole flood; its shader reveals it per pixel from the shared clock, so the
    /// flood reads as one body.
    ///
    /// Stage: the flood moves through FloodStage (Cloudburst -> Flooding ->
    /// Contained / CityFlooded) and announces each change on StageChanged. A
    /// FloodStageBadge in the screen corner shows it; one is created automatically
    /// if the scene has none.
    ///
    /// Setup:
    ///   - Put this on your board object (or any object) and assign 'board' (your HexBoard).
    ///   - Call StartSimulation() to begin — wire your Start button's onClick to it.
    ///     (Turn on 'autoStart' to test without the button.)
    /// </summary>
    /// <summary>Where the flood is at, for HUD / sound / anything that wants to react.</summary>
    public enum FloodStage
    {
        Calm,           // nothing yet (the board is still landing)
        Cloudburst,     // the source has burst: water is welling up, waiting for Start
        Flooding,       // the water is running across the board
        Contained,      // reached the goal and settled without reaching a city: WIN
        CityFlooded,    // the water reached a city: LOSE
        GoalMissed,     // settled without ever reaching the goal: LOSE
    }

    /// <summary>Why a level was lost.</summary>
    public enum LoseReason
    {
        None,
        CityFlooded,    // the water reached a city
        GoalNotReached, // the water settled without reaching every goal tile
    }

    public class WaterSimulation : MonoBehaviour
    {
        /// <summary>Shader global holding WaterTime, read by FlowingWater.shader.</summary>
        public static readonly int WaterTimeId = Shader.PropertyToID("_HexWaterTime");

        /// <summary>Arrival time used for sources: wet long before the clock starts.</summary>
        public const float AlwaysWet = -10f;

        [Header("References")]
        [Tooltip("Your HexBoard — provides the level, hexSize, and coordinate space.")]
        public HexBoard board;

        [Tooltip("Placed items (barricades/bridges). Auto-found if empty. Optional.")]
        public ItemPlacementSystem items;

        [Header("Stage badge")]
        [Tooltip("Show the flood stage (Cloudburst, Flooding commenced...) as a wobbling sticker in a screen corner. Created automatically if the scene has no FloodStageBadge.")]
        public bool showStageBadge = true;

        [Header("Per-tile indicator (old)")]
        [Tooltip("Also mark each tile the water reaches with the prefab / blue plane below. Off by default: the stage badge replaces it.")]
        public bool showTileIndicators = false;

        [Tooltip("Per-tile indicator prefab. Empty = a generated blue plane.")]
        public GameObject waterPlanePrefab;

        [Tooltip("How high above the tiles the per-tile indicator sits.")]
        public float waterYOffset = 2f;

        public Color waterColor = new Color(0.2f, 0.5f, 1f, 1f);

        [Tooltip("Blue plane size as a multiple of hexSize.")]
        public float planeSizeFactor = 1.8f;

        [Header("Flooded tiles")]
        [Tooltip("Sink flooded tiles and show flowing water on them.")]
        public bool floodVisuals = true;

        [Tooltip("Flowing water material. Empty = Resources/FlowingWater.")]
        public Material flowMaterial;

        [Tooltip("How far a flooded tile sinks (board units). More = deeper, bluer water over it.")]
        public float sinkDepth = 0.22f;

        [Tooltip("Seconds a tile takes to sink. It finishes just as the water front reaches it.")]
        [Min(0.01f)] public float sinkDuration = 0.4f;

        [Tooltip("ON = measure the tile model and put the water just below the top of the unsunk tiles around it. OFF = use Flow Surface Height.")]
        public bool autoSurfaceHeight = true;

        [Tooltip("Auto mode: how far below the unsunk tile tops the water surface sits.")]
        public float waterBelowGround = 0.04f;

        [Tooltip("Manual mode: height of the water above a sunk tile's pivot (board units).")]
        public float flowSurfaceHeight = 0.12f;

        [Tooltip("Show source tiles already flooded (sunk, with water) as soon as the board lands.")]
        public bool floodSourcesOnLoad = true;

        [Header("Flow")]
        [Tooltip("Seconds the water front takes to advance one tile.")]
        [Min(0.05f)] public float stepInterval = 1.2f;

        [Tooltip("Local direction that counts as 'down' the board. Default -Z. Flip it if your source ends up at the bottom.")]
        public Vector3 flowDirection = new Vector3(0f, 0f, -1f);

        [Tooltip("Start as soon as each level is built (for testing). Normally the Start button calls StartSimulation().")]
        public bool autoStart = false;

        public bool Running { get; private set; }
        public bool Won { get; private set; }
        public bool Lost { get; private set; }

        /// <summary>Why the level was lost (None while not lost).</summary>
        public LoseReason LostBecause { get; private set; }

        /// <summary>How many goal tiles the visible water has reached / the level has.</summary>
        public int GoalsReached { get; private set; }
        public int GoalCount { get; private set; }

        /// <summary>The playback clock, in tiles: water reaches a tile's centre at its arrival step.</summary>
        public float WaterTime { get; private set; }

        /// <summary>Where the flood is at right now.</summary>
        public FloodStage Stage { get; private set; } = FloodStage.Calm;

        /// <summary>Fires once when the player wins.</summary>
        public event Action WonEvent;
        /// <summary>Fires once when the player loses.</summary>
        public event Action LostEvent;
        /// <summary>Fires whenever the flood moves to a new stage.</summary>
        public event Action<FloodStage> StageChanged;

        private LevelData Level => board != null ? board.Level : null;
        private Transform Root => board != null ? board.transform : transform;
        private float HexSize => Level != null ? Level.hexSize : 1f;

        // The planned flood: every tile the water will reach, and on which step.
        private readonly HashSet<HexCoord> _flooded = new HashSet<HexCoord>();
        private readonly Dictionary<HexCoord, int> _arrival = new Dictionary<HexCoord, int>();
        // For each flooded tile, the neighbours its water came in from.
        private readonly Dictionary<HexCoord, HashSet<HexCoord>> _cameFrom = new Dictionary<HexCoord, HashSet<HexCoord>>();
        // Tiles the visible water has actually reached so far.
        private readonly HashSet<HexCoord> _reached = new HashSet<HexCoord>();

        private readonly Dictionary<HexCoord, GameObject> _waterViews = new Dictionary<HexCoord, GameObject>();
        private readonly Dictionary<HexCoord, FloodedTile> _tileViews = new Dictionary<HexCoord, FloodedTile>();
        private readonly Dictionary<Mesh, float> _meshTop = new Dictionary<Mesh, float>();
        private Transform _waterRoot;
        private WaterBody _waterBody;
        private Coroutine _run;
        private Coroutine _sourcesRun;
        private float _endTime;      // the result is decided here
        private float _visualEnd;    // the water has fully settled here (a little later)

        private void OnEnable()
        {
            if (board == null) return;
            board.LevelLoaded += OnLevelLoaded;
            board.LevelUnloading += ResetFlood;
        }

        private void OnDisable()
        {
            if (board == null) return;
            board.LevelLoaded -= OnLevelLoaded;
            board.LevelUnloading -= ResetFlood;
        }

        private void Start()
        {
            SetWaterTime(0f);
            if (showStageBadge && FindFirstObjectByType<FloodStageBadge>() == null)
                new GameObject("FloodStageBadge").AddComponent<FloodStageBadge>().simulation = this;
        }

        /// <summary>
        /// A new level was built (the first one, or the next after a win): throw away
        /// the old flood and start over on the fresh board.
        /// </summary>
        private void OnLevelLoaded(LevelData level)
        {
            ResetFlood();
            if (autoStart) StartSimulation();
            else if (floodSourcesOnLoad) _sourcesRun = StartCoroutine(FloodSourcesWhenBoardReady());
        }

        /// <summary>
        /// Waits for HexBoard to spawn the source tiles AND for them to finish dropping
        /// in, then shows them flooded, so the water never hangs over an empty table.
        /// </summary>
        private IEnumerator FloodSourcesWhenBoardReady()
        {
            if (Level == null || board == null) yield break;

            float giveUp = Time.time + 10f;
            while (!Running && !SourcesLanded() && Time.time < giveUp) yield return null;
            if (Running || !SourcesLanded()) yield break;       // the simulation got there first

            ClearWater();
            Level.BuildLookups();
            SeedSources();
            CreateWaterRoot();
            SpawnPlannedViews();
            SetStage(FloodStage.Cloudburst);
        }

        /// <summary>Every source tile exists and has finished its drop-in animation.</summary>
        private bool SourcesLanded()
        {
            foreach (var t in Level.tiles)
            {
                if (t.role != TileRole.Source) continue;
                var tile = board.GetTileObject(t.coord);
                if (tile == null || tile.GetComponent<TileDropAnimator>() != null) return false;
            }
            return true;
        }

        private void CreateWaterRoot()
        {
            _waterRoot = new GameObject("WaterSim").transform;
            _waterRoot.SetParent(Root, false);
        }

        // =====================================================================
        // Control
        // =====================================================================

        /// <summary>Stops and removes the current flood (the level is leaving, or a new one just arrived).</summary>
        private void ResetFlood()
        {
            StopSimulation();
            if (_sourcesRun != null) StopCoroutine(_sourcesRun);
            _sourcesRun = null;
            ClearWater();
            ResetResult();
            SetWaterTime(0f);
            SetStage(FloodStage.Calm);
        }

        private void ResetResult()
        {
            Won = Lost = false;
            LostBecause = LoseReason.None;
            GoalsReached = 0;
            GoalCount = 0;
            if (Level != null)
                foreach (var t in Level.tiles)
                    if (t.role == TileRole.Goal) GoalCount++;
        }

        /// <summary>Begin the water flow from the start. Hook your Start button to this.</summary>
        public void StartSimulation()
        {
            if (board != null && board.IsChangingLevel) return;  // between levels: nothing to flood yet
            if (Level == null)
            {
                Debug.LogError("WaterSimulation: assign a HexBoard that has a LevelData.", this);
                return;
            }

            StopSimulation();
            ClearWater();
            Level.BuildLookups();
            if (items == null) items = FindFirstObjectByType<ItemPlacementSystem>();
            ResetResult();

            int lastStep = PlanFlood();
            _endTime = lastStep + 0.5f;                          // the last tiles are full

            CreateWaterRoot();
            SpawnPlannedViews();
            // Keep the clock running past the result until the far corners are full and the foam has faded.
            _visualEnd = Mathf.Max(_endTime, (_waterBody != null ? _waterBody.MaxArrival : _endTime) + 1.5f);

            SetWaterTime(0f);
            Running = true;
            SetStage(FloodStage.Flooding);
            RevealReached();                                     // sources (a source on a city loses here)
            _run = StartCoroutine(RunFlow());
        }

        public void StopSimulation()
        {
            if (_run != null) StopCoroutine(_run);
            _run = null;
            Running = false;
        }

        /// <summary>Plays the planned flood back on one smooth clock.</summary>
        private IEnumerator RunFlow()
        {
            bool decided = false;
            while (true)
            {
                if (!decided && WaterTime >= _endTime)
                {
                    decided = true;
                    if (!Lost)                                   // settled and no city flooded...
                    {
                        if (GoalsReached >= GoalCount) Win();    // ...and the water got to every goal
                        else Lose(LoseReason.GoalNotReached);
                    }
                }
                if (WaterTime >= _visualEnd) break;

                yield return null;
                SetWaterTime(Mathf.Min(_visualEnd, WaterTime + Time.deltaTime / Mathf.Max(0.05f, stepInterval)));
                RevealReached();
            }

            Running = false;
            _run = null;
        }

        private void SetWaterTime(float t)
        {
            WaterTime = t;
            Shader.SetGlobalFloat(WaterTimeId, t);
        }

        /// <summary>
        /// A tile counts as reached once the front crosses into it (half a tile before
        /// its centre). That's when its indicator appears, a city loses and a goal counts.
        /// </summary>
        private void RevealReached()
        {
            foreach (var kv in _arrival)
            {
                if (_reached.Contains(kv.Key) || WaterTime < kv.Value - 0.5f) continue;
                _reached.Add(kv.Key);
                if (showTileIndicators) _waterViews[kv.Key] = SpawnWater(kv.Key);

                TileRole role = Level.GetTile(kv.Key).role;
                if (role == TileRole.Goal) GoalsReached++;
                if (!Lost && role == TileRole.City) Lose(LoseReason.CityFlooded);
            }
        }

        // =====================================================================
        // Flow rules (worked out up front)
        // =====================================================================

        private void SeedSources()
        {
            _flooded.Clear();
            _arrival.Clear();
            _cameFrom.Clear();
            _reached.Clear();
            foreach (var t in Level.tiles)
                if (t.role == TileRole.Source) { _flooded.Add(t.coord); _arrival[t.coord] = 0; }
        }

        /// <summary>
        /// Runs the rules step by step until the water settles or reaches a city, and
        /// records which step each tile gets water on. Returns the last step.
        /// </summary>
        private int PlanFlood()
        {
            SeedSources();
            if (AnyCityFlooded()) return 0;                      // a source sits on a city

            int step = 0;
            while (true)
            {
                var next = SpreadOneStep();
                if (next.Count == 0) break;                      // settled
                step++;

                bool cityHit = false;
                foreach (var n in next)
                {
                    _flooded.Add(n);
                    _arrival[n] = step;
                    if (Level.GetTile(n).role == TileRole.City) cityHit = true;
                }
                if (cityHit) break;                              // the water stops at the city
            }
            return step;
        }

        /// <summary>Every tile the current water may enter on the next step.</summary>
        private HashSet<HexCoord> SpreadOneStep()
        {
            var next = new HashSet<HexCoord>();

            foreach (var cur in _flooded)
            {
                // First, does this tile have any bridge (Force) edges? If so, the
                // water is funnelled: it goes ONLY through the bridge(s), and the
                // tile's other directions are cut off.
                List<HexCoord> bridged = null;
                for (int d = 0; d < 6; d++)
                {
                    var n = cur.Neighbor(d);
                    if (FlowAcross(cur, n) == EdgeFlow.Force)
                    {
                        if (bridged == null) bridged = new List<HexCoord>();
                        bridged.Add(n);
                    }
                }

                if (bridged != null)
                {
                    // Funnel: only the bridged edges, and they ignore the downhill rule.
                    foreach (var n in bridged)
                        Consider(cur, n, next, ignoreDownhill: true);
                }
                else
                {
                    // Normal spread: every downhill neighbour that isn't barricaded.
                    for (int d = 0; d < 6; d++)
                    {
                        var n = cur.Neighbor(d);
                        if (FlowAcross(cur, n) == EdgeFlow.Block) continue;
                        Consider(cur, n, next, ignoreDownhill: false);
                    }
                }
            }
            return next;
        }

        /// <summary>Adds 'n' to the flood set for this step if water may enter it from 'cur'.</summary>
        private void Consider(HexCoord cur, HexCoord n, HashSet<HexCoord> next, bool ignoreDownhill)
        {
            if (_flooded.Contains(n)) return;
            if (!Level.HasTile(n)) return;                           // off-board
            if (!Level.IsEdgeOpen(cur, n)) return;                   // fixed map wall
            if (!ignoreDownhill && !IsDownhill(cur, n)) return;      // normal downhill rule

            var td = Level.GetTile(n);
            if (td == null) return;
            if (td.resource != ResourceType.None) return;            // water can't enter resource tiles

            next.Add(n);
            if (!_cameFrom.TryGetValue(n, out var from)) _cameFrom[n] = from = new HashSet<HexCoord>();
            from.Add(cur);                                           // remember where n's water came from
        }

        /// <summary>
        /// What the item on the edge cur->n does to water leaving cur. The edge the water
        /// came in through is ignored, so a bridge carries water across once and the
        /// tile on the far side then flows on normally instead of pushing it back.
        /// </summary>
        private EdgeFlow FlowAcross(HexCoord cur, HexCoord n)
        {
            if (items == null) return EdgeFlow.Default;
            if (_cameFrom.TryGetValue(cur, out var from) && from.Contains(n)) return EdgeFlow.Default;
            return items.GetEdgeFlow(cur, n);
        }

        /// <summary>True if 'to' is further along the flow direction than 'from'.</summary>
        private bool IsDownhill(HexCoord from, HexCoord to)
        {
            Vector3 a = from.ToWorld(HexSize);
            Vector3 b = to.ToWorld(HexSize);
            Vector3 dir = flowDirection.sqrMagnitude > 0.0001f ? flowDirection.normalized : Vector3.back;
            return Vector3.Dot(b - a, dir) > 0.01f * HexSize;
        }

        private bool AnyCityFlooded()
        {
            foreach (var c in _flooded)
            {
                var td = Level.GetTile(c);
                if (td != null && td.role == TileRole.City) return true;
            }
            return false;
        }

        // =====================================================================
        // Visuals
        // =====================================================================

        /// <summary>
        /// Sets up the whole run's look: a FloodedTile on every tile in the plan (sinks
        /// it just ahead of the front) and one WaterBody surface over all of them.
        /// </summary>
        private void SpawnPlannedViews()
        {
            if (!floodVisuals) return;
            Material mat = FlowMaterial();
            if (mat == null) return;

            var flows = new List<WaterBody.TileFlow>();
            float level = float.NaN;
            float sinkLength = sinkDuration / Mathf.Max(0.05f, stepInterval);

            foreach (var c in _flooded)
            {
                bool source = !_cameFrom.ContainsKey(c);
                int arrival = _arrival[c];
                flows.Add(new WaterBody.TileFlow
                {
                    coord = c,
                    arrival = arrival,
                    parents = source ? new HexCoord[0] : new List<HexCoord>(_cameFrom[c]).ToArray(),
                    flow = FlowOf(c),
                });

                GameObject tile = board != null ? board.GetTileObject(c) : null;
                if (tile == null) continue;

                float restY = c.ToWorld(HexSize).y;
                if (float.IsNaN(level)) level = SurfaceLevelFor(tile, restY);

                var old = tile.GetComponent<FloodedTile>();
                if (old != null) DestroyImmediate(old);           // left over from a previous run

                float sinkEnd = source ? AlwaysWet : arrival - 0.35f;
                var view = tile.AddComponent<FloodedTile>();
                view.Init(this, restY, sinkDepth, sinkEnd - sinkLength, sinkEnd);
                _tileViews[c] = view;
            }
            if (float.IsNaN(level)) return;

            var go = new GameObject("WaterBody");
            go.transform.SetParent(_waterRoot, false);
            go.transform.localPosition = new Vector3(0f, level, 0f);   // one level everywhere
            _waterBody = go.AddComponent<WaterBody>();
            var boardTiles = new HashSet<HexCoord>();               // banks only where water meets dry tiles, not the board's outside
            foreach (var t in Level.tiles) boardTiles.Add(t.coord);
            _waterBody.Build(mat, HexSize, flows, boardTiles);
        }

        /// <summary>
        /// Points a tile's water downstream: towards the tiles it feeds, or onward from
        /// where it came in, or the board's flow direction for a lone source.
        /// </summary>
        private Vector2 FlowOf(HexCoord cur)
        {
            Vector3 sum = Vector3.zero;
            for (int d = 0; d < 6; d++)
            {
                var n = cur.Neighbor(d);
                if (_cameFrom.TryGetValue(n, out var from) && from.Contains(cur))
                    sum += n.ToWorld(HexSize) - cur.ToWorld(HexSize);
            }

            Vector2 dir = new Vector2(sum.x, sum.z);
            if (dir.sqrMagnitude < 1e-6f && _cameFrom.TryGetValue(cur, out var inflow))
            {
                foreach (var f in inflow) sum += cur.ToWorld(HexSize) - f.ToWorld(HexSize);
                dir = new Vector2(sum.x, sum.z);
            }
            if (dir.sqrMagnitude < 1e-6f)
            {
                Vector3 d = flowDirection.sqrMagnitude > 0.0001f ? flowDirection : Vector3.back;
                dir = new Vector2(d.x, d.z);
            }
            return dir.normalized;
        }

        /// <summary>Board-space height of the water surface over a tile resting at restY.</summary>
        private float SurfaceLevelFor(GameObject tile, float restY)
        {
            float manual = restY - sinkDepth + flowSurfaceHeight;
            if (!autoSurfaceHeight) return manual;
            float top = TileTopAbovePivot(tile);
            if (float.IsNaN(top) || float.IsInfinity(top) || Mathf.Abs(top) > 100f) return manual; // bad measurement
            return restY + top - waterBelowGround;              // just under the dry tiles' tops
        }

        /// <summary>
        /// How far the top of the tile's base hex is above its pivot, in board units.
        /// Uses the tile's own mesh (the first one), so houses / trees on it don't count.
        /// </summary>
        private float TileTopAbovePivot(GameObject tile)
        {
            var mf = tile.GetComponent<MeshFilter>();
            if (mf == null) mf = tile.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return float.NaN;

            if (!_meshTop.TryGetValue(mf.sharedMesh, out float top))
            {
                // Mesh bounds work even when the model isn't Read/Write enabled (vertices don't, at runtime).
                Bounds b = mf.sharedMesh.bounds;
                top = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    top = Mathf.Max(top, Root.InverseTransformPoint(mf.transform.TransformPoint(corner)).y - tile.transform.localPosition.y);
                }
                _meshTop[mf.sharedMesh] = top;
            }
            return top;
        }

        private Material FlowMaterial()
        {
            if (flowMaterial == null) flowMaterial = Resources.Load<Material>("FlowingWater");
            if (flowMaterial == null && floodVisuals)
            {
                floodVisuals = false;
                Debug.LogWarning("WaterSimulation: no flowing water material. Assign 'flowMaterial' or keep Resources/FlowingWater.mat.", this);
            }
            return flowMaterial;
        }

        /// <summary>The old per-tile indicator above a tile the water reached: the prefab, else a blue plane.</summary>
        private GameObject SpawnWater(HexCoord c)
        {
            GameObject go = waterPlanePrefab != null ? Instantiate(waterPlanePrefab, _waterRoot) : MakeBluePlane(_waterRoot);
            go.name = $"Water {c}";
            go.transform.localPosition = c.ToWorld(HexSize, waterYOffset);
            go.transform.localRotation = Quaternion.identity;
            return go;
        }

        private GameObject MakeBluePlane(Transform parent)
        {
            // Unity's Plane primitive lies flat (faces up) and is 10x10 units.
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var col = go.GetComponent<Collider>();
            if (col != null) DestroySafe(col);

            go.transform.SetParent(parent, false);
            float s = (HexSize * planeSizeFactor) / 10f;
            go.transform.localScale = new Vector3(s, 1f, s);

            go.GetComponent<MeshRenderer>().sharedMaterial = new Material(BorrowBaseMaterial()) { color = waterColor };
            return go;
        }

        private static Material BorrowBaseMaterial()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var mat = tmp.GetComponent<MeshRenderer>().sharedMaterial;
            DestroySafe(tmp);
            return mat;
        }

        public void ClearWater()
        {
            foreach (var w in _waterViews.Values) if (w) DestroySafe(w);
            _waterViews.Clear();
            foreach (var v in _tileViews.Values) if (v) v.Restore();
            _tileViews.Clear();
            _flooded.Clear();
            _arrival.Clear();
            _cameFrom.Clear();
            _reached.Clear();
            if (_waterRoot) DestroySafe(_waterRoot.gameObject);  // takes the WaterBody with it
            _waterRoot = null;
            _waterBody = null;
        }

        private void SetStage(FloodStage stage)
        {
            if (stage == Stage) return;
            Stage = stage;
            StageChanged?.Invoke(stage);
        }

        private void Win()
        {
            Won = true;
            Debug.Log("WIN — the water reached the goal and no city was flooded.", this);
            SetStage(FloodStage.Contained);
            WonEvent?.Invoke();
        }

        /// <summary>The water keeps flowing to the end of the plan; the result is already decided.</summary>
        private void Lose(LoseReason reason)
        {
            Lost = true;
            LostBecause = reason;
            if (reason == LoseReason.GoalNotReached)
            {
                Debug.Log($"LOSE — the water settled without reaching the goal ({GoalsReached}/{GoalCount} goals).", this);
                SetStage(FloodStage.GoalMissed);
            }
            else
            {
                Debug.Log("LOSE — the water reached a city.", this);
                SetStage(FloodStage.CityFlooded);
            }
            LostEvent?.Invoke();
        }

        private static void DestroySafe(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
