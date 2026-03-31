using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Defines all industry categories in the game world.
    /// Only CoalMine and PowerStation are implemented in the prototype.
    /// </summary>
    public enum IndustryType
    {
        CoalMine        = 0,
        PowerStation    = 1,
        Farm            = 2,
        SawMill         = 3,
        IronOreMine     = 4,
        SteelMill       = 5,
        OilRefinery     = 6,
        Factory         = 7,
        FoodProcessing  = 8,
        PrintingWorks   = 9,
    }

    /// <summary>
    /// Abstract base class for all industries on the game map.
    ///
    /// Subclasses override <see cref="OnProduction"/> to implement cargo generation
    /// (producers) or <see cref="OnConsumption"/> for consumers.
    ///
    /// Setup notes (Unity Editor):
    ///   1. Attach subclass (e.g., CoalMine) to a new GameObject — the base
    ///      class component is added automatically via [RequireComponent].
    ///   2. Assign a 2-unit-wide cube as the visual child named "Visual".
    ///   3. IndustryManager.SpawnIndustries() handles runtime instantiation.
    ///
    /// Grid layout:
    ///   Industries occupy a square footprint of <see cref="sizeInTiles"/> tiles
    ///   (default 2×2). The pivot is the south-west corner tile.
    /// </summary>
    public abstract class Industry : MonoBehaviour
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("Identity")]
        [SerializeField] protected IndustryType industryType = IndustryType.CoalMine;
        [SerializeField] protected string industryName = "Industry";
        [SerializeField] [TextArea(1, 2)] protected string description = "";

        [Header("Grid Placement")]
        [Tooltip("Tile-space position of the south-west corner of this industry.")]
        [SerializeField] protected Vector2Int gridPosition;

        [Tooltip("Footprint in tiles on each axis (industries are square, default 2×2).")]
        [SerializeField] [Range(1, 4)] protected int sizeInTiles = 2;

        [Header("Production")]
        [Tooltip("How often (in real seconds scaled by game speed) one production cycle runs.")]
        [SerializeField] protected float productionIntervalSeconds = 5f;

        [Tooltip("Maximum cargo units that can accumulate before production stops.")]
        [SerializeField] protected int maxStockpile = 500;

        [Header("Cargo Definition")]
        [Tooltip("Cargo types this industry produces. Usually one entry.")]
        [SerializeField] protected List<CargoType> outputCargoTypes = new List<CargoType>();

        [Tooltip("Cargo types this industry accepts/consumes. Usually one entry.")]
        [SerializeField] protected List<CargoType> inputCargoTypes = new List<CargoType>();

        [Header("Visuals")]
        [Tooltip("Child GameObject that holds the visual mesh. Named 'Visual' by convention.")]
        [SerializeField] protected GameObject visualObject;

        [Tooltip("Label displayed above the industry (use world-space canvas or TextMeshPro).")]
        [SerializeField] protected TextMeshPro nameLabel;

        // ─── Runtime State ────────────────────────────────────────────────────────

        /// <summary>Current stockpile per output cargo type (cargo waiting for pickup).</summary>
        protected Dictionary<CargoType, int> outputStockpile = new Dictionary<CargoType, int>();

        /// <summary>Current stockpile per input cargo type (cargo delivered but not yet consumed).</summary>
        protected Dictionary<CargoType, int> inputStockpile = new Dictionary<CargoType, int>();

        private float _productionTimer;
        private bool  _isActive = true;

        // ─── Events ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired at the end of each production cycle with a summary of what changed.
        /// Consumed by IndustryManager and Station for cargo pickup logic.
        /// </summary>
        public event Action<IndustryProductionReport> OnProductionCycle;

        // ─── Public Properties ────────────────────────────────────────────────────

        public IndustryType IndustryType   => industryType;
        public string       IndustryName   => industryName;
        public Vector2Int   GridPosition   => gridPosition;
        public int          SizeInTiles    => sizeInTiles;
        public bool         IsActive       => _isActive;

        /// <summary>Read-only view of output cargo stockpile.</summary>
        public IReadOnlyDictionary<CargoType, int> OutputStockpile => outputStockpile;

        /// <summary>Read-only view of input cargo stockpile.</summary>
        public IReadOnlyDictionary<CargoType, int> InputStockpile => inputStockpile;

        /// <summary>List of cargo types this industry produces.</summary>
        public IReadOnlyList<CargoType> OutputCargoTypes => outputCargoTypes;

        /// <summary>List of cargo types this industry accepts.</summary>
        public IReadOnlyList<CargoType> InputCargoTypes => inputCargoTypes;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        protected virtual void Awake()
        {
            InitialiseStockpiles();
        }

        protected virtual void Start()
        {
            SetupVisual();
        }

        protected virtual void Update()
        {
            if (!_isActive) return;

            _productionTimer += Time.deltaTime;

            if (_productionTimer >= productionIntervalSeconds)
            {
                _productionTimer -= productionIntervalSeconds;
                RunProductionCycle();
            }
        }

        // ─── Initialisation ───────────────────────────────────────────────────────

        private void InitialiseStockpiles()
        {
            foreach (var cargoType in outputCargoTypes)
                if (!outputStockpile.ContainsKey(cargoType))
                    outputStockpile[cargoType] = 0;

            foreach (var cargoType in inputCargoTypes)
                if (!inputStockpile.ContainsKey(cargoType))
                    inputStockpile[cargoType] = 0;
        }

        private void SetupVisual()
        {
            if (nameLabel != null)
                nameLabel.text = industryName;
        }

        // ─── Production / Consumption ─────────────────────────────────────────────

        private void RunProductionCycle()
        {
            var report = new IndustryProductionReport(industryType, gridPosition);

            OnProduction(report);
            OnConsumption(report);

            ClampStockpiles();
            OnProductionCycle?.Invoke(report);
            LogMonthlyReport(report);
        }

        /// <summary>
        /// Override in producer subclasses to add cargo to <see cref="outputStockpile"/>.
        /// </summary>
        protected virtual void OnProduction(IndustryProductionReport report) { }

        /// <summary>
        /// Override in consumer subclasses to remove cargo from <see cref="inputStockpile"/>
        /// and potentially call <see cref="EconomyManager.AddMoney"/>.
        /// </summary>
        protected virtual void OnConsumption(IndustryProductionReport report) { }

        private void ClampStockpiles()
        {
            foreach (var key in new List<CargoType>(outputStockpile.Keys))
                outputStockpile[key] = Mathf.Clamp(outputStockpile[key], 0, maxStockpile);

            foreach (var key in new List<CargoType>(inputStockpile.Keys))
                inputStockpile[key] = Mathf.Clamp(inputStockpile[key], 0, maxStockpile);
        }

        // ─── Cargo Interaction ────────────────────────────────────────────────────

        /// <summary>
        /// Called by a Station when a train picks up cargo.
        /// Removes up to <paramref name="requestedAmount"/> units from the output stockpile.
        /// </summary>
        /// <param name="cargoType">Cargo type being picked up.</param>
        /// <param name="requestedAmount">Maximum amount the train can take.</param>
        /// <returns>Actual amount removed (may be less if stockpile is low).</returns>
        public int PickupCargo(CargoType cargoType, int requestedAmount)
        {
            if (!outputStockpile.ContainsKey(cargoType)) return 0;

            int available = outputStockpile[cargoType];
            int taken     = Mathf.Min(available, requestedAmount);
            outputStockpile[cargoType] -= taken;
            return taken;
        }

        /// <summary>
        /// Called by a Station/Train when delivering cargo to a consumer industry.
        /// Adds cargo to the input stockpile (up to maxStockpile).
        /// </summary>
        /// <param name="cargoType">Cargo type being delivered.</param>
        /// <param name="amount">Amount to deliver.</param>
        /// <returns>Amount actually accepted (0 if this industry doesn't accept the type).</returns>
        public int DeliverCargo(CargoType cargoType, int amount)
        {
            if (!inputStockpile.ContainsKey(cargoType))
            {
                Debug.LogWarning($"[Industry:{industryName}] Does not accept {cargoType}.");
                return 0;
            }

            int current    = inputStockpile[cargoType];
            int space      = maxStockpile - current;
            int accepted   = Mathf.Min(amount, space);
            inputStockpile[cargoType] += accepted;
            return accepted;
        }

        /// <summary>
        /// Returns whether this industry accepts a specific cargo type for delivery.
        /// </summary>
        public bool AcceptsCargo(CargoType cargoType) => inputStockpile.ContainsKey(cargoType);

        /// <summary>
        /// Returns whether this industry produces a specific cargo type.
        /// </summary>
        public bool ProducesCargo(CargoType cargoType) => outputStockpile.ContainsKey(cargoType);

        // ─── Placement ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the grid position (called by IndustryManager at spawn time).
        /// Also repositions the GameObject in world space.
        /// </summary>
        public void SetGridPosition(Vector2Int position)
        {
            gridPosition = position;
            // Each tile is 1 world unit; Y is determined by terrain height (set externally)
            transform.position = new Vector3(position.x, transform.position.y, position.y);
        }

        /// <summary>
        /// Returns all tile positions occupied by this industry.
        /// </summary>
        public List<Vector2Int> GetOccupiedTiles()
        {
            var tiles = new List<Vector2Int>(sizeInTiles * sizeInTiles);
            for (int x = 0; x < sizeInTiles; x++)
                for (int z = 0; z < sizeInTiles; z++)
                    tiles.Add(new Vector2Int(gridPosition.x + x, gridPosition.y + z));
            return tiles;
        }

        // ─── Monthly Report ───────────────────────────────────────────────────────

        private void LogMonthlyReport(IndustryProductionReport report)
        {
            // Lightweight debug log — a proper monthly aggregator would be built on top
            if (report.CargoProduced > 0 || report.CargoConsumed > 0)
            {
                Debug.Log($"[Industry:{industryName}] Cycle — Produced: {report.CargoProduced} {report.OutputCargo}, " +
                          $"Consumed: {report.CargoConsumed} {report.InputCargo}, " +
                          $"Stockpile: {GetTotalOutputStockpile()}");
            }
        }

        private int GetTotalOutputStockpile()
        {
            int total = 0;
            foreach (var v in outputStockpile.Values) total += v;
            return total;
        }

        // ─── Gizmos (Editor) ─────────────────────────────────────────────────────

#if UNITY_EDITOR
        protected virtual void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.8f, 0.5f, 0.1f, 0.5f);
            Vector3 centre = new Vector3(
                gridPosition.x + sizeInTiles * 0.5f,
                transform.position.y + 0.5f,
                gridPosition.y + sizeInTiles * 0.5f);
            Gizmos.DrawWireCube(centre, new Vector3(sizeInTiles, 1f, sizeInTiles));

            UnityEditor.Handles.Label(centre + Vector3.up * 1.5f, industryName);
        }
#endif
    }

    // ─── Supporting Types ──────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot of a single production cycle — passed to event subscribers
    /// and used for monthly report aggregation.
    /// </summary>
    public class IndustryProductionReport
    {
        public IndustryType IndustryType  { get; }
        public Vector2Int   GridPosition  { get; }
        public CargoType    OutputCargo   { get; set; }
        public int          CargoProduced { get; set; }
        public CargoType    InputCargo    { get; set; }
        public int          CargoConsumed { get; set; }
        public long         MoneyEarned   { get; set; }

        public IndustryProductionReport(IndustryType type, Vector2Int pos)
        {
            IndustryType = type;
            GridPosition = pos;
        }
    }
}
