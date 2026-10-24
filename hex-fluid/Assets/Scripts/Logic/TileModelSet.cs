using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Maps tile types to models, so each resource and each role gets its own hex
    /// model automatically. One asset, shared by every level, so the look stays
    /// consistent and you change a model in one place.
    ///
    /// Create via: Assets > Create > HexFluid > Tile Model Set.
    /// Then assign it to HexBoard's 'Model Set' slot.
    ///
    /// Lookup priority (handled by the board):
    ///   1. A tile's explicit modelIndex override (if you set one)
    ///   2. Resource model  (tile.resource != None)
    ///   3. Role model      (City, Source, Goal, ...)
    ///   4. This set's defaultModel, then the board's defaultTileModel
    /// </summary>
    [CreateAssetMenu(fileName = "NewTileModelSet", menuName = "HexFluid/Tile Model Set", order = 3)]
    public class TileModelSet : ScriptableObject
    {
        [Serializable]
        public struct ResourceModel
        {
            public ResourceType resource;
            public GameObject model;
        }

        [Serializable]
        public struct RoleModel
        {
            public TileRole role;
            public GameObject model;
        }

        [Tooltip("Model per resource type. Wood -> forest tile, Stone -> rock tile, etc.")]
        public List<ResourceModel> resourceModels = new List<ResourceModel>();

        [Tooltip("Model per role. City -> city tile, Source -> spring tile, etc.")]
        public List<RoleModel> roleModels = new List<RoleModel>();

        [Tooltip("Used when nothing above matches. Optional — leave empty to fall back to the board's defaultTileModel.")]
        public GameObject defaultModel;

        /// <summary>
        /// Best model for this tile by type, or null if nothing (including this set's
        /// default) matches — the board then uses its own defaultTileModel.
        /// </summary>
        public GameObject GetModel(HexTileData tile)
        {
            if (tile == null) return defaultModel;

            // 1. Resource wins (a resource tile is the thing sitting on the hex).
            if (tile.resource != ResourceType.None)
            {
                for (int i = 0; i < resourceModels.Count; i++)
                    if (resourceModels[i].resource == tile.resource && resourceModels[i].model != null)
                        return resourceModels[i].model;
            }

            // 2. Then role (City / Source / Goal / Blank).
            for (int i = 0; i < roleModels.Count; i++)
                if (roleModels[i].role == tile.role && roleModels[i].model != null)
                    return roleModels[i].model;

            // 3. This set's own default (may be null).
            return defaultModel;
        }
    }
}