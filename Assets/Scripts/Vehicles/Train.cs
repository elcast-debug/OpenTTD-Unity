using System;
using System.Collections;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>State machine states for a train entity.</summary>
    public enum TrainState
    {
        /// <summary>Train is stationary with no orders to execute.</summary>
        Idle,

        /// <summary>Train is moving along a rail path.</summary>
        Moving,

        /// <summary>Train is stopped at a station loading cargo.</summary>
        Loading,

        /// <summary>Train is stopped at a station unloading cargo.</summary>
        Unloading,
    }

    /// <summary>
    /// Core train entity.  Stores train statistics, current cargo, operating state,
    /// and the ordered station list.  Delegates pathfinding to
    /// <see cref="TrainMovement"/> and manages loading/unloading cycles.
    /// </summary>
    [RequireComponent(typeof(TrainMovement))]
    public class Train : MonoBehaviour
    {
        // ── Inspector stats ─────────────────────────────────────────────────

        /// <summary>Display name shown in the UI.</summary>
        [SerializeField] private string trainName = "Train";

        /// <summary>Maximum travel speed in tiles per second.</summary>
        [SerializeField] private float maxSpeed = 3f;

        /// <summary>Maximum cargo units this train can carry.</summary>
        [SerializeField] private int cargoCapacity = 40;

        /// <summary>Type of cargo this train is configured to transport.</summary>
        [SerializeField] private CargoType cargoType = CargoType.Coal;

        /// <summary>Running cost per in-game day (deducted by EconomyManager).</summary>
        [SerializeField] private int runningCostPerDay = 50;

        /// <summary>Seconds spent at a station per loading cycle.</summary>
        [SerializeField] private float loadingTimeSec = 3f;

        /// <summary>Seconds spent at a station per unloading cycle.</summary>
        [SerializeField] private float unloadingTimeSec = 2f;

        // ── Runtime state ───────────────────────────────────────────────────

        private int         currentCargo;
        private TrainState  state         = TrainState.Idle;
        private TrainOrders trainOrders   = new TrainOrders();
        private TrainMovement movement;

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>Fired when the train arrives at a station. Parameter is the station.</summary>
        public event Action<Station> OnArrivedAtStation;

        /// <summary>Fired when the cargo amount changes. Parameters: new amount, capacity.</summary>
        public event Action<int, int> OnCargoChanged;

        // ── Properties ──────────────────────────────────────────────────────

        /// <summary>Display name of this train.</summary>
        public string TrainName => trainName;

        /// <summary>Maximum speed in tiles per second.</summary>
        public float MaxSpeed => maxSpeed;

        /// <summary>Maximum cargo capacity in units.</summary>
        public int CargoCapacity => cargoCapacity;

        /// <summary>Cargo type this train hauls.</summary>
        public CargoType CargoType => cargoType;

        /// <summary>Current amount of cargo on board.</summary>
        public int CurrentCargo => currentCargo;

        /// <summary>Fraction of cargo capacity currently loaded (0–1).</summary>
        public float CargoFraction => cargoCapacity > 0 ? (float)currentCargo / cargoCapacity : 0f;

        /// <summary>Current operating state.</summary>
        public TrainState State => state;

        /// <summary>The train's order list.</summary>
        public TrainOrders Orders => trainOrders;

        /// <summary>Running cost per game-day.</summary>
        public int RunningCostPerDay => runningCostPerDay;

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            movement = GetComponent<TrainMovement>();
        }

        private void Start()
        {
            movement.OnDestinationReached += HandleDestinationReached;
            movement.MaxSpeed = maxSpeed;

            if (trainOrders.Count > 0)
                ExecuteNextOrder();
        }

        private void OnDestroy()
        {
            if (movement != null)
                movement.OnDestinationReached -= HandleDestinationReached;
        }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads cargo from the given station onto this train.
        /// Takes as much of <see cref="CargoType"/> as possible up to capacity.
        /// Calls <see cref="Station.TakeCargo"/> to deduct from the station's waiting cargo.
        /// </summary>
        /// <param name="station">Station to load from.</param>
        public void LoadCargo(Station station)
        {
            if (station == null)
            {
                Debug.LogWarning("[Train] LoadCargo called with null station.");
                return;
            }
            if (state != TrainState.Loading)
            {
                Debug.LogWarning($"[Train] LoadCargo called while in state {state}.");
                return;
            }

            int spaceAvailable = cargoCapacity - currentCargo;
            int loaded         = station.TakeCargo(cargoType, spaceAvailable);
            if (loaded > 0)
            {
                currentCargo += loaded;
                OnCargoChanged?.Invoke(currentCargo, cargoCapacity);
            }
        }

        /// <summary>
        /// Unloads all cargo of <see cref="CargoType"/> at the given station.
        /// Triggers payment via <see cref="EconomyManager"/> based on distance travelled.
        /// </summary>
        /// <param name="station">Station to deliver cargo to.</param>
        public void UnloadCargo(Station station)
        {
            if (station == null)
            {
                Debug.LogWarning("[Train] UnloadCargo called with null station.");
                return;
            }
            if (state != TrainState.Unloading)
            {
                Debug.LogWarning($"[Train] UnloadCargo called while in state {state}.");
                return;
            }
            if (currentCargo <= 0) return;

            // Only unload cargo types the station accepts
            if (!station.AcceptsCargo(cargoType))
            {
                Debug.Log($"[Train] Station '{station.StationName}' does not accept {cargoType}.");
                return;
            }

            int delivered = currentCargo;
            station.DeliverCargo(cargoType, delivered);

            // Pay out income
            if (EconomyManager.Instance != null)
            {
                int income = CargoPayment.Calculate(delivered, cargoType,
                                                    movement.TotalDistanceTravelled);
                EconomyManager.Instance.Earn(income);
            }

            currentCargo = 0;
            OnCargoChanged?.Invoke(currentCargo, cargoCapacity);
        }

        /// <summary>
        /// Immediately sets the train's order list and starts executing orders
        /// from the beginning.
        /// </summary>
        public void SetOrders(TrainOrders orders)
        {
            trainOrders = orders ?? new TrainOrders();
            trainOrders.Reset();
            if (trainOrders.Count > 0)
                ExecuteNextOrder();
        }

        // ── Order execution ─────────────────────────────────────────────────

        private void ExecuteNextOrder()
        {
            if (!trainOrders.Validate() && trainOrders.Count == 0)
            {
                SetState(TrainState.Idle);
                return;
            }

            Order? next = trainOrders.GetNextOrder();
            if (next == null || next.Value.TargetStation == null)
            {
                SetState(TrainState.Idle);
                return;
            }

            var order   = next.Value;
            var station = order.TargetStation;

            SetState(TrainState.Moving);
            movement.MoveTo(station.GridPosition);
        }

        private void HandleDestinationReached(Vector2Int arrivedAt)
        {
            // Find which station we arrived at
            var station = FindStationAt(arrivedAt);
            if (station == null)
            {
                // Not a station tile — continue to next order
                ExecuteNextOrder();
                return;
            }

            OnArrivedAtStation?.Invoke(station);

            // Determine action from the last-consumed order
            var currentOrder = trainOrders.PeekCurrentOrder();
            if (currentOrder == null)
            {
                ExecuteNextOrder();
                return;
            }

            var orderType = currentOrder.Value.Type;
            StartCoroutine(ProcessStationStop(station, orderType));
        }

        private IEnumerator ProcessStationStop(Station station, OrderType orderType)
        {
            switch (orderType)
            {
                case OrderType.Unload:
                    SetState(TrainState.Unloading);
                    yield return new WaitForSeconds(unloadingTimeSec);
                    UnloadCargo(station);
                    break;

                case OrderType.FullLoad:
                    // Wait until full or timeout
                    SetState(TrainState.Loading);
                    float timeout = 30f;
                    float elapsed = 0f;
                    while (currentCargo < cargoCapacity && elapsed < timeout)
                    {
                        yield return new WaitForSeconds(loadingTimeSec);
                        LoadCargo(station);
                        elapsed += loadingTimeSec;
                    }
                    break;

                case OrderType.GoTo:
                default:
                    // Brief pause then continue
                    SetState(TrainState.Loading);
                    yield return new WaitForSeconds(loadingTimeSec);
                    LoadCargo(station);
                    SetState(TrainState.Unloading);
                    yield return new WaitForSeconds(unloadingTimeSec);
                    UnloadCargo(station);
                    break;
            }

            station.UpdateRating();
            ExecuteNextOrder();
        }

        private void SetState(TrainState newState)
        {
            if (state == newState) return;
            state = newState;
        }

        private static Station FindStationAt(Vector2Int gridPos)
        {
            // Query GridManager for a station component at this tile
            if (GridManager.Instance != null)
            {
                var tile = GridManager.Instance.GetTile(gridPos.x, gridPos.y);
                return tile?.Station;
            }
            // Fallback: physics overlap at world position
            // (useful in scenes without GridManager)
            return null;
        }
    }
}
