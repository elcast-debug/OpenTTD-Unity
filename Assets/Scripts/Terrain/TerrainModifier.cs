using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Provides raise/lower terrain tools.
    ///
    /// Workflow:
    ///   1. On each frame, cast a ray from the mouse to the terrain collider.
    ///   2. Show a visual hover highlight over the hovered tile.
    ///   3. Show a preview of what the tile would look like after modification.
    ///   4. On left-click (raise) or right-click (lower):
    ///      a. Check the player has enough money.
    ///      b. Modify the tile height in GridManager.
    ///      c. Call RegenerateMesh on all affected chunks.
    ///      d. Deduct the cost from EconomyManager.
    ///
    /// The hover highlight and preview are implemented with a simple quad
    /// GameObject that is moved and tinted each frame — no extra prefabs required.
    /// </summary>
    public class TerrainModifier : MonoBehaviour
    {
        // -------------------------------------------------------
        // Inspector fields
        // -------------------------------------------------------

        [Header("Tool Settings")]
        [SerializeField, Tooltip("Terrain modification mode.")]
        private ModifierMode _mode = ModifierMode.Raise;

        [SerializeField, Tooltip("Number of tiles modified in a single click (1 = single tile).")]
        [Range(1, 5)]
        private int _brushRadius = 0;

        [Header("Preview")]
        [SerializeField, Tooltip("Material for the hover highlight quad. Should be transparent/unlit.")]
        private Material _highlightMaterial;

        [SerializeField, Tooltip("Colour applied to the highlight quad when the tool is active.")]
        private Color _highlightColor = new Color(1f, 1f, 0f, 0.45f);

        [SerializeField, Tooltip("Colour applied when the player cannot afford the operation.")]
        private Color _cantAffordColor = new Color(1f, 0.2f, 0.2f, 0.45f);

        [Header("Raycast")]
        [SerializeField, Tooltip("LayerMask for terrain colliders. Must match the 'Terrain' layer.")]
        private LayerMask _terrainLayer = ~0; // everything by default

        [SerializeField, Tooltip("Maximum raycast distance.")]
        private float _raycastDistance = 500f;

        // -------------------------------------------------------
        // Events
        // -------------------------------------------------------

        /// <summary>Fired after one or more tiles are successfully modified.</summary>
        public event System.Action<IReadOnlyList<Vector2Int>> OnTerrainModified;

        // -------------------------------------------------------
        // Tool mode enum
        // -------------------------------------------------------

        public enum ModifierMode
        {
            Raise,
            Lower,
        }

        // -------------------------------------------------------
        // State
        // -------------------------------------------------------

        private Camera _camera;
        private bool   _isHovering;
        private Vector2Int _hoveredTile = new Vector2Int(-1, -1);

        // Hover highlight quad
        private GameObject _highlightObj;
        private MeshRenderer _highlightRenderer;
        private MaterialPropertyBlock _highlightMpb;

        // Chunk regeneration tracking
        private readonly HashSet<Vector2Int>        _dirtyChunks  = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, TerrainChunk> _chunkLookup = new Dictionary<Vector2Int, TerrainChunk>();

        // Whether the tool is currently enabled (set by UIManager)
        private bool _toolActive;

        // -------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            _camera = Camera.main;
            if (_camera == null)
                _camera = FindAnyObjectByType<Camera>();

            CreateHighlightQuad();
        }

        private void Start()
        {
            // Populate chunk lookup from all TerrainChunks in scene
            RebuildChunkLookup();
        }

        private void Update()
        {
            if (!_toolActive) return;

            PerformRaycast();
            UpdateHighlight();
            HandleInput();
        }

        private void OnDestroy()
        {
            if (_highlightObj != null)
                Destroy(_highlightObj);
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /// <summary>
        /// Enables or disables this tool. When disabled the highlight is hidden.
        /// Called by UIManager when the player switches build tools.
        /// </summary>
        public void SetToolActive(bool active)
        {
            _toolActive = active;
            if (_highlightObj != null)
                _highlightObj.SetActive(active);
            if (!active) _isHovering = false;
        }

        /// <summary>Sets the current mode (Raise / Lower).</summary>
        public void SetMode(ModifierMode mode) => _mode = mode;

        /// <summary>Sets the brush radius (0 = single tile, 1 = 3×3, etc.).</summary>
        public void SetBrushRadius(int radius) =>
            _brushRadius = Mathf.Clamp(radius, 0, 5);

        /// <summary>
        /// Registers all TerrainChunk instances for fast chunk lookup.
        /// Call this after TerrainChunks are spawned or destroyed.
        /// </summary>
        public void RebuildChunkLookup()
        {
            _chunkLookup.Clear();
            foreach (var chunk in FindObjectsByType<TerrainChunk>(FindObjectsSortMode.None))
            {
                _chunkLookup[new Vector2Int(chunk.ChunkX, chunk.ChunkZ)] = chunk;
            }
        }

        // -------------------------------------------------------
        // Raycast & hover
        // -------------------------------------------------------

        private void PerformRaycast()
        {
            Ray ray = _camera.ScreenPointToRay(InputHelper.mousePosition);
            _isHovering = false;
            _hoveredTile = new Vector2Int(-1, -1);

            if (Physics.Raycast(ray, out RaycastHit hit, _raycastDistance, _terrainLayer))
            {
                GridManager grid = GridManager.Instance;
                if (grid != null)
                {
                    Vector2Int coord = grid.WorldToGrid(hit.point);
                    if (coord.x >= 0)
                    {
                        _hoveredTile = coord;
                        _isHovering  = true;
                    }
                }
            }
        }

        private void UpdateHighlight()
        {
            if (_highlightObj == null) return;

            if (!_isHovering)
            {
                _highlightObj.SetActive(false);
                return;
            }

            GridManager grid = GridManager.Instance;
            if (grid == null) return;

            // Position the highlight just above the tile surface
            Vector3 worldPos = grid.GridToWorld(_hoveredTile.x, _hoveredTile.y);
            float   tileY    = grid.GetTile(_hoveredTile.x, _hoveredTile.y).Height * Constants.HeightStep;
            _highlightObj.transform.position = new Vector3(
                worldPos.x,
                tileY + 0.02f,  // tiny offset above surface to avoid z-fighting
                worldPos.z);

            float halfBrush = (_brushRadius + 0.5f) * Constants.TileSize;
            _highlightObj.transform.localScale = new Vector3(halfBrush * 2f, 1f, halfBrush * 2f);

            // Check affordability
            bool canAfford = CanAffordModification();
            Color targetColor = canAfford ? _highlightColor : _cantAffordColor;
            _highlightMpb.SetColor("_BaseColor", targetColor);
            _highlightMpb.SetColor("_Color",     targetColor);
            _highlightRenderer.SetPropertyBlock(_highlightMpb);

            _highlightObj.SetActive(true);
        }

        // -------------------------------------------------------
        // Input
        // -------------------------------------------------------

        private void HandleInput()
        {
            if (!_isHovering) return;

            bool raise = InputHelper.GetMouseButtonDown(0) && _mode == ModifierMode.Raise;
            bool lower = InputHelper.GetMouseButtonDown(1) || (InputHelper.GetMouseButtonDown(0) && _mode == ModifierMode.Lower);

            // Allow right-click to lower regardless of mode when hovering
            if (InputHelper.GetMouseButtonDown(1) && _mode == ModifierMode.Raise)
            {
                lower = true;
                raise = false;
            }

            if (!raise && !lower) return;

            if (!CanAffordModification())
            {
                Debug.Log("[TerrainModifier] Cannot afford terrain modification.");
                return;
            }

            int delta = raise ? 1 : -1;
            ApplyModification(delta);
        }

        // -------------------------------------------------------
        // Modification
        // -------------------------------------------------------

        private void ApplyModification(int heightDelta)
        {
            GridManager grid = GridManager.Instance;
            if (grid == null) return;

            var modifiedTiles = new List<Vector2Int>();
            _dirtyChunks.Clear();

            // Collect tiles within brush radius
            for (int dz = -_brushRadius; dz <= _brushRadius; dz++)
            {
                for (int dx = -_brushRadius; dx <= _brushRadius; dx++)
                {
                    if (dx * dx + dz * dz > _brushRadius * _brushRadius) continue;

                    int tx = _hoveredTile.x + dx;
                    int tz = _hoveredTile.y + dz;
                    if (!grid.IsValidCoord(tx, tz)) continue;

                    Tile tile   = grid.GetTile(tx, tz);
                    if (tile.HasRail || tile.HasBuilding) continue; // cannot modify occupied tiles

                    int newHeight = Mathf.Clamp(tile.Height + heightDelta,
                        Constants.MinHeight, Constants.MaxHeight);

                    if (newHeight == tile.Height) continue; // no change

                    grid.SetTileHeight(tx, tz, newHeight);

                    // Re-classify type if needed (water/land transition)
                    TileType newType = DetermineType(grid.GetTile(tx, tz));
                    if (newType != tile.Type)
                        grid.SetTileType(tx, tz, newType);

                    modifiedTiles.Add(new Vector2Int(tx, tz));

                    // Mark owning chunk dirty
                    Vector2Int chunk = grid.TileToChunk(tx, tz);
                    _dirtyChunks.Add(chunk);

                    // Also mark neighbour chunks dirty (skirt geometry crosses borders)
                    MarkNeighbourChunksDirty(grid, tx, tz, chunk);
                }
            }

            if (modifiedTiles.Count == 0) return;

            // Deduct cost
            long totalCost = (long)modifiedTiles.Count * Constants.TerrainModifyCost;
            EconomyManager.Instance?.Spend(totalCost, "Terrain modification");

            // Regenerate affected chunks
            foreach (Vector2Int chunkCoord in _dirtyChunks)
            {
                if (_chunkLookup.TryGetValue(chunkCoord, out TerrainChunk chunk))
                    chunk.RegenerateMesh();
            }

            OnTerrainModified?.Invoke(modifiedTiles);
        }

        private void MarkNeighbourChunksDirty(GridManager grid, int tx, int tz, Vector2Int owningChunk)
        {
            // If the tile is on a chunk border, mark the adjacent chunk too
            int localX = tx % Constants.ChunkSize;
            int localZ = tz % Constants.ChunkSize;

            if (localX == 0 && owningChunk.x > 0)
                _dirtyChunks.Add(new Vector2Int(owningChunk.x - 1, owningChunk.y));
            if (localX == Constants.ChunkSize - 1 && owningChunk.x < Constants.ChunksX - 1)
                _dirtyChunks.Add(new Vector2Int(owningChunk.x + 1, owningChunk.y));
            if (localZ == 0 && owningChunk.y > 0)
                _dirtyChunks.Add(new Vector2Int(owningChunk.x, owningChunk.y - 1));
            if (localZ == Constants.ChunkSize - 1 && owningChunk.y < Constants.ChunksZ - 1)
                _dirtyChunks.Add(new Vector2Int(owningChunk.x, owningChunk.y + 1));
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        private bool CanAffordModification()
        {
            if (EconomyManager.Instance == null) return true; // allow if no economy system yet
            int tileCount = GetBrushTileCount();
            long cost = (long)tileCount * Constants.TerrainModifyCost;
            return EconomyManager.Instance.CanAfford(cost);
        }

        private int GetBrushTileCount()
        {
            if (_brushRadius == 0) return 1;
            int count = 0;
            for (int dz = -_brushRadius; dz <= _brushRadius; dz++)
                for (int dx = -_brushRadius; dx <= _brushRadius; dx++)
                    if (dx * dx + dz * dz <= _brushRadius * _brushRadius) count++;
            return Mathf.Max(count, 1);
        }

        private TileType DetermineType(in Tile tile)
        {
            if (tile.HasRail)     return TileType.Rail;
            if (tile.HasBuilding) return TileType.Station; // simplified
            if (tile.Height <= Constants.WaterLevel) return TileType.Water;
            if (tile.Height <= Constants.WaterLevel + 1) return TileType.Sand;
            float heightFrac = (float)tile.Height / Constants.MaxHeight;
            if (heightFrac >= 0.80f) return TileType.Rock;
            return TileType.Grass;
        }

        // -------------------------------------------------------
        // Highlight quad creation
        // -------------------------------------------------------

        private void CreateHighlightQuad()
        {
            _highlightObj = new GameObject("TerrainModifier_Highlight");
            _highlightObj.transform.SetParent(transform);

            var mf = _highlightObj.AddComponent<MeshFilter>();
            _highlightRenderer = _highlightObj.AddComponent<MeshRenderer>();

            // Simple 1×1 XZ quad mesh
            var mesh = new Mesh();
            mesh.vertices  = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
            };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.uv        = new Vector2[]
            {
                new Vector2(0,0), new Vector2(0,1),
                new Vector2(1,1), new Vector2(1,0),
            };
            mesh.RecalculateNormals();
            mf.sharedMesh = mesh;

            // Use a default transparent material if none is assigned
            if (_highlightMaterial != null)
            {
                _highlightRenderer.sharedMaterial = _highlightMaterial;
            }
            else
            {
                // Create a simple fallback transparent unlit material
                _highlightMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                if (_highlightMaterial.shader == null || _highlightMaterial.shader.name == "Hidden/InternalErrorShader")
                    _highlightMaterial = new Material(Shader.Find("Sprites/Default"));

                _highlightMaterial.SetFloat("_Surface", 1f); // Transparent surface type for URP
                _highlightMaterial.renderQueue = 3000;
                _highlightRenderer.sharedMaterial = _highlightMaterial;
            }

            _highlightMpb = new MaterialPropertyBlock();
            _highlightObj.SetActive(false);
        }
    }
}
