using System;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// A tiny on-screen readout of the inventory + harvest phase — a testing aid.
    /// Shows every ResourceType and how much you hold, updated live.
    ///
    /// Put it on any object in the scene (the harvester object is fine). It finds
    /// the harvester and its inventory automatically; or assign them yourself.
    /// Replace it with real UI later — it only reads public values.
    /// </summary>
    public class InventoryDebugUI : MonoBehaviour
    {
        public HexHarvester harvester;
        public PlayerInventory inventory;

        [Tooltip("On-screen position of the panel.")]
        public Vector2 screenOffset = new Vector2(12, 12);

        private void Awake()
        {
            if (harvester == null) harvester = FindObjectOfType<HexHarvester>();
        }

        private PlayerInventory Inv =>
            inventory != null ? inventory : (harvester != null ? harvester.inventory : null);

        private void OnGUI()
        {
            var inv = Inv;
            if (inv == null) return;

            var values = Enum.GetValues(typeof(ResourceType));
            int lines = 1; // title
            foreach (ResourceType rt in values) if (rt != ResourceType.None) lines++;
            if (harvester != null) lines += 3;

            float w = 220f;
            float h = 14f + lines * 22f;
            float x = screenOffset.x;
            float y = screenOffset.y;

            GUI.Box(new Rect(x, y, w, h), "");

            var title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
            GUI.Label(new Rect(x + 10, y + 8, w - 20, 20), "Inventory", title);

            float ly = y + 32;
            foreach (ResourceType rt in values)
            {
                if (rt == ResourceType.None) continue;
                GUI.Label(new Rect(x + 12, ly, w - 24, 20), rt + ":  " + inv.Get(rt));
                ly += 22;
            }

            if (harvester != null)
            {
                ly += 6;
                GUI.Label(new Rect(x + 12, ly, w - 24, 20),
                    harvester.HarvestingActive ? "Phase:  Harvesting" : "Phase:  Harvest done");
                ly += 22;
                GUI.Label(new Rect(x + 12, ly, w - 24, 20),
                    "Harvested:  " + harvester.HarvestCount + " / " + harvester.ResourceTileCount);
            }
        }
    }
}