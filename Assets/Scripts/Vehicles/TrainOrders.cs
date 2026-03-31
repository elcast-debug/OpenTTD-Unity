using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    // ── Order type enum ─────────────────────────────────────────────────────

    /// <summary>Specifies what action the train takes at a station.</summary>
    public enum OrderType
    {
        /// <summary>Travel to the station but do not necessarily wait for a full load.</summary>
        GoTo,

        /// <summary>Wait at the station until cargo is fully loaded.</summary>
        FullLoad,

        /// <summary>Unload all cargo at the station regardless of accepted types.</summary>
        Unload,
    }

    // ── Order struct ────────────────────────────────────────────────────────

    /// <summary>
    /// A single order in a train's order list.
    /// Pairs a target <see cref="Station"/> with an <see cref="OrderType"/> action.
    /// </summary>
    [Serializable]
    public struct Order
    {
        /// <summary>The target station for this order.</summary>
        [SerializeField] public Station TargetStation;

        /// <summary>The action to perform upon arrival.</summary>
        [SerializeField] public OrderType Type;

        /// <summary>Creates a new order.</summary>
        public Order(Station station, OrderType type)
        {
            TargetStation = station;
            Type          = type;
        }

        /// <inheritdoc/>
        public override string ToString() =>
            $"Order({Type} → {(TargetStation != null ? TargetStation.StationName : "null")})";
    }

    // ── TrainOrders class ───────────────────────────────────────────────────

    /// <summary>
    /// Manages the ordered list of <see cref="Order"/>s assigned to a train.
    /// Cycles through orders automatically and validates that all referenced
    /// stations are still active.
    /// </summary>
    [Serializable]
    public class TrainOrders
    {
        // ── Fields ──────────────────────────────────────────────────────────

        [SerializeField] private List<Order> orders = new List<Order>();
        [SerializeField] private int currentIndex   = 0;

        // ── Properties ──────────────────────────────────────────────────────

        /// <summary>Number of orders in the list.</summary>
        public int Count => orders.Count;

        /// <summary>Zero-based index of the currently active order.</summary>
        public int CurrentIndex => currentIndex;

        /// <summary>Returns a read-only view of all orders.</summary>
        public IReadOnlyList<Order> Orders => orders;

        // ── Mutation API ─────────────────────────────────────────────────────

        /// <summary>
        /// Appends a new order at the end of the list.
        /// </summary>
        /// <param name="station">Target station (must not be null).</param>
        /// <param name="type">Action to perform at the station.</param>
        public void AddOrder(Station station, OrderType type = OrderType.GoTo)
        {
            if (station == null)
            {
                Debug.LogWarning("[TrainOrders] Cannot add an order with a null station.");
                return;
            }
            orders.Add(new Order(station, type));
        }

        /// <summary>
        /// Removes the order at the given zero-based <paramref name="index"/>.
        /// Adjusts <see cref="CurrentIndex"/> to remain valid after removal.
        /// </summary>
        /// <param name="index">Index of the order to remove.</param>
        public void RemoveOrder(int index)
        {
            if (index < 0 || index >= orders.Count)
            {
                Debug.LogWarning($"[TrainOrders] RemoveOrder: index {index} out of range.");
                return;
            }
            orders.RemoveAt(index);

            // Keep currentIndex in bounds
            if (orders.Count == 0)
            {
                currentIndex = 0;
            }
            else
            {
                if (currentIndex >= orders.Count)
                    currentIndex = 0;
            }
        }

        /// <summary>
        /// Moves an order from <paramref name="fromIndex"/> to <paramref name="toIndex"/>.
        /// </summary>
        /// <param name="fromIndex">Source position.</param>
        /// <param name="toIndex">Destination position.</param>
        public void ReorderOrder(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= orders.Count ||
                toIndex   < 0 || toIndex   >= orders.Count)
            {
                Debug.LogWarning($"[TrainOrders] ReorderOrder: indices ({fromIndex}→{toIndex}) out of range.");
                return;
            }

            Order order = orders[fromIndex];
            orders.RemoveAt(fromIndex);
            orders.Insert(toIndex, order);

            // Try to keep currentIndex pointing at the same logical order
            if (currentIndex == fromIndex)
                currentIndex = toIndex;
            else if (fromIndex < currentIndex && toIndex >= currentIndex)
                currentIndex--;
            else if (fromIndex > currentIndex && toIndex <= currentIndex)
                currentIndex++;
        }

        // ── Traversal API ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the current order without advancing the index.
        /// Returns <c>null</c> (default Order with null station) if the list is empty.
        /// </summary>
        public Order? PeekCurrentOrder()
        {
            if (orders.Count == 0) return null;
            return orders[currentIndex];
        }

        /// <summary>
        /// Returns the current order and advances the index to the next one
        /// (wrapping around cyclically).
        /// </summary>
        /// <returns>The current <see cref="Order"/>, or null if the list is empty.</returns>
        public Order? GetNextOrder()
        {
            if (orders.Count == 0) return null;

            Order current = orders[currentIndex];
            currentIndex = (currentIndex + 1) % orders.Count;
            return current;
        }

        /// <summary>
        /// Resets the order index back to the first order.
        /// </summary>
        public void Reset() => currentIndex = 0;

        // ── Validation ───────────────────────────────────────────────────

        /// <summary>
        /// Validates all orders: removes any whose <see cref="Order.TargetStation"/>
        /// is null or has been destroyed.  Returns true if the list remained unchanged.
        /// </summary>
        /// <returns>True if all orders were valid; false if any were removed.</returns>
        public bool Validate()
        {
            bool allValid = true;
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                if (orders[i].TargetStation == null)
                {
                    Debug.LogWarning($"[TrainOrders] Removing invalid order at index {i} (station destroyed).");
                    RemoveOrder(i);
                    allValid = false;
                }
            }
            return allValid;
        }
    }
}
