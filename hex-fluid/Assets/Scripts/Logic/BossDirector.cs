using System;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Runs the boss of a boss level. Listens to the board: when a level is built and
    /// it's a boss level (LevelData.isBoss + LevelData.boss), it spawns the boss prefab,
    /// starts it, and forwards the game's events to it (harvest, items, flood stage,
    /// win / lose). When the level leaves, the boss is ended and destroyed. Normal
    /// levels have no boss and nothing here runs.
    ///
    /// This is the only script that knows bosses exist; the board, water, items and
    /// level manager don't. A new boss never needs changes here, only a new
    /// BossBehaviour subclass and prefab.
    ///
    /// Setup: nothing. LevelManager adds one to its own object if the scene has none.
    /// Add your own to set a spawn point.
    /// </summary>
    public class BossDirector : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        public HexBoard board;
        public WaterSimulation water;
        public ItemPlacementSystem items;
        public HexHarvester harvester;
        public PlayerInventory inventory;
        public ItemCrafting crafting;
        public LevelManager levels;

        [Header("Spawn")]
        [Tooltip("Where the boss prefab appears (it's parented here). Empty = at the prefab's own position in the world.")]
        public Transform spawnPoint;

        /// <summary>The boss of the level being played, or null on a normal level.</summary>
        public BossBehaviour ActiveBoss { get; private set; }

        public bool BossActive => ActiveBoss != null;

        /// <summary>Fires after a boss has been spawned and started (for UI, music...).</summary>
        public event Action<BossBehaviour> BossStarted;

        /// <summary>Fires when the boss's level leaves, just before the boss is destroyed.</summary>
        public event Action<BossBehaviour> BossEnded;

        private void Awake()
        {
            if (board == null) board = GetComponent<HexBoard>();
            if (board == null) board = FindFirstObjectByType<HexBoard>();
            if (water == null) water = FindFirstObjectByType<WaterSimulation>();
            if (items == null) items = FindFirstObjectByType<ItemPlacementSystem>();
            if (harvester == null) harvester = FindFirstObjectByType<HexHarvester>();
            if (inventory == null) inventory = FindFirstObjectByType<PlayerInventory>();
            if (crafting == null) crafting = FindFirstObjectByType<ItemCrafting>();
            if (levels == null) levels = FindFirstObjectByType<LevelManager>();
        }

        private void OnEnable()
        {
            if (board != null) { board.LevelLoaded += OnLevelLoaded; board.LevelUnloading += EndBoss; }
            if (water != null) { water.StageChanged += OnStageChanged; water.WonEvent += OnWon; water.LostEvent += OnLost; }
            if (harvester != null) harvester.Harvested += OnHarvested;
            if (items != null) { items.ItemPlaced += OnItemPlaced; items.ItemRemoved += OnItemRemoved; }
        }

        private void OnDisable()
        {
            if (board != null) { board.LevelLoaded -= OnLevelLoaded; board.LevelUnloading -= EndBoss; }
            if (water != null) { water.StageChanged -= OnStageChanged; water.WonEvent -= OnWon; water.LostEvent -= OnLost; }
            if (harvester != null) harvester.Harvested -= OnHarvested;
            if (items != null) { items.ItemPlaced -= OnItemPlaced; items.ItemRemoved -= OnItemRemoved; }
            EndBoss();
        }

        // ---- Lifecycle ---------------------------------------------------------

        /// <summary>A level was built: end the old boss (if any), start this level's.</summary>
        private void OnLevelLoaded(LevelData level)
        {
            EndBoss();
            if (level == null || !level.isBoss) return;

            if (level.boss == null)
            {
                Debug.LogWarning($"BossDirector: '{level.name}' is a boss level but has no boss assigned. Playing it as a normal level.", this);
                return;
            }
            StartBoss(level);
        }

        private void StartBoss(LevelData level)
        {
            BossBehaviour boss = spawnPoint != null
                ? Instantiate(level.boss, spawnPoint.position, spawnPoint.rotation, spawnPoint)
                : Instantiate(level.boss);
            boss.name = $"Boss: {boss.DisplayName}";
            ActiveBoss = boss;

            Sfx.Play(boss.introSound);
            boss.Begin(new BossContext(level, board, water, items, harvester, inventory, crafting, levels, this));
            BossStarted?.Invoke(boss);
        }

        /// <summary>Ends and removes the current boss. Safe to call when there is none.</summary>
        public void EndBoss()
        {
            if (ActiveBoss == null) { ActiveBoss = null; return; }

            BossBehaviour boss = ActiveBoss;
            ActiveBoss = null;
            boss.End();
            BossEnded?.Invoke(boss);
            if (boss != null) Destroy(boss.gameObject);
        }

        // ---- Event forwarding --------------------------------------------------

        private void OnHarvested(HexCoord tile, ResourceType resource, int amount)
        {
            if (ActiveBoss != null) ActiveBoss.NotifyHarvested(tile, resource, amount);
        }

        private void OnItemPlaced(PlacedItem item)
        {
            if (ActiveBoss != null) ActiveBoss.NotifyItemPlaced(item);
        }

        private void OnItemRemoved(PlacedItem item)
        {
            if (ActiveBoss != null) ActiveBoss.NotifyItemRemoved(item);
        }

        private void OnStageChanged(FloodStage stage)
        {
            if (ActiveBoss != null) ActiveBoss.NotifyStageChanged(stage);
        }

        private void OnWon()
        {
            if (ActiveBoss != null) ActiveBoss.NotifyWon();
        }

        private void OnLost()
        {
            if (ActiveBoss != null) ActiveBoss.NotifyLost(water != null ? water.LostBecause : LoseReason.None);
        }
    }
}
