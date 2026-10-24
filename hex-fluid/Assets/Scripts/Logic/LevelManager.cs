#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Owns the list of levels and which one is being played. It's the only thing
    /// that knows about more than one level: it hands the current one to the
    /// HexBoard, and the board tells everyone else (harvester, hover, water, items)
    /// through HexBoard.LevelLoaded, so none of them reference a LevelData directly.
    ///
    /// When the water settles without reaching a city (WaterSimulation.WonEvent) it
    /// waits a moment, despawns the level and drops the next one in. When the water
    /// reaches a city (LostEvent) it plays the same level again.
    ///
    /// Keys: R = restart this level, Esc = back to the first level.
    /// Every change is the same in-scene animation: the old tiles fly up, the new
    /// ones drop in (see HexBoard). No scene is ever reloaded.
    ///
    /// Setup:
    ///   - Put this on your board object (or any object). Everything is auto-found.
    ///   - Fill 'levels' in play order (FirstLevel, SecondLevel, ...).
    ///   - Hook buttons to NextLevel() / RestartLevel() / LoadLevel(index) if you want.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        [Header("References (auto-found if empty)")]
        public HexBoard board;
        public WaterSimulation water;
        public PlayerInventory inventory;
        public ItemCrafting crafting;

        [Header("Levels")]
        [Tooltip("Levels in play order.")]
        public List<LevelData> levels = new List<LevelData>();

        [Tooltip("Index of the level to start on (0 = the first).")]
        [Min(0)] public int startLevel = 0;

        [Header("Flow")]
        [Tooltip("Go to the next level automatically when the player wins.")]
        public bool autoAdvanceOnWin = true;

        [Tooltip("Seconds to wait after a win before the next level loads (enjoy the result first).")]
        [Min(0f)] public float nextLevelDelay = 2.5f;

        [Tooltip("Play the same level again when the water reaches a city.")]
        public bool restartOnFail = true;

        [Tooltip("Seconds to wait after a loss before the level restarts (see the water hit the city first).")]
        [Min(0f)] public float restartDelay = 2f;

        [Header("Keys")]
        [Tooltip("R restarts the current level, Esc goes back to the first level.")]
        public bool keyboardShortcuts = true;

        [Tooltip("Empty the resources and crafted items when a level starts, so each level is played from scratch.")]
        public bool resetInventoryEachLevel = true;

        public int CurrentIndex { get; private set; } = -1;
        public LevelData CurrentLevel => CurrentIndex >= 0 && CurrentIndex < levels.Count ? levels[CurrentIndex] : null;
        public bool HasNextLevel => CurrentIndex + 1 < levels.Count;

        /// <summary>Fires when a level has been built: (index, level asset).</summary>
        public event Action<int, LevelData> LevelStarted;

        /// <summary>Fires when the last level is won.</summary>
        public event Action AllLevelsCompleted;

        private Coroutine _advance;

        private void Awake()
        {
            if (board == null) board = GetComponent<HexBoard>();
            if (board == null) board = FindFirstObjectByType<HexBoard>();
            if (water == null) water = FindFirstObjectByType<WaterSimulation>();
            if (inventory == null) inventory = FindFirstObjectByType<PlayerInventory>();
            if (crafting == null) crafting = FindFirstObjectByType<ItemCrafting>();

            // Hand the board its first level before anything Starts, so it builds
            // that one (once) instead of whatever is assigned on it in the scene.
            if (board != null && levels.Count > 0)
            {
                CurrentIndex = Mathf.Clamp(startLevel, 0, levels.Count - 1);
                board.level = levels[CurrentIndex];
            }
        }

        private void OnEnable()
        {
            if (board != null) board.LevelLoaded += OnLevelLoaded;
            if (water != null) { water.WonEvent += OnWon; water.LostEvent += OnLost; }
        }

        private void OnDisable()
        {
            if (board != null) board.LevelLoaded -= OnLevelLoaded;
            if (water != null) { water.WonEvent -= OnWon; water.LostEvent -= OnLost; }
        }

        private void Update()
        {
            if (!keyboardShortcuts || levels.Count == 0 || InputBlocker.Blocked) return;
            if (RestartPressed()) RestartLevel();
            else if (FirstLevelPressed()) LoadLevel(0);
        }

        // ---- Control -----------------------------------------------------------

        /// <summary>Despawns the current level and loads the one at 'index'.</summary>
        public void LoadLevel(int index)
        {
            if (board == null || index < 0 || index >= levels.Count || levels[index] == null)
            {
                Debug.LogError($"LevelManager: no level at index {index}.", this);
                return;
            }

            if (_advance != null) StopCoroutine(_advance);
            _advance = null;

            CurrentIndex = index;
            if (resetInventoryEachLevel)
            {
                if (inventory != null) inventory.Clear();
                if (crafting != null) crafting.Clear();
            }
            board.LoadLevel(levels[index]);   // board despawns the old tiles, builds the new ones, fires LevelLoaded
        }

        public void NextLevel()
        {
            if (HasNextLevel) LoadLevel(CurrentIndex + 1);
            else
            {
                Debug.Log("LevelManager: all levels complete.", this);
                AllLevelsCompleted?.Invoke();
            }
        }

        public void RestartLevel() => LoadLevel(Mathf.Max(0, CurrentIndex));

        // ---- Events ------------------------------------------------------------

        private void OnLevelLoaded(LevelData level) => LevelStarted?.Invoke(CurrentIndex, CurrentLevel);

        private void OnWon()
        {
            if (autoAdvanceOnWin) After(nextLevelDelay, NextLevel);
        }

        private void OnLost()
        {
            if (restartOnFail) After(restartDelay, RestartLevel);
        }

        /// <summary>Runs 'then' after a delay. Any level load in between (R, Esc...) cancels it.</summary>
        private void After(float delay, Action then)
        {
            if (_advance != null) StopCoroutine(_advance);
            _advance = StartCoroutine(Wait(delay, then));
        }

        private IEnumerator Wait(float delay, Action then)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            _advance = null;
            then();
        }

        // ---- Input (old Input Manager or new Input System) --------------------

        private static bool RestartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        private static bool FirstLevelPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
