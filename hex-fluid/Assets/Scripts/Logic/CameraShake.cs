using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Shakes the camera it sits on. Call Shake(duration, strength); a stronger or
    /// longer shake while one is running takes over, a weaker one is ignored.
    ///
    /// It only adds a small offset on top of wherever the camera is (and takes it
    /// back off next frame), so it works with anything else that moves the camera.
    /// Runs on unscaled time, so it still shakes during a hit-stop.
    ///
    /// Added to the camera automatically by HarvestFeedback; put it on yourself to
    /// tweak 'frequency'.
    /// </summary>
    public class CameraShake : MonoBehaviour
    {
        [Tooltip("How fast the camera jitters. Higher = more violent, lower = more of a wobble.")]
        public float frequency = 25f;

        private Vector3 _offset;      // what we added last frame (world space)
        private float _duration;
        private float _strength;
        private float _timeLeft;
        private float _seed;

        private void Awake() => _seed = Random.value * 100f;

        /// <summary>Shake for 'duration' seconds, moving up to 'strength' world units, fading out.</summary>
        public void Shake(float duration, float strength)
        {
            if (duration <= 0f || strength <= 0f) return;
            if (_timeLeft > 0f && strength < _strength * (_timeLeft / _duration)) return; // a stronger one is running
            _duration = duration;
            _timeLeft = duration;
            _strength = strength;
        }

        private void LateUpdate()
        {
            transform.position -= _offset;
            _offset = Vector3.zero;
            if (_timeLeft <= 0f) return;

            _timeLeft -= Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01(_timeLeft / _duration);
            float t = Time.unscaledTime * frequency;
            float x = Mathf.PerlinNoise(_seed, t) * 2f - 1f;
            float y = Mathf.PerlinNoise(_seed + 7.3f, t) * 2f - 1f;

            // In the screen plane, so it reads as a shake and never zooms.
            _offset = (transform.right * x + transform.up * y) * (_strength * fade * fade);
            transform.position += _offset;
        }

        private void OnDisable()
        {
            transform.position -= _offset;
            _offset = Vector3.zero;
            _timeLeft = 0f;
        }
    }
}
