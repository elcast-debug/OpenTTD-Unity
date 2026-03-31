using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenTTDUnity
{
    /// <summary>
    /// Handles interactive placement of <see cref="Station"/> objects on the tile grid.
    ///
    /// <para>
    /// Rules:
    /// <list type="bullet">
    ///   <item>A station may only be placed on an existing straight rail tile
    ///         (<see cref="RailDirection.North_South"/> or <see cref="RailDirection.East_West"/>).</item>
    ///   <item>Only one station per tile.</item>
    ///   <item>Cost is deducted from <see cref="EconomyManager"/>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Workflow: activate with <see cref="ActivatePlacementMode"/>, hover over the
    /// map to see a ghost preview, left-click to confirm, right-click or Escape to
    /// cancel.
    /// </para>
    /// </summary>
    public class StationPlacer : MonoBehaviour
    {
        // ── Inspector fields ────────────────────────────────────────────────

        /// <summary>Prefab instantiated for each placed station (must have a Station component).</summary>
        [SerializeField] private GameObject stationPrefab;

        /// <summary>Material applied to the ghost preview.</summary>
        [SerializeField] private Material ghostMaterial;

        /// <summary>Layer mask for terrain raycasting.</summary>
        [SerializeField] private LayerMask terrainLayer = ~0;

        /// <summary>Camera used for mouse raycasts. Defaults to Camera.main.</summary>
        [SerializeField] private Camera mainCamera;

        /// <summary>World-space height offset for the ghost above terrain.</summary>
        [SerializeField] private float ghostHeightOffset = 0.1f;

        /// <summary>Cost in currency to place a station.</summary>
        [SerializeField] private int stationCost = 500;

        // ── Runtime state ───────────────────────────────────────────────────

        private bool      isActive      = false;
        private GameObject ghostObject   = null;
        private Vector2Int lastHoveredTile;
        private bool       ghostVisible  = false;

        // ── Properties ──────────────────────────────────────────────────────

        /// <summary>True when the station placement tool is active.</summary>
        public bool IsActive => isActive;

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>Fired after a station is successfully placed. Parameter is the new station.</summary>
        public event System.Action<Station> OnStationPlaced;

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
        }

        private void Update()
        {
            if (!isActive) return;

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                Deactivate();
                return;
            }

            Vector2Int? hoveredTile = GetTileUnderMouse();
            if (hoveredTile.HasValue)
            {
                UpdateGhost(hoveredTile.Value);

                if (Input.GetMouseButtonDown(0) &&
                    EventSystem.current != null &&
                    !EventSystem.current.IsPointerOverGameObject())
                {
                    TryPlaceStation(hoveredTile.Value);
                }
            }
            else
            {
                HideGhost();
            }
        }

        private void OnDestroy()
        {
            if (ghostObject != null)
                Destroy(ghostObject);
        }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>Activates the station placement tool.</summary>
        public void ActivatePlacementMode()
        {
            isActive = true;
            EnsureGhostExists();
        }

        /// <summary>Deactivates the placement tool and hides the ghost.</summary>
        public void Deactivate()
        {
            isActive = false;
            HideGhost();
        }

        // ── Placement logic ─────────────────────────────────────────────────

        private void TryPlaceStation(Vector2Int tile)
        {
            // Must be on a straight rail
            if (!IsValidPlacementTile(tile, out string reason))
            {
                Debug.Log($"[StationPlacer] Cannot place station at {tile}: {reason}");
                // TODO: show UI feedback message
                return;
            }

            // Economy check
            if (EconomyManager.Instance != null &&
                !EconomyManager.Instance.CanAfford(stationCost))
            {
                Debug.LogWarning($"[StationPlacer] Cannot afford station (cost {stationCost}).");
                return;
            }

            // Spawn the station
            Station station = SpawnStation(tile);
            if (station == null) return;

            // Deduct cost
            EconomyManager.Instance?.Spend(stationCost, "Station construction");

            // Register with GridManager
            GridManager.Instance?.SetStationOnTile(tile.x, tile.y, station);

            OnStationPlaced?.Invoke(station);
        }

        private bool IsValidPlacementTile(Vector2Int tile, out string reason)
        {
            reason = string.Empty;

            // Must have a rail segment
            if (RailManager.Instance == null || !RailManager.Instance.HasRail(tile))
            {
                reason = "No rail segment found. Stations must be placed on existing rail.";
                return false;
            }

            var seg = RailManager.Instance.GetSegment(tile);
            if (seg == null)
            {
                reason = "Rail segment is null.";
                return false;
            }

            // Only straight rail is valid
            if (seg.Direction != RailDirection.North_South &&
                seg.Direction != RailDirection.East_West)
            {
                reason = $"Rail direction {seg.Direction} is not a straight segment. Stations require straight rail.";
                return false;
            }

            // No existing station on this tile
            if (GridManager.Instance != null)
            {
                var tileData = GridManager.Instance.GetTile(tile.x, tile.y);
                if (tileData != null && tileData.Station != null)
                {
                    reason = "A station already exists on this tile.";
                    return false;
                }
            }

            return true;
        }

        // ── Ghost preview ───────────────────────────────────────────────────

        private void UpdateGhost(Vector2Int tile)
        {
            EnsureGhostExists();

            Vector3 worldPos = GetWorldPos(tile);
            ghostObject.transform.position = worldPos + Vector3.up * ghostHeightOffset;

            bool valid = IsValidPlacementTile(tile, out _);

            // Tint ghost: green-ish if valid, red-ish if not
            var mr = ghostObject.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (ghostMaterial != null)
                {
                    mr.material = ghostMaterial;
                }
                else
                {
                    Color tint = valid
                        ? new Color(0.2f, 1f, 0.3f, 0.4f)
                        : new Color(1f,   0.2f, 0.2f, 0.4f);
                    mr.material = CreateGhostMaterial(tint);
                }
            }

            if (!ghostVisible)
            {
                ghostObject.SetActive(true);
                ghostVisible = true;
            }

            lastHoveredTile = tile;
        }

        private void HideGhost()
        {
            if (ghostObject != null)
                ghostObject.SetActive(false);
            ghostVisible = false;
        }

        private void EnsureGhostExists()
        {
            if (ghostObject != null) return;

            ghostObject = CreateGhostGameObject();
            ghostObject.SetActive(false);
        }

        private GameObject CreateGhostGameObject()
        {
            GameObject go;

            if (stationPrefab != null)
            {
                go = Instantiate(stationPrefab);
                // Remove/disable the Station component on the ghost
                var stComp = go.GetComponent<Station>();
                if (stComp != null) stComp.enabled = false;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = new Vector3(1f, 0.5f, 1f);
                // Remove physics collider from ghost
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            go.name = "StationGhost";
            return go;
        }

        // ── Station spawning ────────────────────────────────────────────────

        private Station SpawnStation(Vector2Int tile)
        {
            GameObject go;

            if (stationPrefab != null)
            {
                go = Instantiate(stationPrefab);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = new Vector3(1f, 0.5f, 1f);
            }

            go.name = $"Station_{tile.x}_{tile.y}";
            go.transform.position = GetWorldPos(tile) + Vector3.up * 0.25f;

            var station = go.GetComponent<Station>();
            if (station == null)
                station = go.AddComponent<Station>();

            station.Initialise(tile.x, tile.y);
            return station;
        }

        // ── Raycasting / helpers ────────────────────────────────────────────

        private Vector2Int? GetTileUnderMouse()
        {
            if (mainCamera == null) return null;
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, terrainLayer))
            {
                if (GridManager.Instance != null)
                    return GridManager.Instance.WorldToGrid(hit.point);
                return new Vector2Int(Mathf.FloorToInt(hit.point.x),
                                      Mathf.FloorToInt(hit.point.z));
            }
            return null;
        }

        private Vector3 GetWorldPos(Vector2Int tile)
        {
            if (GridManager.Instance != null)
                return GridManager.Instance.GridToWorld(tile.x, tile.y);
            return new Vector3(tile.x, 0f, tile.y);
        }

        private static Material CreateGhostMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite",    0);
            mat.renderQueue = 3000;
            return mat;
        }
    }
}
