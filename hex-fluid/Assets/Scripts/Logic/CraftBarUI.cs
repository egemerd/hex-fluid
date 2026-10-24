using System.Text;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The left-side craft bar: one button per item. Click a button to craft one
    /// (spends the recipe) and arm it; then click an edge to place it. The armed
    /// item is highlighted, and each button shows its cost and how many you hold.
    ///
    /// A testing UI (IMGUI). Put it on any object; it finds the rest automatically.
    /// Swap it for a real Canvas later — it only calls the public methods.
    /// </summary>
    public class CraftBarUI : MonoBehaviour
    {
        public EdgeItemPlacer placer;
        public ItemCrafting crafting;

        [Tooltip("Left/top position of the bar.")]
        public Vector2 screenOffset = new Vector2(12, 120);

        public float buttonWidth = 170f;
        public float buttonHeight = 52f;

        private GUIStyle _btn, _title;

        private void Awake()
        {
            if (placer == null) placer = FindObjectOfType<EdgeItemPlacer>();
            if (crafting == null) crafting = FindObjectOfType<ItemCrafting>();
        }

        private void OnGUI()
        {
            if (placer == null || crafting == null || placer.palette == null) return;
            EnsureStyles();

            float x = screenOffset.x;
            float y = screenOffset.y;

            GUI.Label(new Rect(x, y, buttonWidth, 22), "Craft & place", _title);
            y += 26;

            for (int i = 0; i < placer.palette.Count; i++)
            {
                var def = placer.palette[i];
                if (def == null) continue;

                bool affordable = crafting.CanCraft(def);
                int owned = crafting.Owned(def);
                bool selected = placer.Selected == def;

                // highlight the armed item
                Color prev = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);

                var r = new Rect(x, y, buttonWidth, buttonHeight);
                // clickable if you can craft one OR already hold some to arm
                GUI.enabled = affordable || owned > 0;
                if (GUI.Button(r, Label(def, affordable, owned), _btn))
                    OnClick(def, affordable);
                GUI.enabled = true;

                GUI.backgroundColor = prev;
                y += buttonHeight + 6;
            }

            GUI.Label(new Rect(x, y + 2, buttonWidth + 60, 40),
                "Left-click an edge to place.\nRight-click to pick back up.", _btn);
        }

        private void OnClick(ItemDefinition def, bool affordable)
        {
            // Make one if we can; otherwise just arm what we already hold.
            if (affordable) crafting.Craft(def);
            placer.Select(def);
        }

        private static string Label(ItemDefinition def, bool affordable, int owned)
        {
            var sb = new StringBuilder();
            sb.Append(def.DisplayName);
            if (owned > 0) sb.Append("  x").Append(owned);
            sb.Append('\n');
            sb.Append(CostText(def));
            if (!affordable && owned == 0) sb.Append("  (need more)");
            return sb.ToString();
        }

        private static string CostText(ItemDefinition def)
        {
            if (def.recipe == null || def.recipe.Count == 0) return "free";
            var sb = new StringBuilder();
            for (int i = 0; i < def.recipe.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(def.recipe[i].amount).Append(' ').Append(def.recipe[i].resource);
            }
            return sb.ToString();
        }

        private void EnsureStyles()
        {
            if (_btn != null) return;
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        }
    }
}
