#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HexFluid
{
    /// <summary>
    /// The main menu, on the table (no separate scene): Play and Quit are 3D push
    /// buttons, like the simulation's Start button. The level isn't there yet. Click
    /// Play: the menu buttons fly up and away like the tiles do on a level change,
    /// then the level drops in and the game starts. Quit closes the game (or stops
    /// Play mode in the editor). While the menu is up the game ignores clicks and keys
    /// (see InputBlocker); only the menu's own buttons work.
    ///
    /// Setup (3D buttons):
    ///   - Make two button objects on the table with PressableButton3D (like your
    ///     Start button; a Collider is needed), and assign them to 'play3D' / 'quit3D'.
    ///     Their onClick needs nothing, this hooks them up.
    ///   - Anything else that belongs to the menu (a title sign...) goes in 'alsoFlyAway'.
    ///
    /// Without 3D buttons it falls back to on-screen Play / Quit buttons (built for you,
    /// or your own Canvas panel + Buttons assigned below).
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [Header("3D buttons on the table")]
        public PressableButton3D play3D;
        public PressableButton3D quit3D;

        [Tooltip("Other menu objects that fly away with the buttons when Play is clicked.")]
        public List<Transform> alsoFlyAway = new List<Transform>();

        [Header("Behaviour")]
        [Tooltip("Open the menu when the game starts.")]
        public bool showOnStart = true;

        [Tooltip("Don't build the level until Play is clicked (it drops in after the menu flies away).")]
        public bool holdLevelUntilPlay = true;

        [Tooltip("The board the level is built on. Auto-found if empty.")]
        public HexBoard board;

        [Header("Fly away (on Play)")]
        public float riseHeight = 12f;
        [Min(0.01f)] public float riseDuration = 0.45f;

        [Tooltip("Max random delay before each piece starts rising (0 = all together).")]
        [Min(0f)] public float riseStagger = 0.15f;

        [Tooltip("Extra pause after the menu is gone, before the level drops in.")]
        [Min(0f)] public float levelDelay = 0.1f;

        [Header("On-screen fallback (only without 3D buttons)")]
        [Tooltip("Shown while the menu is open, hidden on Play. Empty = built for you.")]
        public GameObject panel;
        public Button playButton;
        public Button quitButton;

        [Tooltip("Optional title above the buttons. Empty = none.")]
        public string title = "";
        public Vector2 buttonSize = new Vector2(380f, 100f);
        public float buttonSpacing = 28f;
        public float fontSize = 46f;
        public Color buttonColor = new Color(0.36f, 0.22f, 0.13f, 0.95f);
        public Color textColor = new Color(0.98f, 0.93f, 0.82f, 1f);
        [Tooltip("Darkens the table behind the menu.")]
        public Color backdropColor = new Color(0f, 0f, 0f, 0.35f);

        [Header("Audio")]
        [Tooltip("Played when the on-screen buttons are clicked (3D buttons use their own press sound). Optional.")]
        public SoundSO clickSound;

        public bool IsOpen { get; private set; }

        /// <summary>Fires when Play is clicked (before the menu has flown away).</summary>
        public event Action Played;

        private bool Uses3D => play3D != null;

        private void Awake()
        {
            if (board == null) board = FindFirstObjectByType<HexBoard>();
            // Before the board's own Start: it waits for Play instead of building straight away.
            if (showOnStart && holdLevelUntilPlay && board != null) board.buildOnStart = false;
        }

        private void Start()
        {
            if (Uses3D)
            {
                play3D.ignoreInputBlocker = true;                // the menu blocks the game, not itself
                play3D.Clicked += Play;
                if (quit3D != null)
                {
                    quit3D.ignoreInputBlocker = true;
                    quit3D.Clicked += Quit;
                }
            }
            else
            {
                EnsureEventSystem();
                if (panel == null) Build();
                if (playButton != null) playButton.onClick.AddListener(Play);
                if (quitButton != null) quitButton.onClick.AddListener(Quit);
                panel.SetActive(false);
            }

            if (showOnStart) Show();
            else HideMenuObjects();
        }

        private void OnDestroy()
        {
            if (IsOpen) InputBlocker.Unblock(this);
            if (play3D != null) play3D.Clicked -= Play;
            if (quit3D != null) quit3D.Clicked -= Quit;
        }

        // ---- Control -----------------------------------------------------------

        public void Show()
        {
            if (!Uses3D && panel != null) panel.SetActive(true);
            if (!IsOpen) InputBlocker.Block(this);
            IsOpen = true;
        }

        /// <summary>The menu flies away, then the level drops in and the game starts.</summary>
        public void Play()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (!Uses3D) Sfx.Play(clickSound);
            Played?.Invoke();
            StartCoroutine(PlayRoutine());
        }

        private IEnumerator PlayRoutine()
        {
            float wait = Uses3D ? FlyAway() : 0f;
            if (!Uses3D && panel != null) panel.SetActive(false);
            if (wait + levelDelay > 0f) yield return new WaitForSeconds(wait + levelDelay);

            InputBlocker.Unblock(this);
            if (board != null && board.Level == null && board.level != null) board.Build();   // the level drops in
        }

        /// <summary>Sends the 3D buttons (and extras) up and away. Returns how long until they're gone.</summary>
        private float FlyAway()
        {
            float longest = 0f;
            var pieces = new List<Transform>(alsoFlyAway);
            if (play3D != null) pieces.Add(play3D.transform);
            if (quit3D != null) pieces.Add(quit3D.transform);

            foreach (var t in pieces)
            {
                if (t == null) continue;
                foreach (var b in t.GetComponentsInChildren<PressableButton3D>()) b.enabled = false;  // it moves Y itself
                float delay = UnityEngine.Random.Range(0f, riseStagger);
                t.gameObject.AddComponent<TileRiseAnimator>().Play(riseHeight, delay, riseDuration);
                longest = Mathf.Max(longest, delay + riseDuration);
            }
            return longest;
        }

        private void HideMenuObjects()
        {
            if (play3D != null) play3D.gameObject.SetActive(false);
            if (quit3D != null) quit3D.gameObject.SetActive(false);
            foreach (var t in alsoFlyAway) if (t != null) t.gameObject.SetActive(false);
        }

        public void Quit()
        {
            if (!Uses3D) Sfx.Play(clickSound);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- Built-in UI -------------------------------------------------------

        private void Build()
        {
            var canvasGo = new GameObject("MainMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;                                   // above everything else
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Full-screen backdrop: dims the level and swallows clicks around the buttons.
            var backdrop = NewRect("Menu", canvasGo.transform);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            backdrop.gameObject.AddComponent<Image>().color = backdropColor;
            panel = backdrop.gameObject;

            float half = (buttonSize.y + buttonSpacing) * 0.5f;
            playButton = NewButton("Play", backdrop, new Vector2(0f, half));
            quitButton = NewButton("Quit", backdrop, new Vector2(0f, -half));

            if (!string.IsNullOrEmpty(title))
            {
                var t = NewText("Title", backdrop, fontSize * 1.8f);
                var r = t.rectTransform;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
                r.anchoredPosition = new Vector2(0f, half + buttonSize.y + fontSize * 1.6f);
                r.sizeDelta = new Vector2(1400f, fontSize * 2.4f);
                t.text = title;
            }
        }

        private Button NewButton(string label, Transform parent, Vector2 position)
        {
            var rect = NewRect(label + "Button", parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = buttonSize;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = buttonColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var text = NewText("Label", rect, fontSize);
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            text.text = label;
            return button;
        }

        private TextMeshProUGUI NewText(string name, Transform parent, float size)
        {
            var text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset == null)
                text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = textColor;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
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
    }
}
