using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    public enum ItemPlacement { Edge, Tile }

    [Serializable]
    public struct ResourceCost
    {
        public ResourceType resource;
        [Min(1)] public int amount;
    }

    /// <summary>
    /// Base for every craftable, placeable item. Shared data (recipe, placement,
    /// model) lives here; each concrete item is its own small subclass that
    /// overrides only the water hooks it cares about. Adding a new item = one new
    /// subclass + one asset. Nothing else changes.
    ///
    /// Water hooks the simulation calls per edge:
    ///   BlocksEdge  -> water cannot cross this edge (Barricade)
    ///   ForcesEdge  -> water crosses even if the far tile isn't downhill (Bridge)
    /// Add new virtuals here for future effects (one-way valves, tile effects...).
    /// </summary>
    public abstract class ItemDefinition : ScriptableObject
    {
        [Header("Item")]
        public string displayName = "Item";
        public ItemPlacement placement = ItemPlacement.Edge;

        [Tooltip("Resources needed to make one.")]
        public List<ResourceCost> recipe = new List<ResourceCost>();

        [Header("Visual")]
        [Tooltip("Model placed on the board. Empty = a placeholder bar.")]
        public GameObject model;

        public Color color = new Color(0.6f, 0.45f, 0.25f);

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        // ---- Water effect hooks: override per item ----

        /// <summary>Water cannot cross an edge holding this item.</summary>
        public virtual bool BlocksEdge(HexCoord from, HexCoord to) => false;

        /// <summary>Water crosses this edge to the far tile even if it isn't downhill.</summary>
        public virtual bool ForcesEdge(HexCoord from, HexCoord to) => false;
    }
}
