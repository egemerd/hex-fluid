using System.Collections;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The Jester. When the player presses Start, the water runs for a moment, then
    /// the camera whips round to the Jester (the flood pauses), he does his trick on
    /// the board, and the camera whips back and the water carries on.
    ///
    /// The camera feel is all in 'Camera Look' on this component (see BossCameraLook).
    /// The trick itself goes in BoardEffect() below. It's empty for now.
    /// </summary>
    public class JesterBoss : BossBehaviour
    {
        [Header("Jester")]
        [Tooltip("The camera turn to the Jester and back. Tweak the feel here.")]
        public BossCameraLook cameraLook = new BossCameraLook();

        private Coroutine _look;

        protected override void OnFloodStarted()
        {
            StopLook();
            _look = StartCoroutine(cameraLook.Play(this, BoardEffect));
        }

        /// <summary>
        /// The Jester's effect on the board. Runs the moment the camera lands on him,
        /// while the flood is paused; the camera stays on him until this finishes
        /// (and at least 'Hold On Boss' seconds). Use 'yield return' to take time.
        /// Everything is in Context (Board, Items, Water, Harvester...).
        /// </summary>
        private IEnumerator BoardEffect()
        {
            // TODO: the Jester's effect on the board goes here.
            yield break;
        }

        protected override void OnBossEnd() => StopLook();

        private void OnDisable() => StopLook();

        private void StopLook()
        {
            if (_look != null) StopCoroutine(_look);
            _look = null;
            cameraLook.Cancel();
        }
    }
}
