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
    /// The tutorial book on the table. Click the book and your paper pops up; the
    /// paper itself never changes, and each page is a PNG shown on top of it. Your
    /// buttons (or the arrow keys) turn the pages, your close button (or Esc) puts
    /// it away. While it's open the game doesn't react to clicks or keys (see
    /// InputBlocker), and is paused if 'pauseWhileOpen' is on.
    ///
    /// You make the UI, this just drives it:
    ///   1. In a Canvas, add an Image with your paper background and put it where you
    ///      want (e.g. left of the character). Assign it to 'paper'.
    ///   2. Optionally add Next / Prev / Close buttons and a page-number text, and
    ///      assign them. Put them (and the paper) under one object and assign that as
    ///      'panel' so they show and hide together.
    ///   3. Drop your page PNGs into 'pages' (set each PNG's Texture Type to
    ///      "Sprite (2D and UI)"). They're fitted inside the paper, minus 'padding',
    ///      keeping their shape, so moving or resizing the paper moves them too.
    ///   4. Put this on your book model. It needs a Collider to be clicked. A
    ///      PressableButton3D is added with it, which does the click and the dip on hover.
    /// </summary>
    [RequireComponent(typeof(PressableButton3D))]
    public class TutorialBook : MonoBehaviour
    {
        [Serializable]
        public struct Padding
        {
            public float left, right, top, bottom;
        }

        [Header("Your UI")]
        [Tooltip("What shows / hides when the book opens / closes (the paper, or a parent holding it and the buttons). Empty = the paper.")]
        public GameObject panel;

        [Tooltip("The paper background Image. It stays the same; the page PNGs are placed on it.")]
        public RectTransform paper;

        [Tooltip("Optional: the Image the pages are shown in. Empty = one is made inside the paper, filling it minus 'padding'.")]
        public Image pageImage;

        [Tooltip("Gap between the paper's edges and the page PNG, in the paper's own pixels.")]
        public Padding padding = new Padding { left = 40f, right = 40f, top = 40f, bottom = 40f };

        [Tooltip("Optional buttons. Next / Prev fade out on the last / first page.")]
        public Button nextButton, prevButton, closeButton;

        [Tooltip("Optional text that shows \"2 / 5\".")]
        public TMP_Text pageNumber;

        [Header("Pages")]
        [Tooltip("One PNG per page, in order.")]
        public List<Sprite> pages = new List<Sprite>();

        [Header("Behaviour")]
        [Tooltip("Freeze the game (water, animations) while the book is open.")]
        public bool pauseWhileOpen = true;

        [Header("Audio")]
        public SoundSO openSound;
        public SoundSO pageSound;
        public SoundSO closeSound;

        public bool IsOpen { get; private set; }
        public int PageIndex { get; private set; }

        /// <summary>Fires when the book opens / closes.</summary>
        public event Action Opened, Closed;

        private PressableButton3D _button;
        private float _timeScaleBefore = 1f;

        private GameObject Panel => panel != null ? panel : (paper != null ? paper.gameObject : null);

        private void Reset()
        {
            // Added together with this; the button's default "Game started" log makes no sense here.
            var button = GetComponent<PressableButton3D>();
            if (button != null) button.debugMessage = "";
        }

        private void Awake()
        {
            _button = GetComponent<PressableButton3D>();

            if (nextButton != null) nextButton.onClick.AddListener(NextPage);
            if (prevButton != null) prevButton.onClick.AddListener(PrevPage);
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            if (paper == null)
                Debug.LogWarning("TutorialBook: assign 'paper' (the page background Image in your Canvas).", this);
            else
            {
                EnsureEventSystem();
                EnsurePageImage();
            }
            if (Panel != null) Panel.SetActive(false);   // closed until the book is clicked
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
            if (IsOpen || pages.Count == 0 || Panel == null) return;

            IsOpen = true;
            Panel.SetActive(true);
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
            if (Panel != null) Panel.SetActive(false);
            InputBlocker.Unblock(this);
            if (pauseWhileOpen) Time.timeScale = _timeScaleBefore;

            Sfx.Play(closeSound);
            Closed?.Invoke();
        }

        public void NextPage() => TurnTo(PageIndex + 1);
        public void PrevPage() => TurnTo(PageIndex - 1);

        private void TurnTo(int index)
        {
            if (!IsOpen || index < 0 || index >= pages.Count || index == PageIndex) return;
            ShowPage(index);
            Sfx.Play(pageSound);
        }

        private void ShowPage(int index)
        {
            PageIndex = Mathf.Clamp(index, 0, pages.Count - 1);
            if (pageImage != null)
            {
                pageImage.sprite = pages[PageIndex];
                pageImage.enabled = pageImage.sprite != null;     // an empty slot shows just the paper
            }
            if (pageNumber != null) pageNumber.text = $"{PageIndex + 1} / {pages.Count}";
            if (prevButton != null) prevButton.interactable = PageIndex > 0;
            if (nextButton != null) nextButton.interactable = PageIndex < pages.Count - 1;
        }

        // ---- Setup -------------------------------------------------------------

        /// <summary>
        /// Makes the Image the pages go in: a child of the paper, stretched to it
        /// minus the padding, so it follows the paper wherever you put it. Drawn
        /// right above the paper, under any buttons you put on it.
        /// </summary>
        private void EnsurePageImage()
        {
            if (pageImage != null) return;

            var go = new GameObject("PageImage", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(paper, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding.left, padding.bottom);
            rect.offsetMax = new Vector2(-padding.right, -padding.top);

            pageImage = go.AddComponent<Image>();
            pageImage.preserveAspect = true;                       // never squash the PNG
            pageImage.raycastTarget = false;
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
