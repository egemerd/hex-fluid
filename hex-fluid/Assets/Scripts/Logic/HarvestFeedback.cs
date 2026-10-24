using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Quick, visible confirmation that a harvest happened — a testing aid.
    /// Listens to HexHarvester.Harvested and logs it, hides the tile, or destroys it.
    ///
    /// Put it on the same object as HexHarvester. Set 'tilesParent' to the transform
    /// whose children are your tile objects (empty = the harvester's board / this object).
    ///
    /// This is where your real "something happens after harvest" will eventually live;
    /// for now it just makes the click visible.
    /// </summary>
    public class HarvestFeedback : MonoBehaviour
    {
        public enum Mode { LogOnly, HideTile, DestroyTile }

        public HexHarvester harvester;

        [Tooltip("Parent whose direct children are the tile objects. Empty = harvester's board / this object.")]
        public Transform tilesParent;

        public Mode mode = Mode.DestroyTile;

        private readonly Dictionary<HexCoord, Transform> _tiles = new Dictionary<HexCoord, Transform>();
        private Transform _root;
        private int _mappedCount = -1;

        private void Awake()
        {
            if (harvester == null) harvester = GetComponent<HexHarvester>();
        }

        private void OnEnable()
        {
            if (harvester != null) harvester.Harvested += OnHarvested;
            Remap();
        }

        private void OnDisable()
        {
            if (harvester != null) harvester.Harvested -= OnHarvested;
        }

        private Transform Root =>
            tilesParent != null ? tilesParent
            : (harvester != null && harvester.board != null ? harvester.board : transform);

        private void Update()
        {
            Transform r = Root;
            if (r != _root || (r != null && r.childCount != _mappedCount)) Remap();
        }

        private void Remap()
        {
            _tiles.Clear();
            _root = Root;
            if (_root == null) return;
            _mappedCount = _root.childCount;

            float hs = harvester != null ? harvester.hexSize : 1f;
            for (int i = 0; i < _root.childCount; i++)
            {
                Transform child = _root.GetChild(i);
                HexCoord c = HexCoord.FromWorld(child.localPosition, hs);
                if (!_tiles.ContainsKey(c)) _tiles[c] = child;
            }
        }

        private void OnHarvested(HexCoord coord, ResourceType type, int amount)
        {
            Debug.Log($"Harvested {amount} x {type} at {coord}", this);
            if (mode == Mode.LogOnly) return;

            if (_tiles.TryGetValue(coord, out var t) && t != null)
            {
                if (mode == Mode.HideTile) t.gameObject.SetActive(false);
                else Destroy(t.gameObject);
            }
        }
    }
}