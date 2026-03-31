using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// A coal producer industry. Generates Coal cargo at a randomised rate
    /// each production cycle, accumulating it in the output stockpile until
    /// a nearby station collects it.
    ///
    /// Visual setup (Unity Editor):
    ///   1. Attach this component to a new GameObject (e.g., "CoalMine").
    ///   2. Create a child GameObject named "Visual" with a MeshRenderer
    ///      (cube, scale ~1.8×1.2×1.8) and a dark-grey material.
    ///   3. Create a child TextMeshPro (3D) named "Label", positioned
    ///      ~1.5 units above the pivot, facing camera (Billboard or fixed Y).
    ///   4. Assign "Visual" and "Label" in the inspector.
    ///   5. Save as a Prefab in Assets/Prefabs/Industries/CoalMinePrefab.
    ///
    /// Production model:
    ///   - Each cycle produces between <see cref="minProductionPerCycle"/> and
    ///     <see cref="maxProductionPerCycle"/> tonnes.
    ///   - A small noise factor (+/- <see cref="productionVariance"/> %) is applied
    ///     each cycle so output fluctuates naturally.
    ///   - Production halts when the stockpile is full.
    /// </summary>
    public class CoalMine : Industry
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("Coal Mine — Production")]
        [Tooltip("Minimum coal produced per production cycle (tonnes).")]
        [SerializeField] [Range(1, 200)] private int minProductionPerCycle = 8;

        [Tooltip("Maximum coal produced per production cycle (tonnes).")]
        [SerializeField] [Range(1, 200)] private int maxProductionPerCycle = 25;

        [Tooltip("±Percentage variance applied each cycle to simulate realistic fluctuation (0–50%).")]
        [SerializeField] [Range(0f, 0.5f)] private float productionVariance = 0.15f;

        [Tooltip("How many consecutive cycles the mine should be 'boosted' after a delivery. " +
                 "Mirrors OpenTTD's production boost on service.")]
        [SerializeField] [Range(0, 5)] private int serviceBoostedCycles = 2;

        [Header("Coal Mine — Visuals")]
        [Tooltip("Material applied to the visual cube. Assign a dark-grey URP Lit material.")]
        [SerializeField] private Material coalMineMaterial;

        [Tooltip("Colour of the name label TextMeshPro. Defaults to near-white.")]
        [SerializeField] private Color labelColour = new Color(0.9f, 0.9f, 0.9f);

        // ─── Runtime State ────────────────────────────────────────────────────────

        private int   _boostedCyclesRemaining;
        private int   _totalCoalProduced;
        private float _currentProductionRate; // smoothed display value

        // ─── Properties ──────────────────────────────────────────────────────────

        /// <summary>Total coal produced since the mine was created.</summary>
        public int TotalCoalProduced => _totalCoalProduced;

        /// <summary>Smoothed coal-per-cycle rate used for display in the info panel.</summary>
        public float CurrentProductionRate => _currentProductionRate;

        /// <summary>Current coal waiting in the stockpile.</summary>
        public int CoalStockpile =>
            outputStockpile.TryGetValue(CargoType.Coal, out int v) ? v : 0;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        protected override void Awake()
        {
            // Configure base-class fields before Awake initialises stockpiles
            industryType   = IndustryType.CoalMine;
            industryName   = "Coal Mine";
            description    = "Mines coal from underground seams.";
            outputCargoTypes.Clear();
            outputCargoTypes.Add(CargoType.Coal);
            inputCargoTypes.Clear();

            base.Awake();
        }

        protected override void Start()
        {
            base.Start();
            ApplyVisuals();
        }

        // ─── Visual Setup ─────────────────────────────────────────────────────────

        private void ApplyVisuals()
        {
            // Apply material to visual mesh
            if (visualObject != null && coalMineMaterial != null)
            {
                var renderer = visualObject.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material = coalMineMaterial;
            }

            // Style the label
            if (nameLabel != null)
            {
                nameLabel.text  = industryName;
                nameLabel.color = labelColour;
            }
        }

        // ─── Production Override ──────────────────────────────────────────────────

        /// <summary>
        /// Generates coal each cycle. Amount is randomised between min/max,
        /// with a variance factor and an optional service boost.
        /// Production is suppressed when the stockpile is at capacity.
        /// </summary>
        protected override void OnProduction(IndustryProductionReport report)
        {
            int currentStockpile = CoalStockpile;
            if (currentStockpile >= maxStockpile)
            {
                // Stockpile full — skip production
                return;
            }

            // Base production amount (integer random in min–max range)
            int baseAmount = Random.Range(minProductionPerCycle, maxProductionPerCycle + 1);

            // Apply variance: multiply by (1 ± variance)
            float varianceFactor = 1f + Random.Range(-productionVariance, productionVariance);
            int   amount         = Mathf.Max(1, Mathf.RoundToInt(baseAmount * varianceFactor));

            // Service boost: mines served regularly produce more
            if (_boostedCyclesRemaining > 0)
            {
                amount = Mathf.RoundToInt(amount * 1.5f);
                _boostedCyclesRemaining--;
            }

            // Clamp so we don't overflow the stockpile
            int space    = maxStockpile - currentStockpile;
            int produced = Mathf.Min(amount, space);

            outputStockpile[CargoType.Coal] += produced;
            _totalCoalProduced              += produced;

            // Update smoothed production rate (exponential moving average)
            _currentProductionRate = Mathf.Lerp(_currentProductionRate, produced, 0.3f);

            report.OutputCargo   = CargoType.Coal;
            report.CargoProduced = produced;
        }

        // ─── Cargo Interaction ────────────────────────────────────────────────────

        /// <summary>
        /// Called when a station picks up coal from this mine.
        /// Triggers a production boost for the next few cycles (OpenTTD mechanic).
        /// </summary>
        public new int PickupCargo(CargoType cargoType, int requestedAmount)
        {
            int taken = base.PickupCargo(cargoType, requestedAmount);

            if (taken > 0 && cargoType == CargoType.Coal)
            {
                _boostedCyclesRemaining = serviceBoostedCycles;
                Debug.Log($"[CoalMine:{industryName}] Station collected {taken}t coal. " +
                          $"Production boost for {serviceBoostedCycles} cycles.");
            }

            return taken;
        }

        // ─── Info Panel Data ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns a human-readable summary for the info panel.
        /// </summary>
        public string GetInfoPanelText()
        {
            return $"<b>{industryName}</b>\n" +
                   $"Coal stockpile: {CoalStockpile}t / {maxStockpile}t\n" +
                   $"Rate: ~{_currentProductionRate:F1} t/cycle\n" +
                   $"Total produced: {_totalCoalProduced}t";
        }

        // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        protected override void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.25f, 0.25f, 0.25f, 0.8f);
            Vector3 centre = new Vector3(
                gridPosition.x + sizeInTiles * 0.5f,
                transform.position.y + 0.5f,
                gridPosition.y + sizeInTiles * 0.5f);
            Gizmos.DrawWireCube(centre, new Vector3(sizeInTiles, 1.2f, sizeInTiles));
            UnityEditor.Handles.Label(centre + Vector3.up * 1.8f, $"Coal Mine\n{CoalStockpile}t");
        }
#endif
    }
}
