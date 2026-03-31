using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTTDUnity
{
    /// <summary>
    /// Renders the top HUD bar showing company name, current date,
    /// player balance, and game speed controls.
    ///
    /// Canvas Hierarchy Setup (Unity Editor):
    /// ─────────────────────────────────────────────────────────────────────
    ///  Canvas (Screen Space – Overlay)
    ///  └─ TopBar  [RectTransform: Stretch X, Top anchor, height 36px]
    ///     ├─ Background  [Image, dark semi-transparent colour #1A1A1ACC]
    ///     ├─ CompanyLabel  [TextMeshProUGUI, left side]
    ///     ├─ DateLabel     [TextMeshProUGUI, centre-left]
    ///     ├─ MoneyLabel    [TextMeshProUGUI, centre-right, bold]
    ///     └─ SpeedPanel    [HorizontalLayoutGroup, right side]
    ///        ├─ BtnPause   [Button + TextMeshProUGUI "⏸"]
    ///        ├─ BtnSpeed1  [Button + TextMeshProUGUI "1×"]
    ///        ├─ BtnSpeed2  [Button + TextMeshProUGUI "2×"]
    ///        └─ BtnSpeed4  [Button + TextMeshProUGUI "4×"]
    /// ─────────────────────────────────────────────────────────────────────
    ///
    /// Anchors:
    ///   TopBar rect: anchorMin=(0,1) anchorMax=(1,1) pivot=(0.5,1)
    ///   offsetMin.y = -36, offsetMax.y = 0  →  36px strip at top of screen
    /// </summary>
    public class TopBar : MonoBehaviour
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("Text References")]
        [Tooltip("Displays the player's company name (top-left).")]
        [SerializeField] private TextMeshProUGUI companyLabel;

        [Tooltip("Displays the current in-game date, e.g. '15 Mar 1950'.")]
        [SerializeField] private TextMeshProUGUI dateLabel;

        [Tooltip("Displays the player's current balance, e.g. '$100,000'.")]
        [SerializeField] private TextMeshProUGUI moneyLabel;

        [Header("Speed Buttons")]
        [Tooltip("Pause button — sets Time.timeScale to 0.")]
        [SerializeField] private Button pauseButton;

        [Tooltip("1× speed button — sets Time.timeScale to 1.")]
        [SerializeField] private Button speed1Button;

        [Tooltip("2× speed button — sets Time.timeScale to 2.")]
        [SerializeField] private Button speed2Button;

        [Tooltip("4× speed button — sets Time.timeScale to 4.")]
        [SerializeField] private Button speed4Button;

        [Header("Style")]
        [Tooltip("Company name shown in the top bar.")]
        [SerializeField] private string companyName = "Acme Railways";

        [Tooltip("Colour for positive/zero balance.")]
        [SerializeField] private Color positiveMoneyColour = new Color(0.18f, 0.85f, 0.28f);

        [Tooltip("Colour for negative balance.")]
        [SerializeField] private Color negativeMoneyColour = new Color(0.95f, 0.22f, 0.18f);

        [Tooltip("Colour applied to the currently active speed button to show selection.")]
        [SerializeField] private Color activeSpeedButtonColour = new Color(0.25f, 0.6f, 1.0f);

        [Tooltip("Colour for inactive speed buttons.")]
        [SerializeField] private Color inactiveSpeedButtonColour = new Color(0.4f, 0.4f, 0.4f);

        // ─── State ────────────────────────────────────────────────────────────────

        private float _currentTimeScale = 1f;
        private bool  _isPaused         = false;

        // Month name lookup
        private static readonly string[] MonthNames =
            { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            ValidateReferences();
        }

        private void Start()
        {
            // Wire up speed buttons
            pauseButton?.onClick.AddListener(OnPauseClicked);
            speed1Button?.onClick.AddListener(() => SetSpeed(1f));
            speed2Button?.onClick.AddListener(() => SetSpeed(2f));
            speed4Button?.onClick.AddListener(() => SetSpeed(4f));

            // Subscribe to economy events
            EconomyManager.OnMoneyChanged += HandleMoneyChanged;

            // Initialise display
            if (companyLabel != null) companyLabel.text = companyName;
            RefreshAll();
        }

        private void Update()
        {
            // Update date every frame (cheap string build)
            RefreshDate();
        }

        private void OnDestroy()
        {
            EconomyManager.OnMoneyChanged -= HandleMoneyChanged;

            pauseButton?.onClick.RemoveAllListeners();
            speed1Button?.onClick.RemoveAllListeners();
            speed2Button?.onClick.RemoveAllListeners();
            speed4Button?.onClick.RemoveAllListeners();
        }

        // ─── Display Refresh ──────────────────────────────────────────────────────

        private void RefreshAll()
        {
            RefreshDate();
            RefreshMoney(EconomyManager.Instance != null ? EconomyManager.Instance.CurrentMoney : 100_000);
            RefreshSpeedButtons();
        }

        private void RefreshDate()
        {
            if (dateLabel == null || EconomyManager.Instance == null) return;

            int day   = EconomyManager.Instance.CurrentDay;
            int month = EconomyManager.Instance.CurrentMonth - 1; // 0-based for array index
            int year  = EconomyManager.Instance.CurrentYear;

            string monthStr = (month >= 0 && month < 12) ? MonthNames[month] : "???";
            dateLabel.text = $"{day:D2} {monthStr} {year}";
        }

        private void RefreshMoney(long amount)
        {
            if (moneyLabel == null) return;

            moneyLabel.text  = CargoPayment.FormatCurrency(amount);
            moneyLabel.color = amount >= 0 ? positiveMoneyColour : negativeMoneyColour;
        }

        private void RefreshSpeedButtons()
        {
            SetButtonColour(pauseButton,  _isPaused              ? activeSpeedButtonColour : inactiveSpeedButtonColour);
            SetButtonColour(speed1Button, !_isPaused && Mathf.Approximately(_currentTimeScale, 1f) ? activeSpeedButtonColour : inactiveSpeedButtonColour);
            SetButtonColour(speed2Button, !_isPaused && Mathf.Approximately(_currentTimeScale, 2f) ? activeSpeedButtonColour : inactiveSpeedButtonColour);
            SetButtonColour(speed4Button, !_isPaused && Mathf.Approximately(_currentTimeScale, 4f) ? activeSpeedButtonColour : inactiveSpeedButtonColour);
        }

        // ─── Event Handlers ───────────────────────────────────────────────────────

        private void HandleMoneyChanged(long newAmount) => RefreshMoney(newAmount);

        // ─── Speed Controls ───────────────────────────────────────────────────────

        private void OnPauseClicked()
        {
            _isPaused = !_isPaused;
            Time.timeScale = _isPaused ? 0f : _currentTimeScale;
            RefreshSpeedButtons();
        }

        private void SetSpeed(float scale)
        {
            _currentTimeScale = scale;
            _isPaused         = false;
            Time.timeScale    = scale;
            RefreshSpeedButtons();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static void SetButtonColour(Button btn, Color colour)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = colour;
        }

        private void ValidateReferences()
        {
            if (companyLabel == null) Debug.LogWarning("[TopBar] CompanyLabel not assigned.");
            if (dateLabel    == null) Debug.LogWarning("[TopBar] DateLabel not assigned.");
            if (moneyLabel   == null) Debug.LogWarning("[TopBar] MoneyLabel not assigned.");
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Sets the company name displayed in the top bar.
        /// </summary>
        public void SetCompanyName(string name)
        {
            companyName = name;
            if (companyLabel != null) companyLabel.text = name;
        }

        /// <summary>Returns the current game time scale (0 = paused).</summary>
        public float GetCurrentTimeScale() => _isPaused ? 0f : _currentTimeScale;
    }
}
