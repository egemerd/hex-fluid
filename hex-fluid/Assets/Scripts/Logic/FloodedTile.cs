using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Marks a tile the water will reach and sinks it just ahead of the water front.
    /// WaterSimulation adds it to every tile in its planned flood when a run starts;
    /// the water itself is one continuous surface (WaterBody) drawn over the sunk
    /// tiles.
    ///
    /// The sinking follows the shared water clock (WaterSimulation.WaterTime), so it
    /// stays in step with the front. Update switches itself off once the tile has
    /// sunk. Other scripts (e.g. the hover lift) leave tiles that have one alone.
    /// </summary>
    public class FloodedTile : MonoBehaviour
    {
        private WaterSimulation _sim;
        private float _restY, _sinkDepth, _sinkStart, _sinkEnd;

        /// <summary>Is this tile still dropping in? It must land before it can sink.</summary>
        private bool Dropping => GetComponent<TileDropAnimator>() != null;

        /// <summary>The tile rests at restY and sinks by sinkDepth between sinkStart and sinkEnd on the water clock (in tiles).</summary>
        public void Init(WaterSimulation sim, float restY, float sinkDepth, float sinkStart, float sinkEnd)
        {
            _sim = sim;
            _restY = restY;
            _sinkDepth = sinkDepth;
            _sinkStart = sinkStart;
            _sinkEnd = Mathf.Max(sinkEnd, sinkStart + 0.01f);
            enabled = true;
            Sink();
        }

        /// <summary>Puts the tile back at its resting height.</summary>
        public void Restore()
        {
            if (!Dropping)
            {
                Vector3 p = transform.localPosition;
                p.y = _restY;
                transform.localPosition = p;
            }
            Destroy(this);
        }

        private void Update()
        {
            if (!Dropping && Sink() >= 1f) enabled = false;  // sunk for good
        }

        /// <summary>Moves the tile down as the water clock passes its sink window. Returns progress 0..1.</summary>
        private float Sink()
        {
            if (Dropping) return 0f;                         // let it land first
            float now = _sim != null ? _sim.WaterTime : _sinkEnd;
            float k = Mathf.InverseLerp(_sinkStart, _sinkEnd, now);
            Vector3 p = transform.localPosition;
            p.y = _restY - _sinkDepth * Mathf.SmoothStep(0f, 1f, k);
            transform.localPosition = p;
            return k;
        }
    }
}
