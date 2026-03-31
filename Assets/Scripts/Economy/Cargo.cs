using System;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Defines all cargo types available in the game.
    /// Only Coal is used in the prototype but all types are declared
    /// for future expansion.
    /// </summary>
    public enum CargoType
    {
        Coal = 0,
        Passengers = 1,
        Mail = 2,
        Goods = 3,
        Wood = 4,
        Iron = 5,
        Steel = 6,
        Food = 7,
        Oil = 8
    }

    /// <summary>
    /// ScriptableObject that holds all definition data for a single cargo type.
    /// Create via: Assets → Create → OpenTTD → Cargo Definition
    ///
    /// Inspector Setup:
    ///   - One asset per cargo type lives in Assets/ScriptableObjects/Cargo/
    ///   - Assign each asset to IndustryManager or CargoPayment reference lists
    /// </summary>
    [CreateAssetMenu(fileName = "CargoDef_New", menuName = "OpenTTD/Cargo Definition", order = 1)]
    public class CargoDefinition : ScriptableObject
    {
        // ─── Identity ────────────────────────────────────────────────────────────
        [Header("Identity")]
        [SerializeField] private CargoType cargoType = CargoType.Coal;
        [SerializeField] private string displayName = "Coal";
        [SerializeField] [TextArea(1, 2)] private string description = "Raw coal mined from the earth.";

        // ─── Economy ─────────────────────────────────────────────────────────────
        [Header("Economy")]
        [Tooltip("Base payment per tonne for a 20-tile journey at 0 transit days. " +
                 "Final payment is scaled by distance and transit time.")]
        [SerializeField] [Range(1, 5000)] private int basePaymentRate = 100;

        [Tooltip("Maximum number of days in transit before payment drops to the floor value. " +
                 "Mirrors OpenTTD payment speed penalties.")]
        [SerializeField] [Range(1, 255)] private int maxTransitDays = 40;

        [Tooltip("Minimum fraction of base payment even for very old cargo (0.0 – 1.0).")]
        [SerializeField] [Range(0f, 1f)] private float minimumPaymentFraction = 0.1f;

        // ─── UI ───────────────────────────────────────────────────────────────────
        [Header("UI")]
        [Tooltip("Color used for this cargo in charts, info panels, and cargo bars.")]
        [SerializeField] private Color uiColor = new Color(0.22f, 0.22f, 0.22f); // dark grey for coal

        [Tooltip("Optional sprite icon shown in cargo lists and info panels.")]
        [SerializeField] private Sprite icon = null;

        // ─── Public Accessors ─────────────────────────────────────────────────────
        /// <summary>The cargo type enum value this definition represents.</summary>
        public CargoType CargoType => cargoType;

        /// <summary>Human-readable name shown in UI.</summary>
        public string DisplayName => displayName;

        /// <summary>Short description shown in tooltips.</summary>
        public string Description => description;

        /// <summary>Base payment rate in currency units per tonne at reference distance.</summary>
        public int BasePaymentRate => basePaymentRate;

        /// <summary>Days after which payment starts to decay toward the floor.</summary>
        public int MaxTransitDays => maxTransitDays;

        /// <summary>Minimum payment fraction regardless of transit time.</summary>
        public float MinimumPaymentFraction => minimumPaymentFraction;

        /// <summary>UI colour associated with this cargo type.</summary>
        public Color UIColor => uiColor;

        /// <summary>Optional icon sprite for UI elements.</summary>
        public Sprite Icon => icon;

        // ─── Payment Calculation ──────────────────────────────────────────────────

        /// <summary>
        /// Calculates the total payment for delivering <paramref name="amount"/> tonnes
        /// of this cargo over <paramref name="distanceInTiles"/> tiles with a transit
        /// time of <paramref name="transitDays"/> in-game days.
        ///
        /// Formula mirrors OpenTTD:
        ///   payment = baseRate × amount × distanceFactor × timeFactor
        ///
        /// distanceFactor uses a logarithmic curve so that longer routes pay
        /// proportionally more but with diminishing returns (prevents trivial
        /// circumnavigation exploits).
        ///
        /// timeFactor degrades linearly from 1.0 down to minimumPaymentFraction
        /// as transitDays approaches maxTransitDays, then clamps.
        /// </summary>
        /// <param name="amount">Tonnes of cargo delivered.</param>
        /// <param name="distanceInTiles">Manhattan distance (tiles) between origin and destination.</param>
        /// <param name="transitDays">Number of in-game days the cargo spent in transit.</param>
        /// <returns>Payment in currency units (rounded down to integer).</returns>
        public int CalculatePayment(int amount, float distanceInTiles, int transitDays)
        {
            if (amount <= 0 || distanceInTiles <= 0f) return 0;

            float distanceFactor = CalculateDistanceFactor(distanceInTiles);
            float timeFactor     = CalculateTimeFactor(transitDays);

            float rawPayment = basePaymentRate * amount * distanceFactor * timeFactor;
            return Mathf.FloorToInt(rawPayment);
        }

        /// <summary>
        /// Logarithmic distance factor.
        /// At 20 tiles  → ~1.00× (reference distance).
        /// At 80 tiles  → ~1.47× (longer route bonus).
        /// At 200 tiles → ~1.84× (very long route).
        ///
        /// Formula: factor = log(distance + 1) / log(referenceDistance + 1)
        /// </summary>
        private static float CalculateDistanceFactor(float distanceInTiles)
        {
            const float referenceDistance = 20f;
            float factor = Mathf.Log(distanceInTiles + 1f) / Mathf.Log(referenceDistance + 1f);
            return Mathf.Max(factor, 0.1f); // never below 10% for very short hops
        }

        /// <summary>
        /// Linear decay time factor between 1.0 (fresh delivery) and
        /// minimumPaymentFraction (cargo held too long).
        /// </summary>
        private float CalculateTimeFactor(int transitDays)
        {
            if (transitDays <= 0) return 1f;
            if (transitDays >= maxTransitDays) return minimumPaymentFraction;

            float t = (float)transitDays / maxTransitDays;
            return Mathf.Lerp(1f, minimumPaymentFraction, t);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only helper: previews the expected payment for validation.
        /// Printed to console when the asset is right-clicked → "Preview Payment".
        /// </summary>
        [ContextMenu("Preview Payment (100 tonnes, 40 tiles, 5 days)")]
        private void PreviewPayment()
        {
            int payment = CalculatePayment(100, 40f, 5);
            Debug.Log($"[CargoDefinition] '{displayName}': 100 tonnes, 40 tiles, 5 days → ${payment:N0}");
        }
#endif
    }
}
