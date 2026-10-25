#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using TMPro;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// A recipe paper lying on the table, one per buildable item (Barricade, Bridge,
    /// Tunnel...). It replaces the old on-screen craft bar:
    ///   - Hover it: it grows a little.
    ///   - Left-click: choose it for building (an outline shows it's chosen). If you
    ///     hold none of that item yet, one is made from your resources and falls onto
    ///     the table (CraftedItemDropSpawner).
    ///   - Left-click it again: put it down - you're back to harvesting.
    ///   - Right-click: make one more (another one falls onto the table).
    /// While it's chosen, clicking an edge on the board places one from your stock.
    /// When the stock runs out it's put down automatically (see EdgeItemPlacer).
    /// Greyed out while you can't make one and hold none. Next to it, a number shows
    /// how many you hold (made but not placed); it pops when it changes.
    ///
    /// Setup:
    ///   - Make a sprite on the table (GameObject > 2D Object > Sprite, lay it flat by
    ///     rotating X to 90) with your paper picture, and add this. Assign 'item'.
    ///   - A BoxCollider the size of the sprite is added if it has no collider.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class CraftPaper : MonoBehaviour
    {
        [Tooltip("The item this paper makes and arms.")]
        public ItemDefinition item;

        [Header("References (auto-found if empty)")]
        public EdgeItemPlacer placer;
        public ItemCrafting crafting;
        [Tooltip("Camera used for picking. Empty = Camera.main.")]
        public Camera cam;

        [Header("Hover")]
        [Tooltip("Size while the mouse is over it (1 = no change).")]
        public float hoverScale = 1.12f;

        [Tooltip("Higher = snappier.")]
        public float scaleSpeed = 14f;

        [Header("Chosen (outline)")]
        public Color outlineColor = new Color(1f, 0.85f, 0.3f, 1f);

        [Tooltip("Outline thickness, as a fraction of the paper's size.")]
        [Range(0f, 0.3f)] public float outlineWidth = 0.06f;

        [Header("Count (how many you hold)")]
        public bool showCount = true;

        [Tooltip("Optional: your own text to show the count in. Empty = one is made next to the paper.")]
        public TMP_Text countText;

        [Tooltip("What's shown; {0} = how many you hold.")]
        public string countFormat = "x{0}";

        [Tooltip("Show the number even when you hold none.")]
        public bool showZero = true;

        [Tooltip("Where the made number sits, in the paper's own space, from its right edge.")]
        public Vector2 countOffset = new Vector2(0.1f, 0f);

        public float countFontSize = 3f;
        public Color countColor = Color.white;

        [Tooltip("How much the number swells when it changes (0 = no pop).")]
        public float countPop = 0.4f;

        [Header("Unavailable")]
        [Tooltip("Tint while you can't make one and hold none.")]
        public Color unavailableTint = new Color(0.55f, 0.55f, 0.55f, 1f);

        [Header("Audio")]
        [Tooltip("Played when the paper is chosen / put down. Optional.")]
        public SoundSO selectSound;

        [Tooltip("Played when an item is made. Optional.")]
        public SoundSO craftSound;

        [Tooltip("Played when you can't make one (not enough resources). Optional.")]
        public SoundSO deniedSound;

        /// <summary>True while the mouse is over this paper.</summary>
        public bool IsHovered { get; private set; }

        /// <summary>True while this paper's item is the one being built with.</summary>
        public bool IsChosen => placer != null && item != null && placer.Selected == item;

        private SpriteRenderer _sprite;
        private SpriteRenderer _outline;
        private Collider _collider;
        private Vector3 _baseScale;
        private Color _baseColor;
        private int _shownCount = -1;
        private float _pop;          // 1 right after the count changes, fades to 0
        private Vector3 _countScale;

        private void Awake()
        {
            _sprite = GetComponent<SpriteRenderer>();
            _baseScale = transform.localScale;
            _baseColor = _sprite.color;

            if (placer == null) placer = FindFirstObjectByType<EdgeItemPlacer>();
            if (crafting == null) crafting = FindFirstObjectByType<ItemCrafting>();
            if (cam == null) cam = Camera.main;

            _collider = GetComponent<Collider>();
            if (_collider == null && _sprite.sprite != null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                Bounds b = _sprite.sprite.bounds;
                box.center = b.center;
                box.size = new Vector3(b.size.x, b.size.y, 0.02f);
                _collider = box;
            }

            BuildOutline();
            if (showCount) BuildCount();
        }

        /// <summary>The number next to the paper: a flat 3D text lying with it, just right of its edge.</summary>
        private void BuildCount()
        {
            if (countText == null)
            {
                var go = new GameObject("Count", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                Bounds b = _sprite.sprite != null ? _sprite.sprite.bounds : new Bounds(Vector3.zero, Vector3.one);
                var rect = (RectTransform)go.transform;
                rect.pivot = new Vector2(0f, 0.5f);                 // grows to the right of the paper
                rect.sizeDelta = new Vector2(b.size.x * 2f, b.size.y);
                rect.localPosition = new Vector3(b.max.x + countOffset.x, b.center.y + countOffset.y, -0.002f); // just in front

                var text = go.AddComponent<TextMeshPro>();
                if (TMP_Settings.defaultFontAsset == null)
                    text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                text.fontSize = countFontSize;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Left;
                text.color = countColor;
                text.sortingOrder = _sprite.sortingOrder + 1;
                countText = text;
            }
            _countScale = countText.transform.localScale;
        }

        private void UpdateCount()
        {
            if (countText == null) return;
            int owned = item != null && crafting != null ? crafting.Owned(item) : 0;
            if (owned != _shownCount)
            {
                if (_shownCount >= 0) _pop = 1f;                    // not on the first show
                _shownCount = owned;
                countText.text = owned > 0 || showZero ? string.Format(countFormat, owned) : "";
            }

            _pop = Mathf.MoveTowards(_pop, 0f, Time.unscaledDeltaTime * 4f);
            countText.transform.localScale = _countScale * (1f + countPop * _pop * _pop);
        }

        /// <summary>
        /// The outline: a copy of the sprite in the outline colour, a bit bigger,
        /// drawn just behind the paper, so it shows as a coloured rim around it.
        /// </summary>
        private void BuildOutline()
        {
            if (_sprite.sprite == null) return;
            var go = new GameObject("Outline");
            go.transform.SetParent(transform, false);

            float s = 1f + outlineWidth * 2f;
            Vector3 c = _sprite.sprite.bounds.center;
            go.transform.localPosition = c * (1f - s) + new Vector3(0f, 0f, 0.002f); // grow around the middle, just behind
            go.transform.localScale = new Vector3(s, s, 1f);

            _outline = go.AddComponent<SpriteRenderer>();
            _outline.sprite = _sprite.sprite;
            _outline.color = outlineColor;
            _outline.sortingLayerID = _sprite.sortingLayerID;
            _outline.sortingOrder = _sprite.sortingOrder - 1;
            _outline.sharedMaterial = _sprite.sharedMaterial;
            _outline.enabled = false;
        }

        private void Update()
        {
            if (cam == null) { cam = Camera.main; if (cam == null) return; }

            IsHovered = !InputBlocker.Blocked && RaycastHitsSelf();
            if (IsHovered)
            {
                if (LeftPressed()) OnLeftClick();
                else if (RightPressed()) OnRightClick();
            }

            // Hover: ease the size (unscaled, so it still reacts during a hit-stop).
            float target = IsHovered ? hoverScale : 1f;
            float k = 1f - Mathf.Exp(-scaleSpeed * Time.unscaledDeltaTime);
            transform.localScale = Vector3.Lerp(transform.localScale, _baseScale * target, k);

            // Chosen: outline. Can't make one and hold none: greyed out.
            if (_outline != null) _outline.enabled = IsChosen;
            bool available = item != null && crafting != null && (crafting.Owned(item) > 0 || crafting.CanCraft(item));
            _sprite.color = available ? _baseColor : _baseColor * unavailableTint;

            if (showCount) UpdateCount();
        }

        // ---- Clicks ------------------------------------------------------------

        /// <summary>Choose it (making one if you hold none), or put it down if it's already chosen.</summary>
        private void OnLeftClick()
        {
            if (item == null || placer == null || crafting == null) return;

            if (IsChosen)
            {
                placer.Select(null);                             // put it down: back to harvesting
                Sfx.Play(selectSound);
                return;
            }

            if (crafting.Owned(item) <= 0 && !Craft()) return;  // nothing to build with
            placer.Select(item);
            Sfx.Play(selectSound);
        }

        /// <summary>Make one more (it falls onto the table). Doesn't change what's chosen.</summary>
        private void OnRightClick()
        {
            if (item == null || crafting == null) return;
            Craft();
        }

        private bool Craft()
        {
            if (crafting.Craft(item))
            {
                Sfx.Play(craftSound);
                return true;
            }
            Sfx.Play(deniedSound);
            TileShake.On(gameObject, 0.2f, 0.04f);               // "can't": a little shake
            return false;
        }

        private bool RaycastHitsSelf()
        {
            if (_collider == null) return false;
            Ray ray = cam.ScreenPointToRay(MousePosition());
            return _collider.Raycast(ray, out _, 2000f);
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
