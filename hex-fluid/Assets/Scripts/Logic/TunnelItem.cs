using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Tunnel - like a bridge, but it goes UNDER the next tile. Placed on an edge, it
    /// runs in a straight line under the tile across that edge and comes out on the
    /// far side, so it covers two edges and shows its model on both:
    ///
    ///     [A] --entrance-- [B] --exit-- [C]
    ///
    /// Water in A goes straight to C (and water in C to A), skipping B: B stays dry.
    /// Like a bridge it's a funnel (Force): the end tile's water all goes into the
    /// tunnel, ignoring the downhill rule. The water that comes out on the other end
    /// flows on normally. B itself is untouched: its own water flows across those
    /// edges as if the tunnel weren't there.
    ///
    /// Placing: click inside the tile the tunnel starts from (A), near the edge to
    /// the tile it goes under (B). It needs a tile on the far side (C).
    /// Default recipe: 2 Stone.
    ///
    /// Create via: Assets > Create > HexFluid > Items > Tunnel.
    /// </summary>
    [CreateAssetMenu(fileName = "Tunnel", menuName = "HexFluid/Items/Tunnel", order = 12)]
    public class TunnelItem : ItemDefinition
    {
        /// <summary>The tile the tunnel comes out at when it starts at 'a' and goes under 'b'.</summary>
        public static HexCoord FarEnd(HexCoord a, HexCoord b) => b + (b - a);

        /// <summary>Entrance a->b and exit FarEnd->b, both facing the tile it runs under.</summary>
        public override void GetFootprint(HexCoord a, HexCoord b, List<EdgePair> edges)
        {
            edges.Add(new EdgePair(a, b));
            edges.Add(new EdgePair(FarEnd(a, b), b));
        }

        /// <summary>From either end towards the middle: into the tunnel (Force). Anything else: no effect.</summary>
        public override EdgeFlow GetEdgeFlow(PlacedItem placed, HexCoord from, HexCoord to)
        {
            HexCoord far = FarEnd(placed.a, placed.b);
            bool fromEnd = from == placed.a || from == far;
            return fromEnd && to == placed.b ? EdgeFlow.Force : EdgeFlow.Default;
        }

        /// <summary>In at one end, out at the other: the middle tile is skipped.</summary>
        public override HexCoord GetFlowTarget(PlacedItem placed, HexCoord from, HexCoord to)
        {
            HexCoord far = FarEnd(placed.a, placed.b);
            if (from == placed.a) return far;
            if (from == far) return placed.a;
            return to;
        }

        private void Reset()
        {
            displayName = "Tunnel";
            placement = ItemPlacement.Edge;
            color = new Color(0.35f, 0.33f, 0.32f);
            recipe = new List<ResourceCost> { new ResourceCost { resource = ResourceType.Stone, amount = 2 } };
        }
    }
}
