using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Maps each resource to the prefab that drops onto the table when you harvest it.
    /// One asset, shared everywhere. Adding a new resource = add a row and assign its
    /// prefab — no code changes anywhere.
    ///
    /// Create via: Assets > Create > HexFluid > Resource Drop Set.
    /// </summary>
    [CreateAssetMenu(fileName = "NewResourceDropSet", menuName = "HexFluid/Resource Drop Set", order = 4)]
    public class ResourceDropSet : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ResourceType resource;
            public GameObject prefab;
        }

        [Tooltip("The falling prefab per resource (Wood -> log, Stone -> rock, ...).")]
        public List<Entry> drops = new List<Entry>();

        [Tooltip("Used for any resource without its own entry. Optional.")]
        public GameObject defaultPrefab;

        /// <summary>The drop prefab for a resource, or the default (may be null).</summary>
        public GameObject GetPrefab(ResourceType resource)
        {
            for (int i = 0; i < drops.Count; i++)
                if (drops[i].resource == resource && drops[i].prefab != null)
                    return drops[i].prefab;
            return defaultPrefab;
        }
    }
}