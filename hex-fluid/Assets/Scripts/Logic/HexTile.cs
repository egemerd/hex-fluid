using System;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Role a tile plays in the puzzle. "Blank" is the default so the
    /// generator can lay tiles first and you assign meaning later.
    /// Add roles here as the design grows.
    /// </summary>
    public enum TileRole
    {
        Blank = 0,
        Source = 1,      // water originates here
        Goal = 2,        // water must reach here to win
        City = 3,        // must NOT be flooded (lose if water enters)
        Resource = 4,    // harvestable (wood, etc.) — see ResourceType
    }

    /// <summary>Harvestable resource on a tile. Extend freely.</summary>
    public enum ResourceType
    {
        None = 0,
        Wood = 1,
        Stone = 2,
    }

    /// <summary>
    /// How each resource treats the water. Add a case here when you add a resource.
    ///   Stone: a solid rock. Water never enters it, harvested or not.
    ///   Wood:  unharvested, the water floods it like a normal tile. Once harvested
    ///          (the tile is gone) it blocks the water like a harvested stone.
    /// </summary>
    public static class ResourceRules
    {
        public static bool BlocksWater(ResourceType type, bool harvested)
        {
            switch (type)
            {
                case ResourceType.Stone: return true;
                case ResourceType.Wood:  return harvested;
                default:                 return false;
            }
        }
    }

    /// <summary>
    /// Pure data for one tile. This is what the level asset serializes and
    /// what the editor edits. No scene references live here — the runtime
    /// HexBoard turns this into a spawned object.
    /// </summary>
    [Serializable]
    public class HexTileData
    {
        public HexCoord coord;

        [Tooltip("Index into LevelData.tileModels. -1 = use default model.")]
        public int modelIndex = -1;

        public TileRole role = TileRole.Blank;
        public ResourceType resource = ResourceType.None;
        public int resourceAmount = 0;

        // --- Extension point ---
        // Add per-tile gameplay data here (elevation, biome id, custom flags).
        // Keep it serializable so the editor and asset stay in sync.
        [Tooltip("Free-form tag for custom features you add later.")]
        public string customTag = "";

        public HexTileData() { }
        public HexTileData(HexCoord c) { coord = c; }
    }
}