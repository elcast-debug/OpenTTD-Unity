using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Manages all industries on the map: spawning at game start, maintaining
    /// a registry, and providing spatial queries for station acceptance radius.
    ///
    /// Setup (Unity Editor):
    ///   1. Attach to a persistent "Managers" GameObject in MainScene.
    ///   2. Assign CoalMinePrefab and PowerStationPrefab in the inspector.
    ///   3. Assign the GridManager reference (or leave null for auto-find).
    ///   4. Call SpawnIndustries() from GameManager.Start() after terrain is ready.
    ///
    /// Placement algorithm:
    ///   - Iterates the grid looking for candidate tiles that are:
    ///     • Not water terrain
    ///     • Not occupied by another industry footprint
    ///     • Relatively flat (height variance within 2x2 footprint ≤ maxHeightVariance)
    ///     • At least minSeparationTiles away from all existing same-type industries
    ///   - Falls back to random placement if no ideal spot is found after maxAttempts.
    /// </summary>
    public class IndustryManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────────
        public static IndustryManager Instance { get; private set; }

        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("Prefabs")]
        [Tooltip("Prefab for the Coal Mine industry. Must have a CoalMine component.")]
        [SerializeField] private GameObject coalMinePrefab;

        [Tooltip("Prefab for the Power Station industry. Must have a PowerStation component.")]
        [SerializeField] private GameObject powerStationPrefab;

        [Header("Spawn Counts (Prototype)")]
        [Tooltip("Minimum number of coal mines to place at generation.")]
        [SerializeField] [Range(1, 20)] private int minCoalMines = 3;

        [Tooltip("Minimum number of power stations to place at generation.")]
        [SerializeField] [Range(1, 20)] private int minPowerStations = 2;

        [Header("Placement Rules")]
        [Tooltip("Minimum tile distance between two industries of the same type.")]
        [SerializeField] [Range(4, 30)] private int minSeparationTiles = 12;

        [Tooltip("Maximum allowed height difference across the 2×2 footprint for valid placement.")]
        [SerializeField] [Range(0, 4)] private int maxHeightVariance = 1;

        [Tooltip("How many random candidate tiles to test before giving up and using a fallback.")]
        [SerializeField] [Range(50, 2000)] private int maxPlacementAttempts = 500;

        [Tooltip("Border in tiles to keep industries away from map edges.")]
        [SerializeField] [Range(2, 16)] private int mapEdgeBorder = 4;

        [Header("References")]
        [Tooltip("GridManager instance. Auto-found if left null.")]
        [SerializeField] private GridManager gridManager;

        // ─── Runtime State ────────────────────────────────────────────────────────

        private readonly List<Industry> _allIndustries = new List<Industry>();

        // Spatial index: tile → industry (for quick lookups)
        private readonly Dictionary<Vector2Int, Industry> _tileOccupancy = new Dictionary<Vector2Int, Industry>();

        // ─── Events ───────────────────────────────────────────────────────────────

        /// <summary>Fired when a new industry is successfully spawned.</summary>
        public static event Action<Industry> OnIndustrySpawned;

        // ─── Public Properties ────────────────────────────────────────────────────

        /// <summary>All industries currently on the map.</summary>
        public IReadOnlyList<Industry> AllIndustries => _allIndustries;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[IndustryManager] Duplicate instance destroyed.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (gridManager == null)
                gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null)
                gridManager = GridManager.Instance;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─── Spawning ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Places all prototype industries on the map.
        /// Called by <c>GameManager</c> after terrain generation is complete.
        /// </summary>
        public void SpawnIndustries()
        {
            if (coalMinePrefab == null || powerStationPrefab == null)
            {
                Debug.LogError("[IndustryManager] Prefabs not assigned. Cannot spawn industries.");
                return;
            }

            int gridSize = gridManager != null ? gridManager.Width : 128;
            Debug.Log($"[IndustryManager] Spawning industries on {gridSize}×{gridSize} grid…");

            // Spawn coal mines first (producers)
            int minesSpawned = SpawnIndustryType(coalMinePrefab, IndustryType.CoalMine, minCoalMines, gridSize);

            // Then power stations (consumers)
            int stationsSpawned = SpawnIndustryType(powerStationPrefab, IndustryType.PowerStation, minPowerStations, gridSize);

            Debug.Log($"[IndustryManager] Spawned {minesSpawned} coal mines and {stationsSpawned} power stations.");
        }

        private int SpawnIndustryType(GameObject prefab, IndustryType type, int count, int gridSize)
        {
            int spawned = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2Int? position = FindValidPlacement(type, gridSize);

                if (position == null)
                {
                    Debug.LogWarning($"[IndustryManager] Could not find valid placement for {type} #{i + 1}.");
                    continue;
                }

                SpawnIndustryAt(prefab, position.Value);
                spawned++;
            }

            return spawned;
        }

        private void SpawnIndustryAt(GameObject prefab, Vector2Int gridPos)
        {
            float worldY = GetWorldHeight(gridPos);
            Vector3 worldPos = new Vector3(gridPos.x + 1f, worldY, gridPos.y + 1f); // centre of 2×2

            GameObject go = Instantiate(prefab, worldPos, Quaternion.identity, transform);
            go.name = $"{prefab.name}_{gridPos.x}_{gridPos.y}";

            Industry industry = go.GetComponent<Industry>();
            if (industry == null)
            {
                Debug.LogError($"[IndustryManager] Prefab '{prefab.name}' has no Industry component.");
                Destroy(go);
                return;
            }

            industry.SetGridPosition(gridPos);
            RegisterIndustry(industry);
            OnIndustrySpawned?.Invoke(industry);
        }

        // ─── Placement Logic ──────────────────────────────────────────────────────

        /// <summary>
        /// Finds a valid grid position for an industry of the given type.
        /// Returns null if no valid position was found within maxPlacementAttempts.
        /// </summary>
        private Vector2Int? FindValidPlacement(IndustryType type, int gridSize)
        {
            int border  = mapEdgeBorder;
            int minCoord = border;
            int maxCoord = gridSize - border - 2; // -2 for 2×2 footprint

            if (maxCoord <= minCoord)
            {
                Debug.LogError("[IndustryManager] Grid too small for industry placement with current border settings.");
                return null;
            }

            for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
            {
                int x = UnityEngine.Random.Range(minCoord, maxCoord);
                int z = UnityEngine.Random.Range(minCoord, maxCoord);
                var candidate = new Vector2Int(x, z);

                if (IsValidPlacement(candidate, type))
                    return candidate;
            }

            return null;
        }

        private bool IsValidPlacement(Vector2Int pos, IndustryType type)
        {
            // 1. Check tile occupancy for all 4 tiles of the 2×2 footprint
            for (int dx = 0; dx < 2; dx++)
                for (int dz = 0; dz < 2; dz++)
                    if (_tileOccupancy.ContainsKey(new Vector2Int(pos.x + dx, pos.y + dz)))
                        return false;

            // 2. Check terrain — no water, reasonably flat
            if (gridManager != null)
            {
                if (!IsTerrainSuitable(pos))
                    return false;
            }

            // 3. Minimum separation from same-type industries
            foreach (var industry in _allIndustries)
            {
                if (industry.IndustryType != type) continue;

                int separation = ManhattanDistance(pos, industry.GridPosition);
                if (separation < minSeparationTiles)
                    return false;
            }

            return true;
        }

        private bool IsTerrainSuitable(Vector2Int pos)
        {
            if (gridManager == null) return true;

            int minHeight = int.MaxValue;
            int maxHeight = int.MinValue;

            for (int dx = 0; dx < 2; dx++)
            {
                for (int dz = 0; dz < 2; dz++)
                {
                    var tilePos2 = new Vector2Int(pos.x + dx, pos.y + dz);
                    var tile     = gridManager.GetTile(tilePos2.x, tilePos2.y);

                    if (tile == null) return false;

                    // Reject water tiles
                    if (tile.Terrain == TerrainType.Water) return false;

                    minHeight = Mathf.Min(minHeight, tile.Height);
                    maxHeight = Mathf.Max(maxHeight, tile.Height);
                }
            }

            return (maxHeight - minHeight) <= maxHeightVariance;
        }

        // ─── Registry ─────────────────────────────────────────────────────────────

        private void RegisterIndustry(Industry industry)
        {
            _allIndustries.Add(industry);

            // Register all occupied tiles
            foreach (var tile in industry.GetOccupiedTiles())
                _tileOccupancy[tile] = industry;
        }

        /// <summary>
        /// Removes an industry from the registry (e.g., if demolished in the future).
        /// </summary>
        public void UnregisterIndustry(Industry industry)
        {
            _allIndustries.Remove(industry);
            foreach (var tile in industry.GetOccupiedTiles())
                _tileOccupancy.Remove(tile);
        }

        // ─── Spatial Queries ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns all industries whose footprint overlaps a circle of
        /// <paramref name="radius"/> tiles centred on <paramref name="center"/>.
        /// Used by stations to determine acceptance radius.
        /// </summary>
        /// <param name="center">Grid tile at the centre of the search area.</param>
        /// <param name="radius">Search radius in tiles.</param>
        /// <returns>List of industries within range (may be empty).</returns>
        public List<Industry> GetIndustriesInRadius(Vector2Int center, int radius)
        {
            var result = new List<Industry>();

            foreach (var industry in _allIndustries)
            {
                // Check if any tile of the industry is within radius
                foreach (var tile in industry.GetOccupiedTiles())
                {
                    if (ManhattanDistance(center, tile) <= radius)
                    {
                        result.Add(industry);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the industry at a specific grid tile, or null if none.
        /// </summary>
        public Industry GetIndustryAtTile(Vector2Int tilePos)
        {
            _tileOccupancy.TryGetValue(tilePos, out Industry industry);
            return industry;
        }

        /// <summary>
        /// Called by <see cref="Station"/> when a train delivers cargo.
        /// Routes the delivered cargo to the nearest accepting industry in radius.
        /// </summary>
        /// <param name="stationPos">Grid position of the delivering station.</param>
        /// <param name="cargoType">Cargo type being delivered.</param>
        /// <param name="amount">Amount delivered.</param>
        /// <param name="radius">Station acceptance radius.</param>
        public void DeliverCargo(Vector2Int stationPos, CargoType cargoType, int amount, int radius)
        {
            if (amount <= 0) return;

            var nearby = GetIndustriesInRadius(stationPos, radius);
            int remaining = amount;

            foreach (var industry in nearby)
            {
                if (!industry.AcceptsCargo(cargoType)) continue;

                // If it's a power station, trigger the full delivery payment flow
                if (industry is PowerStation ps)
                {
                    // Estimate route distance as station-to-industry Manhattan distance
                    float dist = ManhattanDistance(stationPos, industry.GridPosition);
                    dist = Mathf.Max(dist, 1f);
                    ps.ReceiveDelivery(remaining, dist, transitDays: 0);
                    return;
                }

                int accepted = industry.DeliverCargo(cargoType, remaining);
                remaining   -= accepted;
                if (remaining <= 0) break;
            }
        }

        /// <summary>
        /// Returns whether there is an industry within <paramref name="radius"/> tiles of
        /// <paramref name="stationPos"/> that produces or consumes the given cargo type.
        /// Used by <see cref="Station"/> to determine cargo acceptance.
        /// </summary>
        public bool HasIndustryNearby(Vector2Int stationPos, int radius, CargoType cargoType, bool producer)
        {
            var nearby = GetIndustriesInRadius(stationPos, radius);
            foreach (var industry in nearby)
            {
                if (producer && industry.ProducesCargo(cargoType)) return true;
                if (!producer && industry.AcceptsCargo(cargoType))  return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the nearest industry of a specific type to the given tile.
        /// </summary>
        public Industry GetNearestIndustryOfType(Vector2Int origin, IndustryType type)
        {
            Industry nearest    = null;
            int      nearestDist = int.MaxValue;

            foreach (var industry in _allIndustries)
            {
                if (industry.IndustryType != type) continue;

                int dist = ManhattanDistance(origin, industry.GridPosition);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest     = industry;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Returns all coal mines on the map.
        /// </summary>
        public List<CoalMine> GetAllCoalMines()
        {
            var result = new List<CoalMine>();
            foreach (var ind in _allIndustries)
                if (ind is CoalMine mine)
                    result.Add(mine);
            return result;
        }

        /// <summary>
        /// Returns all power stations on the map.
        /// </summary>
        public List<PowerStation> GetAllPowerStations()
        {
            var result = new List<PowerStation>();
            foreach (var ind in _allIndustries)
                if (ind is PowerStation ps)
                    result.Add(ps);
            return result;
        }

        // ─── Utilities ────────────────────────────────────────────────────────────

        private static int ManhattanDistance(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        private float GetWorldHeight(Vector2Int gridPos)
        {
            if (gridManager == null) return 0f;
            var tile = gridManager.GetTile(gridPos.x, gridPos.y);
            // Each height level = 0.5 world units (adjust to match TerrainChunk.cs HeightStep)
            const float heightScale = 0.5f;
            return tile != null ? tile.Height * heightScale : 0f;
        }

#if UNITY_EDITOR
        [ContextMenu("Debug: Spawn Industries Now")]
        private void DebugSpawn() => SpawnIndustries();

        [ContextMenu("Debug: Print Industry List")]
        private void DebugPrintList()
        {
            Debug.Log($"[IndustryManager] {_allIndustries.Count} industries on map:");
            foreach (var ind in _allIndustries)
                Debug.Log($"  • {ind.IndustryType} at {ind.GridPosition}");
        }
#endif
    }
}
