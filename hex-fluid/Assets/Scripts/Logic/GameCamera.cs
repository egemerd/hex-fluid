using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The one camera the game is played through. Set it ONCE on the HexBoard
    /// ('Game Camera'); every script that needs the camera asks GameCamera.Current
    /// instead of keeping its own camera field: tile hover, harvesting, item placing,
    /// the craft paper, 3D buttons, harvest / denied camera shakes, the boss camera
    /// turn, stickers and the flood stage badge. Switching cameras = changing that
    /// one field.
    ///
    /// If nothing is assigned it falls back to Camera.main (and warns once), but
    /// with several cameras tagged MainCamera that pick is unreliable, so assign it.
    /// </summary>
    public static class GameCamera
    {
        private static HexBoard _board;
        private static int _searchedFrame = -1;
        private static bool _warned;

        /// <summary>The game camera: HexBoard's 'Game Camera', else Camera.main. Can be null.</summary>
        public static Camera Current
        {
            get
            {
                // The board registers itself when enabled; look for one only if it hasn't
                // (and at most once a frame, so a scene without a board stays cheap).
                if (_board == null && _searchedFrame != Time.frameCount)
                {
                    _searchedFrame = Time.frameCount;
                    _board = Object.FindFirstObjectByType<HexBoard>();
                }
                if (_board != null && _board.gameCamera != null) return _board.gameCamera;

                if (!_warned && _board != null)
                {
                    _warned = true;
                    Debug.LogWarning("GameCamera: no 'Game Camera' assigned on the HexBoard, using Camera.main. Assign it so every script uses the right camera.", _board);
                }
                return Camera.main;
            }
        }

        internal static void Register(HexBoard board) => _board = board;

        internal static void Unregister(HexBoard board)
        {
            if (_board == board) _board = null;
        }
    }
}
