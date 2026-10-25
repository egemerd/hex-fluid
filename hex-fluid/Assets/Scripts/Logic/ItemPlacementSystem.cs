using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>One item placed on the board.</summary>
    public class PlacedItem
    {
        public ItemDefinition def;
        public HexCoord a, b;   // the edge the player placed it on ('a' = the tile they clicked in)
        public readonly List<EdgePair> edges = new List<EdgePair>();     // every edge it covers (its footprint)
        public readonly List<GameObject> views = new List<GameObject>(); // one model per covered edge

        /// <summary>The model on the edge it was placed on.</summary>
        public GameObject view => views.Count > 0 ? views[0] : null;
    }

    /// <summary>
    /// Holds every placed item and answers the questions the water asks about an
    /// edge (blocked? forced? where to?). The water never knows about specific item
    /// types — it just asks GetEdgeFlow / GetFlowTarget, which defer to each item's
    /// own logic. So a new item type needs no changes here.
    ///
    /// An item may cover more than one edge (its footprint, e.g. a tunnel's entrance
    /// and exit). It's then found from any of them, and picking it up removes all.
    ///
    /// Put it on your board object (or assign 'board'). The placer calls PlaceEdge;
    /// the water calls GetEdgeFlow / GetFlowTarget.
    /// </summary>
    public class ItemPlacementSystem : MonoBehaviour
    {
        [Header("References")]
        public HexBoard board;

        [Tooltip("Knows which resource tiles were harvested (destroyed): nothing can be built next to those. Auto-found if empty.")]
        public HexHarvester harvester;

        [Tooltip("Items knocked off by a harvest go back here. Auto-found if empty.")]
        public ItemCrafting crafting;

        [Tooltip("How high above the tiles items sit.")]
        public float itemYOffset = 0.1f;

        [Header("Rules")]
        [Tooltip("No building on any edge of a Source tile: the water's starting point can't be walled in.")]
        public bool blockAroundSources = true;

        private readonly Dictionary<EdgeKey, PlacedItem> _edgeItems = new Dictionary<EdgeKey, PlacedItem>();
        private readonly List<PlacedItem> _items = new List<PlacedItem>();
        private readonly List<EdgePair> _footprint = new List<EdgePair>();   // scratch
        private Transform _root;

        private LevelData Level => board != null ? board.Level : null;
        private Transform BoardT => board != null ? board.transform : transform;
        private float HexSize => Level != null ? Level.hexSize : 1f;

        public IEnumerable<PlacedItem> Items => _items;

        private void Awake()
        {
            if (board == null) board = GetComponent<HexBoard>();
            if (harvester == null) harvester = FindFirstObjectByType<HexHarvester>();
            if (crafting == null) crafting = FindFirstObjectByType<ItemCrafting>();
            EnsureRoot();
        }

        private void OnEnable()
        {
            if (harvester != null) harvester.Harvested += OnHarvested;
            if (board == null) return;
            board.LevelLoaded += OnLevelLoaded;
            board.LevelUnloading += FlyAwayAll;
        }

        private void OnDisable()
        {
            if (harvester != null) harvester.Harvested -= OnHarvested;
            if (board == null) return;
            board.LevelLoaded -= OnLevelLoaded;
            board.LevelUnloading -= FlyAwayAll;
        }

        /// <summary>
        /// A tile was harvested and is gone: anything built on its edges has nothing to
        /// stand on, so it's taken off and goes back to your stock.
        /// </summary>
        private void OnHarvested(HexCoord c, ResourceType type, int amount)
        {
            var knocked = new List<PlacedItem>();
            foreach (var p in _items)
                foreach (var e in p.edges)
                    if (e.from == c || e.to == c) { knocked.Add(p); break; }

            foreach (var p in knocked)
            {
                RemoveEdge(p.a, p.b);
                if (crafting != null) crafting.Add(p.def);
            }
        }

        /// <summary>A harvested resource tile: destroyed, so it can't hold anything.</summary>
        private bool IsGone(HexCoord c) => harvester != null && harvester.IsHarvested(c);

        // A new level starts empty: the old level's items go with it.
        private void OnLevelLoaded(LevelData level) => ClearAll();

        /// <summary>The level is leaving: placed items fly up with the tiles (and destroy themselves).</summary>
        private void FlyAwayAll()
        {
            foreach (var p in _items)
                foreach (var v in p.views)
                    if (v) v.AddComponent<TileRiseAnimator>().Play(board.riseHeight, Random.Range(0f, board.riseStagger), board.riseDuration);
            _edgeItems.Clear();
            _items.Clear();
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("Items").transform;
            _root.SetParent(BoardT, false);
        }

        // ---- Placement --------------------------------------------------------

        public bool HasEdgeItem(HexCoord a, HexCoord b) => _edgeItems.ContainsKey(EdgeKey.Between(a, b));

        public ItemDefinition EdgeItemDef(HexCoord a, HexCoord b) =>
            _edgeItems.TryGetValue(EdgeKey.Between(a, b), out var p) ? p.def : null;

        /// <summary>Can a single edge take an item: both tiles exist, neighbours, allowed, and free?</summary>
        public bool CanPlaceEdge(HexCoord a, HexCoord b)
        {
            if (Level == null) return false;
            if (!Level.HasTile(a) || !Level.HasTile(b)) return false;
            if (a.DistanceTo(b) != 1) return false;              // must be adjacent
            if (IsGone(a) || IsGone(b)) return false;            // a harvested (destroyed) tile on either side
            if (IsEdgeForbidden(a, b)) return false;             // a rule says no building here
            return !_edgeItems.ContainsKey(EdgeKey.Between(a, b)); // one item per edge
        }

        /// <summary>Can 'def' be placed on a->b: is every edge it would cover free and allowed?</summary>
        public bool CanPlace(ItemDefinition def, HexCoord a, HexCoord b)
        {
            if (def == null || def.placement != ItemPlacement.Edge) return false;
            Footprint(def, a, b);
            if (_footprint.Count == 0) return false;
            foreach (var e in _footprint)
                if (!CanPlaceEdge(e.from, e.to)) return false;
            return true;
        }

        /// <summary>
        /// True if the rules forbid building on this edge at all (not just "taken"):
        /// it touches a Source tile, where the water starts.
        /// </summary>
        public bool IsEdgeForbidden(HexCoord a, HexCoord b) =>
            blockAroundSources && Level != null && (IsSource(a) || IsSource(b));

        /// <summary>True if a rule forbids any edge 'def' would cover when placed on a->b.</summary>
        public bool IsForbidden(ItemDefinition def, HexCoord a, HexCoord b)
        {
            if (def == null) return IsEdgeForbidden(a, b);
            Footprint(def, a, b);
            foreach (var e in _footprint)
                if (IsEdgeForbidden(e.from, e.to)) return true;
            return false;
        }

        private bool IsSource(HexCoord c)
        {
            var t = Level.GetTile(c);
            return t != null && t.role == TileRole.Source;
        }

        private void Footprint(ItemDefinition def, HexCoord a, HexCoord b)
        {
            _footprint.Clear();
            def.GetFootprint(a, b, _footprint);
        }

        public PlacedItem PlaceEdge(ItemDefinition def, HexCoord a, HexCoord b)
        {
            if (!CanPlace(def, a, b)) return null;
            EnsureRoot();

            var p = new PlacedItem { def = def, a = a, b = b };
            p.edges.AddRange(_footprint);                        // filled by CanPlace
            // The model keeps its prefab's own rotation (e.g. a Blender import's -90 X),
            // turned into the edge's frame, plus the item's fit-to-edge tweak.
            Quaternion own = def.model != null ? def.model.transform.localRotation : Quaternion.identity;
            Quaternion fit = Quaternion.Euler(def.modelRotation);
            foreach (var e in p.edges)
            {
                GetEdgePose(e.from, e.to, out var pos, out var rot);
                var view = def.model != null ? Instantiate(def.model, _root) : MakeBar(def, _root);
                view.transform.SetPositionAndRotation(pos + rot * def.modelOffset, rot * fit * own);
                view.name = $"{def.DisplayName} {e.from}-{e.to}";
                p.views.Add(view);
                _edgeItems[e.Key] = p;
            }
            _items.Add(p);
            return p;
        }

        /// <summary>Removes the item on this edge, with every other edge it covers.</summary>
        public void RemoveEdge(HexCoord a, HexCoord b)
        {
            if (!_edgeItems.TryGetValue(EdgeKey.Between(a, b), out var p)) return;
            foreach (var v in p.views) if (v) DestroySafe(v);
            foreach (var e in p.edges) _edgeItems.Remove(e.Key);
            _items.Remove(p);
        }

        public void ClearAll()
        {
            foreach (var p in _items)
                foreach (var v in p.views) if (v) DestroySafe(v);
            _edgeItems.Clear();
            _items.Clear();
        }

        // ---- Water queries (all the simulation needs) -------------------------

        /// <summary>
        /// What the item on this edge (if any) does to water crossing it. The water
        /// just asks this and obeys — it never knows about specific item types, so a
        /// new item needs no changes here or in the simulation.
        /// </summary>
        public EdgeFlow GetEdgeFlow(HexCoord from, HexCoord to) =>
            _edgeItems.TryGetValue(EdgeKey.Between(from, to), out var p)
                ? p.def.GetEdgeFlow(p, from, to)
                : EdgeFlow.Default;

        /// <summary>
        /// Where water forced across this edge from 'from' ends up: normally 'to', the
        /// neighbour; an item like a tunnel can send it further.
        /// </summary>
        public HexCoord GetFlowTarget(HexCoord from, HexCoord to) =>
            _edgeItems.TryGetValue(EdgeKey.Between(from, to), out var p)
                ? p.def.GetFlowTarget(p, from, to)
                : to;

        // ---- Visuals ----------------------------------------------------------

        /// <summary>World pose for an item on the edge between a and b (bar spans the edge, facing a->b).</summary>
        public void GetEdgePose(HexCoord a, HexCoord b, out Vector3 pos, out Quaternion rot)
        {
            Vector3 pa = a.ToWorld(HexSize, itemYOffset);
            Vector3 pb = b.ToWorld(HexSize, itemYOffset);
            pos = BoardT.TransformPoint((pa + pb) * 0.5f);
            Vector3 dir = pb - pa;
            rot = dir.sqrMagnitude > 0.0001f
                ? BoardT.rotation * Quaternion.LookRotation(dir, Vector3.up)
                : BoardT.rotation;
        }

        private GameObject MakeBar(ItemDefinition def, Transform parent)
        {
            var root = new GameObject("ItemView");
            root.transform.SetParent(parent, false);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = cube.GetComponent<Collider>();
            if (col != null) DestroySafe(col);
            cube.transform.SetParent(root.transform, false);
            // long axis (X) spans the shared edge; thin along Z (across, the way water crosses)
            cube.transform.localScale = new Vector3(HexSize * 0.9f, HexSize * 0.4f, HexSize * 0.12f);
            cube.transform.localPosition = new Vector3(0f, HexSize * 0.2f, 0f);
            cube.GetComponent<MeshRenderer>().sharedMaterial = new Material(BorrowBaseMaterial()) { color = def.color };
            return root;
        }

        private static Material BorrowBaseMaterial()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var mat = tmp.GetComponent<MeshRenderer>().sharedMaterial;
            DestroySafe(tmp);
            return mat;
        }

        private static void DestroySafe(Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
