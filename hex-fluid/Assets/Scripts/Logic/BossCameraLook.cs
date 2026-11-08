using System;
using System.Collections;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// A boss "look": the camera whips round to the boss, holds on it while the boss
    /// does its thing, then whips back to the board. The flood is paused the whole
    /// time and carries on from where it was once the camera is back.
    ///
    /// Rotation only: the camera never moves, it just turns (plus a rotational kick
    /// on impact). Every part of the feel is a field here, so tweak it on the boss
    /// prefab's Inspector.
    ///
    /// Reusable: any boss can have one. Declare a field and run it from a hook:
    ///
    ///     public BossCameraLook cameraLook = new BossCameraLook();
    ///     protected override void OnFloodStarted() =>
    ///         StartCoroutine(cameraLook.Play(this, MyEffect));
    ///
    /// and call cameraLook.Cancel() in OnBossEnd (see JesterBoss).
    ///
    /// Timeline:
    ///   Start pressed → [delayAfterStart: water flows] → PAUSE
    ///   → turn to boss (turnToBossDuration, turnToBossCurve) → impact kick
    ///   → hold (holdOnBoss, and until the boss's effect has finished)
    ///   → turn back (turnBackDuration, turnBackCurve) → small kick
    ///   → [holdAfterReturn] → RESUME
    ///
    /// Runs on unscaled time, so a hit-stop (timeScale = 0) doesn't freeze it.
    /// </summary>
    [Serializable]
    public class BossCameraLook
    {
        [Header("Timing (seconds)")]
        [Tooltip("How long the water flows after Start before the camera snaps to the boss. 0 = straight away.")]
        [Min(0f)] public float delayAfterStart = 0.35f;

        [Tooltip("How long the whip to the boss takes. Shorter = harder.")]
        [Min(0.01f)] public float turnToBossDuration = 0.22f;

        [Tooltip("How long the camera stays on the boss. If the boss's effect takes longer, it waits for that.")]
        [Min(0f)] public float holdOnBoss = 1.1f;

        [Tooltip("How long the whip back to the board takes.")]
        [Min(0.01f)] public float turnBackDuration = 0.3f;

        [Tooltip("A beat on the board before the water starts moving again.")]
        [Min(0f)] public float holdAfterReturn = 0.15f;

        [Header("Aim")]
        [Tooltip("What the camera looks at. Empty = the middle of the boss's model.")]
        public Transform lookTarget;

        [Tooltip("Moves the aim point (world units), e.g. up towards the boss's face.")]
        public Vector3 aimOffset = Vector3.zero;

        [Header("Turn feel")]
        [Tooltip("Shape of the turn TO the boss. X = time (0..1), Y = how far round (0 = board view, 1 = on the boss). " +
                 "Going above 1 overshoots past the boss and snaps back. Steep at the start = a hard snap.")]
        public AnimationCurve turnToBossCurve = DefaultSnapCurve();

        [Tooltip("Shape of the turn BACK to the board (0 = on the boss, 1 = board view). " +
                 "A dip below 0 at the start is a wind-up before the whip; above 1 overshoots.")]
        public AnimationCurve turnBackCurve = DefaultWhipBackCurve();

        [Tooltip("Tilts the camera sideways while it looks at the boss (degrees), a dramatic dutch angle. 0 = level, negative = the other way.")]
        [Range(-30f, 30f)] public float tiltOnBoss = 7f;

        [Header("Impact kick")]
        [Tooltip("How hard the camera jolts (degrees) when it lands on the boss. 0 = no kick.")]
        [Min(0f)] public float impactShake = 2.5f;

        [Tooltip("How long the jolt takes to die out.")]
        [Min(0.01f)] public float impactShakeDuration = 0.35f;

        [Tooltip("How fast the jolt jitters. Higher = more violent, lower = more of a wobble.")]
        [Min(0f)] public float impactShakeFrequency = 30f;

        [Tooltip("Kick when the camera lands back on the board, as a share of the boss kick. 0 = none.")]
        [Range(0f, 1f)] public float returnShakeScale = 0.4f;

        [Header("Audio (optional)")]
        [Tooltip("Played as the camera starts turning to the boss (a whoosh).")]
        public SoundSO turnSound;

        [Tooltip("Played as the camera lands on the boss (a hit / sting).")]
        public SoundSO impactSound;

        [Tooltip("Played as the camera starts turning back to the board.")]
        public SoundSO returnSound;

        [Header("Control")]
        [Tooltip("Ignore the player's clicks and keys (R, Esc...) while the look plays.")]
        public bool blockInput = true;

        /// <summary>True from Play until the look has finished (or was cancelled).</summary>
        public bool IsPlaying { get; private set; }

        private Camera _cam;
        private Quaternion _home;        // the board view the camera returns to
        private bool _homeSaved;
        private WaterSimulation _water;
        private bool _pausing;
        private bool _blocking;
        private float _seed;

        // =====================================================================

        /// <summary>
        /// Plays the whole look. Run it as a coroutine on the boss. 'atBoss' (optional)
        /// is the boss's effect: it starts the moment the camera lands on the boss, and
        /// the camera doesn't turn back until it has finished.
        /// </summary>
        public IEnumerator Play(BossBehaviour boss, Func<IEnumerator> atBoss = null)
        {
            Cancel();
            _cam = GameCamera.Current;
            if (boss == null || _cam == null)
            {
                Debug.LogWarning("BossCameraLook: needs a boss and a camera (set 'Game Camera' on HexBoard).", boss);
                yield break;
            }

            IsPlaying = true;
            _water = boss.Context != null ? boss.Context.Water : null;
            _seed = UnityEngine.Random.value * 100f;

            yield return WaitUnscaled(delayAfterStart);

            // Freeze the flood and the player, remember the board view.
            Transform cam = _cam.transform;
            _home = cam.rotation;
            _homeSaved = true;
            if (_water != null) { _water.Pause(this); _pausing = true; }
            if (blockInput) { InputBlocker.Block(this); _blocking = true; }

            // Whip to the boss.
            Quaternion onBoss = AimAt(boss, cam.position);
            Sfx.Play(turnSound);
            yield return Turn(cam, _home, onBoss, turnToBossDuration, turnToBossCurve);
            Sfx.Play(impactSound);

            // Hold on the boss (kick, then steady) while its effect plays.
            bool effectDone = true;
            IEnumerator effect = atBoss != null ? atBoss() : null;
            if (effect != null)
            {
                effectDone = false;
                boss.StartCoroutine(RunThen(effect, () => effectDone = true));
            }
            float t = 0f;
            while (t < holdOnBoss || t < impactShakeDuration || !effectDone)
            {
                cam.rotation = onBoss * Kick(t, impactShake);
                yield return null;
                t += Time.unscaledDeltaTime;
            }

            // Whip back to the board.
            Sfx.Play(returnSound);
            yield return Turn(cam, onBoss, _home, turnBackDuration, turnBackCurve);

            // Land: small kick, then let the water go.
            float landKick = impactShake * returnShakeScale;
            float landTime = landKick > 0f ? impactShakeDuration : 0f;
            t = 0f;
            while (t < holdAfterReturn || t < landTime)
            {
                if (t >= holdAfterReturn) ReleaseFlood();
                cam.rotation = _home * Kick(t, landKick);
                yield return null;
                t += Time.unscaledDeltaTime;
            }

            Finish();
        }

        /// <summary>
        /// Stops the look right now: the camera snaps back to the board view and the
        /// flood and input are released. Call it when the boss ends mid-look (and stop
        /// the coroutine). Safe to call when nothing is playing.
        /// </summary>
        public void Cancel() => Finish();

        // =====================================================================

        private void Finish()
        {
            if (_homeSaved && _cam != null) _cam.transform.rotation = _home;
            _homeSaved = false;
            ReleaseFlood();
            if (_blocking) { InputBlocker.Unblock(this); _blocking = false; }
            IsPlaying = false;
        }

        private void ReleaseFlood()
        {
            if (!_pausing) return;
            if (_water != null) _water.Resume(this);
            _pausing = false;
        }

        /// <summary>The rotation that looks at the boss from 'from', with the dutch tilt.</summary>
        private Quaternion AimAt(BossBehaviour boss, Vector3 from)
        {
            Vector3 target = (lookTarget != null ? lookTarget.position : ModelCenter(boss)) + aimOffset;
            Vector3 dir = target - from;
            if (dir.sqrMagnitude < 1e-6f) return _home;
            return Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, tiltOnBoss);
        }

        /// <summary>Middle of everything the boss renders, or its pivot if it renders nothing.</summary>
        private static Vector3 ModelCenter(BossBehaviour boss)
        {
            var renderers = boss.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return boss.transform.position;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.center;
        }

        /// <summary>Turns the camera from 'a' to 'b' along 'curve' (unclamped, so it can overshoot).</summary>
        private static IEnumerator Turn(Transform cam, Quaternion a, Quaternion b, float duration, AnimationCurve curve)
        {
            float t = 0f;
            while (t < duration)
            {
                float k = curve != null && curve.length > 0 ? curve.Evaluate(t / duration) : t / duration;
                cam.rotation = Quaternion.SlerpUnclamped(a, b, k);
                yield return null;
                t += Time.unscaledDeltaTime;
            }
            cam.rotation = b;
        }

        /// <summary>A rotational jolt 't' seconds after impact, dying out over impactShakeDuration.</summary>
        private Quaternion Kick(float t, float degrees)
        {
            if (degrees <= 0f || t >= impactShakeDuration) return Quaternion.identity;
            float fade = 1f - t / impactShakeDuration;
            fade *= fade;
            float n = t * impactShakeFrequency;
            float pitch = Mathf.PerlinNoise(_seed, n) * 2f - 1f;
            float yaw = Mathf.PerlinNoise(_seed + 3.1f, n) * 2f - 1f;
            float roll = (Mathf.PerlinNoise(_seed + 7.7f, n) * 2f - 1f) * 0.6f;
            return Quaternion.Euler(new Vector3(pitch, yaw, roll) * (degrees * fade));
        }

        private static IEnumerator WaitUnscaled(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        private static IEnumerator RunThen(IEnumerator routine, Action done)
        {
            yield return routine;
            done();
        }

        // ---- Default feel: hard snap with overshoot, wind-up then whip back ----

        /// <summary>Fast start, overshoots the boss by ~12%, settles.</summary>
        public static AnimationCurve DefaultSnapCurve() => new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4.5f),
            new Keyframe(0.55f, 1.12f, 0f, 0f),
            new Keyframe(0.8f, 0.97f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));

        /// <summary>A small wind-up away from the board, then a whip back that overshoots ~8%.</summary>
        public static AnimationCurve DefaultWhipBackCurve() => new AnimationCurve(
            new Keyframe(0f, 0f, 0f, -0.6f),
            new Keyframe(0.18f, -0.05f, 0f, 3f),
            new Keyframe(0.62f, 1.08f, 0f, 0f),
            new Keyframe(0.85f, 0.985f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));
    }
}
