using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// When you harvest a resource, drops its prefab straight down onto a table at a
    /// random spot inside a chosen area, with no overlap between drops. Which prefab
    /// falls comes from a ResourceDropSet, so a new resource just needs a row there —
    /// this script never changes.
    ///
    /// Setup:
    ///   - Put this on your board object (or any object).
    ///   - Assign 'dropSet' and 'table' (the surface they land on — give it a Collider!).
    ///   - Move the spawn area with 'spawnAreaOffset' (in the table's local space, e.g.
    ///     negative X for the left side) and size it with 'spawnRadius'.
    /// </summary>
    public class ResourceDropSpawner : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        public HexHarvester harvester;
        public ResourceDropSet dropSet;

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
        [Tooltip("One drop per unit of the resource. Off = one drop per harvest.")]
        public bool oneDropPerUnit = true;

        [Min(1)] public int maxDropsPerHarvest = 12;

        [Header("Housekeeping")]
        [Min(1)] public int maxDropsAlive = 80;

        [Tooltip("Add a Collider + Rigidbody to a prefab that has none.")]
        public bool ensurePhysics = true;

        private struct Placed { public GameObject go; public Vector3 slot; }

        private Transform _root;
        private readonly Queue<Placed> _alive = new Queue<Placed>();
        private bool _warned;

        private void Awake()
        {
            if (harvester == null) harvester = FindObjectOfType<HexHarvester>();
            if (table == null) table = transform;
        }

        private void OnEnable()
        {
            if (harvester != null) harvester.Harvested += OnHarvested;
        }

        private void OnDisable()
        {
            if (harvester != null) harvester.Harvested -= OnHarvested;
        }

        private void OnHarvested(HexCoord coord, ResourceType type, int amount)
        {
            GameObject prefab = dropSet != null ? dropSet.GetPrefab(type) : null;
            if (prefab == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning($"ResourceDropSpawner: no drop prefab for {type}. Add it to the Resource Drop Set.", this);
                }
                return;
            }

            int count = oneDropPerUnit ? Mathf.Clamp(amount, 1, maxDropsPerHarvest) : 1;
            for (int i = 0; i < count; i++) SpawnOne(prefab);
        }

        private void SpawnOne(GameObject prefab)
        {
            EnsureRoot();

            Vector3 center = table.position + table.rotation * spawnAreaOffset;
            Vector3 slot = FindFreeSlot(center);                 // ground-level spot, no overlap
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
                rb.linearVelocity = -table.up * fallSpeed;             // straight down at the chosen speed, no sideways throw
                if (freezeRotation)                              // keep its random pose, no tumble/bounce
                    rb.constraints = RigidbodyConstraints.FreezeRotationX
                                   | RigidbodyConstraints.FreezeRotationY
                                   | RigidbodyConstraints.FreezeRotationZ;

                if (settleAfterLanding) StartCoroutine(SettleAfterLanding(rb));
            }

            Track(go, slot);
        }

        /// <summary>Waits for the drop to land, waits settleDelay, then eases it to a full stop.</summary>
        private IEnumerator SettleAfterLanding(Rigidbody rb)
        {
            var step = new WaitForFixedUpdate();
            Vector3 down = -table.up;

            // Landed = it suddenly lost most of its falling speed (hit the table or another drop).
            float peakFall = 0f;
            while (true)
            {
                if (rb == null) yield break;
                float fall = Vector3.Dot(rb.linearVelocity, down);
                if (fall > peakFall) peakFall = fall;
                if (peakFall > 0.5f && fall < peakFall * 0.5f) break;
                yield return step;
            }

            for (float t = 0f; t < settleDelay; t += Time.fixedDeltaTime)
            {
                if (rb == null) yield break;
                yield return step;
            }

            // Ease velocity down to zero over settleDuration.
            if (rb == null) yield break;
            Vector3 v0 = rb.linearVelocity;
            Vector3 w0 = rb.angularVelocity;
            for (float t = 0f; t < settleDuration; t += Time.fixedDeltaTime)
            {
                if (rb == null) yield break;
                float k = 1f - Mathf.SmoothStep(0f, 1f, t / settleDuration);
                rb.linearVelocity = v0 * k;
                rb.angularVelocity = w0 * k;
                yield return step;
            }

            if (rb == null) yield break;
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
            foreach (var p in _alive)
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

        private void Track(GameObject go, Vector3 slot)
        {
            _alive.Enqueue(new Placed { go = go, slot = slot });
            while (_alive.Count > maxDropsAlive)
            {
                var old = _alive.Dequeue();
                if (old.go) Destroy(old.go);
            }
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("ResourceDrops").transform;
        }

        /// <summary>Clears every dropped object (e.g. between levels).</summary>
        public void ClearAll()
        {
            while (_alive.Count > 0)
            {
                var p = _alive.Dequeue();
                if (p.go) Destroy(p.go);
            }
        }
    }
}