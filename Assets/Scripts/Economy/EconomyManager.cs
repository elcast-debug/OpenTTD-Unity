using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Tracks player money, processes transactions, records monthly income/expense
    /// history, and fires events consumed by the UI and other game systems.
    ///
    /// Setup: Attach to a persistent GameObject in MainScene (e.g., "Managers").
    ///        This script ensures it survives scene loads via DontDestroyOnLoad.
    ///
    /// Singleton access: EconomyManager.Instance
    /// </summary>
    public class EconomyManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────────
        public static EconomyManager Instance { get; private set; }

        // ─── Inspector Fields ─────────────────────────────────────────────────────
        [Header("Starting Conditions")]
        [Tooltip("Amount of money the player starts with.")]
        [SerializeField] private long startingMoney = 100_000;

        [Header("Running Costs")]
        [Tooltip("How often (real seconds) the monthly cost tick fires. " +
                 "At normal speed 1 game month ≈ 2.5 seconds.")]
        [SerializeField] private float monthDurationSeconds = 2.5f;

        [Tooltip("Fixed overhead cost applied every game month (e.g., company upkeep).")]
        [SerializeField] private long monthlyOverheadCost = 0;

        [Header("History")]
        [Tooltip("Maximum number of transactions kept in the rolling history list.")]
        [SerializeField] [Range(10, 200)] private int maxTransactionHistory = 50;

        [Tooltip("Number of months to retain in the income/expense graph history.")]
        [SerializeField] [Range(6, 120)] private int maxMonthHistory = 24;

        // ─── State ────────────────────────────────────────────────────────────────
        private long _currentMoney;
        private float _monthTimer;
        private int _currentMonth; // 0-11
        private int _currentYear;  // e.g. 1950
        private int _currentDay;   // 1-30

        // Rolling transaction log (last N entries, newest at index 0)
        private readonly LinkedList<Transaction> _transactionHistory = new LinkedList<Transaction>();

        // Per-month totals: index 0 = oldest month in window
        private readonly List<MonthlyRecord> _monthlyHistory = new List<MonthlyRecord>();
        private MonthlyRecord _currentMonthRecord;

        // ─── Events ───────────────────────────────────────────────────────────────

        /// <summary>Fired whenever the player's balance changes. Passes the new balance.</summary>
        public static event Action<long> OnMoneyChanged;

        /// <summary>Fired after every completed transaction (income or expense).</summary>
        public static event Action<Transaction> OnTransaction;

        /// <summary>Fired at the end of each in-game month with that month's summary.</summary>
        public static event Action<MonthlyRecord> OnMonthEnd;

        // ─── Public Properties ────────────────────────────────────────────────────

        /// <summary>Current player balance in currency units.</summary>
        public long CurrentMoney => _currentMoney;

        /// <summary>Current in-game year.</summary>
        public int CurrentYear => _currentYear;

        /// <summary>Current in-game month (1–12).</summary>
        public int CurrentMonth => _currentMonth + 1;

        /// <summary>Current in-game day (1–30).</summary>
        public int CurrentDay => _currentDay;

        /// <summary>Read-only view of the transaction history (newest first).</summary>
        public IReadOnlyCollection<Transaction> TransactionHistory => _transactionHistory;

        /// <summary>Read-only view of completed monthly records.</summary>
        public IReadOnlyList<MonthlyRecord> MonthlyHistory => _monthlyHistory;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[EconomyManager] Duplicate instance destroyed.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Initialise();
        }

        private void Update()
        {
            TickMonth();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─── Initialisation ───────────────────────────────────────────────────────

        private void Initialise()
        {
            _currentMoney       = startingMoney;
            _monthTimer         = 0f;
            _currentYear        = 1950;
            _currentMonth       = 0; // January
            _currentDay         = 1;
            _currentMonthRecord = new MonthlyRecord(_currentYear, _currentMonth);
        }

        // ─── Month Tick ───────────────────────────────────────────────────────────

        private void TickMonth()
        {
            _monthTimer += Time.deltaTime;

            // Advance day counter (30 days per month evenly distributed)
            _currentDay = Mathf.Clamp(1 + Mathf.FloorToInt((_monthTimer / monthDurationSeconds) * 30), 1, 30);

            if (_monthTimer < monthDurationSeconds) return;

            _monthTimer -= monthDurationSeconds;
            EndMonth();
        }

        private void EndMonth()
        {
            // Apply fixed overhead
            if (monthlyOverheadCost > 0)
            {
                SpendMoney(monthlyOverheadCost, "Monthly overhead");
            }

            // Archive completed month record
            _monthlyHistory.Add(_currentMonthRecord);
            if (_monthlyHistory.Count > maxMonthHistory)
                _monthlyHistory.RemoveAt(0);

            OnMonthEnd?.Invoke(_currentMonthRecord);

            // Advance calendar
            _currentMonth++;
            if (_currentMonth >= 12)
            {
                _currentMonth = 0;
                _currentYear++;
            }
            _currentDay = 1;

            _currentMonthRecord = new MonthlyRecord(_currentYear, _currentMonth);
        }

        // ─── Money Operations ─────────────────────────────────────────────────────

        /// <summary>
        /// Adds money to the player's balance (income).
        /// </summary>
        /// <param name="amount">Amount to add (must be positive).</param>
        /// <param name="description">Short human-readable reason shown in UI.</param>
        /// <returns>The new balance after the transaction.</returns>
        public long AddMoney(long amount, string description)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[EconomyManager] AddMoney called with non-positive amount: {amount}");
                return _currentMoney;
            }

            _currentMoney += amount;
            _currentMonthRecord.AddIncome(amount);

            var tx = new Transaction(amount, description, _currentYear, _currentMonth, _currentDay, TransactionType.Income);
            RecordTransaction(tx);

            OnMoneyChanged?.Invoke(_currentMoney);
            OnTransaction?.Invoke(tx);

            return _currentMoney;
        }

        /// <summary>
        /// Deducts money from the player's balance (expense).
        /// </summary>
        /// <param name="amount">Amount to deduct (must be positive).</param>
        /// <param name="description">Short human-readable reason shown in UI.</param>
        /// <returns>
        /// <c>true</c> if the player could afford it and the money was deducted;
        /// <c>false</c> if the balance was insufficient (no change applied).
        /// </returns>
        public bool SpendMoney(long amount, string description)
        {
            if (amount <= 0)
            {
                Debug.LogWarning($"[EconomyManager] SpendMoney called with non-positive amount: {amount}");
                return false;
            }

            if (!CanAfford(amount))
            {
                Debug.Log($"[EconomyManager] Cannot afford {description} (${amount:N0}). Balance: ${_currentMoney:N0}");
                return false;
            }

            _currentMoney -= amount;
            _currentMonthRecord.AddExpense(amount);

            var tx = new Transaction(-amount, description, _currentYear, _currentMonth, _currentDay, TransactionType.Expense);
            RecordTransaction(tx);

            OnMoneyChanged?.Invoke(_currentMoney);
            OnTransaction?.Invoke(tx);

            return true;
        }

        /// <summary>
        /// Returns <c>true</c> if the player currently has at least
        /// <paramref name="amount"/> currency units available.
        /// </summary>
        public bool CanAfford(long amount) => _currentMoney >= amount;

        /// <summary>
        /// Overload accepting an <c>int</c> for convenience with smaller cost values.
        /// </summary>
        public bool CanAfford(int amount) => CanAfford((long)amount);

        /// <summary>
        /// Adds money without charging — used internally by derived/editor calls.
        /// Prefer <see cref="AddMoney"/> for normal income.
        /// </summary>
        public void ForceSetMoney(long amount)
        {
            _currentMoney = amount;
            OnMoneyChanged?.Invoke(_currentMoney);
        }

        // ─── Compatibility Shims ───────────────────────────────────────────────────
        // These methods allow external scripts (e.g., GameManager, Train) written
        // before EconomyManager was finalised to call in without modification.

        /// <summary>
        /// Re-initialises the economy with the given starting balance.
        /// Called by GameManager after scene load.
        /// </summary>
        public void Initialize(long startBalance)
        {
            _currentMoney       = startBalance;
            _transactionHistory.Clear();
            _monthlyHistory.Clear();
            _currentMonthRecord = new MonthlyRecord(_currentYear, _currentMonth);
            OnMoneyChanged?.Invoke(_currentMoney);
        }

        /// <summary>
        /// Alias for <see cref="AddMoney"/> — used by Train.cs for cargo income.
        /// </summary>
        public void Earn(long amount) => AddMoney(amount, "Cargo income");

        /// <summary>
        /// Alias for <see cref="AddMoney"/> — int overload for convenience.
        /// </summary>
        public void Earn(int amount) => AddMoney(amount, "Cargo income");

        /// <summary>
        /// Alias for <see cref="SpendMoney"/> — used by Rail/Station placers.
        /// </summary>
        public bool Spend(long amount, string description) => SpendMoney(amount, description);

        // ─── History ──────────────────────────────────────────────────────────────

        private void RecordTransaction(Transaction tx)
        {
            _transactionHistory.AddFirst(tx);
            while (_transactionHistory.Count > maxTransactionHistory)
                _transactionHistory.RemoveLast();
        }

        /// <summary>
        /// Returns the most recent N transactions (newest first), clamped to history size.
        /// </summary>
        public List<Transaction> GetRecentTransactions(int count)
        {
            var result = new List<Transaction>(count);
            foreach (var tx in _transactionHistory)
            {
                if (result.Count >= count) break;
                result.Add(tx);
            }
            return result;
        }

        /// <summary>
        /// Returns total income earned this in-game month so far.
        /// </summary>
        public long GetCurrentMonthIncome() => _currentMonthRecord.TotalIncome;

        /// <summary>
        /// Returns total expenses this in-game month so far.
        /// </summary>
        public long GetCurrentMonthExpenses() => _currentMonthRecord.TotalExpenses;

        /// <summary>
        /// Returns net profit/loss this in-game month so far.
        /// </summary>
        public long GetCurrentMonthNet() => _currentMonthRecord.NetProfit;

        // ─── Debug ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        [ContextMenu("Debug: Add $10,000")]
        private void DebugAddMoney() => AddMoney(10_000, "Debug cheat");

        [ContextMenu("Debug: Print Balance")]
        private void DebugPrintBalance() =>
            Debug.Log($"[EconomyManager] Balance: ${_currentMoney:N0} | Month: {_currentYear}-{_currentMonth + 1:D2}");
#endif
    }

    // ─── Supporting Structs ────────────────────────────────────────────────────

    /// <summary>Distinguishes income from expenses in the transaction log.</summary>
    public enum TransactionType { Income, Expense }

    /// <summary>
    /// Immutable record of a single monetary transaction.
    /// Stored in the rolling history and broadcast via <see cref="EconomyManager.OnTransaction"/>.
    /// </summary>
    [Serializable]
    public readonly struct Transaction
    {
        /// <summary>
        /// Signed amount: positive = income, negative = expense.
        /// </summary>
        public readonly long Amount;

        /// <summary>Short human-readable description (e.g., "Coal delivery: Central → East Station").</summary>
        public readonly string Description;

        /// <summary>In-game year at the time of the transaction.</summary>
        public readonly int Year;

        /// <summary>In-game month (0–11) at the time of the transaction.</summary>
        public readonly int Month;

        /// <summary>In-game day (1–30) at the time of the transaction.</summary>
        public readonly int Day;

        /// <summary>Whether this was income or an expense.</summary>
        public readonly TransactionType Type;

        public Transaction(long amount, string description, int year, int month, int day, TransactionType type)
        {
            Amount      = amount;
            Description = description;
            Year        = year;
            Month       = month;
            Day         = day;
            Type        = type;
        }

        /// <summary>Formatted date string, e.g. "15 Mar 1950".</summary>
        public string FormattedDate
        {
            get
            {
                string[] months = { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };
                string m = (Month >= 0 && Month < 12) ? months[Month] : "???";
                return $"{Day} {m} {Year}";
            }
        }

        public override string ToString() =>
            $"[{FormattedDate}] {Description}: {(Amount >= 0 ? "+" : "")}${Amount:N0}";
    }

    /// <summary>
    /// Aggregated income and expense totals for a single in-game month.
    /// Stored in <see cref="EconomyManager.MonthlyHistory"/> for graphing.
    /// </summary>
    [Serializable]
    public class MonthlyRecord
    {
        public int Year  { get; private set; }
        public int Month { get; private set; } // 0-11

        public long TotalIncome   { get; private set; }
        public long TotalExpenses { get; private set; }
        public long NetProfit     => TotalIncome - TotalExpenses;

        public MonthlyRecord(int year, int month)
        {
            Year  = year;
            Month = month;
        }

        internal void AddIncome(long amount)  => TotalIncome   += amount;
        internal void AddExpense(long amount) => TotalExpenses += amount;

        /// <summary>Display string, e.g. "Mar 1950".</summary>
        public string DisplayLabel
        {
            get
            {
                string[] months = { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };
                string m = (Month >= 0 && Month < 12) ? months[Month] : "???";
                return $"{m} {Year}";
            }
        }
    }
}
