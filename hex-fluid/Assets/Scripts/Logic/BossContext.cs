namespace HexFluid
{
    /// <summary>
    /// Everything a boss can reach while its level is played: the level itself and
    /// the game systems around it. The BossDirector builds one per boss level and
    /// hands it to the boss in Begin, so a boss never has to search the scene.
    ///
    /// Any reference can be null if that system isn't in the scene; check before use.
    /// When a boss ability needs a system that isn't here yet, add it here (and fill
    /// it in BossDirector.StartBoss) so every boss gets it the same way.
    /// </summary>
    public sealed class BossContext
    {
        /// <summary>The level being played (the board's runtime copy, safe to change).</summary>
        public LevelData Level { get; }

        public HexBoard Board { get; }
        public WaterSimulation Water { get; }
        public ItemPlacementSystem Items { get; }
        public HexHarvester Harvester { get; }
        public PlayerInventory Inventory { get; }
        public ItemCrafting Crafting { get; }
        public LevelManager Levels { get; }
        public BossDirector Director { get; }

        public BossContext(LevelData level, HexBoard board, WaterSimulation water,
                           ItemPlacementSystem items, HexHarvester harvester,
                           PlayerInventory inventory, ItemCrafting crafting,
                           LevelManager levels, BossDirector director)
        {
            Level = level;
            Board = board;
            Water = water;
            Items = items;
            Harvester = harvester;
            Inventory = inventory;
            Crafting = crafting;
            Levels = levels;
            Director = director;
        }
    }
}
