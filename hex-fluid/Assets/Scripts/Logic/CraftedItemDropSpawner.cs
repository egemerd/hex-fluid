using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Shows what you've crafted but not placed yet as physical objects on the table,
    /// the same way ResourceDropSpawner shows wood and stone: make a barricade and one
    /// falls into this area; place it on the board and one shrinks away; pick it back
    /// up and it falls in again. It only listens to ItemCrafting.Changed.
    ///
    /// Each item's object is its 'dropModel' (or its board 'model' if that's empty).
    ///
    /// Setup:
    ///   - Put this on any object. Assign 'table' and move / size the area with
    ///     'spawnAreaOffset' and 'spawnRadius' (select it to see the circles).
    /// </summary>
    public class CraftedItemDropSpawner : TableDropPile<ItemDefinition>
    {
        [Header("What to show (auto-found if empty)")]
        public ItemCrafting crafting;

        protected override string RootName => "CraftedItemDrops";

        protected override void Awake()
        {
            base.Awake();
            if (crafting == null) crafting = FindFirstObjectByType<ItemCrafting>();
        }

        private void OnEnable()
        {
            if (crafting == null) return;
            crafting.Changed += Sync;
            Sync();
        }

        private void OnDisable()
        {
            if (crafting != null) crafting.Changed -= Sync;
        }

        protected override IEnumerable<KeyValuePair<ItemDefinition, int>> Counts() =>
            crafting != null ? crafting.Stock : System.Array.Empty<KeyValuePair<ItemDefinition, int>>();

        protected override GameObject PrefabFor(ItemDefinition item) => item.dropModel != null ? item.dropModel : item.model;

        protected override SoundSO LandSoundFor(ItemDefinition item) => item.dropLandSound;
    }
}
