#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HexFluid
{
    /// <summary>
    /// The tutorial book on the table. Click the book and a page pops up on screen;
    /// the arrow buttons turn the pages (each page is just a title and some text),
    /// X or Esc closes it. While it's open the game is paused and doesn't react to
    /// clicks or keys (see InputBlocker).
    ///
    /// Setup:
    ///   - Put this on your book model. It needs a Collider to be clicked (add a
    ///     BoxCollider if the model has none). A PressableButton3D is added with it,
    ///     which does the click and the little dip on hover.
    ///   - Write your pages in 'pages'.
    ///
    /// Self-contained: it builds its own overlay canvas, so it needs no scene setup.
    /// </summary>
    [RequireComponent(typeof(PressableButton3D))]
    public class TutorialBook : MonoBehaviour
    {
        [Serializable]
        public class Page
        {
            public string title;
            [TextArea(4, 12)] public string text;
        }

        [Header("Pages")]
        public List<Page> pages = new List<Page>
        {
            new Page { title = "The flood is coming",
                       text = "Water bursts out of the source tile and runs down the board.\n\nYour job: keep it away from the houses. If the water settles without reaching a house, you win the level." },
            new Page { title = "Harvest",
                       text = "Click the tiles with trees, stones and other resources to collect them.\n\nWater can't run through resource tiles, so think before you take them away." },
            new Page { title = "Craft",
                       text = "Use the craft bar on the left to turn your resources into barricades and bridges.\n\nEach button shows what it costs and how many you already have." },
            new Page { title = "Build",
                       text = "With an item selected, click the edge between two tiles to place it. Right-click an item to pick it back up.\n\nBarricades stop the water. Bridges carry it across.\n\nYou can't build right next to the water source." },
            new Page { title = "Let it flow",
                       text = "When you're ready, press the Start button and watch where the water goes.\n\nR restarts the level. Esc goes back to the first level." },
        };

        [Header("Behaviour")]
        [Tooltip("Freeze the game (water, animations) while the book is open.")]
        public bool pauseWhileOpen = true;

        [Header("Look (pixels at 1920x1080, scales with the screen)")]
        public Vector2 pageSize = new Vector2(900f, 640f);
        public Color paperColor = new Color(0.96f, 0.92f, 0.82f, 1f);
        public Color inkColor = new Color(0.22f, 0.15f, 0.09f, 1f);
        public Color buttonColor = new Color(0.45f, 0.31f, 0.19f, 1f);
        [Tooltip("Darkens the game behind the page.")]
        public Color backdropColor = new Color(0f, 0f, 0f, 0.55f);
        public float titleSize = 52f;
        public float textSize = 32f;

        [Header("Audio")]
        public SoundSO openSound;
        public SoundSO pageSound;
        public SoundSO closeSound;

        public bool IsOpen { get; private set; }
        public int PageIndex { get; private set; }

        /// <summary>Fires when the book opens / closes.</summary>
        public event Action Opened, Closed;

        private PressableButton3D _button;
        private GameObject _canvas;
        private TextMeshProUGUI _title, _text, _pageNumber;
        private Button _prev, _next;
        private float _timeScaleBefore = 1f;

        private void Reset()
        {
            // Added together with this; the button's default "Game started" log makes no sense here.
            var button = GetComponent<PressableButton3D>();
            if (button != null) button.debugMessage = "";
        }

        private void Awake()
        {
            _button = GetComponent<PressableButton3D>();
        }

        private void OnEnable() => _button.Clicked += Open;

        private void OnDisable()
        {
            _button.Clicked -= Open;
            if (IsOpen) Close();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (EscPressed()) Close();
            else if (RightPressed()) NextPage();
            else if (LeftPressed()) PrevPage();
        }

        // ---- Control -----------------------------------------------------------

        public void Open()
        {
            if (IsOpen || pages.Count == 0) return;
            if (_canvas == null) Build();

            IsOpen = true;
            _canvas.SetActive(true);
            InputBlocker.Block(this);
            if (pauseWhileOpen)
            {
                _timeScaleBefore = Time.timeScale;
                Time.timeScale = 0f;
            }

            ShowPage(0);
            Sfx.Play(openSound);
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _canvas.SetActive(false);
            InputBlocker.Unblock(this);
            if (pauseWhileOpen) Time.timeScale = _timeScaleBefore;

            Sfx.Play(closeSound);
            Closed?.Invoke();
        }

        public void NextPage() => TurnTo(PageIndex + 1);
        public void PrevPage() => TurnTo(PageIndex - 1);

        private void TurnTo(int index)
        {
            if (index < 0 || index >= pages.Count || index == PageIndex) return;
            ShowPage(index);
            Sfx.Play(pageSound);
        }

        private void ShowPage(int index)
        {
            PageIndex = Mathf.Clamp(index, 0, pages.Count - 1);
            Page page = pages[PageIndex];
            _title.text = page.title;
            _text.text = page.text;
            _pageNumber.text = $"{PageIndex + 1} / {pages.Count}";
            _prev.interactable = PageIndex > 0;
            _next.interactable = PageIndex < pages.Count - 1;
        }

        // ---- UI ----------------------------------------------------------------

        private void Build()
        {
            EnsureEventSystem();

            _canvas = new GameObject("TutorialBookCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas.transform.SetParent(transform, false);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;                                   // above the flood badge
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Full-screen backdrop: dims the game and swallows clicks around the page.
            var backdrop = NewRect("Backdrop", _canvas.transform);
            Stretch(backdrop);
            backdrop.gameObject.AddComponent<Image>().color = backdropColor;

            var paper = NewRect("Page", backdrop);
            paper.anchorMin = paper.anchorMax = paper.pivot = new Vector2(0.5f, 0.5f);
            paper.sizeDelta = pageSize;
            paper.gameObject.AddComponent<Image>().color = paperColor;

            const float pad = 56f;
            const float buttonSize = 72f;

            _title = NewText("Title", paper, titleSize, FontStyles.Bold, TextAlignmentOptions.Top);
            var titleRect = _title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(pad + buttonSize, -pad - titleSize * 1.3f);  // leave room for the X
            titleRect.offsetMax = new Vector2(-pad - buttonSize, -pad);

            _text = NewText("Text", paper, textSize, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            var textRect = _text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(pad, pad + buttonSize + 24f);
            textRect.offsetMax = new Vector2(-pad, -pad - titleSize * 1.3f - 28f);

            _pageNumber = NewText("PageNumber", paper, textSize * 0.85f, FontStyles.Italic, TextAlignmentOptions.Center);
            var numberRect = _pageNumber.rectTransform;
            numberRect.anchorMin = numberRect.anchorMax = numberRect.pivot = new Vector2(0.5f, 0f);
            numberRect.anchoredPosition = new Vector2(0f, pad * 0.5f);
            numberRect.sizeDelta = new Vector2(240f, buttonSize);

            _prev = NewButton("Prev", paper, "<", new Vector2(0f, 0f), new Vector2(pad * 0.5f, pad * 0.5f), buttonSize, PrevPage);
            _next = NewButton("Next", paper, ">", new Vector2(1f, 0f), new Vector2(-pad * 0.5f, pad * 0.5f), buttonSize, NextPage);
            NewButton("Close", paper, "X", new Vector2(1f, 1f), new Vector2(-pad * 0.4f, -pad * 0.4f), buttonSize * 0.8f, Close);

            _canvas.SetActive(false);
        }

        private TextMeshProUGUI NewText(string name, Transform parent, float size, FontStyles style, TextAlignmentOptions align)
        {
            var text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset == null)
                text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = align;
            text.color = inkColor;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>A square button pinned to 'corner' of the page, nudged by 'offset'.</summary>
        private Button NewButton(string name, Transform parent, string label, Vector2 corner, Vector2 offset, float size, Action onClick)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(size, size);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = buttonColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.25f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var text = NewText("Label", rect, size * 0.55f, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.color = paperColor;
            return button;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>UI buttons need an EventSystem; make one if the scene has none.</summary>
        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
#if ENABLE_INPUT_SYSTEM
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
#else
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
        }

        // ---- Input (old Input Manager or new Input System) --------------------

        private static bool EscPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static bool RightPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rightArrowKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.RightArrow);
#endif
        }

        private static bool LeftPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.leftArrowKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.LeftArrow);
#endif
        }
    }
}
