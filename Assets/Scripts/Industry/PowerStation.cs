using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// A coal-consuming industry. Accepts Coal deliveries from trains,
    /// consumes coal from its input stockpile over time, and pays the
    /// <see cref="EconomyManager"/> for each batch consumed.
    ///
    /// Visual setup (Unity Editor):
    ///   1. Attach this component to a new GameObject (e.g., "PowerStation").
    ///   2. Create a child cube named "Visual" (scale ~1.8×1.8×1.8)
    ///      with a red/dark-brown URP Lit material.
    ///   3. Optionally add a second smaller cube as a "chimney" child for
    ///      visual interest (scale ~0.3×0.8×0.3, offset to one corner).
    ///   4. Create a child TextMeshPro (3D) named "Label" above the building.
    ///   5. Save as Prefab at Assets/Prefabs/Industries/PowerStationPrefab.
    ///
    /// Economy model:
    ///   - When a train delivers coal, <see cref="ReceiveDelivery"/> is called
    ///     with the delivery amount and route distance/transit data.
    ///   - The station pays out based on <see cref="CargoPayment.CalculatePayment"/>
    ///     at the moment of delivery (not on consumption).
    ///   - The power station then burns coal from its stockpile at
    ///     <see cref="consumptionRatePerCycle"/> tonnes per cycle.
    /// </summary>
    public class PowerStation : Industry
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("Power Station — Consumption")]
        [Tooltip("Coal consumed from the input stockpile per production cycle.")]
        [SerializeField] [Range(1, 100)] private int consumptionRatePerCycle = 10;

        [Tooltip("When stockpile drops below this threshold, the power station is " +
                 "considered under-supplied (used for visual/alert feedback).")]
        [SerializeField] [Range(0, 200)] private int lowStockpileWarningThreshold = 20;

        [Header("Power Station — Economy")]
        [Tooltip("Multiplier applied to the base CargoPayment rate when paying for coal. " +
                 "Set > 1.0 to make power stations higher-value destinations.")]
        [SerializeField] [Range(0.5f, 3f)] private float paymentMultiplier = 1.0f;

        [Header("Power Station — Visuals")]
        [Tooltip("Material applied to the main visual cube. Use a red/brown URP Lit material.")]
        [SerializeField] private Material powerStationMaterial;

        [Tooltip("Label colour. Defaults to near-white.")]
        [SerializeField] private Color labelColour = new Color(0.95f, 0.9f, 0.85f);

        // ─── Runtime State ────────────────────────────────────────────────────────

        private int  _totalCoalConsumed;
        private int  _totalCoalReceived;
        private long _totalMoneyPaid;
        private bool _isUnderSupplied;

        // ─── Properties ──────────────────────────────────────────────────────────

        /// <summary>Current coal waiting in the input stockpile.</summary>
        public int CoalStockpile =>
            inputStockpile.TryGetValue(CargoType.Coal, out int v) ? v : 0;

        /// <summary>Total coal tonnes received via deliveries since spawn.</summary>
        public int TotalCoalReceived => _totalCoalReceived;

        /// <summary>Total coal tonnes actually burned since spawn.</summary>
        public int TotalCoalConsumed => _totalCoalConsumed;

        /// <summary>Total money paid out to train operators.</summary>
        public long TotalMoneyPaid => _totalMoneyPaid;

        /// <summary>True when the stockpile is below the warning threshold.</summary>
        public bool IsUnderSupplied => _isUnderSupplied;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        protected override void Awake()
        {
            // Configure base-class fields before Awake initialises stockpiles
            industryType   = IndustryType.PowerStation;
            industryName   = "Power Station";
            description    = "Burns coal to generate electricity. Accepts coal deliveries.";
            outputCargoTypes.Clear();
            inputCargoTypes.Clear();
            inputCargoTypes.Add(CargoType.Coal);

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
            if (visualObject != null && powerStationMaterial != null)
            {
                var renderer = visualObject.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material = powerStationMaterial;
            }

            if (nameLabel != null)
            {
                nameLabel.text  = industryName;
                nameLabel.color = labelColour;
            }
        }

        // ─── Consumption Override ─────────────────────────────────────────────────

        /// <summary>
        /// Burns coal from the input stockpile each production cycle.
        /// The power station does not pay on consumption — payment is handled
        /// at delivery time via <see cref="ReceiveDelivery"/>.
        /// </summary>
        protected override void OnConsumption(IndustryProductionReport report)
        {
            int stockpile = CoalStockpile;
            if (stockpile <= 0) return;

            int consumed = Mathf.Min(consumptionRatePerCycle, stockpile);
            inputStockpile[CargoType.Coal] -= consumed;
            _totalCoalConsumed            += consumed;

            // Update under-supply flag
            _isUnderSupplied = CoalStockpile < lowStockpileWarningThreshold;

            report.InputCargo    = CargoType.Coal;
            report.CargoConsumed = consumed;
        }

        // ─── Delivery API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Called by the Station or Train system when a train delivers coal here.
        /// Accepts the cargo into the stockpile and pays the player immediately.
        /// </summary>
        /// <param name="amount">Tonnes of coal being delivered.</param>
        /// <param name="routeDistanceTiles">Distance of the route that carried the cargo.</param>
        /// <param name="transitDays">Days the cargo spent in transit (affects payment).</param>
        /// <returns>Payment made to the player (in currency units).</returns>
        public long ReceiveDelivery(int amount, float routeDistanceTiles, int transitDays)
        {
            if (amount <= 0) return 0;

            // Accept as many tonnes as the stockpile has room for
            int accepted = DeliverCargo(CargoType.Coal, amount);
            if (accepted == 0)
            {
                Debug.LogWarning($"[PowerStation:{industryName}] Stockpile full — delivery rejected.");
                return 0;
            }

            _totalCoalReceived += accepted;

            // Calculate payment
            int   basePayment = CargoPayment.CalculatePayment(CargoType.Coal, accepted, routeDistanceTiles, transitDays);
            long  payment     = Mathf.RoundToInt(basePayment * paymentMultiplier);

            if (payment > 0 && EconomyManager.Instance != null)
            {
                EconomyManager.Instance.AddMoney(payment,
                    $"Coal delivery to {industryName} ({accepted}t, {routeDistanceTiles:F0} tiles)");
            }

            _totalMoneyPaid += payment;

            Debug.Log($"[PowerStation:{industryName}] Received {accepted}t coal → paid ${payment:N0}. " +
                      $"Stockpile: {CoalStockpile}t");

            return payment;
        }

        // ─── Info Panel Data ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns a human-readable summary for the info panel.
        /// </summary>
        public string GetInfoPanelText()
        {
            string supplyStatus = _isUnderSupplied
                ? "<color=#FF4444>LOW SUPPLY</color>"
                : "<color=#44FF44>Supplied</color>";

            return $"<b>{industryName}</b>\n" +
                   $"Coal stockpile: {CoalStockpile}t / {maxStockpile}t  {supplyStatus}\n" +
                   $"Consumption: {consumptionRatePerCycle}t/cycle\n" +
                   $"Total received: {_totalCoalReceived}t\n" +
                   $"Total paid out: {CargoPayment.FormatCurrency(_totalMoneyPaid)}";
        }

        // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        protected override void OnDrawGizmos()
        {
            Gizmos.color = _isUnderSupplied
                ? new Color(1f, 0.2f, 0.1f, 0.8f)
                : new Color(0.75f, 0.25f, 0.1f, 0.7f);

            Vector3 centre = new Vector3(
                gridPosition.x + sizeInTiles * 0.5f,
                transform.position.y + 0.5f,
                gridPosition.y + sizeInTiles * 0.5f);
            Gizmos.DrawWireCube(centre, new Vector3(sizeInTiles, 1.8f, sizeInTiles));
            UnityEditor.Handles.Label(centre + Vector3.up * 2f,
                $"Power Station\n{CoalStockpile}t {(_isUnderSupplied ? "⚠" : "")}");
        }
#endif
    }
}
