using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    public enum ItemPlacement { Edge, Tile }

    /// <summary>
    /// What an item does to water trying to cross the edge it sits on.
    ///   Default = item has no opinion; the normal rules apply (downhill only).
    ///   Block   = water cannot cross this edge at all.
    ///   Force   = funnel: ALL of the tile's water goes through this edge (ignoring
    ///             the downhill rule), and the tile's other directions are cut off.
    /// Add new values here only for a genuinely new behaviour; existing items
    /// keep working because they return what they always returned.
    /// </summary>
    public enum EdgeFlow { Default, Block, Force }

    [Serializable]
    public struct ResourceCost
    {
        public ResourceType resource;
        [Min(1)] public int amount;
    }

    /// <summary>
    /// Base for every craftable, placeable item. Shared data (recipe, placement,
    /// model) lives here; each concrete item is its own small subclass that
    /// overrides ONE method — GetEdgeFlow — to say how it treats water.
    ///
    /// Adding a new item = one new subclass + one asset. The water never changes,
    /// because it only ever asks GetEdgeFlow and does what the item says.
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

        [Header("Audio")]
        [Tooltip("Played when this item is placed. Empty = the placer's shared place sound.")]
        public SoundSO placeSound;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>
        /// How this item treats water crossing the edge it's on, from 'from' to 'to'.
        /// Override this per item. Default = no effect.
        /// </summary>
        public virtual EdgeFlow GetEdgeFlow(HexCoord from, HexCoord to) => EdgeFlow.Default;
    }
}