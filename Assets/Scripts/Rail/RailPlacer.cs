using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OpenTTDUnity
{
    /// <summary>
    /// Handles click-and-drag rail placement and bulldoze (removal) in build mode.
    ///
    /// Workflow:
    /// <list type="number">
    ///   <item>Player activates build mode via <see cref="ActivateBuildMode"/>.</item>
    ///   <item>Mouse-down records the start tile.</item>
    ///   <item>Dragging shows a semi-transparent ghost preview of the planned path.</item>
    ///   <item>Mouse-up commits all segments to <see cref="RailManager"/>.</item>
    ///   <item>Right-click or Escape cancels the drag.</item>
    /// </list>
    ///
    /// Uses OpenTTD-style auto-rail routing: straight line preferred, then
    /// an L-shaped bend (one straight segment + one curve).
    /// </summary>
    [RequireComponent(typeof(RailManager))]
    public class RailPlacer : MonoBehaviour
    {
        // ── Enums ───────────────────────────────────────────────────────────

        /// <summary>Active tool mode for the rail placer.</summary>
        public enum PlacerMode { Inactive, Build, Bulldoze }

        // ── Inspector fields ────────────────────────────────────────────────

        /// <summary>Material applied to ghost/preview segments. Should be semi-transparent.</summary>
        [SerializeField] private Material ghostMaterial;

        /// <summary>Material applied to ghost segments in bulldoze mode (e.g. red tint).</summary>
        [SerializeField] private Material bulldozeMaterial;

        /// <summary>Layer mask used for raycast against terrain.</summary>
        [SerializeField] private LayerMask terrainLayer = ~0;

        /// <summary>Camera used for ray-casting. Defaults to Camera.main if null.</summary>
        [SerializeField] private Camera mainCamera;

        /// <summary>Height offset applied to ghost meshes above terrain.</summary>
        [SerializeField] private float ghostHeightOffset = 0.05f;

        // ── Runtime state ───────────────────────────────────────────────────

        private PlacerMode currentMode = PlacerMode.Inactive;
        private bool isDragging = false;
        private Vector2Int dragStart;
        private Vector2Int dragCurrent;

        // Pool of reusable ghost GameObjects
        private readonly List<GameObject> ghostPool = new List<GameObject>();
        private readonly List<GameObject> activeGhosts = new List<GameObject>();

        // Path calculated on each drag update
        private List<(Vector2Int pos, RailDirection dir)> plannedPath =
            new List<(Vector2Int, RailDirection)>();

        // ── Properties ──────────────────────────────────────────────────────

        /// <summary>Current active mode (Inactive / Build / Bulldoze).</summary>
        public PlacerMode CurrentMode => currentMode;

        /// <summary>
        /// Total placement cost of the current ghost path preview.
        /// Zero when not dragging or in bulldoze mode.
        /// </summary>
        public int PreviewCost { get; private set; }

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>Fired when the previewed path cost changes (e.g. during drag).</summary>
        public event System.Action<int> OnPreviewCostChanged;

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
        }

        private void Update()
        {
            if (currentMode == PlacerMode.Inactive) return;

            // Cancel via right-click or Escape
            if (InputHelper.GetMouseButtonDown(1) || InputHelper.GetKeyDown(UnityEngine.InputSystem.Key.Escape))
            {
                CancelDrag();
                return;
            }

            HandleInput();
        }

        private void OnDestroy()
        {
            ClearGhosts();
            foreach (var g in ghostPool)
                if (g != null) Destroy(g);
            ghostPool.Clear();
        }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>Activates rail build mode.</summary>
        public void ActivateBuildMode()
        {
            currentMode = PlacerMode.Build;
            isDragging  = false;
            ClearGhosts();
        }

        /// <summary>Activates bulldoze (removal) mode.</summary>
        public void ActivateBulldozeMode()
        {
            currentMode = PlacerMode.Bulldoze;
            isDragging  = false;
            ClearGhosts();
        }

        /// <summary>Deactivates the placer tool.</summary>
        public void Deactivate()
        {
            CancelDrag();
            currentMode = PlacerMode.Inactive;
        }

        // ── Input handling ──────────────────────────────────────────────────

        private void HandleInput()
        {
            // Ignore clicks over UI
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Vector2Int? hoveredTile = GetTileUnderMouse();

            // Mouse down — start drag
            if (InputHelper.GetMouseButtonDown(0) && hoveredTile.HasValue)
            {
                isDragging  = true;
                dragStart   = hoveredTile.Value;
                dragCurrent = hoveredTile.Value;
                UpdatePreview(dragStart, dragCurrent);
            }

            // Mouse held — update preview
            if (isDragging && InputHelper.GetMouseButton(0) && hoveredTile.HasValue)
            {
                if (hoveredTile.Value != dragCurrent)
                {
                    dragCurrent = hoveredTile.Value;
                    UpdatePreview(dragStart, dragCurrent);
                }
            }

            // Mouse up — commit
            if (isDragging && InputHelper.GetMouseButtonUp(0))
            {
                CommitPlacement();
                isDragging = false;
                ClearGhosts();
            }
        }

        // ── Preview (ghost) rendering ───────────────────────────────────────

        private void UpdatePreview(Vector2Int start, Vector2Int end)
        {
            plannedPath = CalculatePath(start, end);
            ClearGhosts();

            int cost = 0;
            foreach (var (pos, dir) in plannedPath)
            {
                bool occupied = RailManager.Instance != null &&
                                RailManager.Instance.HasRail(pos);

                if (currentMode == PlacerMode.Bulldoze)
                {
                    if (!occupied) continue; // nothing to bulldoze here
                }
                else
                {
                    // In build mode count cost for unoccupied tiles only
                    // (occupied tiles will be junction-merged at no extra cost)
                    if (!occupied) cost += GetCostPerSegment();
                }

                ShowGhost(pos, dir, occupied);
            }

            if (currentMode == PlacerMode.Build && PreviewCost != cost)
            {
                PreviewCost = cost;
                OnPreviewCostChanged?.Invoke(cost);
            }
        }

        private void ShowGhost(Vector2Int pos, RailDirection dir, bool isOccupied)
        {
            GameObject ghost = GetOrCreateGhost();
            activeGhosts.Add(ghost);
            ghost.SetActive(true);

            // Position
            Vector3 worldPos;
            if (GridManager.Instance != null)
                worldPos = GridManager.Instance.GridToWorld(pos.x, pos.y);
            else
                worldPos = new Vector3(pos.x, 0f, pos.y);

            ghost.transform.position = worldPos + Vector3.up * ghostHeightOffset;

            // Mesh
            var mf = ghost.GetComponent<MeshFilter>();
            if (mf != null)
                mf.mesh = RailMeshGenerator.GenerateRailMesh(dir);

            // Material
            var mr = ghost.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (currentMode == PlacerMode.Bulldoze)
                    mr.material = bulldozeMaterial != null
                        ? bulldozeMaterial
                        : RailMeshGenerator.GetOrCreateBulldozeMaterial();
                else
                    mr.material = ghostMaterial != null
                        ? ghostMaterial
                        : RailMeshGenerator.GetOrCreateGhostMaterial();
            }
        }

        // ── Commit placement ────────────────────────────────────────────────

        private void CommitPlacement()
        {
            if (RailManager.Instance == null) return;

            foreach (var (pos, dir) in plannedPath)
            {
                if (currentMode == PlacerMode.Build)
                    RailManager.Instance.AddRail(pos.x, pos.y, dir);
                else if (currentMode == PlacerMode.Bulldoze)
                    RailManager.Instance.RemoveRail(pos.x, pos.y);
            }

            plannedPath.Clear();
            PreviewCost = 0;
            OnPreviewCostChanged?.Invoke(0);
        }

        private void CancelDrag()
        {
            isDragging = false;
            ClearGhosts();
            plannedPath.Clear();
            PreviewCost = 0;
            OnPreviewCostChanged?.Invoke(0);
        }

        // ── Path calculation ────────────────────────────────────────────────

        /// <summary>
        /// Computes the list of tiles and directions for an OpenTTD-style auto-rail
        /// path from <paramref name="start"/> to <paramref name="end"/>.
        ///
        /// Priority:
        /// <list type="number">
        ///   <item>Horizontal or vertical straight line if start and end share an axis.</item>
        ///   <item>Otherwise an L-shaped path: one straight segment followed by a curve
        ///         into the perpendicular segment (matching OpenTTD's autorail behaviour).</item>
        /// </list>
        /// </summary>
        public static List<(Vector2Int pos, RailDirection dir)> CalculatePath(
            Vector2Int start, Vector2Int end)
        {
            var result = new List<(Vector2Int, RailDirection)>();
            if (start == end)
            {
                // Single tile
                result.Add((start, RailDirection.North_South));
                return result;
            }

            int dx = end.x - start.x;
            int dz = end.y - start.y; // y in Vector2Int maps to Z in 3D

            // ── Straight horizontal ──────────────────────────────────────
            if (dz == 0)
            {
                int stepX = dx > 0 ? 1 : -1;
                for (int x = start.x; x != end.x + stepX; x += stepX)
                    result.Add((new Vector2Int(x, start.y), RailDirection.East_West));
                return result;
            }

            // ── Straight vertical ────────────────────────────────────────
            if (dx == 0)
            {
                int stepZ = dz > 0 ? 1 : -1;
                for (int z = start.y; z != end.y + stepZ; z += stepZ)
                    result.Add((new Vector2Int(start.x, z), RailDirection.North_South));
                return result;
            }

            // ── L-shaped (OpenTTD autorail style) ────────────────────────
            // Strategy: lay the horizontal stretch first, then the vertical stretch,
            // putting the curve tile at the corner.
            //
            //  H   H   H   C
            //              V
            //              V

            int signX = dx > 0 ? 1 : -1;
            int signZ = dz > 0 ? 1 : -1;

            // Entry direction at corner coming from horizontal
            var entryH = new Vector2Int(signX, 0); // e.g. (+1, 0) = from west heading east
            // Exit direction at corner going into vertical
            var exitV  = new Vector2Int(0, signZ);  // e.g. (0, +1) = heading south

            RailDirection curveDir = RailManager.DirectionFromEntryExit(entryH, exitV);

            // Corner tile position
            var corner = new Vector2Int(end.x, start.y);

            // Horizontal segment (start → corner, exclusive of corner to avoid overlap)
            for (int x = start.x; x != end.x; x += signX)
                result.Add((new Vector2Int(x, start.y), RailDirection.East_West));

            // Corner curve
            result.Add((corner, curveDir));

            // Vertical segment (corner → end, exclusive of corner)
            for (int z = start.y + signZ; z != end.y + signZ; z += signZ)
                result.Add((new Vector2Int(end.x, z), RailDirection.North_South));

            return result;
        }

        // ── Ghost pool management ───────────────────────────────────────────

        private GameObject GetOrCreateGhost()
        {
            // Find an inactive ghost in the pool
            foreach (var g in ghostPool)
            {
                if (g != null && !activeGhosts.Contains(g))
                    return g;
            }

            // Create a new one
            var go = new GameObject("RailGhost");
            go.transform.SetParent(transform);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            ghostPool.Add(go);
            return go;
        }

        private void ClearGhosts()
        {
            foreach (var g in activeGhosts)
                if (g != null) g.SetActive(false);
            activeGhosts.Clear();
        }

        // ── Raycasting ──────────────────────────────────────────────────────

        private Vector2Int? GetTileUnderMouse()
        {
            if (mainCamera == null) return null;

            Ray ray = mainCamera.ScreenPointToRay(InputHelper.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, terrainLayer))
            {
                Vector3 worldPos = hit.point;
                if (GridManager.Instance != null)
                    return GridManager.Instance.WorldToGrid(worldPos);
                // Fallback: floor to int
                return new Vector2Int(Mathf.FloorToInt(worldPos.x),
                                      Mathf.FloorToInt(worldPos.z));
            }
            return null;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static int GetCostPerSegment()
        {
            // Reads the cost dynamically at runtime from EconomyManager/Constants
            return Constants.RailCostPerSegment;
        }
    }
}
