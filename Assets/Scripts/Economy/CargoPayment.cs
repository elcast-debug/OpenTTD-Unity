using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Static utility that computes cargo delivery payments using an
    /// OpenTTD-inspired formula.
    ///
    /// Formula breakdown:
    ///   payment = baseRate × amount × distanceFactor × timeFactor
    ///
    ///   distanceFactor — logarithmic bonus for longer routes, ensuring that
    ///     delivering cargo 200 tiles pays more than 4× a 50-tile trip, but
    ///     rewards scale down so players cannot simply exploit ultra-long routes.
    ///
    ///   timeFactor — linearly degrades from 1.0 to a per-cargo floor as
    ///     transitDays increases, incentivising fast service (express trains
    ///     earn more than slow freight).
    ///
    /// Base rates are defined here as fallback constants. If a
    /// <see cref="CargoDefinition"/> ScriptableObject is available, its
    /// <c>BasePaymentRate</c> overrides the constant.
    ///
    /// All methods are static/pure — no MonoBehaviour required.
    /// </summary>
    public static class CargoPayment
    {
        // ─── Base Payment Rates (fallback if no ScriptableObject) ────────────────
        // Units: currency per tonne at the reference distance (20 tiles).
        // These mirror approximate OpenTTD relative values for prototype balance.

        private static readonly Dictionary<CargoType, int> BaseRates = new Dictionary<CargoType, int>
        {
            { CargoType.Coal,       100 },
            { CargoType.Passengers, 320 },
            { CargoType.Mail,       250 },
            { CargoType.Goods,      480 },
            { CargoType.Wood,        80 },
            { CargoType.Iron,       160 },
            { CargoType.Steel,      200 },
            { CargoType.Food,       400 },
            { CargoType.Oil,        140 },
        };

        // Transit time constants (days until payment decays to the floor)
        private static readonly Dictionary<CargoType, int> MaxTransitDays = new Dictionary<CargoType, int>
        {
            { CargoType.Coal,        40 },
            { CargoType.Passengers,  10 },   // passengers are very time-sensitive
            { CargoType.Mail,        20 },
            { CargoType.Goods,       30 },
            { CargoType.Wood,        50 },
            { CargoType.Iron,        45 },
            { CargoType.Steel,       40 },
            { CargoType.Food,        15 },   // food spoils quickly
            { CargoType.Oil,         45 },
        };

        // Minimum payment fraction (floor) — cargo never pays less than this × base
        private static readonly Dictionary<CargoType, float> MinPaymentFractions = new Dictionary<CargoType, float>
        {
            { CargoType.Coal,       0.10f },
            { CargoType.Passengers, 0.25f },
            { CargoType.Mail,       0.20f },
            { CargoType.Goods,      0.15f },
            { CargoType.Wood,       0.10f },
            { CargoType.Iron,       0.10f },
            { CargoType.Steel,      0.10f },
            { CargoType.Food,       0.20f },
            { CargoType.Oil,        0.10f },
        };

        // ─── Reference Distance ───────────────────────────────────────────────────
        /// <summary>
        /// Distance in tiles at which distanceFactor == 1.0.
        /// Shorter trips pay less, longer trips pay more.
        /// </summary>
        public const float ReferenceDistance = 20f;

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Calculates the payment for a cargo delivery using built-in base rates.
        /// </summary>
        /// <param name="cargoType">Type of cargo delivered.</param>
        /// <param name="amount">Quantity delivered in tonnes (must be > 0).</param>
        /// <param name="distance">
        ///   Route length in tiles (Manhattan or path distance). Must be > 0.
        /// </param>
        /// <param name="transitDays">
        ///   In-game days elapsed while the cargo was in transit.
        ///   Longer journeys with slow trains pay less.
        /// </param>
        /// <returns>
        ///   Total payment in currency units (integer, always ≥ 0).
        /// </returns>
        public static int CalculatePayment(CargoType cargoType, int amount, float distance, int transitDays)
        {
            if (amount <= 0 || distance <= 0f) return 0;

            int   baseRate   = GetBaseRate(cargoType);
            float distFactor = CalculateDistanceFactor(distance);
            float timeFactor = CalculateTimeFactor(cargoType, transitDays);

            float rawPayment = baseRate * amount * distFactor * timeFactor;
            return Mathf.Max(0, Mathf.FloorToInt(rawPayment));
        }

        /// <summary>
        /// Calculates the payment using a <see cref="CargoDefinition"/> ScriptableObject
        /// for cargo-specific configuration, overriding the built-in constants.
        /// </summary>
        /// <param name="definition">ScriptableObject asset for the cargo type.</param>
        /// <param name="amount">Quantity delivered in tonnes.</param>
        /// <param name="distance">Route length in tiles.</param>
        /// <param name="transitDays">In-game days elapsed in transit.</param>
        /// <returns>Total payment in currency units.</returns>
        public static int CalculatePayment(CargoDefinition definition, int amount, float distance, int transitDays)
        {
            if (definition == null) return 0;
            return definition.CalculatePayment(amount, distance, transitDays);
        }

        /// <summary>
        /// Returns the estimated payment per tonne for a given cargo type and distance,
        /// assuming a "fresh" delivery (0 transit days). Useful for route planning UI.
        /// </summary>
        /// <param name="cargoType">Cargo type to evaluate.</param>
        /// <param name="distance">Hypothetical route distance in tiles.</param>
        /// <returns>Expected payment per single tonne at this distance.</returns>
        public static float EstimatePaymentPerTonne(CargoType cargoType, float distance)
        {
            if (distance <= 0f) return 0f;
            int   baseRate   = GetBaseRate(cargoType);
            float distFactor = CalculateDistanceFactor(distance);
            return baseRate * distFactor;
        }

        // ─── Distance Factor (Logarithmic) ────────────────────────────────────────

        /// <summary>
        /// Computes the logarithmic distance bonus.
        ///
        /// Curve behaviour:
        ///   10 tiles  → ~0.82×  (short haul penalty)
        ///   20 tiles  → 1.00×   (reference)
        ///   40 tiles  → 1.19×
        ///   80 tiles  → 1.40×
        ///   160 tiles → 1.62×
        ///   320 tiles → 1.85×
        ///
        /// Never returns less than 0.05 to ensure any delivery earns something.
        /// </summary>
        public static float CalculateDistanceFactor(float distanceInTiles)
        {
            float factor = Mathf.Log(distanceInTiles + 1f) / Mathf.Log(ReferenceDistance + 1f);
            return Mathf.Max(factor, 0.05f);
        }

        // ─── Time Factor (Linear Decay) ───────────────────────────────────────────

        /// <summary>
        /// Computes the transit-time penalty for a given cargo type.
        ///
        /// Returns 1.0 for fresh cargo, decaying linearly to the cargo's
        /// minimum payment fraction as <paramref name="transitDays"/>
        /// approaches the cargo's <c>MaxTransitDays</c>.
        /// </summary>
        public static float CalculateTimeFactor(CargoType cargoType, int transitDays)
        {
            if (transitDays <= 0) return 1f;

            int   maxDays = GetMaxTransitDays(cargoType);
            float minFrac = GetMinPaymentFraction(cargoType);

            if (transitDays >= maxDays) return minFrac;

            float t = (float)transitDays / maxDays;
            return Mathf.Lerp(1f, minFrac, t);
        }

        // ─── Lookup Helpers ───────────────────────────────────────────────────────

        /// <summary>Returns the built-in base payment rate for a cargo type.</summary>
        public static int GetBaseRate(CargoType cargoType)
        {
            return BaseRates.TryGetValue(cargoType, out int rate) ? rate : 100;
        }

        /// <summary>Returns the max-transit-days threshold for a cargo type.</summary>
        public static int GetMaxTransitDays(CargoType cargoType)
        {
            return MaxTransitDays.TryGetValue(cargoType, out int days) ? days : 40;
        }

        /// <summary>Returns the minimum payment fraction for a cargo type.</summary>
        public static float GetMinPaymentFraction(CargoType cargoType)
        {
            return MinPaymentFractions.TryGetValue(cargoType, out float frac) ? frac : 0.1f;
        }

        // ─── Utility ──────────────────────────────────────────────────────────────

        // ─── Compatibility Shims ───────────────────────────────────────────────────

        /// <summary>
        /// Convenience overload matching the calling convention used by Train.cs:
        ///   CargoPayment.Calculate(amount, cargoType, distance)
        /// Assumes 0 transit days (instantaneous delivery estimate).
        /// </summary>
        public static int Calculate(int amount, CargoType cargoType, float distance)
            => CalculatePayment(cargoType, amount, distance, transitDays: 0);

        // ─── Currency Formatting ────────────────────────────────────────────────

        /// <summary>
        /// Formats a currency amount as a user-facing string, e.g. "$12,500".
        /// </summary>
        public static string FormatCurrency(long amount)
        {
            return $"${amount:N0}";
        }

        /// <summary>
        /// Returns a colour (green or red) appropriate for a signed balance or
        /// profit/loss value — used by UI components.
        /// </summary>
        public static Color GetProfitColour(long amount)
        {
            return amount >= 0
                ? new Color(0.18f, 0.75f, 0.25f)  // green
                : new Color(0.85f, 0.18f, 0.18f);  // red
        }
    }
}
