using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// A short, sideways jitter on one object (the tile you just harvested), fading
    /// out, then it removes itself. Like CameraShake it only adds an offset on top of
    /// the object's position, so the hover lift keeps working while it shakes.
    /// Runs on unscaled time, so it plays through a hit-stop.
    /// </summary>
    public class TileShake : MonoBehaviour
    {
        private Vector3 _offset;      // what we added last frame (local space)
        private float _duration;
        private float _strength;
        private float _frequency;
        private float _timeLeft;
        private float _seed;

        /// <summary>Shake this object. Adds (or restarts) the shake on it.</summary>
        public static void On(GameObject go, float duration, float strength, float frequency = 40f)
        {
            if (go == null || duration <= 0f || strength <= 0f) return;
            // Something else drives its position outright: an offset would drift.
            if (go.GetComponent<TileDropAnimator>() != null || go.GetComponent<TileRiseAnimator>() != null) return;

            var shake = go.GetComponent<TileShake>();
            if (shake == null) shake = go.AddComponent<TileShake>();
            shake._duration = duration;
            shake._timeLeft = duration;
            shake._strength = strength;
            shake._frequency = frequency;
            shake._seed = Random.value * 100f;
        }

        private void LateUpdate()
        {
            transform.localPosition -= _offset;
            _offset = Vector3.zero;

            _timeLeft -= Time.unscaledDeltaTime;
            if (_timeLeft <= 0f) { Destroy(this); return; }

            float fade = _timeLeft / _duration;
            float t = Time.unscaledTime * _frequency;
            float x = Mathf.PerlinNoise(_seed, t) * 2f - 1f;
            float z = Mathf.PerlinNoise(_seed + 7.3f, t) * 2f - 1f;
            _offset = new Vector3(x, 0f, z) * (_strength * fade);   // sideways only, never into the table
            transform.localPosition += _offset;
        }

        private void OnDisable()
        {
            transform.localPosition -= _offset;
            _offset = Vector3.zero;
        }
    }
}
