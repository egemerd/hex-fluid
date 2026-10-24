using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// What a harvest feels like. Listens to HexHarvester.Harvested and, on every hit:
    ///   - freezes time for a moment (hit-stop),
    ///   - shakes the camera,
    ///   - shakes the tile you hit,
    ///   - then hides or destroys that tile (or just logs it).
    ///
    /// Put it on the same object as HexHarvester. Tiles come from the harvester's
    /// HexBoard; 'tilesParent' is only a fallback for boards built some other way.
    /// The camera is the harvester's camera (or Camera.main); a CameraShake is added
    /// to it automatically.
    /// </summary>
    public class HarvestFeedback : MonoBehaviour
    {
        public enum Mode { LogOnly, HideTile, DestroyTile }

        public HexHarvester harvester;

        [Tooltip("Parent whose direct children are the tile objects. Empty = harvester's board / this object.")]
        public Transform tilesParent;

        [Tooltip("What happens to the tile once its shake is over.")]
        public Mode mode = Mode.DestroyTile;

        [Header("Hit-stop")]
        [Tooltip("Seconds (real time) the game freezes on a hit. 0 = off.")]
        [Min(0f)] public float hitStopDuration = 0.08f;

        [Tooltip("Time scale during the freeze. 0 = full stop, 0.05 = nearly stopped.")]
        [Range(0f, 1f)] public float hitStopTimeScale = 0f;

        [Header("Camera shake")]
        [Tooltip("Camera to shake. Empty = the harvester's camera / Camera.main (a CameraShake is added to it).")]
        public CameraShake cameraShake;

        [Tooltip("Seconds the camera shakes.")]
        [Min(0f)] public float cameraShakeDuration = 0.2f;

        [Tooltip("How far the camera moves, in world units. 0 = off.")]
        [Min(0f)] public float cameraShakeStrength = 0.12f;

        [Header("Tile shake")]
        [Tooltip("Seconds the hit tile shakes before it's hidden / destroyed.")]
        [Min(0f)] public float tileShakeDuration = 0.25f;

        [Tooltip("How far the hit tile moves sideways, in board units. 0 = off.")]
        [Min(0f)] public float tileShakeStrength = 0.08f;

        private readonly Dictionary<HexCoord, Transform> _tiles = new Dictionary<HexCoord, Transform>();
        private Transform _root;
        private int _mappedCount = -1;

        private float _hitStopUntil;       // real time the current freeze ends
        private float _timeScaleBefore = 1f;
        private Coroutine _hitStop;

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
            EndHitStop();
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

        /// <summary>The tile object at a coord: asked from the board, else from our own map.</summary>
        private GameObject TileAt(HexCoord c)
        {
            if (harvester != null && harvester.hexBoard != null)
            {
                var go = harvester.hexBoard.GetTileObject(c);
                if (go != null) return go;
            }
            return _tiles.TryGetValue(c, out var t) && t != null ? t.gameObject : null;
        }

        private void OnHarvested(HexCoord coord, ResourceType type, int amount)
        {
            Debug.Log($"Harvested {amount} x {type} at {coord}", this);

            HitStop();
            ShakeCamera();

            GameObject tile = TileAt(coord);
            if (tile == null) return;
            TileShake.On(tile, tileShakeDuration, tileShakeStrength);
            if (mode != Mode.LogOnly) StartCoroutine(RemoveAfterShake(tile));
        }

        // ---- Hit-stop ---------------------------------------------------------

        private void HitStop()
        {
            if (hitStopDuration <= 0f) return;
            if (_hitStop == null) _timeScaleBefore = Time.timeScale;   // a hit during a freeze just extends it
            _hitStopUntil = Mathf.Max(_hitStopUntil, Time.unscaledTime + hitStopDuration);
            Time.timeScale = hitStopTimeScale;
            if (_hitStop == null) _hitStop = StartCoroutine(HitStopRoutine());
        }

        private IEnumerator HitStopRoutine()
        {
            while (Time.unscaledTime < _hitStopUntil) yield return null;
            _hitStop = null;
            Time.timeScale = _timeScaleBefore;
        }

        private void EndHitStop()
        {
            if (_hitStop == null) return;
            StopCoroutine(_hitStop);
            _hitStop = null;
            Time.timeScale = _timeScaleBefore;
        }

        // ---- Shakes -----------------------------------------------------------

        private void ShakeCamera()
        {
            if (cameraShakeStrength <= 0f) return;
            if (cameraShake == null)
            {
                Camera cam = harvester != null && harvester.cam != null ? harvester.cam : Camera.main;
                if (cam == null) return;
                cameraShake = cam.GetComponent<CameraShake>();
                if (cameraShake == null) cameraShake = cam.gameObject.AddComponent<CameraShake>();
            }
            cameraShake.Shake(cameraShakeDuration, cameraShakeStrength);
        }

        /// <summary>Lets the hit tile finish shaking (real time, so the hit-stop doesn't matter), then hides / destroys it.</summary>
        private IEnumerator RemoveAfterShake(GameObject tile)
        {
            if (tileShakeStrength > 0f && tileShakeDuration > 0f)
                yield return new WaitForSecondsRealtime(tileShakeDuration);
            if (tile == null) yield break;                       // gone already (level changed)
            if (mode == Mode.HideTile) tile.SetActive(false);
            else Destroy(tile);
        }
    }
}
