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
    /// waits a moment, despawns the level and drops the next one in.
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
            if (water != null) water.WonEvent += OnWon;
        }

        private void OnDisable()
        {
            if (board != null) board.LevelLoaded -= OnLevelLoaded;
            if (water != null) water.WonEvent -= OnWon;
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
            if (!autoAdvanceOnWin) return;
            if (_advance != null) StopCoroutine(_advance);
            _advance = StartCoroutine(NextLevelAfterDelay());
        }

        private IEnumerator NextLevelAfterDelay()
        {
            if (nextLevelDelay > 0f) yield return new WaitForSeconds(nextLevelDelay);
            _advance = null;
            NextLevel();
        }
    }
}
