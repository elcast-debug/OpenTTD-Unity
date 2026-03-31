using TMPro;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Displays a floating build cost preview near the cursor when a build
    /// mode is active, and optionally renders a ghost/preview of the object
    /// about to be placed.
    ///
    /// The cost label uses green text when the player can afford the action,
    /// and red when they cannot. When no build mode is active, the label is hidden.
    ///
    /// Canvas Hierarchy Setup (Unity Editor):
    /// ─────────────────────────────────────────────────────────────────────
    ///  Canvas (Screen Space – Overlay)
    ///  └─ BuildPreview  [attach this component here]
    ///     ├─ CostPanel  [RectTransform, no anchoring — positioned via code]
    ///     │  ├─ Background  [Image, #000000AA, rounded rect]
    ///     │  └─ CostLabel   [TextMeshProUGUI]
    ///     └─ (Ghost objects are spawned in world space, not under this canvas)
    /// ─────────────────────────────────────────────────────────────────────
    ///
    /// Ghost object setup:
    ///   Assign prefabs to the ghostPrefabs array (index matches InputMode int value).
    ///   Ghosts are rendered with a semi-transparent material (ghostMaterial).
    ///   Ghosts follow the cursor tile and are shown/hidden with the preview.
    /// </summary>
    public class BuildPreview : MonoBehaviour
    {
        // ─── Inspector Fields ─────────────────────────────────────────────────────

        [Header("UI Cost Label")]
        [Tooltip("Panel GameObject that holds the cost label. Positioned near the cursor.")]
        [SerializeField] private RectTransform costPanel;

        [Tooltip("TextMeshPro label that shows the cost string.")]
        [SerializeField] private TextMeshProUGUI costLabel;

        [Tooltip("Pixel offset from the cursor position to draw the cost panel.")]
        [SerializeField] private Vector2 cursorOffset = new Vector2(16f, 20f);

        [Header("Colours")]
        [Tooltip("Text colour when the player can afford the action.")]
        [SerializeField] private Color affordableColour = new Color(0.18f, 0.85f, 0.28f);

        [Tooltip("Text colour when the player cannot afford the action.")]
        [SerializeField] private Color unaffordableColour = new Color(0.95f, 0.22f, 0.18f);

        [Header("Ghost Object")]
        [Tooltip("Material applied to ghost preview meshes (semi-transparent).")]
        [SerializeField] private Material ghostMaterial;

        [Tooltip("One ghost prefab per InputMode value. Index 0 = Normal (unused). " +
                 "Leave slots null for modes with no ghost (e.g., Bulldoze).")]
        [SerializeField] private GameObject[] ghostPrefabs;

        [Tooltip("Ghost objects are scaled down by this fraction to indicate 'preview' state.")]
        [SerializeField] [Range(0.5f, 1f)] private float ghostScale = 0.95f;

        [Header("Rail Ghost")]
        [Tooltip("When in BuildRail mode, snap the ghost to tile grid coordinates.")]
        [SerializeField] private bool snapGhostToGrid = true;

        // ─── State ────────────────────────────────────────────────────────────────

        private InputMode _currentMode   = InputMode.Normal;
        private bool      _isVisible     = false;
        private int       _lastCost      = 0;
        private bool      _lastAffordable = true;

        private GameObject _activeGhost;      // currently instantiated ghost
        private Camera     _mainCamera;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _mainCamera = Camera.main;
            HideCostLabel();
        }

        private void Update()
        {
            if (!_isVisible) return;

            MoveCostLabelToCursor();
            UpdateGhostPosition();
        }

        // ─── Mode Changes ─────────────────────────────────────────────────────────

        /// <summary>
        /// Called by <see cref="UIManager"/> when the input mode changes.
        /// Shows or hides the preview panel and activates the appropriate ghost.
        /// </summary>
        public void OnModeChanged(InputMode newMode)
        {
            _currentMode = newMode;

            // Destroy old ghost
            if (_activeGhost != null)
            {
                Destroy(_activeGhost);
                _activeGhost = null;
            }

            bool showPreview = (newMode != InputMode.Normal);
            _isVisible = showPreview;

            if (showPreview)
            {
                SpawnGhostForMode(newMode);
                // Cost label stays hidden until the first UpdatePreview() call
            }
            else
            {
                HideCostLabel();
            }
        }

        // ─── Cost Label Updates ───────────────────────────────────────────────────

        /// <summary>
        /// Updates the floating cost label with the given cost and affordability.
        /// Call this every frame from the active build tool (RailPlacer, StationPlacer…)
        /// while hovering over a valid placement tile.
        /// </summary>
        /// <param name="cost">Cost in currency units. Pass 0 to hide the label.</param>
        /// <param name="canAfford">True if the player has sufficient funds.</param>
        /// <param name="extraLabel">Optional extra text appended after cost (e.g., tile count).</param>
        public void UpdatePreview(int cost, bool canAfford, string extraLabel = "")
        {
            _lastCost       = cost;
            _lastAffordable = canAfford;

            if (!_isVisible || _currentMode == InputMode.Normal)
            {
                HideCostLabel();
                return;
            }

            if (cost <= 0)
            {
                HideCostLabel();
                return;
            }

            ShowCostLabel(cost, canAfford, extraLabel);
        }

        /// <summary>
        /// Hides the cost label (e.g., when the cursor is over the UI or invalid terrain).
        /// Does NOT deactivate the full preview — call <see cref="OnModeChanged"/> for that.
        /// </summary>
        public void HideCostLabel()
        {
            if (costPanel != null) costPanel.gameObject.SetActive(false);
        }

        private void ShowCostLabel(int cost, bool canAfford, string extra)
        {
            if (costPanel == null || costLabel == null) return;

            costPanel.gameObject.SetActive(true);
            costLabel.color = canAfford ? affordableColour : unaffordableColour;

            string prefix = canAfford ? "" : "⚠ ";
            string suffix = string.IsNullOrEmpty(extra) ? "" : $"\n<size=70%>{extra}</size>";
            costLabel.text = $"{prefix}{CargoPayment.FormatCurrency(cost)}{suffix}";
        }

        // ─── Label Positioning ────────────────────────────────────────────────────

        private void MoveCostLabelToCursor()
        {
            if (costPanel == null || !costPanel.gameObject.activeSelf) return;

            Vector2 cursorPos = Input.mousePosition;
            costPanel.position = cursorPos + cursorOffset;

            // Keep label inside screen bounds
            ClampPanelToScreen();
        }

        private void ClampPanelToScreen()
        {
            if (costPanel == null) return;

            Vector3 pos    = costPanel.position;
            Vector2 size   = costPanel.sizeDelta;
            float   screenW = Screen.width;
            float   screenH = Screen.height;

            pos.x = Mathf.Clamp(pos.x, size.x * 0.5f, screenW - size.x * 0.5f);
            pos.y = Mathf.Clamp(pos.y, size.y * 0.5f, screenH - size.y * 0.5f);

            costPanel.position = pos;
        }

        // ─── Ghost Object ─────────────────────────────────────────────────────────

        private void SpawnGhostForMode(InputMode mode)
        {
            int modeIndex = (int)mode;
            if (ghostPrefabs == null || modeIndex >= ghostPrefabs.Length) return;

            GameObject prefab = ghostPrefabs[modeIndex];
            if (prefab == null) return;

            _activeGhost = Instantiate(prefab);
            _activeGhost.name = $"Ghost_{mode}";

            // Apply ghost material to all renderers
            if (ghostMaterial != null)
            {
                foreach (var renderer in _activeGhost.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterial = ghostMaterial;
            }

            // Scale slightly down
            _activeGhost.transform.localScale = Vector3.one * ghostScale;

            // Disable colliders so ghost doesn't interfere with raycasts
            foreach (var col in _activeGhost.GetComponentsInChildren<Collider>())
                col.enabled = false;
        }

        private void UpdateGhostPosition()
        {
            if (_activeGhost == null) return;

            // Raycast from cursor to world to get tile position
            Ray ray = _mainCamera != null
                ? _mainCamera.ScreenPointToRay(Input.mousePosition)
                : new Ray(Vector3.zero, Vector3.down);

            if (Physics.Raycast(ray, out RaycastHit hit, 500f, LayerMask.GetMask("Terrain")))
            {
                Vector3 worldPos = hit.point;

                if (snapGhostToGrid)
                {
                    worldPos.x = Mathf.Floor(worldPos.x) + 0.5f;
                    worldPos.z = Mathf.Floor(worldPos.z) + 0.5f;
                }

                worldPos.y += 0.01f; // tiny lift to avoid z-fighting

                _activeGhost.transform.position = worldPos;
                _activeGhost.SetActive(true);
            }
            else
            {
                // Cursor not over terrain — hide ghost
                _activeGhost.SetActive(false);
            }
        }

        // ─── Public Utilities ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> if the preview overlay is currently active
        /// (i.e., a build mode is selected and the cursor is over the game world).
        /// </summary>
        public bool IsVisible => _isVisible;

        /// <summary>Returns the last cost value passed to <see cref="UpdatePreview"/>.</summary>
        public int LastCost => _lastCost;
    }
}
