using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Your harvests, as pickaxes lying in a row at the top of the table. Each level
    /// drops in as many pickaxes as its Max Harvests (lined up, each turned a little
    /// at random). Harvesting a tile takes the last pickaxe and plays the chop in
    /// three steps:
    ///   1. it lifts off the table and flies into the READY pose (raised, about to swing),
    ///   2. holds there for a moment,
    ///   3. swings into the HIT pose - the tile breaks right then - and the pickaxe breaks.
    /// The harvest itself (resources, sound, hit-stop, tile breaking) happens on the
    /// hit, not on the click: HexHarvester waits for Strike()'s callback.
    ///
    /// A level with Max Harvests = 0 (unlimited) shows no pickaxes and harvests
    /// straight away.
    ///
    /// Setup:
    ///   - Put this on any object (not under the HexBoard). Assign 'pickaxePrefab'.
    ///   - Make an empty GameObject at the top of the table where the row should be,
    ///     and assign it to 'rackAnchor'. The row is centred on it and runs along its
    ///     red (X) axis; turn the anchor to turn the row.
    ///
    /// Tuning the hit (no Play mode needed):
    ///   - Select this object. The Scene view shows two ghost pickaxes over 'previewTile':
    ///     yellow = Ready pose, red = Hit pose. Change the poses' offset / rotation and
    ///     watch them move.
    ///   - Or pose it by hand: drop a Pickaxe prefab in the scene, assign it to
    ///     'poseHelper', move/rotate it over the preview tile, then right-click this
    ///     component > "Set Ready Pose From Helper" (or "Set Hit Pose From Helper").
    ///     "Move Helper To Ready/Hit Pose" puts it back on a saved pose to fine-tune it.
    /// </summary>
    public class PickaxeRack : MonoBehaviour
    {
        /// <summary>A pickaxe pose relative to the tile it chops, in the board's axes.</summary>
        [Serializable]
        public struct Pose
        {
            [Tooltip("Where the pickaxe is, from the tile's centre (board axes: X right, Y up, Z forward).")]
            public Vector3 offset;

            [Tooltip("How the pickaxe is turned (board axes).")]
            public Vector3 rotation;
        }

        [Header("References (auto-found if empty)")]
        public HexBoard board;

        [Header("Rack")]
        public GameObject pickaxePrefab;

        [Tooltip("Centre of the row, on the table. The row runs along its X axis. Empty = this object.")]
        public Transform rackAnchor;

        [Tooltip("Gap between pickaxes in the row.")]
        public float spacing = 0.7f;

        [Tooltip("Each pickaxe is turned by up to this many degrees either way, so the row looks hand-placed.")]
        [Range(0f, 90f)] public float randomYaw = 20f;

        [Header("Drop in (each level)")]
        [Tooltip("Seconds after the level appears before the pickaxes start falling.")]
        [Min(0f)] public float dropDelay = 0.5f;

        public float dropHeight = 6f;
        [Min(0.01f)] public float fallDuration = 0.45f;

        [Tooltip("Max random extra delay per pickaxe (0 = all together).")]
        [Min(0f)] public float maxStagger = 0.35f;

        public bool dropBounce = true;

        [Tooltip("Played as each pickaxe lands. Optional.")]
        public SoundSO landSound;

        [Header("Chop poses (relative to the tile)")]
        [Tooltip("Raised, about to swing. The pickaxe flies here from the table first.")]
        public Pose readyPose = new Pose { offset = new Vector3(0f, 1.6f, 0.8f), rotation = new Vector3(-40f, 0f, 0f) };

        [Tooltip("The moment of impact: the tile breaks when the pickaxe gets here.")]
        public Pose hitPose = new Pose { offset = new Vector3(0f, 0.5f, 0.4f), rotation = new Vector3(50f, 0f, 0f) };

        [Header("Chop timing")]
        [Tooltip("Seconds to fly from the table to the Ready pose.")]
        [Min(0.01f)] public float flyDuration = 0.35f;

        [Tooltip("How high the flight arcs above the straight line.")]
        public float arcHeight = 1f;

        [Tooltip("Seconds it holds the Ready pose before swinging (anticipation).")]
        [Min(0f)] public float readyHold = 0.12f;

        [Tooltip("Seconds the swing from Ready to Hit takes. Short = hard hit.")]
        [Min(0.01f)] public float swingDuration = 0.08f;

        [Tooltip("Chops per harvest. The tile breaks on the last one; earlier ones just shake it.")]
        [Min(1)] public int hits = 1;

        [Tooltip("Seconds to pull back from Hit to Ready between chops (only with more than one hit).")]
        [Min(0.01f)] public float pullBackDuration = 0.15f;

        [Tooltip("Played as it swings down. Optional.")]
        public SoundSO swingSound;

        [Header("Break")]
        [Tooltip("Seconds the pickaxe takes to burst apart after the last chop.")]
        [Min(0.01f)] public float breakDuration = 0.3f;

        [Tooltip("Played as the pickaxe breaks. Optional.")]
        public SoundSO breakSound;

        [Header("Tuning (editor)")]
        [Tooltip("Show ghost pickaxes in the Scene view for the Ready (yellow) and Hit (red) poses.")]
        public bool showPreview = true;

        [Tooltip("The tile the ghosts are shown over (q, r - the numbers the level editor shows on each tile).")]
        public HexCoord previewTile;

        [Tooltip("A pickaxe in the scene you pose by hand; copy its pose with the component's right-click menu.")]
        public Transform poseHelper;

        /// <summary>Pickaxes still in the row (not yet used).</summary>
        public int Count => _rack.Count;

        private readonly List<GameObject> _rack = new List<GameObject>();
        private readonly List<GameObject> _busy = new List<GameObject>();   // flying / chopping / breaking
        private Coroutine _spawn;

        private Transform Anchor => rackAnchor != null ? rackAnchor : transform;

        private void Awake()
        {
            if (board == null) board = FindFirstObjectByType<HexBoard>();
        }

        private void OnEnable()
        {
            if (board == null) return;
            board.LevelLoaded += OnLevelLoaded;
            board.LevelUnloading += OnLevelUnloading;
        }

        private void OnDisable()
        {
            if (board == null) return;
            board.LevelLoaded -= OnLevelLoaded;
            board.LevelUnloading -= OnLevelUnloading;
        }

        private void Start()
        {
            // The board may have built before we were listening.
            if (board != null && board.Level != null && _rack.Count == 0 && _spawn == null)
                OnLevelLoaded(board.Level);
        }

        // ---- Per level ---------------------------------------------------------

        private void OnLevelLoaded(LevelData level)
        {
            ClearAll();
            if (level != null && level.maxHarvests > 0 && pickaxePrefab != null)
                _spawn = StartCoroutine(SpawnRow(level.maxHarvests));
        }

        /// <summary>The level is leaving: the row flies up with the tiles, any chop in progress is dropped.</summary>
        private void OnLevelUnloading()
        {
            CancelStrikes();
            foreach (var p in _rack)
            {
                if (!p) continue;
                var drop = p.GetComponent<TileDropAnimator>();
                if (drop) DestroyImmediate(drop);
                p.AddComponent<TileRiseAnimator>().Play(board.riseHeight, UnityEngine.Random.Range(0f, board.riseStagger), board.riseDuration);
            }
            _rack.Clear();
        }

        private void ClearAll()
        {
            CancelStrikes();
            foreach (var p in _rack) if (p) Destroy(p);
            _rack.Clear();
        }

        private void CancelStrikes()
        {
            StopAllCoroutines();                     // strikes never call back after this
            _spawn = null;
            foreach (var p in _busy) if (p) Destroy(p);
            _busy.Clear();
        }

        /// <summary>Lines up 'count' pickaxes along the anchor and drops them in.</summary>
        private IEnumerator SpawnRow(int count)
        {
            if (dropDelay > 0f) yield return new WaitForSeconds(dropDelay);

            Transform anchor = Anchor;
            float first = -(count - 1) * spacing * 0.5f;               // centred on the anchor
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(pickaxePrefab, anchor);
                go.name = $"Pickaxe {i + 1}";
                Vector3 slot = new Vector3(first + i * spacing, 0f, 0f);
                go.transform.localRotation =
                    Quaternion.AngleAxis(UnityEngine.Random.Range(-randomYaw, randomYaw), Vector3.up) * pickaxePrefab.transform.localRotation;

                float delay = UnityEngine.Random.Range(0f, maxStagger);
                go.AddComponent<TileDropAnimator>().Play(slot, dropHeight, delay, fallDuration, dropBounce);
                if (landSound != null) StartCoroutine(PlayAfter(landSound, delay + fallDuration * (dropBounce ? 1f / 2.75f : 1f)));
                _rack.Add(go);
            }
            _spawn = null;
        }

        private IEnumerator PlayAfter(SoundSO sound, float delay)
        {
            yield return new WaitForSeconds(delay);
            Sfx.Play(sound);
        }

        // ---- Strike ------------------------------------------------------------

        /// <summary>
        /// Sends the last pickaxe in the row to chop the tile at 'c'. 'onHit' runs the
        /// moment the final chop lands (that's when the harvest should happen). Returns
        /// false if there's no pickaxe to send; then nothing is called back.
        /// </summary>
        public bool Strike(HexCoord c, Action onHit)
        {
            if (_rack.Count == 0 || board == null || board.Level == null) return false;

            GameObject pick = _rack[_rack.Count - 1];
            _rack.RemoveAt(_rack.Count - 1);
            if (pick == null) return false;

            var drop = pick.GetComponent<TileDropAnimator>();
            if (drop) DestroyImmediate(drop);                           // still landing: take it from mid-air
            pick.transform.SetParent(null, true);
            _busy.Add(pick);

            StartCoroutine(StrikeRoutine(pick, c, onHit));
            return true;
        }

        private IEnumerator StrikeRoutine(GameObject pick, HexCoord c, Action onHit)
        {
            Transform t = pick.transform;
            Vector3 up = board.transform.up;
            float hexSize = board.Level.hexSize;
            ToWorld(board.transform, readyPose, c, hexSize, out Vector3 readyPos, out Quaternion readyRot);
            ToWorld(board.transform, hitPose, c, hexSize, out Vector3 hitPos, out Quaternion hitRot);

            // 1. Lift off the table and fly into the Ready pose, along an arc.
            Vector3 p0 = t.position;
            Quaternion r0 = t.rotation;
            Vector3 mid = (p0 + readyPos) * 0.5f + up * arcHeight;
            for (float k = 0f; k < 1f; k += Time.deltaTime / flyDuration)
            {
                float e = Mathf.SmoothStep(0f, 1f, k);
                t.SetPositionAndRotation(Bezier(p0, mid, readyPos, e), Quaternion.Slerp(r0, readyRot, e));
                yield return null;
            }
            t.SetPositionAndRotation(readyPos, readyRot);

            // 2 + 3. Hold, then swing to Hit. The last hit breaks the tile.
            for (int h = 0; h < hits; h++)
            {
                if (h > 0) yield return Move(t, hitPos, hitRot, readyPos, readyRot, pullBackDuration, EaseOut);
                if (readyHold > 0f) yield return new WaitForSeconds(readyHold);

                Sfx.PlayAt(swingSound, hitPos);
                yield return Move(t, readyPos, readyRot, hitPos, hitRot, swingDuration, EaseIn);

                if (h < hits - 1) TileShake.On(board.GetTileObject(c), 0.12f, 0.05f);
            }
            onHit?.Invoke();

            // Break: a little hop and spin while it shrinks away.
            Sfx.PlayAt(breakSound, t.position);
            Vector3 s0 = t.localScale;
            Vector3 b0 = t.position;
            Quaternion br = t.rotation;
            float spin = UnityEngine.Random.Range(0, 2) == 0 ? -1f : 1f;
            for (float k = 0f; k < 1f; k += Time.deltaTime / breakDuration)
            {
                t.position = b0 + up * (Mathf.Sin(k * Mathf.PI) * 0.35f);
                t.rotation = Quaternion.AngleAxis(spin * 220f * k, up) * br;
                t.localScale = s0 * (1f - EaseIn(k));
                yield return null;
            }

            _busy.Remove(pick);
            Destroy(pick);
        }

        private static IEnumerator Move(Transform t, Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot,
                                        float duration, Func<float, float> ease)
        {
            for (float k = 0f; k < 1f; k += Time.deltaTime / duration)
            {
                float e = ease(k);
                t.SetPositionAndRotation(Vector3.LerpUnclamped(fromPos, toPos, e), Quaternion.SlerpUnclamped(fromRot, toRot, e));
                yield return null;
            }
            t.SetPositionAndRotation(toPos, toRot);
        }

        /// <summary>A pose over tile 'c' of the board 'b', in world space.</summary>
        private static void ToWorld(Transform b, Pose pose, HexCoord c, float hexSize, out Vector3 pos, out Quaternion rot)
        {
            pos = b.TransformPoint(c.ToWorld(hexSize) + pose.offset);
            rot = b.rotation * Quaternion.Euler(pose.rotation);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float k)
        {
            float u = 1f - k;
            return u * u * a + 2f * u * k * b + k * k * c;
        }

        private static float EaseIn(float k) => k * k * k;
        private static float EaseOut(float k) { float u = 1f - k; return 1f - u * u * u; }

        // ---- Tuning in the editor ----------------------------------------------

        private HexBoard PreviewBoard => board != null ? board : FindFirstObjectByType<HexBoard>();

        private float PreviewHexSize(HexBoard b) =>
            b.Level != null ? b.Level.hexSize : (b.level != null ? b.level.hexSize : 1f);

        private void OnDrawGizmosSelected()
        {
            if (!showPreview || pickaxePrefab == null) return;
            HexBoard b = PreviewBoard;
            if (b == null) return;

            float hexSize = PreviewHexSize(b);
            ToWorld(b.transform, readyPose, previewTile, hexSize, out Vector3 readyPos, out Quaternion readyRot);
            ToWorld(b.transform, hitPose, previewTile, hexSize, out Vector3 hitPos, out Quaternion hitRot);
            Vector3 tilePos = b.transform.TransformPoint(previewTile.ToWorld(hexSize));

            Vector3 scale = Vector3.Scale(pickaxePrefab.transform.localScale, Anchor.lossyScale);
            DrawGhost(readyPos, readyRot, scale, new Color(1f, 0.85f, 0.2f));
            DrawGhost(hitPos, hitRot, scale, new Color(1f, 0.25f, 0.2f));

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.5f, 0.2f);
            Gizmos.DrawLine(readyPos, hitPos);
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(tilePos, 0.08f);                     // the tile's centre
        }

        /// <summary>Draws the pickaxe prefab's meshes as a wireframe at a pose.</summary>
        private void DrawGhost(Vector3 pos, Quaternion rot, Vector3 scale, Color color)
        {
            Transform root = pickaxePrefab.transform;
            Matrix4x4 pose = Matrix4x4.TRS(pos, rot, scale);
            Gizmos.color = color;
            foreach (var mf in pickaxePrefab.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Gizmos.matrix = pose * (root.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                Gizmos.DrawWireMesh(mf.sharedMesh);
            }
        }

        [ContextMenu("Set Ready Pose From Helper")]
        private void SetReadyFromHelper() => CopyFromHelper(ref readyPose, "Set Ready Pose");

        [ContextMenu("Set Hit Pose From Helper")]
        private void SetHitFromHelper() => CopyFromHelper(ref hitPose, "Set Hit Pose");

        [ContextMenu("Move Helper To Ready Pose")]
        private void HelperToReady() => MoveHelper(readyPose);

        [ContextMenu("Move Helper To Hit Pose")]
        private void HelperToHit() => MoveHelper(hitPose);

        /// <summary>Saves where the helper is, relative to the preview tile, as a pose.</summary>
        private void CopyFromHelper(ref Pose pose, string undoName)
        {
            HexBoard b = PreviewBoard;
            if (poseHelper == null || b == null) { Debug.LogWarning("PickaxeRack: assign 'poseHelper' (and have a HexBoard in the scene).", this); return; }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, undoName);
#endif
            Transform bt = b.transform;
            pose.offset = bt.InverseTransformPoint(poseHelper.position) - previewTile.ToWorld(PreviewHexSize(b));
            pose.rotation = (Quaternion.Inverse(bt.rotation) * poseHelper.rotation).eulerAngles;
        }

        /// <summary>Puts the helper on a saved pose over the preview tile, to fine-tune it.</summary>
        private void MoveHelper(Pose pose)
        {
            HexBoard b = PreviewBoard;
            if (poseHelper == null || b == null) { Debug.LogWarning("PickaxeRack: assign 'poseHelper' (and have a HexBoard in the scene).", this); return; }
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(poseHelper, "Move Pose Helper");
#endif
            ToWorld(b.transform, pose, previewTile, PreviewHexSize(b), out Vector3 pos, out Quaternion rot);
            poseHelper.SetPositionAndRotation(pos, rot);
        }
    }
}
