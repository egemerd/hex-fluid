using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Bridge — placed on an edge, carries water across it to the next hex even
    /// when that hex isn't downhill. Lets you route water where it couldn't flow
    /// on its own. Default recipe: 2 Wood.
    ///
    /// Create via: Assets > Create > HexFluid > Items > Bridge.
    /// </summary>
    [CreateAssetMenu(fileName = "Bridge", menuName = "HexFluid/Items/Bridge", order = 11)]
    public class BridgeItem : ItemDefinition
    {
        public override EdgeFlow GetEdgeFlow(HexCoord from, HexCoord to) => EdgeFlow.Force;

        private void Reset()
        {
            displayName = "Bridge";
            placement = ItemPlacement.Edge;
            color = new Color(0.75f, 0.6f, 0.35f);
            recipe = new List<ResourceCost> { new ResourceCost { resource = ResourceType.Wood, amount = 2 } };
        }
    }
}