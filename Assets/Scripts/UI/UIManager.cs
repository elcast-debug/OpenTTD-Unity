using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenTTDUnity
{
    /// <summary>
    /// Defines all interactive build/edit modes the player can be in.
    /// The current mode controls how mouse clicks on the game world are interpreted.
    /// </summary>
    public enum InputMode
    {
        /// <summary>Normal camera pan/select mode — no build action active.</summary>
        Normal      = 0,

        /// <summary>Click-drag to place rail segments.</summary>
        BuildRail   = 1,

        /// <summary>Click to place a station on a rail tile.</summary>
        BuildStation = 2,

        /// <summary>Click-drag to raise terrain.</summary>
        TerraformUp  = 3,

        /// <summary>Click-drag to lower terrain.</summary>
        TerraformDown = 4,

        /// <summary>Click to remove rail, station, or other objects.</summary>
        Bulldoze    = 5,

        /// <summary>Click to place a train depot.</summary>
        BuildDepot  = 6,
    }

    /// <summary>
    /// Central UI coordinator. Manages panel visibility, tracks the current
    /// <see cref="InputMode"/>, and routes keyboard shortcuts to the appropriate
    /// build systems.
    ///
    /// Canvas Setup (Unity Editor):
    ///   1. Create a Canvas (Screen Space – Overlay, sort order 10).
    ///   2. Attach UIManager to the Canvas root or a child "UIManager" object.
    ///   3. Assign TopBar, Toolbar, InfoPanel, and BuildPreview references.
    ///   4. EventSystem must be present in the scene (Unity adds one by default).
    ///
    /// Keyboard shortcuts (mode toggle):
    ///   Escape → Normal (cancel)
    ///   R      → BuildRail
    ///   S      → BuildStation
    ///   D      → BuildDepot
    ///   T      → TerraformUp
    ///   G      → TerraformDown
    ///   B      → Bulldoze
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        // ─── Singleton ────────────────────────────────────────────────────────────
        public static UIManager Instance { get; private set; }

        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("UI Panels")]
        [SerializeField] private TopBar     topBar;
        [SerializeField] private Toolbar    toolbar;
        [SerializeField] private InfoPanel  infoPanel;
        [SerializeField] private BuildPreview buildPreview;

        [Header("Input Settings")]
        [Tooltip("When true, pressing a mode key while that mode is already active returns to Normal mode.")]
        [SerializeField] private bool toggleModeOnRepeat = true;

        // ─── State ────────────────────────────────────────────────────────────────

        private InputMode _currentMode = InputMode.Normal;

        // ─── Events ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Fired whenever the input mode changes.
        /// Subscribe here to react to mode switches (e.g., RailPlacer, TerrainModifier).
        /// </summary>
        public static event Action<InputMode> OnInputModeChanged;

        // ─── Public Properties ────────────────────────────────────────────────────

        /// <summary>The player's current interaction mode.</summary>
        public InputMode CurrentMode => _currentMode;

        /// <summary>Convenience: returns true when ANY build mode is active.</summary>
        public bool IsBuildModeActive => _currentMode != InputMode.Normal;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[UIManager] Duplicate instance destroyed.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            ValidatePanelReferences();
            SetMode(InputMode.Normal);
        }

        private void Update()
        {
            HandleKeyboardShortcuts();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─── Mode Management ──────────────────────────────────────────────────────

        /// <summary>
        /// Switches to the specified input mode. Notifies all subscribers and
        /// updates the toolbar's active-button highlight.
        /// </summary>
        public void SetMode(InputMode newMode)
        {
            if (_currentMode == newMode && toggleModeOnRepeat)
            {
                // Second click on the same mode cancels back to Normal
                newMode = InputMode.Normal;
            }

            _currentMode = newMode;

            // Update sub-components
            toolbar?.OnModeChanged(_currentMode);
            buildPreview?.OnModeChanged(_currentMode);

            // Hide info panel when entering a build mode
            if (_currentMode != InputMode.Normal)
                infoPanel?.Hide();

            OnInputModeChanged?.Invoke(_currentMode);
            Debug.Log($"[UIManager] Mode → {_currentMode}");
        }

        /// <summary>
        /// Cancels the current build mode and returns to Normal.
        /// </summary>
        public void CancelBuildMode() => SetMode(InputMode.Normal);

        // ─── Keyboard Shortcuts ───────────────────────────────────────────────────

        private void HandleKeyboardShortcuts()
        {
            // Suppress shortcuts when typing in an input field
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))  { SetMode(InputMode.Normal);       return; }
            if (Input.GetKeyDown(KeyCode.R))        { SetMode(InputMode.BuildRail);    return; }
            if (Input.GetKeyDown(KeyCode.S))        { SetMode(InputMode.BuildStation); return; }
            if (Input.GetKeyDown(KeyCode.D))        { SetMode(InputMode.BuildDepot);   return; }
            if (Input.GetKeyDown(KeyCode.T))        { SetMode(InputMode.TerraformUp);  return; }
            if (Input.GetKeyDown(KeyCode.G))        { SetMode(InputMode.TerraformDown);return; }
            if (Input.GetKeyDown(KeyCode.B))        { SetMode(InputMode.Bulldoze);     return; }
        }

        // ─── Panel Management ─────────────────────────────────────────────────────

        /// <summary>
        /// Shows the info panel populated with data from a selected game object.
        /// Call this when the player clicks on a station, industry, or train.
        /// </summary>
        public void ShowInfoPanel(GameObject selected)
        {
            if (IsBuildModeActive) return; // don't open info while building
            infoPanel?.ShowForObject(selected);
        }

        /// <summary>
        /// Hides the info panel (e.g., when clicking empty terrain).
        /// </summary>
        public void HideInfoPanel() => infoPanel?.Hide();

        // ─── Build Cost Preview ───────────────────────────────────────────────────

        /// <summary>
        /// Updates the build cost preview display.
        /// Called every frame by the active build tool with the current hover tile.
        /// </summary>
        public void SetBuildCostPreview(int cost, bool canAfford, string label = "")
        {
            buildPreview?.UpdatePreview(cost, canAfford, label);
        }

        // ─── Validation ───────────────────────────────────────────────────────────

        private void ValidatePanelReferences()
        {
            if (topBar     == null) Debug.LogWarning("[UIManager] TopBar reference not assigned.");
            if (toolbar    == null) Debug.LogWarning("[UIManager] Toolbar reference not assigned.");
            if (infoPanel  == null) Debug.LogWarning("[UIManager] InfoPanel reference not assigned.");
            if (buildPreview == null) Debug.LogWarning("[UIManager] BuildPreview reference not assigned.");
        }

#if UNITY_EDITOR
        [ContextMenu("Debug: Set BuildRail Mode")]
        private void DebugBuildRail() => SetMode(InputMode.BuildRail);

        [ContextMenu("Debug: Set Normal Mode")]
        private void DebugNormalMode() => SetMode(InputMode.Normal);
#endif
    }
}
