using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// What the player is holding. Keyed by ResourceType, so when you add a new
    /// value to that enum it works here with no changes. The build phase you add
    /// later reads this to know what you can craft.
    ///
    /// Put it on the same object as HexHarvester (it will add one automatically
    /// if you don't), or on its own object and drag it into the harvester.
    /// </summary>
    public class PlayerInventory : MonoBehaviour
    {
        private readonly Dictionary<ResourceType, int> _amounts = new Dictionary<ResourceType, int>();

        /// <summary>Fires whenever the held amounts change.</summary>
        public event Action Changed;

        public IReadOnlyDictionary<ResourceType, int> Amounts => _amounts;

        public int Get(ResourceType r) => _amounts.TryGetValue(r, out var n) ? n : 0;

        public void Add(ResourceType r, int amount)
        {
            if (r == ResourceType.None || amount <= 0) return;
            _amounts[r] = Get(r) + amount;
            Changed?.Invoke();
        }

        public bool CanSpend(ResourceType r, int amount) => Get(r) >= amount;

        public bool TrySpend(ResourceType r, int amount)
        {
            if (!CanSpend(r, amount)) return false;
            _amounts[r] -= amount;
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            _amounts.Clear();
            Changed?.Invoke();
        }
    }
}