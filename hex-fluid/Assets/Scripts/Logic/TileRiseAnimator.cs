using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The reverse of TileDropAnimator: waits a random delay, then shoots up
    /// 'riseHeight' units and destroys the object. HexBoard adds one to
    /// every tile (and ItemPlacementSystem to every placed item) when a level is
    /// unloaded, so the old board flies away before the next one drops in.
    ///
    /// It animates local Y only. Other scripts (hover lift, flooded sinking) leave
    /// objects that have one alone.
    /// </summary>
    public class TileRiseAnimator : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _target;
        private float _delay;
        private float _duration;
        private float _t = -1f; // <0 = idle

        /// <summary>Begin the rise from where the object is now.</summary>
        public void Play(float riseHeight, float delay, float duration)
        {
            _start = transform.localPosition;
            _target = _start + Vector3.up * riseHeight;
            _delay = Mathf.Max(0f, delay);
            _duration = Mathf.Max(0.01f, duration);
            _t = 0f;
            enabled = true;
        }

        private void Update()
        {
            if (_t < 0f) return;

            if (_delay > 0f)
            {
                _delay -= Time.deltaTime;
                return;
            }

            _t += Time.deltaTime / _duration;
            transform.localPosition = Vector3.Lerp(_start, _target, EaseInCubic(Mathf.Clamp01(_t)));

            if (_t >= 1f)
            {
                _t = -1f;
                Destroy(gameObject);                // gone off the top
            }
        }

        // Starts slow and accelerates away, never going below the start (so the
        // tile can't dip into the table).
        private static float EaseInCubic(float t) => t * t * t;
    }
}
