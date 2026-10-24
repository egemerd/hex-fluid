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
    /// Plays nicely with TileDropAnimator: while a tile is still falling in, the
    /// hover leaves it alone, and it only records a tile's resting height once the
    /// tile has landed. So hovering during the drop no longer sends tiles flying.
    ///
    /// Setup:
    ///   - Put this on your board GameObject and press Play.
    ///   - Assign 'level'. Assign 'tilesParent' = the transform whose direct children
    ///     are the tile objects (empty = 'board' / this object).
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

        private Transform _mappedRoot;
        private int _mappedChildCount = -1;
        private bool _warnedEmpty;

        private void Awake()
        {
            _boardT = board != null ? board : transform;
            if (cam == null) cam = Camera.main;
            if (level != null) hexSize = level.hexSize;
        }

        private void OnEnable() => RebuildTileMap();
        private void OnDisable() => RestoreLifted();

        private void OnValidate()
        {
            if (level != null) hexSize = level.hexSize;
        }

        /// <summary>Is this tile still falling in (owned by a drop animator)?</summary>
        private static bool IsDropping(Transform t) =>
            t != null && t.GetComponent<TileDropAnimator>() != null;

        /// <summary>
        /// Maps each tile object under 'tilesParent' to its hex coord by its X/Z
        /// (ignores Y, so it works even while tiles are mid-fall). Resting heights
        /// are captured lazily once a tile has landed — not here.
        /// </summary>
        public void RebuildTileMap()
        {
            RestoreLifted();           // put back anything we had lifted, then remap
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
                HexCoord c = HexCoord.FromWorld(child.localPosition, hexSize); // uses X/Z only
                if (!_tileByCoord.ContainsKey(c)) _tileByCoord[c] = child;     // first child wins
            }

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

            // Never touch a tile that's still dropping in.
            if (hovered != null && IsDropping(hovered)) hovered = null;

            if (hovered != null)
            {
                // Capture its resting height the first time we lift it (now that it's landed).
                if (!_baseY.ContainsKey(hovered)) _baseY[hovered] = hovered.localPosition.y;
                _animating.Add(hovered);
            }

            if (_animating.Count == 0) return;

            float t = 1f - Mathf.Exp(-liftSpeed * Time.deltaTime);
            var done = new List<Transform>();

            foreach (var tr in _animating)
            {
                if (tr == null || IsDropping(tr) || !_baseY.TryGetValue(tr, out float baseY))
                {
                    done.Add(tr);
                    continue;
                }

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

        /// <summary>Returns any tiles we lifted to their resting height (skips dropping ones).</summary>
        private void RestoreLifted()
        {
            foreach (var tr in _animating)
            {
                if (tr == null || IsDropping(tr)) continue;
                if (_baseY.TryGetValue(tr, out float y))
                {
                    Vector3 p = tr.localPosition;
                    p.y = y;
                    tr.localPosition = p;
                }
            }
            _animating.Clear();
        }

        // ---- Input -------------------------------------------------------------

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