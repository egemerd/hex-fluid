#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Raises the tile under the mouse on the Y axis, smoothly, and eases it back
    /// down when you move off it. Nothing is drawn or spawned — it only moves the
    /// existing tile objects.
    ///
    /// Setup:
    ///   - Put this on your board GameObject and press Play.
    ///   - Assign 'level' (the LevelData the board was built from).
    ///   - Assign 'tilesParent' = the transform whose direct children are the tile
    ///     objects. Leave empty to use 'board' (or this object if 'board' is empty).
    ///
    /// Works with any grid built from HexCoord.ToWorld — it doesn't call into a
    /// specific HexBoard. HoveredTile / HasHover / HoverChanged are here so you can
    /// hook harvesting onto the tile under the mouse later.
    /// </summary>
    public class HexTileHover : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The level the board was built from (for hexSize + which tiles exist). Optional.")]
        public LevelData level;

        [Tooltip("The transform the tiles sit under for coordinate space. Empty = this object.")]
        public Transform board;

        [Tooltip("Parent whose direct children are the tile objects. Empty = 'board'.")]
        public Transform tilesParent;

        [Tooltip("Camera used for picking. Empty = Camera.main.")]
        public Camera cam;

        [Header("Board surface")]
        [Tooltip("Hex radius (center to corner). Taken from 'level' if one is assigned.")]
        public float hexSize = 1f;

        [Tooltip("Height of the tile tops in board space, used for the mouse plane.")]
        public float surfaceY = 0f;

        [Header("Hover lift")]
        [Tooltip("How far up the hovered tile rises, in world units.")]
        public float liftHeight = 0.25f;

        [Tooltip("Higher = snappier. ~8-15 feels good.")]
        public float liftSpeed = 12f;

        [Tooltip("Only lift tiles that exist in 'level'. Off = lift wherever the mouse is on a tile.")]
        public bool requireExistingTile = true;

        /// <summary>True when the mouse is over a valid tile this frame.</summary>
        public bool HasHover { get; private set; }

        /// <summary>The tile under the mouse (only meaningful when HasHover is true).</summary>
        public HexCoord HoveredTile { get; private set; }

        /// <summary>Fires when the hovered tile changes (including to/from "no tile").</summary>
        public event Action<bool, HexCoord> HoverChanged;

        private Transform _boardT;
        private bool _hadHover;
        private HexCoord _lastTile;

        private readonly Dictionary<HexCoord, Transform> _tileByCoord = new Dictionary<HexCoord, Transform>();
        private readonly Dictionary<Transform, float> _baseY = new Dictionary<Transform, float>();
        private readonly HashSet<Transform> _animating = new HashSet<Transform>();

        // Auto-rebuild bookkeeping: the grid is often generated AFTER this wakes up,
        // so we re-map whenever the tile parent's child count changes.
        private Transform _mappedRoot;
        private int _mappedChildCount = -1;
        private bool _warnedEmpty;

        private void Awake()
        {
            _boardT = board != null ? board : transform;
            if (cam == null) cam = Camera.main;
            if (level != null) hexSize = level.hexSize;
        }

        private void OnEnable()
        {
            RebuildTileMap();
        }

        private void OnDisable()
        {
            RestoreAllTiles();
        }

        private void OnValidate()
        {
            if (level != null) hexSize = level.hexSize;
        }

        /// <summary>
        /// Maps each tile object under 'tilesParent' to its hex coord, by reading its
        /// position. Call this again whenever you rebuild the grid at runtime.
        /// </summary>
        public void RebuildTileMap()
        {
            RestoreAllTiles();
            _tileByCoord.Clear();
            _baseY.Clear();
            _animating.Clear();

            Transform root = tilesParent != null ? tilesParent : (board != null ? board : transform);
            if (root == null) return;

            _mappedRoot = root;
            _mappedChildCount = root.childCount;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                HexCoord c = HexCoord.FromWorld(child.localPosition, hexSize);
                if (!_tileByCoord.ContainsKey(c)) // first child wins; ignore stray overlays
                {
                    _tileByCoord[c] = child;
                    _baseY[child] = child.localPosition.y;
                }
            }

            // One-time hint if we found nothing useful, so setup problems are obvious.
            if (_tileByCoord.Count == 0 && !_warnedEmpty && Application.isPlaying)
            {
                _warnedEmpty = true;
                Debug.LogWarning(
                    "HexTileHover found 0 tiles under '" + root.name +
                    "'. Set 'Tiles Parent' to the object your tile GameObjects are direct children of.", this);
            }
            else if (_tileByCoord.Count > 0)
            {
                _warnedEmpty = false;
            }
        }

        private void Update()
        {
            if (cam == null) { cam = Camera.main; if (cam == null) return; }
            _boardT = board != null ? board : transform;

            // Re-map if the grid was (re)generated after we last looked.
            Transform root = tilesParent != null ? tilesParent : (board != null ? board : transform);
            if (root != _mappedRoot || (root != null && root.childCount != _mappedChildCount))
                RebuildTileMap();

            bool hover = TryGetTileUnderMouse(out var tile);
            HasHover = hover;
            HoveredTile = tile;

            UpdateLift(hover, tile);

            if (hover != _hadHover || (hover && tile != _lastTile))
            {
                _hadHover = hover;
                _lastTile = tile;
                HoverChanged?.Invoke(hover, tile);
            }
        }

        /// <summary>Raycasts the board plane and returns the hex under the mouse.</summary>
        public bool TryGetTileUnderMouse(out HexCoord tile)
        {
            tile = default;

            Ray ray = cam.ScreenPointToRay(MousePosition());
            Vector3 planePoint = _boardT.TransformPoint(new Vector3(0f, surfaceY, 0f));
            var plane = new Plane(_boardT.up, planePoint);
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 world = ray.GetPoint(enter);
            Vector3 local = _boardT.InverseTransformPoint(world);
            tile = HexCoord.FromWorld(local, hexSize);

            if (requireExistingTile && level != null && !level.HasTile(tile)) return false;
            return true;
        }

        // ---- Lift --------------------------------------------------------------

        private void UpdateLift(bool hover, HexCoord tile)
        {
            Transform hovered = null;
            if (hover) _tileByCoord.TryGetValue(tile, out hovered);
            if (hovered != null) _animating.Add(hovered);

            if (_animating.Count == 0) return;

            float t = 1f - Mathf.Exp(-liftSpeed * Time.deltaTime); // frame-rate independent ease
            var done = new List<Transform>();

            foreach (var tr in _animating)
            {
                if (tr == null || !_baseY.TryGetValue(tr, out float baseY)) { done.Add(tr); continue; }

                float target = (tr == hovered) ? baseY + liftHeight : baseY;
                Vector3 p = tr.localPosition;
                p.y = Mathf.Lerp(p.y, target, t);

                if (tr != hovered && Mathf.Abs(p.y - baseY) < 0.001f)
                {
                    p.y = baseY;
                    done.Add(tr);
                }
                tr.localPosition = p;
            }

            foreach (var tr in done) _animating.Remove(tr);
        }

        private void RestoreAllTiles()
        {
            foreach (var kv in _baseY)
            {
                if (kv.Key == null) continue;
                Vector3 p = kv.Key.localPosition;
                p.y = kv.Value;
                kv.Key.localPosition = p;
            }
            _animating.Clear();
        }

        /// <summary>Mouse position in screen pixels (old Input Manager or new Input System).</summary>
        private static Vector3 MousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return Vector3.zero;
            Vector2 p = Mouse.current.position.ReadValue();
            return new Vector3(p.x, p.y, 0f);
#else
            return Input.mousePosition;
#endif
        }
    }
}