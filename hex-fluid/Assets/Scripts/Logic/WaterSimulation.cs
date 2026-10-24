using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The water flow. Starts at Source tiles, spreads DOWNWARD only, one ring every
    /// 'stepInterval' seconds, and shows a blue plane above each flooded tile.
    ///
    /// Rules (all enforced here):
    ///   - Water starts at every Source tile.
    ///   - It only enters a neighbor that is lower down the board (the flow direction).
    ///   - It can't cross a Barricade, a bridge carries it across even if not downhill,
    ///     and a fixed map wall always blocks (see ItemPlacementSystem).
    ///   - It can't enter a resource tile.
    ///   - Entering a City (house) = LOSE.
    ///   - Water settles with no city flooded = WIN.
    ///
    /// Setup:
    ///   - Put this on your board object (or any object) and assign 'board' (your HexBoard).
    ///   - Call StartSimulation() to begin — wire your Start button's onClick to it.
    ///     (Turn on 'autoStart' to test without the button.)
    /// </summary>
    public class WaterSimulation : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Your HexBoard — provides the level, hexSize, and coordinate space.")]
        public HexBoard board;

        [Tooltip("Placed items (barricades/bridges). Auto-found if empty. Optional.")]
        public ItemPlacementSystem items;

        [Header("Water visual")]
        [Tooltip("Optional custom water prefab. Empty = a generated blue plane.")]
        public GameObject waterPlanePrefab;

        [Tooltip("How high above the tiles the water sits.")]
        public float waterYOffset = 2f;

        public Color waterColor = new Color(0.2f, 0.5f, 1f, 1f);

        [Tooltip("Water plane size as a multiple of hexSize.")]
        public float planeSizeFactor = 1.8f;

        [Header("Flow")]
        [Tooltip("Seconds between each water step.")]
        [Min(0.05f)] public float stepInterval = 1.2f;

        [Tooltip("Local direction that counts as 'down' the board. Default -Z. Flip it if your source ends up at the bottom.")]
        public Vector3 flowDirection = new Vector3(0f, 0f, -1f);

        [Tooltip("Start the moment Play begins (for testing). Normally the Start button calls StartSimulation().")]
        public bool autoStart = false;

        public bool Running { get; private set; }
        public bool Won { get; private set; }
        public bool Lost { get; private set; }

        /// <summary>Fires once when the player wins.</summary>
        public event Action WonEvent;
        /// <summary>Fires once when the player loses.</summary>
        public event Action LostEvent;

        private LevelData Level => board != null ? board.level : null;
        private Transform Root => board != null ? board.transform : transform;
        private float HexSize => Level != null ? Level.hexSize : 1f;

        private readonly HashSet<HexCoord> _flooded = new HashSet<HexCoord>();
        private readonly Dictionary<HexCoord, GameObject> _waterViews = new Dictionary<HexCoord, GameObject>();
        private Transform _waterRoot;
        private Coroutine _run;

        private void Start()
        {
            if (autoStart) StartSimulation();
        }

        // =====================================================================
        // Control
        // =====================================================================

        /// <summary>Begin the water flow from the start. Hook your Start button to this.</summary>
        public void StartSimulation()
        {
            if (Level == null)
            {
                Debug.LogError("WaterSimulation: assign a HexBoard that has a LevelData.", this);
                return;
            }

            StopSimulation();
            ClearWater();
            Level.BuildLookups();
            if (items == null) items = FindObjectOfType<ItemPlacementSystem>();
            Won = Lost = false;

            _waterRoot = new GameObject("WaterSim").transform;
            _waterRoot.SetParent(Root, false);

            // Seed every source.
            foreach (var t in Level.tiles)
                if (t.role == TileRole.Source) Flood(t.coord);

            // A source sitting on a city loses immediately.
            if (AnyCityFlooded()) { Lose(); return; }

            Running = true;
            _run = StartCoroutine(RunFlow());
        }

        public void StopSimulation()
        {
            if (_run != null) StopCoroutine(_run);
            _run = null;
            Running = false;
        }

        private IEnumerator RunFlow()
        {
            var wait = new WaitForSeconds(Mathf.Max(0.05f, stepInterval));
            while (Running)
            {
                yield return wait;

                int added = SpreadOneStep(out bool cityHit);
                if (cityHit) { Lose(); yield break; }
                if (added == 0) { Win(); yield break; } // settled and no city flooded
            }
        }

        // =====================================================================
        // Flow rules
        // =====================================================================

        private int SpreadOneStep(out bool cityHit)
        {
            cityHit = false;
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
                    if (items != null && items.GetEdgeFlow(cur, n) == EdgeFlow.Force)
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
                        if (items != null && items.GetEdgeFlow(cur, n) == EdgeFlow.Block) continue;
                        Consider(cur, n, next, ignoreDownhill: false);
                    }
                }
            }

            foreach (var n in next)
            {
                Flood(n);
                if (Level.GetTile(n).role == TileRole.City) cityHit = true;
            }
            return next.Count;
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

        private void Flood(HexCoord c)
        {
            if (!_flooded.Add(c)) return;
            _waterViews[c] = SpawnWater(c);
        }

        private GameObject SpawnWater(HexCoord c)
        {
            GameObject go = waterPlanePrefab != null
                ? Instantiate(waterPlanePrefab, _waterRoot)
                : MakeBluePlane(_waterRoot);

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
            _flooded.Clear();
            if (_waterRoot) DestroySafe(_waterRoot.gameObject);
            _waterRoot = null;
        }

        private void Win()
        {
            Running = false;
            Won = true;
            Debug.Log("WIN — the water settled and no city was flooded.", this);
            WonEvent?.Invoke();
        }

        private void Lose()
        {
            Running = false;
            Lost = true;
            Debug.Log("LOSE — the water reached a city.", this);
            LostEvent?.Invoke();
        }

        private static void DestroySafe(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}