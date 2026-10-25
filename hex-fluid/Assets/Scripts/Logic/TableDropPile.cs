using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace HexFluid
{
    /// <summary>
    /// A pile of physical objects on the table that mirrors a count per kind: when a
    /// count goes up, that many objects fall from above into a round area on the table;
    /// when it goes down, the newest ones shrink away. They spread out so they don't
    /// land on each other, keep a random pose, settle, and an invisible round wall keeps
    /// them inside the area.
    ///
    /// This holds all the dropping / landing / settling. A subclass only says what it
    /// counts and which prefab stands for each kind, and calls Sync() when the counts
    /// change:
    ///   - ResourceDropSpawner: the resources you hold (wood, stone).
    ///   - CraftedItemDropSpawner: the items you've made but not placed yet.
    /// </summary>
    public abstract class TableDropPile<TKey> : MonoBehaviour
    {
        [Tooltip("Surface the drops land on. Needs a Collider. Empty = this object.")]
        public Transform table;

        [Header("Spawn area (in the table's local space)")]
        [Tooltip("Moves the drop area. e.g. negative X = left side of the table.")]
        public Vector3 spawnAreaOffset = Vector3.zero;

        [Tooltip("Random radius around the area centre where a drop can appear.")]
        public float spawnRadius = 0.5f;

        [Tooltip("How high above the area a drop starts, then falls straight down.")]
        public float spawnHeight = 5f;

        [Tooltip("Initial downward speed. Higher = drops faster. 0 = gravity only.")]
        public float fallSpeed = 8f;

        [Header("Stay in the area")]
        [Tooltip("ON = an invisible round wall at the edge of the area: drops that bounce or get pushed out hit it and stay inside.")]
        public bool keepInsideArea = true;

        [Tooltip("Extra room past 'spawnRadius' before the wall, so drops at the edge still have space to bounce.")]
        [Min(0f)] public float wallPadding = 0.1f;

        [Tooltip("How much a drop bounces back off the wall. 0 = stops dead at it, 1 = full bounce.")]
        [Range(0f, 1f)] public float wallBounce = 0.3f;

        [Header("No overlap")]
        [Tooltip("Minimum gap between drops so they don't land on top of each other.")]
        public float minSpacing = 0.4f;

        [Tooltip("How many random spots to try before taking the least-crowded one.")]
        [Min(1)] public int placementTries = 24;

        [Header("Fall")]
        [Tooltip("ON = each drop starts its fall in a random rotation and lands in that same pose.")]
        public bool randomFallRotation = true;

        [Tooltip("How far a drop can tip away from upright, in degrees. 180 = any pose, 0 = only a random spin around up.")]
        [Range(0f, 180f)] public float maxTilt = 180f;

        [Tooltip("ON = each drop KEEPS its random spawn rotation, so every landing looks different. OFF = physics settles them, which tends to make them all rest the same way.")]
        public bool freezeRotation = true;

        [Header("Settle")]
        [Tooltip("ON = a drop eases to a full stop shortly after it lands.")]
        public bool settleAfterLanding = true;

        [Tooltip("Seconds after landing before the drop starts stopping.")]
        [Min(0f)] public float settleDelay = 0.5f;

        [Tooltip("Seconds it takes to ease from moving to fully stopped.")]
        [Min(0f)] public float settleDuration = 0.3f;

        [Header("Amount")]
        [Tooltip("How many of a kind one drop stands for. 1 = one drop each.")]
        [Min(1)] public int unitsPerDrop = 1;

        [Tooltip("Most drops shown per kind, so a huge stock doesn't flood the table.")]
        [FormerlySerializedAs("maxDropsPerResource")]
        [Min(1)] public int maxDropsPerKind = 30;

        [Header("Remove")]
        [Tooltip("Seconds a spent drop takes to shrink away. 0 = vanish instantly.")]
        [Min(0f)] public float vanishDuration = 0.25f;

        [Header("Housekeeping")]
        [Tooltip("Add a Collider + Rigidbody to a prefab that has none.")]
        public bool ensurePhysics = true;

        // ---- What the subclass provides -------------------------------------------

        /// <summary>How many of each kind there are right now. Kinds left out count as 0.</summary>
        protected abstract IEnumerable<KeyValuePair<TKey, int>> Counts();

        /// <summary>The prefab that falls for one of this kind (null = can't show it).</summary>
        protected abstract GameObject PrefabFor(TKey kind);

        /// <summary>Played when one of this kind lands. Optional.</summary>
        protected virtual SoundSO LandSoundFor(TKey kind) => null;

        /// <summary>Kinds that are never shown (e.g. "no resource").</summary>
        protected virtual bool IsShown(TKey kind) => true;

        /// <summary>Name of the scene object the drops are put under.</summary>
        protected virtual string RootName => "TableDrops";

        // ---- State --------------------------------------------------------------------

        private struct Placed { public GameObject go; public Rigidbody rb; public Vector3 slot; }

        /// <summary>World centre of the drop area, on the table.</summary>
        private Vector3 AreaCenter => table.position + table.rotation * spawnAreaOffset;

        private Transform _root;
        private readonly Dictionary<TKey, List<Placed>> _drops = new Dictionary<TKey, List<Placed>>();
        private readonly Dictionary<TKey, int> _counts = new Dictionary<TKey, int>();
        private readonly List<TKey> _kinds = new List<TKey>();
        private readonly HashSet<TKey> _warned = new HashSet<TKey>();

        protected virtual void Awake()
        {
            if (table == null) table = transform;
        }

        // ---- Mirror the counts -----------------------------------------------------

        /// <summary>Spawns or removes drops so each kind shows its current count.</summary>
        public void Sync()
        {
            _counts.Clear();
            foreach (var kv in Counts()) _counts[kv.Key] = kv.Value;

            _kinds.Clear();
            foreach (var kv in _counts) _kinds.Add(kv.Key);
            foreach (var kv in _drops) if (!_counts.ContainsKey(kv.Key)) _kinds.Add(kv.Key);

            foreach (var kind in _kinds)
                SyncKind(kind, _counts.TryGetValue(kind, out int n) ? n : 0);
        }

        private void SyncKind(TKey kind, int amount)
        {
            if (kind == null || !IsShown(kind)) return;

            int want = Mathf.Min(Mathf.CeilToInt(Mathf.Max(0, amount) / (float)unitsPerDrop), maxDropsPerKind);
            List<Placed> list = DropsOf(kind);

            while (list.Count > want)                            // spent -> newest drops go first
            {
                int last = list.Count - 1;
                GameObject go = list[last].go;
                list.RemoveAt(last);
                if (go) StartCoroutine(Vanish(go));
            }

            if (list.Count >= want) return;

            GameObject prefab = PrefabFor(kind);
            if (prefab == null)
            {
                if (_warned.Add(kind))
                    Debug.LogWarning($"{GetType().Name}: no drop prefab for {kind}.", this);
                return;
            }

            SoundSO landSound = LandSoundFor(kind);
            while (list.Count < want) list.Add(SpawnOne(prefab, landSound)); // gained -> new drops fall in
        }

        private List<Placed> DropsOf(TKey kind)
        {
            if (!_drops.TryGetValue(kind, out var list))
            {
                list = new List<Placed>();
                _drops[kind] = list;
            }
            return list;
        }

        /// <summary>Shrinks a spent drop to nothing, then destroys it.</summary>
        private IEnumerator Vanish(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;               // also ends its settle coroutine

            Vector3 s0 = go.transform.localScale;
            for (float t = 0f; t < vanishDuration; t += Time.deltaTime)
            {
                if (go == null) yield break;
                go.transform.localScale = s0 * (1f - Mathf.SmoothStep(0f, 1f, t / vanishDuration));
                yield return null;
            }

            if (go != null) Destroy(go);
        }

        // ---- Spawning --------------------------------------------------------------

        private Placed SpawnOne(GameObject prefab, SoundSO landSound)
        {
            EnsureRoot();

            Vector3 slot = FindFreeSlot(AreaCenter);             // ground-level spot, no overlap
            Vector3 pos = slot + table.up * spawnHeight;         // start high above it

            GameObject go = Instantiate(prefab, pos, FallRotation(prefab), _root);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (ensurePhysics)
            {
                if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();
                if (rb == null) rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true;
            }

            if (rb != null)
            {
                rb.linearVelocity = -table.up * fallSpeed;       // straight down at the chosen speed, no sideways throw
                if (freezeRotation)                              // keep its random pose, no tumble/bounce
                    rb.constraints = RigidbodyConstraints.FreezeRotationX
                                   | RigidbodyConstraints.FreezeRotationY
                                   | RigidbodyConstraints.FreezeRotationZ;

                StartCoroutine(SettleAfterLanding(rb, landSound));
            }

            return new Placed { go = go, rb = rb, slot = slot };
        }

        // ---- Keep them in the area --------------------------------------------------

        /// <summary>
        /// The invisible wall: any moving drop whose centre has left the circle is put
        /// back on its edge, and its outward speed is turned around (scaled by
        /// wallBounce). Only sideways movement is touched, so falling and landing on the
        /// table work as normal. Done in code rather than with wall colliders, so mouse
        /// clicks and other objects never bump into it.
        /// </summary>
        protected virtual void FixedUpdate()
        {
            if (!keepInsideArea || table == null) return;

            Vector3 center = AreaCenter;
            Vector3 up = table.up;
            float limit = spawnRadius + wallPadding;

            foreach (var list in _drops.Values)
            foreach (var p in list)
            {
                Rigidbody rb = p.rb;
                if (rb == null || rb.isKinematic) continue;      // settled or vanishing: already still

                Vector3 offset = Vector3.ProjectOnPlane(rb.position - center, up);  // sideways only
                float dist = offset.magnitude;
                if (dist <= limit) continue;

                Vector3 outward = offset / dist;
                rb.position -= outward * (dist - limit);         // back onto the wall

                float outSpeed = Vector3.Dot(rb.linearVelocity, outward);
                if (outSpeed > 0f) rb.linearVelocity -= outward * outSpeed * (1f + wallBounce);
            }
        }

        protected virtual void OnDrawGizmosSelected()
        {
            Transform t = table != null ? table : transform;
            Vector3 center = t.position + t.rotation * spawnAreaOffset;
            DrawCircle(center, t, spawnRadius, new Color(1f, 0.8f, 0.2f));          // where drops appear
            if (keepInsideArea) DrawCircle(center, t, spawnRadius + wallPadding, Color.red); // the wall
        }

        private static void DrawCircle(Vector3 center, Transform t, float radius, Color color)
        {
            Gizmos.color = color;
            const int segments = 48;
            Vector3 prev = center + t.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = center + (t.right * Mathf.Cos(a) + t.forward * Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        /// <summary>Waits for the drop to land, plays its landing sound, then (if on) eases it to a full stop.</summary>
        private IEnumerator SettleAfterLanding(Rigidbody rb, SoundSO landSound)
        {
            var step = new WaitForFixedUpdate();
            Vector3 down = -table.up;

            // Landed = it suddenly lost most of its falling speed (hit the table or another drop).
            float peakFall = 0f;
            while (true)
            {
                if (rb == null || rb.isKinematic) yield break;
                float fall = Vector3.Dot(rb.linearVelocity, down);
                if (fall > peakFall) peakFall = fall;
                if (peakFall > 0.5f && fall < peakFall * 0.5f) break;
                yield return step;
            }

            Sfx.PlayAt(landSound, rb.position);
            if (!settleAfterLanding) yield break;

            for (float t = 0f; t < settleDelay; t += Time.fixedDeltaTime)
            {
                if (rb == null || rb.isKinematic) yield break;
                yield return step;
            }

            // Ease velocity down to zero over settleDuration.
            if (rb == null || rb.isKinematic) yield break;
            Vector3 v0 = rb.linearVelocity;
            Vector3 w0 = rb.angularVelocity;
            for (float t = 0f; t < settleDuration; t += Time.fixedDeltaTime)
            {
                if (rb == null || rb.isKinematic) yield break;
                float k = 1f - Mathf.SmoothStep(0f, 1f, t / settleDuration);
                rb.linearVelocity = v0 * k;
                rb.angularVelocity = w0 * k;
                yield return step;
            }

            if (rb == null || rb.isKinematic) yield break;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;                               // fully stopped, stays put
        }

        /// <summary>A random spot in the area that's at least minSpacing from existing drops.</summary>
        private Vector3 FindFreeSlot(Vector3 center)
        {
            Vector3 best = center;
            float bestMin = -1f;

            for (int i = 0; i < placementTries; i++)
            {
                Vector2 r = Random.insideUnitCircle * spawnRadius;
                Vector3 cand = center + table.right * r.x + table.forward * r.y;

                float md = MinDistanceToExisting(cand);
                if (md >= minSpacing) return cand;               // free spot found
                if (md > bestMin) { bestMin = md; best = cand; } // remember least-crowded
            }
            return best; // everything crowded — use the most open spot we saw
        }

        private float MinDistanceToExisting(Vector3 cand)
        {
            float min = float.MaxValue;
            foreach (var list in _drops.Values)
            foreach (var p in list)
            {
                float dx = p.slot.x - cand.x;
                float dz = p.slot.z - cand.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);         // horizontal distance only
                if (d < min) min = d;
            }
            return min;
        }

        /// <summary>The pose a drop starts its fall in. Rotation is frozen, so it lands in it too.</summary>
        private Quaternion FallRotation(GameObject prefab)
        {
            Quaternion baseRot = table.rotation * prefab.transform.rotation; // prefab's own pose, relative to the table
            if (!randomFallRotation) return baseRot;

            Quaternion spin = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);   // random facing
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 tiltAxis = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));                // random sideways axis
            Quaternion tilt = Quaternion.AngleAxis(Random.Range(0f, maxTilt), tiltAxis);   // random tip

            return table.rotation * tilt * spin * prefab.transform.rotation;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject(RootName).transform;
        }

        /// <summary>
        /// Instantly removes every drop (e.g. between levels). The next change re-syncs,
        /// so clear what's counted too if you want the table to stay empty.
        /// </summary>
        public void ClearAll()
        {
            foreach (var list in _drops.Values)
                foreach (var p in list)
                    if (p.go) Destroy(p.go);
            _drops.Clear();
        }
    }
}
