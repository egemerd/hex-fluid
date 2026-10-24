#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The harvest phase. At the start of the game the player clicks resource tiles
    /// to collect what's on them. Each tile is read from LevelData (its ResourceType
    /// and resourceAmount), added to the PlayerInventory, and marked harvested so it
    /// can't be taken twice. Nothing is destroyed — the asset is left untouched and
    /// "harvested" is tracked only at runtime.
    ///
    /// Setup:
    ///   - Put this on your board object (or any object), press Play.
    ///   - Assign 'level'. Assign 'board' = the transform your tiles are positioned
    ///     under (empty = this object). It uses the same picking as HexTileHover.
    ///   - An inventory is created automatically if you don't assign one.
    ///
    /// Fits the build phase you'll add next:
    ///   - Harvesting only works while HarvestingActive is true.
    ///   - When the last resource is taken (or the limit is hit) it ends the phase
    ///     and fires HarvestPhaseEnded — start your build phase there.
    ///   - Harvested(tile, type, amount) fires per harvest, for the "something happens
    ///     after" you mentioned (VFX, swap the tile model, etc.).
    /// </summary>
    public class HexHarvester : MonoBehaviour
    {
        [Header("References")]
        public LevelData level;

        [Tooltip("Transform the tiles are positioned under (coordinate space). Empty = this object.")]
        public Transform board;

        [Tooltip("Camera used for picking. Empty = Camera.main.")]
        public Camera cam;

        [Tooltip("Where harvested resources go. Empty = one is found/added on this object.")]
        public PlayerInventory inventory;

        [Header("Board surface")]
        [Tooltip("Hex radius (center to corner). Taken from 'level' if assigned.")]
        public float hexSize = 1f;

        [Tooltip("Height of the tile tops in board space, used for the mouse plane.")]
        public float surfaceY = 0f;

        [Header("Harvest phase")]
        [Tooltip("Harvesting is only allowed while this is true.")]
        public bool harvestingActive = true;

        [Tooltip("0 = unlimited. Otherwise the max number of tiles the player may harvest.")]
        [Min(0)] public int maxHarvests = 0;

        [Tooltip("End the harvest phase automatically once every resource tile is harvested (or the limit is hit).")]
        public bool autoEndWhenDone = true;

        public bool HarvestingActive => harvestingActive;
        public int HarvestCount { get; private set; }
        public int ResourceTileCount { get; private set; }
        public int HarvestsLeft => maxHarvests > 0 ? Mathf.Max(0, maxHarvests - HarvestCount) : int.MaxValue;

        /// <summary>Per harvest: (tile, resource type, amount added).</summary>
        public event Action<HexCoord, ResourceType, int> Harvested;

        /// <summary>Fires once when the harvest phase ends. Start your build phase here.</summary>
        public event Action HarvestPhaseEnded;

        /// <summary>Convenience: fires whenever a harvest changes state.</summary>
        public event Action Changed;

        private Transform _boardT;
        private readonly HashSet<HexCoord> _harvested = new HashSet<HexCoord>();
        private bool _ended;

        private void Awake()
        {
            _boardT = board != null ? board : transform;
            if (cam == null) cam = Camera.main;
            if (level != null) hexSize = level.hexSize;

            if (inventory == null)
            {
                inventory = GetComponent<PlayerInventory>();
                if (inventory == null) inventory = gameObject.AddComponent<PlayerInventory>();
            }

            CountResourceTiles();
        }

        private void OnValidate()
        {
            if (level != null) hexSize = level.hexSize;
        }

        /// <summary>Counts how many resource tiles the level has (for UI / completion).</summary>
        public void CountResourceTiles()
        {
            ResourceTileCount = 0;
            if (level == null) return;
            foreach (var t in level.tiles)
                if (t.resource != ResourceType.None) ResourceTileCount++;
        }

        // ---- Phase control ----------------------------------------------------

        /// <summary>(Re)start the harvest phase.</summary>
        public void BeginHarvesting()
        {
            harvestingActive = true;
            _ended = false;
            Changed?.Invoke();
        }

        /// <summary>End the harvest phase. Safe to call once; fires HarvestPhaseEnded.</summary>
        public void FinishHarvesting()
        {
            if (_ended) return;
            _ended = true;
            harvestingActive = false;
            Changed?.Invoke();
            HarvestPhaseEnded?.Invoke();
        }

        // ---- Queries ----------------------------------------------------------

        public bool IsHarvested(HexCoord c) => _harvested.Contains(c);

        public bool CanHarvest(HexCoord c)
        {
            if (!harvestingActive) return false;
            if (maxHarvests > 0 && HarvestCount >= maxHarvests) return false;
            if (_harvested.Contains(c)) return false;
            var t = level != null ? level.GetTile(c) : null;
            return t != null && t.resource != ResourceType.None && t.resourceAmount > 0;
        }

        public bool IsHarvestComplete()
        {
            if (maxHarvests > 0 && HarvestCount >= maxHarvests) return true;
            if (level == null || ResourceTileCount == 0) return false;
            foreach (var t in level.tiles)
                if (t.resource != ResourceType.None && !_harvested.Contains(t.coord))
                    return false;
            return true; // there were resource tiles and all are harvested
        }

        // ---- Harvesting -------------------------------------------------------

        /// <summary>Harvest a specific tile. Also called by the click handler.</summary>
        public bool TryHarvest(HexCoord c)
        {
            if (!CanHarvest(c)) return false;

            var t = level.GetTile(c);
            int amount = Mathf.Max(1, t.resourceAmount);
            ResourceType type = t.resource;

            inventory.Add(type, amount);
            _harvested.Add(c);
            HarvestCount++;

            Harvested?.Invoke(c, type, amount);
            Changed?.Invoke();

            if (autoEndWhenDone && IsHarvestComplete()) FinishHarvesting();
            return true;
        }

        private void Update()
        {
            if (!harvestingActive) return;
            if (cam == null) { cam = Camera.main; if (cam == null) return; }
            _boardT = board != null ? board : transform;

            if (!LeftClicked()) return;
            if (TryPickTile(out var c)) TryHarvest(c);
        }

        /// <summary>The tile under the mouse (board plane raycast), if any exists there.</summary>
        public bool TryPickTile(out HexCoord tile)
        {
            tile = default;
            Ray ray = cam.ScreenPointToRay(MousePosition());
            Vector3 planePoint = _boardT.TransformPoint(new Vector3(0f, surfaceY, 0f));
            var plane = new Plane(_boardT.up, planePoint);
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 world = ray.GetPoint(enter);
            Vector3 local = _boardT.InverseTransformPoint(world);
            tile = HexCoord.FromWorld(local, hexSize);
            return level == null || level.HasTile(tile);
        }

        // ---- Input (old Input Manager or new Input System) --------------------

        private static Vector3 MousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return Vector3.zero;
            Vector2 p = Mouse.current.position.ReadValue();
            return new Vector3(p.x, p.y, 0f);
#else
            return Input.mousePosition;
#endif
        }

        private static bool LeftClicked()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}