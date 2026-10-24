using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Makes a sticker sprite feel alive, like a cheap hand-made stop-motion
    /// animation: it only changes pose a few times a second (framesPerSecond) and
    /// holds each pose in between, so it moves in choppy, laggy steps instead of a
    /// smooth wobble. Each pose leans left/right with a little squash and stretch
    /// and a small random jitter, so it reads as redrawn every frame. It pops in
    /// (also in steps) and always faces the camera. Every sticker starts at a
    /// random point so a row of them never moves in lockstep.
    ///
    /// Works on a world sprite (SpriteRenderer, faceCamera on) or a UI element
    /// (faceCamera off: it just moves in its own plane). Put the pivot at the
    /// bottom so the lean hinges there. Call Pop() to replay the pop-in.
    /// Turn 'choppy' off to get the old smooth sway back.
    /// </summary>
    public class StickerWobble : MonoBehaviour
    {
        [Tooltip("World stickers turn to face the camera. Turn off for UI.")]
        public bool faceCamera = true;

        [Header("Low frame rate")]
        [Tooltip("ON = hold each pose and jump to the next, like a laggy low-fps animation. OFF = smooth sway.")]
        public bool choppy = true;

        [Tooltip("Poses per second. Lower = laggier (6-10 reads as stop-motion).")]
        [Min(1f)] public float framesPerSecond = 8f;

        [Tooltip("Random extra lean per frame, in degrees, so each pose looks redrawn.")]
        public float jitterAngle = 2.5f;

        [Tooltip("Random extra size change per frame (0.03 = up to 3%).")]
        public float jitterScale = 0.03f;

        [Header("Motion")]
        [Tooltip("How far it leans to each side, in degrees.")]
        public float swingAngle = 12f;

        [Tooltip("Sways per second (roughly).")]
        public float swingSpeed = 0.9f;

        [Tooltip("How much it squashes and stretches as it sways (0 = none).")]
        public float squash = 0.05f;

        [Tooltip("Seconds the pop-in takes.")]
        public float popDuration = 0.35f;

        private Vector3 _baseScale;
        private float _phase, _age;
        private int _frame = int.MinValue;     // which held pose we're on
        private float _jitterAngle, _jitterScale;

        private void Awake()
        {
            _baseScale = transform.localScale;
            _phase = Random.value * Mathf.PI * 2f;
            transform.localScale = Vector3.zero;
        }

        /// <summary>Replays the pop-in (e.g. when the sticker's content changes).</summary>
        public void Pop() => _age = 0f;

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            float time = Time.time;
            float age = _age;

            if (choppy)
            {
                // Snap the clock to the last whole frame: the pose only changes on frame ticks.
                float step = 1f / Mathf.Max(1f, framesPerSecond);
                int frame = Mathf.FloorToInt(time / step);
                time = frame * step;
                age = Mathf.Floor(_age / step) * step;

                if (frame != _frame)
                {
                    _frame = frame;
                    _jitterAngle = Random.Range(-jitterAngle, jitterAngle);
                    _jitterScale = Random.Range(-jitterScale, jitterScale);
                }
            }
            else
            {
                _jitterAngle = _jitterScale = 0f;
            }

            float t = time * swingSpeed * Mathf.PI * 2f + _phase;

            // Sway: a main swing plus a quicker, smaller one so it never looks mechanical.
            float angle = Mathf.Sin(t) * swingAngle + Mathf.Sin(t * 2.3f + 1.7f) * swingAngle * 0.25f + _jitterAngle;

            if (faceCamera)
            {
                Camera cam = Camera.main;
                Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
                transform.rotation = face * Quaternion.Euler(0f, 0f, angle);
            }
            else
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            // Pop in with overshoot, then breathe: taller as it swings through the middle.
            float pop = EaseOutBack(Mathf.Clamp01(age / Mathf.Max(0.01f, popDuration))) * (1f + _jitterScale);
            float stretch = 1f + squash * Mathf.Cos(t * 2f);
            transform.localScale = new Vector3(_baseScale.x * pop / stretch, _baseScale.y * pop * stretch, _baseScale.z * pop);
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = x - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
