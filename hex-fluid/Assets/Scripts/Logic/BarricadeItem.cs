using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Barricade — placed on an edge, blocks water from crossing it.
    /// Default recipe: 1 Wood.
    ///
    /// Create via: Assets > Create > HexFluid > Items > Barricade.
    /// </summary>
    [CreateAssetMenu(fileName = "Barricade", menuName = "HexFluid/Items/Barricade", order = 10)]
    public class BarricadeItem : ItemDefinition
    {
        public override EdgeFlow GetEdgeFlow(HexCoord from, HexCoord to) => EdgeFlow.Block;

        private void Reset()
        {
            displayName = "Barricade";
            placement = ItemPlacement.Edge;
            color = new Color(0.55f, 0.35f, 0.18f);
            recipe = new List<ResourceCost> { new ResourceCost { resource = ResourceType.Wood, amount = 1 } };
        }
    }
}