using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Lets a popup (like the tutorial book) switch off the game's own mouse and key
    /// handling while it's on screen, so a click on a UI button never harvests a
    /// tile or places a barricade behind it, and Esc closes the popup without also
    /// restarting the game.
    ///
    /// Popups call Block(this) / Unblock(this). Gameplay scripts just check Blocked.
    /// It stays blocked for the rest of the frame a popup closes in, so the key or
    /// click that closed it isn't seen by the game too.
    /// </summary>
    public static class InputBlocker
    {
        private static readonly HashSet<object> _owners = new HashSet<object>();
        private static int _releasedFrame = -1;

        public static bool Blocked => _owners.Count > 0 || Time.frameCount == _releasedFrame;

        public static void Block(object owner)
        {
            if (owner != null) _owners.Add(owner);
        }

        public static void Unblock(object owner)
        {
            if (owner != null && _owners.Remove(owner)) _releasedFrame = Time.frameCount;
        }
    }
}
