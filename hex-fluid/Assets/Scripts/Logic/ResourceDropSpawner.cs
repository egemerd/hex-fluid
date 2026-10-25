using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Shows the PlayerInventory as physical drops on a table. It only listens to
    /// inventory.Changed and keeps the number of drops per resource equal to what you
    /// hold: gain wood -> logs fall onto the table, spend wood (crafting, or anything
    /// else) -> that many logs shrink away. Harvesting, crafting and any future system
    /// never need to know this script exists. Which prefab falls comes from a
    /// ResourceDropSet, so a new resource just needs a row there.
    ///
    /// The dropping itself (area, landing, settling, staying inside) is shared with
    /// CraftedItemDropSpawner, in TableDropPile.
    ///
    /// Setup:
    ///   - Put this on your board object (or any object).
    ///   - Assign 'dropSet' and 'table' (the surface they land on — give it a Collider!).
    ///   - Move the spawn area with 'spawnAreaOffset' (in the table's local space, e.g.
    ///     negative X for the left side) and size it with 'spawnRadius'.
    /// </summary>
    public class ResourceDropSpawner : TableDropPile<ResourceType>
    {
        [Header("What to show (auto-found if empty)")]
        [Tooltip("The inventory these drops show. Empty = the scene's PlayerInventory.")]
        public PlayerInventory inventory;
        public ResourceDropSet dropSet;

        protected override string RootName => "ResourceDrops";

        protected override void Awake()
        {
            base.Awake();
            if (inventory == null) inventory = FindFirstObjectByType<PlayerInventory>();
        }

        private void OnEnable()
        {
            if (inventory == null) return;
            inventory.Changed += Sync;
            Sync();
        }

        private void Start()
        {
            // The harvester may only create its inventory in its own Awake — pick it up late.
            if (inventory != null) return;
            var harvester = FindFirstObjectByType<HexHarvester>();
            inventory = harvester != null ? harvester.inventory : FindFirstObjectByType<PlayerInventory>();
            if (inventory == null) return;
            inventory.Changed += Sync;
            Sync();
        }

        private void OnDisable()
        {
            if (inventory != null) inventory.Changed -= Sync;
        }

        protected override IEnumerable<KeyValuePair<ResourceType, int>> Counts() =>
            inventory != null ? inventory.Amounts : System.Array.Empty<KeyValuePair<ResourceType, int>>();

        protected override bool IsShown(ResourceType kind) => kind != ResourceType.None;

        protected override GameObject PrefabFor(ResourceType kind) => dropSet != null ? dropSet.GetPrefab(kind) : null;

        protected override SoundSO LandSoundFor(ResourceType kind) => dropSet != null ? dropSet.GetLandSound(kind) : null;
    }
}
