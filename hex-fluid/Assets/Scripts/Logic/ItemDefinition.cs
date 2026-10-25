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

        [Tooltip("Turns the model to fit the edge, on top of the prefab's own rotation. Edge space: X runs along the edge, Z goes across it (from one tile to the other), Y is up. e.g. (0, 90, 0) turns it a quarter.")]
        public Vector3 modelRotation = Vector3.zero;

        [Tooltip("Moves the model from the middle of the edge, in the same edge space (Y = up).")]
        public Vector3 modelOffset = Vector3.zero;

        [Tooltip("Falls onto the table for each one you've made but not placed yet (see CraftedItemDropSpawner). Empty = 'model'.")]
        public GameObject dropModel;

        [Tooltip("Played when one lands on the table. Optional.")]
        public SoundSO dropLandSound;

        [Header("Audio")]
        [Tooltip("Played when this item is placed. Empty = the placer's shared place sound.")]
        public SoundSO placeSound;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>
        /// How this item treats water crossing the edge it's on, from 'from' to 'to'.
        /// Override this per item. Default = no effect.
        /// </summary>
        public virtual EdgeFlow GetEdgeFlow(HexCoord from, HexCoord to) => EdgeFlow.Default;

        // ---- For items that cover more than one edge (e.g. a tunnel) -----------
        // Simple items ignore everything below: they cover the edge they're placed on,
        // and water crossing them lands on the neighbour across it.

        /// <summary>
        /// The edges this item covers when the player places it on the edge a->b
        /// ('a' = the tile they clicked in). Each pair is (outside tile, tile it faces);
        /// the model is shown on every one. Default: just a->b.
        /// </summary>
        public virtual void GetFootprint(HexCoord a, HexCoord b, List<EdgePair> edges) => edges.Add(new EdgePair(a, b));

        /// <summary>
        /// Like GetEdgeFlow(from, to), but knowing exactly where this item was placed.
        /// Override when the answer depends on that (a tunnel's two ends act differently
        /// from the tile in its middle). Default: GetEdgeFlow(from, to).
        /// </summary>
        public virtual EdgeFlow GetEdgeFlow(PlacedItem placed, HexCoord from, HexCoord to) => GetEdgeFlow(from, to);

        /// <summary>
        /// Where water that a Force edge sends from 'from' towards 'to' ends up.
        /// Default: 'to', the neighbour across the edge. A tunnel sends it further.
        /// </summary>
        public virtual HexCoord GetFlowTarget(PlacedItem placed, HexCoord from, HexCoord to) => to;
    }

    /// <summary>An edge between two neighbouring tiles, with a direction (from -> to).</summary>
    [Serializable]
    public struct EdgePair
    {
        public HexCoord from, to;
        public EdgePair(HexCoord from, HexCoord to) { this.from = from; this.to = to; }
        public EdgeKey Key => EdgeKey.Between(from, to);
    }
}