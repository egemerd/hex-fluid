using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Null-safe shortcut to the AudioManager. Gameplay scripts call Sfx.Play(sound)
    /// and never check anything: an empty sound slot, or no AudioManager in the
    /// scene, just plays nothing instead of throwing.
    /// </summary>
    public static class Sfx
    {
        public static void Play(SoundSO sound)
        {
            if (sound != null && AudioManager.Instance != null) AudioManager.Instance.Play(sound);
        }

        public static void PlayAt(SoundSO sound, Vector3 worldPos)
        {
            if (sound != null && AudioManager.Instance != null) AudioManager.Instance.PlayAt(sound, worldPos);
        }
    }
}
