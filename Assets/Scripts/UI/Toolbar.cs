using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace OpenTTDUnity
{
    /// <summary>
    /// Bottom toolbar presenting build tool buttons.
    /// Each button maps to an <see cref="InputMode"/>. Clicking the active
    /// button deactivates the mode (toggle behaviour). An active button is
    /// highlighted with <see cref="activeColour"/>.
    ///
    /// Keyboard shortcuts are handled by <see cref="UIManager"/> and propagated
    /// here via <see cref="OnModeChanged"/>.
    ///
    /// Canvas Hierarchy Setup (Unity Editor):
    /// ─────────────────────────────────────────────────────────────────────
    ///  Canvas
    ///  └─ Toolbar  [RectTransform: Stretch X, Bottom anchor, height 48px]
    ///     ├─ Background  [Image, dark semi-transparent #1A1A1ACC]
    ///     └─ ButtonRow   [HorizontalLayoutGroup, padding 4px, spacing 4px]
    ///        ├─ BtnRail       [Button]  → InputMode.BuildRail     (R)
    ///        ├─ BtnStation    [Button]  → InputMode.BuildStation  (S)
    ///        ├─ BtnDepot      [Button]  → InputMode.BuildDepot    (D)
    ///        ├─ Divider       [Image, 1px wide, grey]
    ///        ├─ BtnTerraUp    [Button]  → InputMode.TerraformUp   (T)
    ///        ├─ BtnTerraDown  [Button]  → InputMode.TerraformDown (G)
    ///        ├─ Divider       [Image, 1px wide, grey]
    ///        └─ BtnBulldoze   [Button]  → InputMode.Bulldoze      (B)
    /// ─────────────────────────────────────────────────────────────────────
    ///
    /// Each button should have:
    ///   - A child TextMeshProUGUI for the button icon/label
    ///   - A child GameObject named "Tooltip" (TextMeshProUGUI) initially disabled
    ///
    /// Anchors:
    ///   Toolbar rect: anchorMin=(0,0) anchorMax=(1,0) pivot=(0.5,0)
    ///   offsetMin.y = 0, offsetMax.y = 48  →  48px strip at bottom of screen
    /// </summary>
    public class Toolbar : MonoBehaviour
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("Tool Buttons")]
        [SerializeField] private Button railButton;
        [SerializeField] private Button stationButton;
        [SerializeField] private Button depotButton;
        [SerializeField] private Button terraformUpButton;
        [SerializeField] private Button terraformDownButton;
        [SerializeField] private Button bulldozeButton;

        [Header("Tooltip")]
        [Tooltip("Tooltip panel that appears on hover. Position near the hovered button.")]
        [SerializeField] private GameObject tooltipPanel;
        [SerializeField] private TextMeshProUGUI tooltipText;
        [Tooltip("Offset from the button's centre to place the tooltip (in screen pixels).")]
        [SerializeField] private Vector2 tooltipOffset = new Vector2(0f, 56f);

        [Header("Style")]
        [SerializeField] private Color activeColour   = new Color(0.25f, 0.60f, 1.00f);
        [SerializeField] private Color inactiveColour = new Color(0.30f, 0.30f, 0.30f);
        [SerializeField] private Color hoverColour    = new Color(0.50f, 0.50f, 0.55f);

        // ─── Button Definition ────────────────────────────────────────────────────

        /// <summary>Internal mapping of a UI button to its mode and metadata.</summary>
        private class ToolButton : IPointerEnterHandler, IPointerExitHandler
        {
            public Button     UnityButton { get; }
            public InputMode  Mode        { get; }
            public string     Label       { get; }
            public string     Shortcut    { get; }
            public string     CostHint    { get; }

            private readonly Toolbar _owner;
            private Image    _image;

            public ToolButton(Button btn, InputMode mode, string label, string shortcut, string costHint, Toolbar owner)
            {
                UnityButton = btn;
                Mode        = mode;
                Label       = label;
                Shortcut    = shortcut;
                CostHint    = costHint;
                _owner      = owner;

                if (btn != null)
                {
                    _image = btn.GetComponent<Image>();
                    btn.onClick.AddListener(OnClicked);

                    // Add pointer handlers via the EventTrigger approach
                    AddEventTrigger(btn.gameObject, EventTriggerType.PointerEnter, (_) => OnEnter());
                    AddEventTrigger(btn.gameObject, EventTriggerType.PointerExit,  (_) => OnExit());
                }
            }

            private void OnClicked()
            {
                _owner.HandleButtonClicked(Mode);
            }

            public void OnPointerEnter(PointerEventData eventData) => OnEnter();
            public void OnPointerExit(PointerEventData eventData)  => OnExit();

            private void OnEnter() => _owner.ShowTooltip(this);
            private void OnExit()  => _owner.HideTooltip();

            public void SetHighlight(bool isActive)
            {
                if (_image == null) return;
                _image.color = isActive ? _owner.activeColour : _owner.inactiveColour;
            }

            public void Destroy()
            {
                if (UnityButton != null) UnityButton.onClick.RemoveAllListeners();
            }

            private static void AddEventTrigger(GameObject go, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
            {
                var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
                var entry   = new EventTrigger.Entry { eventID = type };
                entry.callback.AddListener(action);
                trigger.triggers.Add(entry);
            }
        }

        // ─── State ────────────────────────────────────────────────────────────────

        private List<ToolButton> _buttons = new List<ToolButton>();
        private InputMode        _activeMode = InputMode.Normal;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Start()
        {
            BuildButtonList();
            HideTooltip();
            RefreshHighlights();
        }

        private void OnDestroy()
        {
            foreach (var btn in _buttons)
                btn.Destroy();
        }

        // ─── Setup ────────────────────────────────────────────────────────────────

        private void BuildButtonList()
        {
            _buttons.Clear();

            // Order here reflects left-to-right layout on the toolbar
            AddButton(railButton,         InputMode.BuildRail,     "Build Rail",         "R", "$100 / tile");
            AddButton(stationButton,      InputMode.BuildStation,  "Build Station",      "S", "$500");
            AddButton(depotButton,        InputMode.BuildDepot,    "Build Depot",        "D", "$1,000");
            AddButton(terraformUpButton,  InputMode.TerraformUp,   "Raise Terrain",      "T", "$100 / tile");
            AddButton(terraformDownButton,InputMode.TerraformDown, "Lower Terrain",      "G", "$100 / tile");
            AddButton(bulldozeButton,     InputMode.Bulldoze,      "Bulldoze",           "B", "Free");
        }

        private void AddButton(Button btn, InputMode mode, string label, string shortcut, string costHint)
        {
            if (btn == null)
            {
                Debug.LogWarning($"[Toolbar] Button for {mode} not assigned.");
                return;
            }
            _buttons.Add(new ToolButton(btn, mode, label, shortcut, costHint, this));
        }

        // ─── Mode Handling ────────────────────────────────────────────────────────

        /// <summary>
        /// Called by UIManager when the input mode changes (including via keyboard).
        /// Updates button highlights to reflect the new active mode.
        /// </summary>
        public void OnModeChanged(InputMode newMode)
        {
            _activeMode = newMode;
            RefreshHighlights();
        }

        private void HandleButtonClicked(InputMode mode)
        {
            // Delegate to UIManager — it will fire OnInputModeChanged which calls back here
            UIManager.Instance?.SetMode(mode);
        }

        private void RefreshHighlights()
        {
            foreach (var btn in _buttons)
                btn.SetHighlight(btn.Mode == _activeMode);
        }

        // ─── Tooltip ──────────────────────────────────────────────────────────────

        private void ShowTooltip(ToolButton btn)
        {
            if (tooltipPanel == null || tooltipText == null) return;

            tooltipText.text = $"<b>{btn.Label}</b>  [{btn.Shortcut}]\nCost: {btn.CostHint}";

            // Position above the button
            if (btn.UnityButton != null)
            {
                var rt = tooltipPanel.GetComponent<RectTransform>();
                if (rt != null && btn.UnityButton.TryGetComponent<RectTransform>(out var btnRt))
                {
                    rt.position = btnRt.position + (Vector3)tooltipOffset;
                }
            }

            tooltipPanel.SetActive(true);
        }

        private void HideTooltip()
        {
            tooltipPanel?.SetActive(false);
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Programmatically activates a tool button (e.g., from a script or tutorial).
        /// </summary>
        public void ActivateTool(InputMode mode) => HandleButtonClicked(mode);
    }
}
