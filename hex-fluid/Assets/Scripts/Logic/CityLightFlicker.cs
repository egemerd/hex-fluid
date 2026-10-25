using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Makes a city tile's lights flicker once the water reaches it, then die out as
    /// the tile goes under. Put it on the city tile prefab; it drives every Light
    /// below it.
    ///
    /// It reads the FloodedTile that WaterSimulation adds to the tile: no FloodedTile,
    /// or one that hasn't started sinking yet, means the city is dry and the lights
    /// burn steady. FloodedTile.Restore removes it on a reset, so the lights come back
    /// on their own.
    /// </summary>
    public class CityLightFlicker : MonoBehaviour
    {
        [Tooltip("How fast the flicker jitters. Higher = more nervous.")]
        public float flickerSpeed = 14f;

        [Tooltip("How deep the flicker dips. 0 = steady, 1 = can drop to dark.")]
        [Range(0f, 1f)] public float flickerDepth = 0.7f;

        [Tooltip("Short blackouts per second while flickering (more as the water rises).")]
        public float blackoutRate = 1.5f;

        [Tooltip("How long a blackout lasts, in seconds.")]
        public float blackoutLength = 0.08f;

        [Tooltip("Sink progress (0..1) at which the lights are fully out.")]
        [Range(0.05f, 1f)] public float deadAt = 0.9f;

        private Light[] _lights;
        private float[] _baseIntensity;
        private float[] _seed;
        private float[] _blackoutUntil;
        private FloodedTile _flood;
        private bool _flickering;

        private void Awake()
        {
            _lights = GetComponentsInChildren<Light>(true);
            _baseIntensity = new float[_lights.Length];
            _seed = new float[_lights.Length];
            _blackoutUntil = new float[_lights.Length];
            for (int i = 0; i < _lights.Length; i++)
            {
                _baseIntensity[i] = _lights[i].intensity;
                _seed[i] = Random.Range(0f, 100f);           // each light flickers on its own
            }
        }

        private void Update()
        {
            if (_flood == null) _flood = GetComponentInParent<FloodedTile>();
            float k = _flood != null ? _flood.Progress : 0f;
            if (k <= 0f)
            {
                if (_flickering) SetSteady();                // dry again (reset or new run)
                return;
            }
            _flickering = true;

            float life = 1f - Mathf.Clamp01(k / deadAt);     // 1 as the water arrives, 0 once under
            float rate = blackoutRate * (1f + 2f * (1f - life));
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] == null) continue;
                float level = Mathf.Lerp(1f, Mathf.PerlinNoise(_seed[i], Time.time * flickerSpeed), flickerDepth);

                if (Time.time < _blackoutUntil[i]) level = 0f;
                else if (Random.value < rate * Time.deltaTime)
                    _blackoutUntil[i] = Time.time + blackoutLength * Random.Range(0.5f, 2f);

                _lights[i].intensity = _baseIntensity[i] * level * life;
            }
        }

        /// <summary>Puts every light back at its normal brightness.</summary>
        private void SetSteady()
        {
            _flickering = false;
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null) _lights[i].intensity = _baseIntensity[i];
        }
    }
}
