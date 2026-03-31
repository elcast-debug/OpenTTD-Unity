using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    // -------------------------------------------------------
    // Game state and speed enums
    // -------------------------------------------------------

    /// <summary>
    /// Broad game states used to gate input and drive UI visibility.
    /// </summary>
    public enum GameState
    {
        /// <summary>Normal gameplay — trains run, economy ticks, player can build.</summary>
        Playing,

        /// <summary>Game is paused — economy and trains frozen, camera still moves.</summary>
        Paused,

        /// <summary>
        /// Player is actively placing a build (rail, station, etc.).
        /// UI shows cost preview; train scheduling is still active.
        /// </summary>
        Building,
    }

    /// <summary>Multiplier applied to <see cref="Time.timeScale"/> for fast-forward.</summary>
    public enum GameSpeed
    {
        Paused  = 0,
        Normal  = 1,
        Fast    = 2,
        Fastest = 4,
    }

    // -------------------------------------------------------
    // GameDate struct
    // -------------------------------------------------------

    /// <summary>
    /// Lightweight in-game calendar date.  Does NOT account for leap years
    /// in the prototype (all months are treated as 30 days for simplicity).
    /// </summary>
    [Serializable]
    public struct GameDate
    {
        public int Year;
        public int Month;  // 1–12
        public int Day;    // 1–30

        public static GameDate Start => new GameDate
        {
            Year  = Constants.StartYear,
            Month = Constants.StartMonth,
            Day   = Constants.StartDay,
        };

        /// <summary>Advances the date by one day, wrapping months and years.</summary>
        public void AdvanceDay()
        {
            Day++;
            if (Day > 30) { Day = 1; Month++; }
            if (Month > 12) { Month = 1; Year++; }
        }

        public override string ToString() =>
            $"{Day:D2}/{Month:D2}/{Year}";
    }

    // -------------------------------------------------------
    // GameManager
    // -------------------------------------------------------

    /// <summary>
    /// Central singleton that owns:
    ///   - Game state machine (Playing / Paused / Building)
    ///   - Speed control
    ///   - In-game calendar
    ///   - Initialization sequence for all sub-systems
    ///   - References to all manager singletons
    ///
    /// Attach to a persistent root GameObject in the MainScene.
    /// All other managers should be children or siblings — they will be
    /// found during <see cref="InitializeSystems"/>.
    ///
    /// The initialization sequence is run as a coroutine so Unity's
    /// frame pipeline is unblocked between heavy operations (mesh gen, etc.).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        // -------------------------------------------------------
        // Singleton
        // -------------------------------------------------------

        /// <summary>Global singleton accessor.</summary>
        public static GameManager Instance { get; private set; }

        // -------------------------------------------------------
        // Inspector references
        // -------------------------------------------------------

        [Header("Manager References (auto-found if null)")]
        [SerializeField] private GridManager     _gridManager;
        [SerializeField] private TerrainGenerator _terrainGenerator;
        [SerializeField] private EconomyManager  _economyManager;
        // TerrainChunks are spawned procedurally — reference kept as array
        private TerrainChunk[] _terrainChunks;

        [Header("Terrain Chunk")]
        [SerializeField, Tooltip("Prefab for each 16×16 terrain chunk. Must have TerrainChunk component.")]
        private GameObject _chunkPrefab;

        [SerializeField, Tooltip("Parent transform for spawned chunk GameObjects (keeps hierarchy tidy).")]
        private Transform _chunkContainer;

        [SerializeField, Tooltip("Material shared by all terrain chunks.")]
        private Material _terrainMaterial;

        // -------------------------------------------------------
        // Inspector: game settings
        // -------------------------------------------------------

        [Header("Game Settings")]
        [SerializeField, Tooltip("Starting game speed.")]
        private GameSpeed _startingSpeed = GameSpeed.Normal;

        // -------------------------------------------------------
        // State (public read-only)
        // -------------------------------------------------------

        /// <summary>Current high-level game state.</summary>
        public GameState State { get; private set; } = GameState.Paused;

        /// <summary>Current game speed setting.</summary>
        public GameSpeed Speed { get; private set; } = GameSpeed.Normal;

        /// <summary>Current in-game calendar date.</summary>
        public GameDate Date { get; private set; } = GameDate.Start;

        /// <summary>True while the initialization sequence is still running.</summary>
        public bool IsInitializing { get; private set; }

        // -------------------------------------------------------
        // Events
        // -------------------------------------------------------

        /// <summary>Fired each time the in-game date advances by one day.</summary>
        public event Action<GameDate> OnDayAdvanced;

        /// <summary>Fired when the game state changes.</summary>
        public event Action<GameState> OnStateChanged;

        /// <summary>Fired when the game speed changes.</summary>
        public event Action<GameSpeed> OnSpeedChanged;

        /// <summary>Fired once the initialization sequence completes.</summary>
        public event Action OnGameReady;

        // -------------------------------------------------------
        // Internal timers
        // -------------------------------------------------------

        private float _dayTimer;  // accumulates unscaled time to advance days

        // -------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameManager] Duplicate instance destroyed.", gameObject);
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Cache manager references — find in scene if not set in inspector
            if (_gridManager      == null) _gridManager      = FindAnyObjectByType<GridManager>();
            if (_terrainGenerator == null) _terrainGenerator = FindAnyObjectByType<TerrainGenerator>();
            if (_economyManager   == null) _economyManager   = FindAnyObjectByType<EconomyManager>();
        }

        private void Start()
        {
            StartCoroutine(InitializeSystems());
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (IsInitializing) return;

            HandleGlobalInput();
            TickGameClock();
        }

        // -------------------------------------------------------
        // Initialization sequence
        // -------------------------------------------------------

        /// <summary>
        /// Ordered initialization coroutine.  Each step yields one frame so
        /// the UI can show loading progress if desired.
        /// </summary>
        private IEnumerator InitializeSystems()
        {
            IsInitializing = true;
            SetState(GameState.Paused);
            Time.timeScale = 0f;

            Debug.Log("[GameManager] ── Initialization start ──");

            // Step 1: Grid
            yield return null;
            Debug.Log("[GameManager] Step 1/5 — Initializing GridManager...");
            if (_gridManager == null)
            {
                Debug.LogError("[GameManager] GridManager not found. Aborting initialization.");
                IsInitializing = false;
                yield break;
            }
            _gridManager.InitialiseGrid();

            // Step 2: Terrain heights
            yield return null;
            Debug.Log("[GameManager] Step 2/5 — Generating terrain...");
            if (_terrainGenerator != null)
                _terrainGenerator.Generate();
            else
                Debug.LogWarning("[GameManager] TerrainGenerator not found — skipping terrain generation.");

            // Step 3: Spawn and build terrain chunk meshes
            yield return null;
            Debug.Log("[GameManager] Step 3/5 — Building terrain chunks...");
            yield return StartCoroutine(SpawnTerrainChunks());

            // Step 4: Economy initialization
            yield return null;
            Debug.Log("[GameManager] Step 4/5 — Initializing economy...");
            if (_economyManager != null)
                _economyManager.Initialize(Constants.StartingMoney);
            else
                Debug.LogWarning("[GameManager] EconomyManager not found — skipping economy init.");

            // Step 5: UI — find and notify UIManager
            yield return null;
            Debug.Log("[GameManager] Step 5/5 — Initializing UI...");
            // UIManager.Instance?.Initialize();  // uncomment when UIManager exists

            // Done
            IsInitializing = false;
            Date  = GameDate.Start;
            Speed = _startingSpeed;
            ApplySpeed(Speed);
            SetState(GameState.Playing);

            Debug.Log("[GameManager] ── Initialization complete ──");
            OnGameReady?.Invoke();
        }

        // -------------------------------------------------------
        // Chunk spawning
        // -------------------------------------------------------

        /// <summary>
        /// Spawns one TerrainChunk GameObject per chunk and calls
        /// <see cref="TerrainChunk.Initialize"/> and <see cref="TerrainChunk.RegenerateMesh"/>
        /// on each.  Yields after every row of chunks to avoid hitching.
        /// </summary>
        private IEnumerator SpawnTerrainChunks()
        {
            if (_chunkContainer == null)
            {
                var containerGO = new GameObject("TerrainChunks");
                _chunkContainer = containerGO.transform;
            }

            int total = Constants.ChunksX * Constants.ChunksZ;
            _terrainChunks = new TerrainChunk[total];
            int idx = 0;

            for (int cz = 0; cz < Constants.ChunksZ; cz++)
            {
                for (int cx = 0; cx < Constants.ChunksX; cx++)
                {
                    GameObject chunkGO;

                    if (_chunkPrefab != null)
                    {
                        chunkGO = Instantiate(_chunkPrefab, _chunkContainer);
                    }
                    else
                    {
                        chunkGO = new GameObject($"Chunk_{cx}_{cz}");
                        chunkGO.transform.SetParent(_chunkContainer);
                        chunkGO.AddComponent<MeshFilter>();
                        chunkGO.AddComponent<MeshRenderer>();
                        chunkGO.AddComponent<MeshCollider>();
                        chunkGO.AddComponent<TerrainChunk>();
                    }

                    var chunk = chunkGO.GetComponent<TerrainChunk>();
                    if (chunk == null) chunk = chunkGO.AddComponent<TerrainChunk>();

                    chunk.Initialize(cx, cz, _terrainMaterial);
                    chunk.RegenerateMesh();

                    _terrainChunks[idx++] = chunk;
                }

                // Yield after each row to keep frame rate smooth
                yield return null;
            }

            Debug.Log($"[GameManager] Spawned {total} terrain chunks.");
        }

        // -------------------------------------------------------
        // State machine
        // -------------------------------------------------------

        /// <summary>
        /// Transitions to a new game state.
        /// </summary>
        public void SetState(GameState newState)
        {
            if (State == newState) return;
            State = newState;
            OnStateChanged?.Invoke(State);
        }

        // -------------------------------------------------------
        // Speed control
        // -------------------------------------------------------

        /// <summary>
        /// Sets the simulation speed.  <see cref="GameSpeed.Paused"/> sets
        /// <see cref="Time.timeScale"/> to zero; other values set it to the
        /// integer multiplier.
        /// </summary>
        public void SetSpeed(GameSpeed speed)
        {
            Speed = speed;
            ApplySpeed(speed);
            OnSpeedChanged?.Invoke(speed);
        }

        /// <summary>
        /// Cycles through Normal → Fast → Fastest → Normal.
        /// </summary>
        public void CycleSpeed()
        {
            GameSpeed next = Speed switch
            {
                GameSpeed.Normal  => GameSpeed.Fast,
                GameSpeed.Fast    => GameSpeed.Fastest,
                GameSpeed.Fastest => GameSpeed.Normal,
                _                 => GameSpeed.Normal,
            };
            SetSpeed(next);
        }

        private void ApplySpeed(GameSpeed speed)
        {
            if (State == GameState.Paused)
            {
                Time.timeScale = 0f;
                return;
            }
            Time.timeScale = speed == GameSpeed.Paused ? 0f : (int)speed;
        }

        // -------------------------------------------------------
        // Pause / unpause
        // -------------------------------------------------------

        /// <summary>
        /// Toggles between Paused and Playing states.
        /// </summary>
        public void TogglePause()
        {
            if (State == GameState.Paused)
                Unpause();
            else
                Pause();
        }

        /// <summary>Pauses the simulation.</summary>
        public void Pause()
        {
            if (State == GameState.Paused) return;
            SetState(GameState.Paused);
            Time.timeScale = 0f;
        }

        /// <summary>Resumes the simulation at the current speed.</summary>
        public void Unpause()
        {
            if (State != GameState.Paused) return;
            SetState(GameState.Playing);
            ApplySpeed(Speed);
        }

        // -------------------------------------------------------
        // Global input (space = pause, speed keys)
        // -------------------------------------------------------

        private void HandleGlobalInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                TogglePause();

            // Number keys 1/2/3 for speed — only when not paused
            if (State != GameState.Paused)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(GameSpeed.Normal);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(GameSpeed.Fast);
                if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(GameSpeed.Fastest);
            }
        }

        // -------------------------------------------------------
        // Game clock
        // -------------------------------------------------------

        private void TickGameClock()
        {
            if (State == GameState.Paused) return;

            // Use unscaled time for the accumulator so the speed multiplier
            // doesn't double-count (Time.timeScale is already applied to
            // Time.deltaTime; we scale manually via Constants.SecondsPerDay).
            _dayTimer += Time.unscaledDeltaTime * (int)Speed;

            float secsPerDay = Constants.SecondsPerDay;

            while (_dayTimer >= secsPerDay)
            {
                _dayTimer -= secsPerDay;
                Date.AdvanceDay();
                OnDayAdvanced?.Invoke(Date);
            }
        }

        // -------------------------------------------------------
        // Accessors for other systems
        // -------------------------------------------------------

        /// <summary>Reference to the GridManager (read-only).</summary>
        public GridManager Grid => _gridManager;

        /// <summary>Reference to the EconomyManager (read-only).</summary>
        public EconomyManager Economy => _economyManager;

        /// <summary>Read-only snapshot of all spawned terrain chunks.</summary>
        public IReadOnlyList<TerrainChunk> TerrainChunks =>
            _terrainChunks ?? System.Array.Empty<TerrainChunk>();

        // -------------------------------------------------------
        // Utility
        // -------------------------------------------------------

        /// <summary>
        /// Returns the TerrainChunk at the given chunk coordinates,
        /// or null if out of bounds or not yet spawned.
        /// </summary>
        public TerrainChunk GetChunk(int cx, int cz)
        {
            if (_terrainChunks == null) return null;
            if (cx < 0 || cx >= Constants.ChunksX || cz < 0 || cz >= Constants.ChunksZ) return null;
            return _terrainChunks[cz * Constants.ChunksX + cx];
        }

        /// <summary>
        /// Triggers a mesh regeneration for the chunk containing the given world tile.
        /// </summary>
        public void RefreshChunkAt(int tileX, int tileZ)
        {
            int cx = tileX / Constants.ChunkSize;
            int cz = tileZ / Constants.ChunkSize;
            GetChunk(cx, cz)?.RegenerateMesh();
        }
    }
}
