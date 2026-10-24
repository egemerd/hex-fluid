using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Makes a sticker sprite feel alive: it pops in with a little overshoot, always
    /// faces the camera, and sways left and right from its base with a gentle
    /// squash and stretch. Every sticker starts at a random point in its sway so a
    /// row of them never moves in lockstep.
    ///
    /// Works on a world sprite (SpriteRenderer, faceCamera on) or a UI element
    /// (faceCamera off: it just sways in its own plane). Put the pivot at the
    /// bottom so the sway hinges there. Call Pop() to replay the pop-in.
    /// </summary>
    public class StickerWobble : MonoBehaviour
    {
        [Tooltip("World stickers turn to face the camera. Turn off for UI.")]
        public bool faceCamera = true;

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
            float t = Time.time * swingSpeed * Mathf.PI * 2f + _phase;

            // Sway: a main swing plus a quicker, smaller one so it never looks mechanical.
            float angle = Mathf.Sin(t) * swingAngle + Mathf.Sin(t * 2.3f + 1.7f) * swingAngle * 0.25f;

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
            float pop = EaseOutBack(Mathf.Clamp01(_age / Mathf.Max(0.01f, popDuration)));
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
