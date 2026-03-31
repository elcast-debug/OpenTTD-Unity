using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenTTDUnity
{
    /// <summary>
    /// Right-side info panel that adapts its content based on the currently
    /// selected game object (Station, Industry, or Train).
    ///
    /// The panel listens for selection events, detects the component type on
    /// the selected object, and populates the appropriate view.
    ///
    /// Canvas Hierarchy Setup (Unity Editor):
    /// ─────────────────────────────────────────────────────────────────────
    ///  Canvas
    ///  └─ InfoPanel  [RectTransform: Right anchor, width 260px, full height - topbar - toolbar]
    ///     ├─ Background    [Image, #1E2330CC]
    ///     ├─ Header        [HorizontalLayoutGroup]
    ///     │  ├─ TitleLabel [TextMeshProUGUI, bold, expand]
    ///     │  └─ CloseBtn   [Button, "✕"]
    ///     ├─ ScrollView    [ScrollRect + Mask]
    ///     │  └─ Content    [VerticalLayoutGroup, ContentSizeFitter]
    ///     │     ├─ SubtitleLabel  [TextMeshProUGUI]
    ///     │     ├─ Divider        [Image, 1px, grey]
    ///     │     └─ BodyLabel      [TextMeshProUGUI, rich text enabled]
    ///     └─ (optional) CargoBar  [Slider or custom bar for cargo amounts]
    /// ─────────────────────────────────────────────────────────────────────
    ///
    /// Anchors:
    ///   anchorMin=(1,0) anchorMax=(1,1) pivot=(1,0.5)
    ///   offsetMin.x = -260, offsetMax.x = 0
    ///   offsetMin.y = 48  (above toolbar)
    ///   offsetMax.y = -36 (below topbar)
    /// </summary>
    public class InfoPanel : MonoBehaviour
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("Panel Root")]
        [Tooltip("The panel root GameObject to show/hide.")]
        [SerializeField] private GameObject panelRoot;

        [Header("Text Components")]
        [SerializeField] private TextMeshProUGUI titleLabel;
        [SerializeField] private TextMeshProUGUI subtitleLabel;
        [SerializeField] private TextMeshProUGUI bodyLabel;

        [Header("Controls")]
        [SerializeField] private Button closeButton;

        [Header("Update Rate")]
        [Tooltip("How often (seconds) the panel refreshes while open. " +
                 "0 = every frame (expensive). Recommended: 0.5")]
        [SerializeField] [Range(0f, 5f)] private float refreshInterval = 0.5f;

        // ─── State ────────────────────────────────────────────────────────────────

        private GameObject    _selectedObject;
        private SelectionType _selectionType;
        private float         _refreshTimer;

        private enum SelectionType { None, Station, Industry, Train }

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Start()
        {
            closeButton?.onClick.AddListener(Hide);
            Hide();
        }

        private void Update()
        {
            if (_selectedObject == null || _selectionType == SelectionType.None)
                return;

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= refreshInterval || Mathf.Approximately(refreshInterval, 0f))
            {
                _refreshTimer = 0f;
                RefreshContent();
            }
        }

        private void OnDestroy()
        {
            closeButton?.onClick.RemoveAllListeners();
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Examines <paramref name="selected"/> for known component types and shows
        /// the appropriate info view. Automatically refreshes while the panel is open.
        /// </summary>
        public void ShowForObject(GameObject selected)
        {
            if (selected == null) { Hide(); return; }

            _selectedObject = selected;
            _selectionType  = DetectSelectionType(selected);
            _refreshTimer   = refreshInterval; // force immediate first refresh

            if (_selectionType == SelectionType.None)
            {
                Hide();
                return;
            }

            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshContent();
        }

        /// <summary>
        /// Hides the info panel and clears the current selection.
        /// </summary>
        public void Hide()
        {
            _selectedObject = null;
            _selectionType  = SelectionType.None;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ─── Selection Detection ──────────────────────────────────────────────────

        private static SelectionType DetectSelectionType(GameObject go)
        {
            if (go.GetComponent<Station>()  != null) return SelectionType.Station;
            if (go.GetComponent<Industry>() != null) return SelectionType.Industry;
            if (go.GetComponent<Train>()    != null) return SelectionType.Train;
            return SelectionType.None;
        }

        // ─── Content Refresh ──────────────────────────────────────────────────────

        private void RefreshContent()
        {
            if (_selectedObject == null) { Hide(); return; }

            switch (_selectionType)
            {
                case SelectionType.Station:  PopulateStation(_selectedObject.GetComponent<Station>());   break;
                case SelectionType.Industry: PopulateIndustry(_selectedObject.GetComponent<Industry>()); break;
                case SelectionType.Train:    PopulateTrain(_selectedObject.GetComponent<Train>());       break;
            }
        }

        // ─── Station View ─────────────────────────────────────────────────────────

        private void PopulateStation(Station station)
        {
            if (station == null) { Hide(); return; }

            SetTitle("Station", station.StationName);

            // Build cargo waiting string
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>Cargo Waiting:</b>");

            var waiting = station.GetAllWaitingCargo();
            if (waiting == null || waiting.Count == 0)
            {
                sb.AppendLine("  <i>None</i>");
            }
            else
            {
                foreach (var kvp in waiting)
                    sb.AppendLine($"  {kvp.Key}: <b>{kvp.Value}t</b>");
            }

            sb.AppendLine();
            sb.AppendLine($"<b>Rating:</b> {station.Rating}/100");
            sb.AppendLine($"<b>Acceptance radius:</b> {station.AcceptanceRadius} tiles");
            sb.AppendLine();

            // Connected industries
            sb.AppendLine("<b>Nearby Industries:</b>");
            var nearby = IndustryManager.Instance?.GetIndustriesInRadius(station.GridPosition, station.AcceptanceRadius);
            if (nearby == null || nearby.Count == 0)
            {
                sb.AppendLine("  <i>None</i>");
            }
            else
            {
                foreach (var ind in nearby)
                    sb.AppendLine($"  \u2022 {ind.IndustryName}");
            }

            SetBody(sb.ToString());
        }

        // ─── Industry View ────────────────────────────────────────────────────────

        private void PopulateIndustry(Industry industry)
        {
            if (industry == null) { Hide(); return; }

            SetTitle($"{industry.IndustryType}", industry.IndustryName);

            // Use specialised info text if available
            if (industry is CoalMine mine)
            {
                SetBody(mine.GetInfoPanelText());
                return;
            }

            if (industry is PowerStation ps)
            {
                SetBody(ps.GetInfoPanelText());
                return;
            }

            // Generic fallback
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Type:</b> {industry.IndustryType}");
            sb.AppendLine($"<b>Grid Position:</b> {industry.GridPosition}");
            sb.AppendLine();

            if (industry.OutputCargoTypes.Count > 0)
            {
                sb.AppendLine("<b>Produces:</b>");
                foreach (var cargoType in industry.OutputCargoTypes)
                {
                    int stockpile = industry.OutputStockpile.TryGetValue(cargoType, out int v) ? v : 0;
                    sb.AppendLine($"  {cargoType}: <b>{stockpile}t</b> waiting");
                }
            }

            if (industry.InputCargoTypes.Count > 0)
            {
                sb.AppendLine("<b>Accepts:</b>");
                foreach (var cargoType in industry.InputCargoTypes)
                {
                    int stockpile = industry.InputStockpile.TryGetValue(cargoType, out int v) ? v : 0;
                    sb.AppendLine($"  {cargoType}: <b>{stockpile}t</b> in stock");
                }
            }

            SetBody(sb.ToString());
        }

        // ─── Train View ───────────────────────────────────────────────────────────

        private void PopulateTrain(Train train)
        {
            if (train == null) { Hide(); return; }

            SetTitle("Train", train.TrainName);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Status:</b> {train.State}");
            sb.AppendLine($"<b>Max Speed:</b> {train.MaxSpeed:F0} tiles/s");
            sb.AppendLine();

            // Cargo
            sb.AppendLine("<b>Cargo:</b>");
            if (train.CurrentCargo <= 0)
            {
                sb.AppendLine("  <i>Empty</i>");
            }
            else
            {
                sb.AppendLine($"  {train.CargoType}: <b>{train.CurrentCargo}t</b> / {train.CargoCapacity}t");
                sb.AppendLine($"  Load: {train.CargoFraction:P0}");
            }

            sb.AppendLine();
            sb.AppendLine("<b>Orders:</b>");
            if (train.Orders != null)
                sb.AppendLine($"  {train.Orders.Count} order(s)");
            else
                sb.AppendLine("  <i>None</i>");

            sb.AppendLine();
            sb.AppendLine($"<b>Running cost:</b> {CargoPayment.FormatCurrency(train.RunningCostPerDay)}/day");

            SetBody(sb.ToString());
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private void SetTitle(string subtitle, string title)
        {
            if (titleLabel    != null) titleLabel.text    = title;
            if (subtitleLabel != null) subtitleLabel.text = subtitle;
        }

        private void SetBody(string text)
        {
            if (bodyLabel != null) bodyLabel.text = text;
        }
    }
}
