using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HexFluid
{
    /// <summary>
    /// A wobbling sticker in a screen corner that announces the flood stage:
    /// "Cloudburst!" when the source wells up, "Flooding commenced" when the water
    /// runs, then "Flood contained!" or "City flooded!". It listens to
    /// WaterSimulation.StageChanged and pops in again on every change.
    ///
    /// Self-contained: it builds its own overlay canvas, so it needs no scene setup.
    /// WaterSimulation creates one automatically if the scene has none; add your own
    /// to the scene instead if you want to tweak the look in the inspector.
    /// </summary>
    public class FloodStageBadge : MonoBehaviour
    {
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Serializable]
        public struct StageLook
        {
            public FloodStage stage;
            [Tooltip("Caption under the sticker. Empty = hide the badge in this stage.")]
            public string label;
            [Tooltip("Sticker for this stage. Empty = the default sticker.")]
            public Sprite sticker;
            public Color labelColor;
        }

        [Header("References")]
        [Tooltip("The simulation to follow. Empty = the scene's WaterSimulation.")]
        public WaterSimulation simulation;

        [Header("Look")]
        [Tooltip("Sticker used for every stage that has none of its own. Empty = Resources/WaterSticker.")]
        public Sprite defaultSticker;

        public Corner corner = Corner.TopRight;

        [Tooltip("Gap to the screen edges, in pixels at 1920x1080 (scales with the screen).")]
        public Vector2 margin = new Vector2(48f, 40f);

        [Tooltip("Sticker height, in pixels at 1920x1080.")]
        public float stickerHeight = 180f;

        [Tooltip("Caption font size, in pixels at 1920x1080.")]
        public float labelSize = 38f;

        public StageLook[] stages =
        {
            new StageLook { stage = FloodStage.Calm,        label = "",                   labelColor = Color.white },
            new StageLook { stage = FloodStage.Cloudburst,  label = "Cloudburst!",        labelColor = new Color(0.78f, 0.92f, 1f) },
            new StageLook { stage = FloodStage.Flooding,    label = "Flooding commenced", labelColor = new Color(0.62f, 0.86f, 1f) },
            new StageLook { stage = FloodStage.Contained,   label = "Flood contained!",   labelColor = new Color(0.62f, 1f, 0.72f) },
            new StageLook { stage = FloodStage.CityFlooded, label = "City flooded!",      labelColor = new Color(1f, 0.58f, 0.48f) },
        };

        private CanvasGroup _group;
        private Image _image;
        private TextMeshProUGUI _label;
        private StickerWobble _wobble;

        private void Start()
        {
            if (simulation == null) simulation = FindFirstObjectByType<WaterSimulation>();
            if (defaultSticker == null) defaultSticker = Resources.Load<Sprite>("WaterSticker");
            Build();

            if (simulation == null) { Show(FloodStage.Calm); return; }
            simulation.StageChanged += Show;
            Show(simulation.Stage);
        }

        private void OnDestroy()
        {
            if (simulation != null) simulation.StageChanged -= Show;
        }

        /// <summary>Shows the sticker and caption for a stage (hidden if it has no caption).</summary>
        public void Show(FloodStage stage)
        {
            if (_group == null) return;
            StageLook look = LookFor(stage);
            bool visible = !string.IsNullOrEmpty(look.label);
            _group.alpha = visible ? 1f : 0f;
            if (!visible) return;

            Sprite sprite = look.sticker != null ? look.sticker : defaultSticker;
            _image.sprite = sprite;
            _image.enabled = sprite != null;
            _label.text = look.label;
            _label.color = look.labelColor.a > 0f ? look.labelColor : Color.white;
            _wobble.Pop();
        }

        private StageLook LookFor(FloodStage stage)
        {
            foreach (var s in stages)
                if (s.stage == stage) return s;
            return new StageLook { stage = stage, label = stage.ToString(), labelColor = Color.white };
        }

        // ---- UI ---------------------------------------------------------------------

        private void Build()
        {
            var canvasGo = new GameObject("FloodStageCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Badge area pinned to the chosen corner.
            bool right = corner == Corner.TopRight || corner == Corner.BottomRight;
            bool top = corner == Corner.TopLeft || corner == Corner.TopRight;
            Vector2 anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
            float width = Mathf.Max(stickerHeight * 1.3f, labelSize * 12f);
            float height = stickerHeight + labelSize * 1.5f;

            var badge = NewRect("Badge", canvasGo.transform);
            badge.anchorMin = badge.anchorMax = badge.pivot = anchor;
            badge.anchoredPosition = new Vector2(right ? -margin.x : margin.x, top ? -margin.y : margin.y);
            badge.sizeDelta = new Vector2(width, height);
            _group = badge.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // Everything inside sways together, hinged at the bottom middle.
            var swing = NewRect("Wobble", badge);
            swing.anchorMin = swing.anchorMax = swing.pivot = new Vector2(0.5f, 0f);
            swing.anchoredPosition = Vector2.zero;
            swing.sizeDelta = new Vector2(width, height);

            var sticker = NewRect("Sticker", swing);
            sticker.anchorMin = sticker.anchorMax = sticker.pivot = new Vector2(0.5f, 0f);
            sticker.anchoredPosition = new Vector2(0f, labelSize * 1.3f);
            sticker.sizeDelta = new Vector2(stickerHeight * 1.15f, stickerHeight);
            _image = sticker.gameObject.AddComponent<Image>();
            _image.preserveAspect = true;
            _image.raycastTarget = false;

            var label = NewRect("Label", swing);
            label.anchorMin = new Vector2(0f, 0f);
            label.anchorMax = new Vector2(1f, 0f);
            label.pivot = new Vector2(0.5f, 0f);
            label.anchoredPosition = Vector2.zero;
            label.sizeDelta = new Vector2(0f, labelSize * 1.4f);
            _label = label.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset == null)
                _label.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            _label.fontSize = labelSize;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.raycastTarget = false;
            _label.outlineWidth = 0.22f;
            _label.outlineColor = new Color32(12, 34, 66, 255);

            _wobble = swing.gameObject.AddComponent<StickerWobble>();
            _wobble.faceCamera = false;
            _wobble.swingAngle = 7f;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
