using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Drops a tile in from above: starts it 'dropHeight' units up, waits a random
    /// delay, then falls to its final local position and snaps (with an optional
    /// bounce). One of these is added to each tile by HexBoard when the board builds.
    ///
    /// It animates local Y only, so the tile lands exactly where it was placed.
    /// Self-destructs when done, leaving the tile at rest.
    /// </summary>
    public class TileDropAnimator : MonoBehaviour
    {
        private Vector3 _start;
        private Vector3 _target;
        private float _delay;
        private float _duration;
        private bool _bounce;
        private float _t = -1f; // <0 = idle

        /// <summary>Begin the drop. targetLocal is the tile's resting local position.</summary>
        public void Play(Vector3 targetLocal, float dropHeight, float delay, float duration, bool bounce)
        {
            _target = targetLocal;
            _start = targetLocal + Vector3.up * dropHeight;
            _delay = Mathf.Max(0f, delay);
            _duration = Mathf.Max(0.01f, duration);
            _bounce = bounce;
            _t = 0f;

            transform.localPosition = _start;
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
            float e = _bounce ? EaseOutBounce(Mathf.Clamp01(_t)) : EaseOutCubic(Mathf.Clamp01(_t));
            transform.localPosition = Vector3.LerpUnclamped(_start, _target, e);

            if (_t >= 1f)
            {
                transform.localPosition = _target; // exact snap
                _t = -1f;
                Destroy(this);                      // remove the animator, keep the tile
            }
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        // Standard "bounce" ease — reads like the tile dropping and settling.
        private static float EaseOutBounce(float t)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}