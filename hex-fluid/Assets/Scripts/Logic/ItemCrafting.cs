using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Crafting + a stock of made-but-not-yet-placed items. Clicking a craft button
    /// calls Craft(): it spends the recipe from the PlayerInventory and adds one to
    /// the owned stock. Placing an item consumes one from the stock.
    ///
    /// Put it on your board object (or any object). It finds the inventory itself.
    /// </summary>
    public class ItemCrafting : MonoBehaviour
    {
        public PlayerInventory inventory;

        private readonly Dictionary<ItemDefinition, int> _owned = new Dictionary<ItemDefinition, int>();

        /// <summary>Fires when crafted/placed counts change.</summary>
        public event Action Changed;

        private void Awake()
        {
            if (inventory == null) inventory = FindObjectOfType<PlayerInventory>();
        }

        public int Owned(ItemDefinition def) =>
            def != null && _owned.TryGetValue(def, out var n) ? n : 0;

        public bool CanCraft(ItemDefinition def)
        {
            if (def == null || inventory == null) return false;
            foreach (var c in def.recipe)
                if (inventory.Get(c.resource) < c.amount) return false;
            return true;
        }

        /// <summary>Spend the recipe and add one to the owned stock.</summary>
        public bool Craft(ItemDefinition def)
        {
            if (!CanCraft(def)) return false;
            foreach (var c in def.recipe) inventory.TrySpend(c.resource, c.amount);
            _owned[def] = Owned(def) + 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Use one from the stock (on placement).</summary>
        public bool Consume(ItemDefinition def)
        {
            if (Owned(def) <= 0) return false;
            _owned[def] = Owned(def) - 1;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Put one back in the stock (e.g. when you pick a placed item up).</summary>
        public void Add(ItemDefinition def, int count = 1)
        {
            if (def == null || count <= 0) return;
            _owned[def] = Owned(def) + count;
            Changed?.Invoke();
        }

        /// <summary>Empty the whole stock (e.g. when a new level starts).</summary>
        public void Clear()
        {
            _owned.Clear();
            Changed?.Invoke();
        }
    }
}
