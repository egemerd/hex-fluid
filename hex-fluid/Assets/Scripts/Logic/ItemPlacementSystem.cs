using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>One item placed on the board.</summary>
    public class PlacedItem
    {
        public ItemDefinition def;
        public HexCoord a, b;   // the two tiles the edge sits between
        public GameObject view;
    }

    /// <summary>
    /// Holds every placed item and answers the questions the water asks about an
    /// edge (blocked? forced?). The water never knows about specific item types —
    /// it just asks these two methods, which defer to each item's own logic. So a
    /// new item type needs no changes here.
    ///
    /// Put it on your board object (or assign 'board'). The placer calls PlaceEdge;
    /// the water calls IsEdgeBlocked / IsEdgeForced.
    /// </summary>
    public class ItemPlacementSystem : MonoBehaviour
    {
        [Header("References")]
        public HexBoard board;

        [Tooltip("How high above the tiles items sit.")]
        public float itemYOffset = 0.1f;

        private readonly Dictionary<EdgeKey, PlacedItem> _edgeItems = new Dictionary<EdgeKey, PlacedItem>();
        private Transform _root;

        private LevelData Level => board != null ? board.level : null;
        private Transform BoardT => board != null ? board.transform : transform;
        private float HexSize => Level != null ? Level.hexSize : 1f;

        public IEnumerable<PlacedItem> Items => _edgeItems.Values;

        private void Awake()
        {
            if (board == null) board = GetComponent<HexBoard>();
            EnsureRoot();
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

        public bool CanPlaceEdge(HexCoord a, HexCoord b)
        {
            if (Level == null) return false;
            if (!Level.HasTile(a) || !Level.HasTile(b)) return false;
            if (a.DistanceTo(b) != 1) return false;              // must be adjacent
            return !_edgeItems.ContainsKey(EdgeKey.Between(a, b)); // one item per edge
        }

        public PlacedItem PlaceEdge(ItemDefinition def, HexCoord a, HexCoord b)
        {
            if (def == null || def.placement != ItemPlacement.Edge) return null;
            if (!CanPlaceEdge(a, b)) return null;
            EnsureRoot();

            var p = new PlacedItem { def = def, a = a, b = b };
            GetEdgePose(a, b, out var pos, out var rot);
            p.view = def.model != null ? Instantiate(def.model, _root) : MakeBar(def, _root);
            p.view.transform.SetPositionAndRotation(pos, rot);
            p.view.name = $"{def.DisplayName} {a}-{b}";

            _edgeItems[EdgeKey.Between(a, b)] = p;
            return p;
        }

        public void RemoveEdge(HexCoord a, HexCoord b)
        {
            var key = EdgeKey.Between(a, b);
            if (_edgeItems.TryGetValue(key, out var p))
            {
                if (p.view) DestroySafe(p.view);
                _edgeItems.Remove(key);
            }
        }

        public void ClearAll()
        {
            foreach (var p in _edgeItems.Values) if (p.view) DestroySafe(p.view);
            _edgeItems.Clear();
        }

        // ---- Water queries (the only thing the simulation needs) --------------

        public bool IsEdgeBlocked(HexCoord from, HexCoord to) =>
            _edgeItems.TryGetValue(EdgeKey.Between(from, to), out var p) && p.def.BlocksEdge(from, to);

        public bool IsEdgeForced(HexCoord from, HexCoord to) =>
            _edgeItems.TryGetValue(EdgeKey.Between(from, to), out var p) && p.def.ForcesEdge(from, to);

        // ---- Visuals ----------------------------------------------------------

        /// <summary>World pose for an item on the edge between a and b (bar spans the edge).</summary>
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
