using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Base for every boss. A boss is a PREFAB with one BossBehaviour subclass on its
    /// root (plus its model, effects, anything it needs). A boss level points at that
    /// prefab (LevelData.boss); when the level starts the BossDirector spawns it,
    /// calls Begin, forwards the game's events to it while the level is played, and
    /// calls End and destroys it when the level leaves.
    ///
    /// Adding a boss = one new subclass + one prefab:
    ///
    ///     public class MyBoss : BossBehaviour
    ///     {
    ///         [Header("My Boss")]
    ///         public int someSetting = 3;          // tune per prefab in the Inspector
    ///
    ///         protected override void OnBossBegin()  { /* the level started */ }
    ///         protected override void OnFloodStarted() { /* Start was pressed */ }
    ///         protected override void OnHarvested(HexCoord tile, ResourceType r, int n) { }
    ///     }
    ///
    /// Override only the hooks the boss cares about; every hook does nothing by
    /// default. The game's systems are in Context (board, water, items, inventory...).
    /// Being a MonoBehaviour, a boss can also use Update and coroutines; it only
    /// exists while its level is on the board.
    ///
    /// Hooks are only called while the boss is active (between Begin and End).
    /// </summary>
    public abstract class BossBehaviour : MonoBehaviour
    {
        [Header("Boss")]
        [Tooltip("Name shown to the player.")]
        public string displayName = "Boss";

        [Tooltip("Short description of what this boss does (for UI / your notes).")]
        [TextArea(2, 5)] public string description = "";

        [Tooltip("Icon for UI. Optional.")]
        public Sprite icon;

        [Tooltip("Played once when the boss appears (its level starts). Optional.")]
        public SoundSO introSound;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>The level and game systems this boss can use. Set in Begin.</summary>
        public BossContext Context { get; private set; }

        /// <summary>The level being played (the board's runtime copy).</summary>
        public LevelData Level => Context != null ? Context.Level : null;

        /// <summary>True between Begin and End: the boss's level is being played.</summary>
        public bool IsActive { get; private set; }

        // =====================================================================
        // Hooks: override the ones this boss needs
        // =====================================================================

        /// <summary>The boss level has started (the board was just built).</summary>
        protected virtual void OnBossBegin() { }

        /// <summary>The boss level is leaving (next level, restart, or the scene closing). Clean up here.</summary>
        protected virtual void OnBossEnd() { }

        /// <summary>The player harvested a resource tile.</summary>
        protected virtual void OnHarvested(HexCoord tile, ResourceType resource, int amount) { }

        /// <summary>The player placed an item on the board.</summary>
        protected virtual void OnItemPlaced(PlacedItem item) { }

        /// <summary>A placed item was taken off the board (picked up, or knocked off by a harvest).</summary>
        protected virtual void OnItemRemoved(PlacedItem item) { }

        /// <summary>The flood moved to a new stage (Cloudburst, Flooding, Contained...).</summary>
        protected virtual void OnStageChanged(FloodStage stage) { }

        /// <summary>The player pressed Start: the water has begun to flow.</summary>
        protected virtual void OnFloodStarted() { }

        /// <summary>The player beat the boss level.</summary>
        protected virtual void OnWon() { }

        /// <summary>The player lost the boss level.</summary>
        protected virtual void OnLost(LoseReason reason) { }

        // =====================================================================
        // Called by BossDirector
        // =====================================================================

        internal void Begin(BossContext context)
        {
            Context = context;
            IsActive = true;
            OnBossBegin();
        }

        internal void End()
        {
            if (!IsActive) return;
            IsActive = false;
            OnBossEnd();
        }

        internal void NotifyHarvested(HexCoord tile, ResourceType resource, int amount)
        {
            if (IsActive) OnHarvested(tile, resource, amount);
        }

        internal void NotifyItemPlaced(PlacedItem item)
        {
            if (IsActive) OnItemPlaced(item);
        }

        internal void NotifyItemRemoved(PlacedItem item)
        {
            if (IsActive) OnItemRemoved(item);
        }

        internal void NotifyStageChanged(FloodStage stage)
        {
            if (!IsActive) return;
            OnStageChanged(stage);
            if (stage == FloodStage.Flooding) OnFloodStarted();
        }

        internal void NotifyWon()
        {
            if (IsActive) OnWon();
        }

        internal void NotifyLost(LoseReason reason)
        {
            if (IsActive) OnLost(reason);
        }
    }
}
