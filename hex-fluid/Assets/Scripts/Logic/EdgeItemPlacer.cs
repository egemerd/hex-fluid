#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Places items you've already crafted. The craft bar arms an item with
    /// Select(); then left-click near an edge to place one from your stock,
    /// right-click an edge to pick the item back up (returned to your stock).
    ///
    /// Put it on your board object. It finds everything automatically. Fill
    /// 'palette' with your item assets (the craft bar reads this list).
    /// </summary>
    public class EdgeItemPlacer : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        public HexBoard board;
        public ItemPlacementSystem items;
        public ItemCrafting crafting;
        public Camera cam;

        [Header("Placement")]
        [Tooltip("Items that can be crafted/placed (the craft bar shows these).")]
        public List<ItemDefinition> palette = new List<ItemDefinition>();

        [Tooltip("Match your tiles' top height.")]
        public float surfaceY = 0f;

        [Tooltip("Turn off while the water flows so the player only builds beforehand.")]
        public bool placementEnabled = true;

        [Header("Audio")]
        [Tooltip("Played when any item is placed on an edge. An item's own placeSound overrides it.")]
        public SoundSO placeSound;

        /// <summary>The item currently armed for placing (set by the craft bar).</summary>
        public ItemDefinition Selected { get; private set; }

        private LevelData Level => board != null ? board.Level : null;
        private Transform BoardT => board != null ? board.transform : transform;
        private float HexSize => Level != null ? Level.hexSize : 1f;

        private void Awake()
        {
            if (board == null) board = FindObjectOfType<HexBoard>();
            if (items == null) items = FindObjectOfType<ItemPlacementSystem>();
            if (crafting == null) crafting = FindObjectOfType<ItemCrafting>();
            if (cam == null) cam = Camera.main;
        }

        public void Select(ItemDefinition def) => Selected = def;

        private void Update()
        {
            if (!placementEnabled || Level == null || items == null) return;
            if (cam == null) { cam = Camera.main; if (cam == null) return; }

            if (LeftPressed()) TryPlaceUnderMouse();
            else if (RightPressed()) TryPickUpUnderMouse();
        }

        private void TryPlaceUnderMouse()
        {
            var def = Selected;
            if (def == null) return;
            if (crafting == null || crafting.Owned(def) <= 0) return;   // nothing made to place
            if (!PickEdge(out var a, out var b)) return;
            if (!items.CanPlaceEdge(a, b)) return;

            var placed = items.PlaceEdge(def, a, b);
            if (placed == null) return;

            crafting.Consume(def);
            Sfx.PlayAt(def.placeSound != null ? def.placeSound : placeSound, placed.view.transform.position);
        }

        private void TryPickUpUnderMouse()
        {
            if (!PickEdge(out var a, out var b)) return;
            var def = items.EdgeItemDef(a, b);
            if (def == null) return;
            items.RemoveEdge(a, b);
            if (crafting != null) crafting.Add(def);   // return it to the stock
        }

        /// <summary>Finds the edge (between two existing tiles) nearest the mouse.</summary>
        private bool PickEdge(out HexCoord a, out HexCoord b)
        {
            a = default; b = default;

            Ray ray = cam.ScreenPointToRay(MousePosition());
            var plane = new Plane(BoardT.up, BoardT.TransformPoint(new Vector3(0f, surfaceY, 0f)));
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 local = BoardT.InverseTransformPoint(ray.GetPoint(enter));
            local.y = 0f;

            HexCoord center = HexCoord.FromWorld(local, HexSize);
            if (!Level.HasTile(center)) return false;

            float best = float.MaxValue;
            bool found = false;
            for (int d = 0; d < 6; d++)
            {
                var n = center.Neighbor(d);
                if (!Level.HasTile(n)) continue;
                Vector3 mid = (center.ToWorld(HexSize) + n.ToWorld(HexSize)) * 0.5f;
                float dist = (mid - local).sqrMagnitude;
                if (dist < best) { best = dist; a = center; b = n; found = true; }
            }
            return found;
        }

        // ---- Input ------------------------------------------------------------

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

        private static bool LeftPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool RightPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }
    }
}