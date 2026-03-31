using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Represents a train station placed on the tile grid.
    ///
    /// <para>
    /// A station occupies a single rail tile in the prototype.  It maintains a
    /// cargo waiting dictionary, a service-frequency-based rating (0–100), and
    /// queries nearby industries to determine which cargo types it produces and
    /// accepts.
    /// </para>
    /// </summary>
    public class Station : MonoBehaviour
    {
        // ── Inspector fields ────────────────────────────────────────────────

        /// <summary>Human-readable station name (auto-generated or set via inspector).</summary>
        [SerializeField] private string stationName = "New Station";

        /// <summary>Grid X coordinate of this station.</summary>
        [SerializeField] private int gridX;

        /// <summary>Grid Z coordinate of this station.</summary>
        [SerializeField] private int gridZ;

        /// <summary>
        /// Acceptance radius in grid tiles.  All industry tiles within this
        /// Chebyshev distance affect which cargos this station produces/accepts.
        /// </summary>
        [SerializeField, Min(1)] private int acceptanceRadius = 3;

        /// <summary>
        /// Seconds between automatic rating-update ticks.  A train visit also
        /// triggers an update immediately.
        /// </summary>
        [SerializeField] private float ratingUpdateInterval = 30f;

        // ── Runtime state ───────────────────────────────────────────────────

        /// <summary>Cargo waiting at this station, keyed by cargo type.</summary>
        private readonly Dictionary<CargoType, int> waitingCargo = new Dictionary<CargoType, int>();

        /// <summary>Station rating 0–100, affected by service frequency.</summary>
        private int rating = 50;

        /// <summary>Time of the last train visit (game time or real time).</summary>
        private float lastVisitTime;

        /// <summary>Number of train visits since the last rating update.</summary>
        private int visitsSinceLastRatingUpdate;

        private float ratingUpdateTimer;

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>Fired whenever the cargo amounts change.</summary>
        public event Action OnCargoUpdated;

        /// <summary>Fired when the station rating changes.</summary>
        public event Action<int> OnRatingChanged;

        // ── Properties ──────────────────────────────────────────────────────

        /// <summary>Display name of this station.</summary>
        public string StationName
        {
            get => stationName;
            set => stationName = value;
        }

        /// <summary>Grid position (X, Z) of this station tile.</summary>
        public Vector2Int GridPosition => new Vector2Int(gridX, gridZ);

        /// <summary>Current service rating (0–100).</summary>
        public int Rating => rating;

        /// <summary>Acceptance radius in tiles.</summary>
        public int AcceptanceRadius => acceptanceRadius;

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Start()
        {
            // Auto-generate a name based on position if none was set
            if (string.IsNullOrWhiteSpace(stationName) || stationName == "New Station")
                stationName = GenerateName(gridX, gridZ);

            ratingUpdateTimer = ratingUpdateInterval;
        }

        private void Update()
        {
            ratingUpdateTimer -= Time.deltaTime;
            if (ratingUpdateTimer <= 0f)
            {
                UpdateRating();
                ratingUpdateTimer = ratingUpdateInterval;
            }
        }

        // ── Initialisation ──────────────────────────────────────────────────

        /// <summary>
        /// Initialises the station at the given grid position.
        /// Called by <see cref="StationPlacer"/> after instantiation.
        /// </summary>
        public void Initialise(int x, int z, string name = null)
        {
            gridX       = x;
            gridZ       = z;
            stationName = string.IsNullOrWhiteSpace(name) ? GenerateName(x, z) : name;
        }

        // ── Cargo production / acceptance ────────────────────────────────────

        /// <summary>
        /// Returns true if any industry within the acceptance radius produces
        /// the given cargo type (i.e., the station will collect it for trains to pick up).
        /// </summary>
        /// <param name="type">The cargo type to test.</param>
        public bool ProducesCargo(CargoType type)
        {
            return IndustryExistsNearby(type, producer: true);
        }

        /// <summary>
        /// Returns true if any industry within the acceptance radius consumes
        /// the given cargo type (i.e., a train can deliver it here).
        /// </summary>
        /// <param name="type">The cargo type to test.</param>
        public bool AcceptsCargo(CargoType type)
        {
            return IndustryExistsNearby(type, producer: false);
        }

        // ── Cargo management ────────────────────────────────────────────────

        /// <summary>
        /// Adds cargo to this station's waiting queue (called by nearby industries).
        /// </summary>
        /// <param name="type">Cargo type to add.</param>
        /// <param name="amount">Amount to add.</param>
        public void AddWaitingCargo(CargoType type, int amount)
        {
            if (amount <= 0) return;
            if (!waitingCargo.ContainsKey(type))
                waitingCargo[type] = 0;
            waitingCargo[type] += amount;
            OnCargoUpdated?.Invoke();
        }

        /// <summary>
        /// Removes up to <paramref name="amount"/> units of <paramref name="type"/>
        /// from the waiting queue.  Used by trains when loading.
        /// </summary>
        /// <param name="type">Cargo type to load.</param>
        /// <param name="amount">Maximum amount to take.</param>
        /// <returns>Actual amount taken (may be less than requested).</returns>
        public int TakeCargo(CargoType type, int amount)
        {
            if (amount <= 0) return 0;
            if (!waitingCargo.TryGetValue(type, out int available)) return 0;
            if (available <= 0) return 0;

            int taken = Mathf.Min(available, amount);
            waitingCargo[type] -= taken;
            if (waitingCargo[type] <= 0)
                waitingCargo.Remove(type);

            OnCargoUpdated?.Invoke();
            return taken;
        }

        /// <summary>
        /// Records a cargo delivery at this station (called by trains on unload).
        /// The station routes the cargo to nearby consuming industries.
        /// </summary>
        /// <param name="type">Cargo type delivered.</param>
        /// <param name="amount">Amount delivered.</param>
        public void DeliverCargo(CargoType type, int amount)
        {
            if (amount <= 0) return;

            visitsSinceLastRatingUpdate++;
            lastVisitTime = Time.time;

            // Notify nearby consuming industries
            if (IndustryManager.Instance != null)
                IndustryManager.Instance.DeliverCargo(GridPosition, type, amount, acceptanceRadius);
        }

        /// <summary>
        /// Returns the amount of a given cargo type currently waiting at this station.
        /// </summary>
        public int GetWaitingCargo(CargoType type) =>
            waitingCargo.TryGetValue(type, out int amt) ? amt : 0;

        /// <summary>Returns a copy of the full waiting cargo dictionary.</summary>
        public Dictionary<CargoType, int> GetAllWaitingCargo() =>
            new Dictionary<CargoType, int>(waitingCargo);

        // ── Rating ──────────────────────────────────────────────────────────

        /// <summary>
        /// Recalculates the station's service rating based on how frequently
        /// trains have visited.  Rating decays over time if no trains arrive.
        ///
        /// Rating formula (simplified OpenTTD approach):
        /// <list type="bullet">
        ///   <item>Base decay of 1 point per interval without service.</item>
        ///   <item>+15 per visit since last update, capped at 100.</item>
        /// </list>
        /// </summary>
        public void UpdateRating()
        {
            int visits = visitsSinceLastRatingUpdate;
            visitsSinceLastRatingUpdate = 0;

            int delta = (visits * 15) - (visits == 0 ? 3 : 0);
            int newRating = Mathf.Clamp(rating + delta, 0, 100);

            if (newRating != rating)
            {
                rating = newRating;
                OnRatingChanged?.Invoke(rating);
            }
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private bool IndustryExistsNearby(CargoType type, bool producer)
        {
            if (IndustryManager.Instance == null) return false;
            return IndustryManager.Instance.HasIndustryNearby(
                GridPosition, acceptanceRadius, type, producer);
        }

        private static string GenerateName(int x, int z)
        {
            // Simple procedural name: two-part word based on position hash
            string[] prefixes = { "North", "South", "East", "West", "Central", "New", "Old", "Upper", "Lower" };
            string[] suffixes = { "Junction", "Station", "Halt", "Yard", "Crossing", "Depot" };

            int hash    = Mathf.Abs(x * 31 + z * 17);
            string pre  = prefixes[hash % prefixes.Length];
            string suf  = suffixes[(hash / prefixes.Length) % suffixes.Length];
            return $"{pre} {suf}";
        }
    }
}
