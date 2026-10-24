using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// A complete, serialized level. The editor tool writes this; the game
    /// reads it. This is the contract between tool and runtime — keep it
    /// clean and everything downstream stays modular.
    ///
    /// Create via: Assets > Create > HexFluid > Level.
    /// </summary>
    [CreateAssetMenu(fileName = "NewHexLevel", menuName = "HexFluid/Level", order = 0)]
    public class LevelData : ScriptableObject
    {
        [Header("Board")]
        [Tooltip("Outer radius of a hex (center to corner) in world units.")]
        public float hexSize = 1f;

        [Tooltip("All tiles that exist on this board.")]
        public List<HexTileData> tiles = new List<HexTileData>();

        [Tooltip("Only edges that are NOT open are stored (barricades, walls).")]
        public List<EdgeEntry> edges = new List<EdgeEntry>();

        [Header("Models")]
        [Tooltip("Model used when a tile's modelIndex is -1.")]
        public GameObject defaultTileModel;

        [Tooltip("Palette of per-tile models. HexTileData.modelIndex references these.")]
        public List<GameObject> tileModels = new List<GameObject>();

        [Header("Water")]
        [Tooltip("How many tiles water advances per step (flood-fill rings).")]
        public int waterStepsPerTurn = 1;

        // ---- Fast lookup built at load; not serialized ----
        private Dictionary<HexCoord, HexTileData> _tileLookup;
        private Dictionary<EdgeKey, EdgeState> _edgeLookup;

        public void BuildLookups()
        {
            _tileLookup = new Dictionary<HexCoord, HexTileData>(tiles.Count);
            foreach (var t in tiles) _tileLookup[t.coord] = t;

            _edgeLookup = new Dictionary<EdgeKey, EdgeState>(edges.Count);
            foreach (var e in edges)
                _edgeLookup[EdgeKey.Between(e.a, e.b)] = e.state;
        }

        public bool HasTile(HexCoord c)
        {
            if (_tileLookup == null) BuildLookups();
            return _tileLookup.ContainsKey(c);
        }

        public HexTileData GetTile(HexCoord c)
        {
            if (_tileLookup == null) BuildLookups();
            return _tileLookup.TryGetValue(c, out var t) ? t : null;
        }

        /// <summary>Edge state between two coords; Open if nothing stored.</summary>
        public EdgeState GetEdge(HexCoord a, HexCoord b)
        {
            if (_edgeLookup == null) BuildLookups();
            return _edgeLookup.TryGetValue(EdgeKey.Between(a, b), out var st) ? st : EdgeState.Open;
        }

        /// <summary>Set/clear an edge at runtime or in the editor (e.g. placing a barricade).</summary>
        public void SetEdge(HexCoord a, HexCoord b, EdgeState state)
        {
            var key = EdgeKey.Between(a, b);
            // keep the serialized list in sync
            edges.RemoveAll(e => EdgeKey.Between(e.a, e.b).Equals(key));
            if (state != EdgeState.Open)
                edges.Add(new EdgeEntry { a = key.a, b = key.b, state = state });

            if (_edgeLookup == null) BuildLookups();
            if (state == EdgeState.Open) _edgeLookup.Remove(key);
            else _edgeLookup[key] = state;
        }

        /// <summary>True if water can flow across the edge between a and b.</summary>
        public bool IsEdgeOpen(HexCoord a, HexCoord b) => GetEdge(a, b) == EdgeState.Open;

        // ---- Convenience queries ----
        public IEnumerable<HexTileData> TilesWithRole(TileRole role)
        {
            foreach (var t in tiles) if (t.role == role) yield return t;
        }

        /// <summary>
        /// Checks the level can actually be played. Returns true if OK,
        /// otherwise fills 'problems' with human-readable reasons. The
        /// editor calls this before letting you save/ship a level.
        /// </summary>
        public bool Validate(List<string> problems)
        {
            problems.Clear();
            BuildLookups();

            int sources = 0, goals = 0;
            foreach (var t in tiles)
            {
                if (t.role == TileRole.Source) sources++;
                if (t.role == TileRole.Goal) goals++;
            }

            if (tiles.Count == 0) problems.Add("Board has no tiles.");
            if (sources == 0) problems.Add("No Source tile — water has nowhere to start.");
            if (goals == 0) problems.Add("No Goal tile — level can never be won.");

            // Every goal must be reachable from some source ignoring barricades
            // (barricades are player-placed; map walls are not). We check with
            // walls treated as blocking and barricades treated as open, so the
            // level is solvable in principle.
            foreach (var goal in TilesWithRole(TileRole.Goal))
            {
                if (!IsReachableIgnoringBarricades(goal.coord, out _))
                    problems.Add($"Goal at {goal.coord} is not reachable from any source.");
            }

            // Duplicate coords
            var seen = new HashSet<HexCoord>();
            foreach (var t in tiles)
                if (!seen.Add(t.coord))
                    problems.Add($"Duplicate tile at {t.coord}.");

            return problems.Count == 0;
        }

        private bool IsReachableIgnoringBarricades(HexCoord target, out int fromSources)
        {
            fromSources = 0;
            var starts = new List<HexCoord>();
            foreach (var s in TilesWithRole(TileRole.Source)) starts.Add(s.coord);
            if (starts.Count == 0) return false;

            var visited = new HashSet<HexCoord>();
            var queue = new Queue<HexCoord>();
            foreach (var s in starts) { visited.Add(s); queue.Enqueue(s); }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur == target) { fromSources = starts.Count; return true; }
                for (int d = 0; d < 6; d++)
                {
                    var n = cur.Neighbor(d);
                    if (!HasTile(n) || visited.Contains(n)) continue;
                    // Walls block even for solvability; barricades do not.
                    if (GetEdge(cur, n) == EdgeState.Wall) continue;
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
            return false;
        }
    }
}