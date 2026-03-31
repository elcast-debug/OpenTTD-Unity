using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Generates and manages the procedural mesh for one 16×16 chunk of terrain.
    ///
    /// Mesh strategy — "stepped" look (classic TTD):
    ///   Each tile is a flat quad whose Y value matches the tile's height level.
    ///   Vertical "skirt" quads fill the gaps between adjacent tiles of different
    ///   heights so the mesh is always closed (no cracks or holes).
    ///
    /// Vertex colours are computed per-vertex from height and tile type so the
    /// terrain looks reasonable with no textures at all.  UV coordinates are
    /// also set for a simple 1×6 horizontal texture atlas (one row per TileType).
    ///
    /// Call <see cref="Initialize"/> once, then <see cref="RegenerateMesh"/>
    /// whenever tile data in this chunk changes.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class TerrainChunk : MonoBehaviour
    {
        // -------------------------------------------------------
        // Inspector fields
        // -------------------------------------------------------

        [SerializeField, Tooltip("Material applied to the chunk mesh. Vertex colours work without any texture.")]
        private Material _material;

        // -------------------------------------------------------
        // Chunk identity
        // -------------------------------------------------------

        /// <summary>Chunk X index (column), set by TerrainManager on spawn.</summary>
        public int ChunkX { get; private set; }

        /// <summary>Chunk Z index (row), set by TerrainManager on spawn.</summary>
        public int ChunkZ { get; private set; }

        /// <summary>Tile X of this chunk's origin (top-left corner).</summary>
        public int OriginTileX => ChunkX * Constants.ChunkSize;

        /// <summary>Tile Z of this chunk's origin.</summary>
        public int OriginTileZ => ChunkZ * Constants.ChunkSize;

        // -------------------------------------------------------
        // Component references (set in Initialize)
        // -------------------------------------------------------

        private MeshFilter   _filter;
        private MeshRenderer _renderer;
        private MeshCollider _collider;
        private Mesh         _mesh;

        // -------------------------------------------------------
        // Vertex colour palette
        // -------------------------------------------------------

        // Deep water — dark blue
        private static readonly Color32 ColorWaterDeep   = new Color32(20,  80, 160, 255);
        // Shallow water — lighter blue
        private static readonly Color32 ColorWaterShallow = new Color32(50, 120, 200, 255);
        // Sand / beach
        private static readonly Color32 ColorSand        = new Color32(210, 190, 120, 255);
        // Low grass
        private static readonly Color32 ColorGrassLow    = new Color32( 80, 160,  60, 255);
        // Mid grass
        private static readonly Color32 ColorGrassMid    = new Color32( 60, 130,  45, 255);
        // High grass / shrubs
        private static readonly Color32 ColorGrassHigh   = new Color32( 90, 110,  50, 255);
        // Rock / cliff
        private static readonly Color32 ColorRock        = new Color32(130, 120, 110, 255);
        // Rail (dark grey with slight blue)
        private static readonly Color32 ColorRail        = new Color32( 70,  70,  80, 255);
        // Station platform (light grey)
        private static readonly Color32 ColorStation     = new Color32(190, 185, 180, 255);

        // -------------------------------------------------------
        // UV atlas layout
        // -------------------------------------------------------
        // The atlas is a 1-wide, N-tile-type-tall strip.
        // Row index matches (int)TileType.
        private const int AtlasRows = 6; // matches TileType count

        // -------------------------------------------------------
        // Initialization
        // -------------------------------------------------------

        /// <summary>
        /// Sets up component references and creates an empty mesh.
        /// Must be called once before <see cref="RegenerateMesh"/>.
        /// </summary>
        public void Initialize(int chunkX, int chunkZ, Material material = null)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;

            _filter   = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            _collider = GetComponent<MeshCollider>();

            _mesh = new Mesh();
            _mesh.name = $"TerrainChunk_{chunkX}_{chunkZ}";
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // supports > 65k verts
            _filter.sharedMesh = _mesh;

            if (material != null) _material = material;
            if (_material != null) _renderer.sharedMaterial = _material;

            // Set world position of the chunk's origin corner
            transform.position = new Vector3(
                OriginTileX * Constants.TileSize,
                0f,
                OriginTileZ * Constants.TileSize);
        }

        // -------------------------------------------------------
        // Mesh generation
        // -------------------------------------------------------

        /// <summary>
        /// Rebuilds the chunk mesh from the current tile data in
        /// <see cref="GridManager"/>.  Call this whenever any tile
        /// within this chunk (or on its border) changes height or type.
        /// </summary>
        public void RegenerateMesh()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null || _mesh == null)
            {
                Debug.LogError("[TerrainChunk] Cannot regenerate mesh — GridManager or Mesh is null.");
                return;
            }

            // Pre-allocate lists with generous capacity to avoid resizing
            // Each tile can contribute up to 1 top quad + 4 side quads = 5 quads = 10 tris = 30 verts
            int capacity = Constants.ChunkSize * Constants.ChunkSize * 30;
            var verts    = new List<Vector3>(capacity);
            var tris     = new List<int>(capacity);
            var uvs      = new List<Vector2>(capacity);
            var colors   = new List<Color32>(capacity);

            int ox = OriginTileX;
            int oz = OriginTileZ;

            for (int localZ = 0; localZ < Constants.ChunkSize; localZ++)
            {
                for (int localX = 0; localX < Constants.ChunkSize; localX++)
                {
                    int worldTileX = ox + localX;
                    int worldTileZ = oz + localZ;

                    if (!grid.IsValidCoord(worldTileX, worldTileZ)) continue;

                    Tile tile      = grid.GetTile(worldTileX, worldTileZ);
                    float tileY    = tile.Height * Constants.HeightStep;
                    Color32 topCol = GetVertexColor(tile);

                    // ---- Top face (2 triangles, 4 vertices) ----
                    // Quad corners in local chunk space:
                    //  v0 ------ v1
                    //  |  \  tri1 |
                    //  | tri0 \  |
                    //  v2 ------ v3
                    //
                    //  v0 = (x,   z+1), v1 = (x+1, z+1)
                    //  v2 = (x,   z  ), v3 = (x+1, z  )

                    float lx = localX * Constants.TileSize;
                    float lz = localZ * Constants.TileSize;
                    float s  = Constants.TileSize;

                    int baseIdx = verts.Count;

                    verts.Add(new Vector3(lx,     tileY, lz + s)); // v0
                    verts.Add(new Vector3(lx + s, tileY, lz + s)); // v1
                    verts.Add(new Vector3(lx,     tileY, lz));     // v2
                    verts.Add(new Vector3(lx + s, tileY, lz));     // v3

                    // UV atlas row for this tile type
                    float uvRow    = (int)tile.Type;
                    float uvBottom = uvRow / AtlasRows;
                    float uvTop    = (uvRow + 1f) / AtlasRows;

                    uvs.Add(new Vector2(0f, uvTop));
                    uvs.Add(new Vector2(1f, uvTop));
                    uvs.Add(new Vector2(0f, uvBottom));
                    uvs.Add(new Vector2(1f, uvBottom));

                    colors.Add(topCol);
                    colors.Add(topCol);
                    colors.Add(topCol);
                    colors.Add(topCol);

                    // Two triangles for the top quad (CW winding = Unity default)
                    tris.Add(baseIdx);     // v0
                    tris.Add(baseIdx + 1); // v1
                    tris.Add(baseIdx + 2); // v2

                    tris.Add(baseIdx + 1); // v1
                    tris.Add(baseIdx + 3); // v3
                    tris.Add(baseIdx + 2); // v2

                    // ---- Side skirts ----
                    // Check all 4 neighbours; if they are lower, add a vertical wall
                    // facing outward so there are no gaps between height levels.

                    AddSkirtsIfNeeded(
                        grid, tile, tileY, topCol,
                        localX, localZ, worldTileX, worldTileZ,
                        verts, tris, uvs, colors);
                }
            }

            // Apply to mesh
            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.SetUVs(0, uvs);
            _mesh.SetColors(colors);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            _mesh.Optimize();

            // Update collider
            _collider.sharedMesh = null; // force refresh
            _collider.sharedMesh = _mesh;
        }

        // -------------------------------------------------------
        // Skirt generation
        // -------------------------------------------------------

        private void AddSkirtsIfNeeded(
            GridManager grid,
            Tile tile,
            float tileY,
            Color32 topCol,
            int localX, int localZ,
            int worldX, int worldZ,
            List<Vector3> verts,
            List<int> tris,
            List<Vector2> uvs,
            List<Color32> colors)
        {
            float s = Constants.TileSize;
            float lx = localX * s;
            float lz = localZ * s;

            // Slightly darker colour for sides to fake ambient occlusion
            Color32 sideCol = Darken(topCol, 0.65f);

            // North (+Z) neighbour
            if (grid.IsValidCoord(worldX, worldZ + 1))
            {
                Tile n = grid.GetTile(worldX, worldZ + 1);
                if (n.Height < tile.Height)
                    AddSkirtFace(verts, tris, uvs, colors,
                        new Vector3(lx + s, tileY, lz + s),     // top-right
                        new Vector3(lx,     tileY, lz + s),     // top-left
                        n.Height, sideCol, tile.Type);
            }

            // South (-Z) neighbour
            if (grid.IsValidCoord(worldX, worldZ - 1))
            {
                Tile n = grid.GetTile(worldX, worldZ - 1);
                if (n.Height < tile.Height)
                    AddSkirtFace(verts, tris, uvs, colors,
                        new Vector3(lx,     tileY, lz),         // top-left
                        new Vector3(lx + s, tileY, lz),         // top-right
                        n.Height, sideCol, tile.Type);
            }

            // East (+X) neighbour
            if (grid.IsValidCoord(worldX + 1, worldZ))
            {
                Tile n = grid.GetTile(worldX + 1, worldZ);
                if (n.Height < tile.Height)
                    AddSkirtFace(verts, tris, uvs, colors,
                        new Vector3(lx + s, tileY, lz + s),     // top-left
                        new Vector3(lx + s, tileY, lz),         // top-right
                        n.Height, sideCol, tile.Type);
            }

            // West (-X) neighbour
            if (grid.IsValidCoord(worldX - 1, worldZ))
            {
                Tile n = grid.GetTile(worldX - 1, worldZ);
                if (n.Height < tile.Height)
                    AddSkirtFace(verts, tris, uvs, colors,
                        new Vector3(lx, tileY, lz),             // top-left
                        new Vector3(lx, tileY, lz + s),         // top-right
                        n.Height, sideCol, tile.Type);
            }

            // Edge of map — add a skirt down to height 0 so the map has visible sides
            float edgeBottom = 0f;

            if (worldX == 0)
                AddSkirtFace(verts, tris, uvs, colors,
                    new Vector3(lx, tileY, lz + s),
                    new Vector3(lx, tileY, lz),
                    0, sideCol, tile.Type, overrideBottom: edgeBottom);

            if (worldX == grid.Width - 1)
                AddSkirtFace(verts, tris, uvs, colors,
                    new Vector3(lx + s, tileY, lz),
                    new Vector3(lx + s, tileY, lz + s),
                    0, sideCol, tile.Type, overrideBottom: edgeBottom);

            if (worldZ == 0)
                AddSkirtFace(verts, tris, uvs, colors,
                    new Vector3(lx + s, tileY, lz),
                    new Vector3(lx, tileY, lz),
                    0, sideCol, tile.Type, overrideBottom: edgeBottom);

            if (worldZ == grid.Height - 1)
                AddSkirtFace(verts, tris, uvs, colors,
                    new Vector3(lx, tileY, lz + s),
                    new Vector3(lx + s, tileY, lz + s),
                    0, sideCol, tile.Type, overrideBottom: edgeBottom);
        }

        /// <summary>
        /// Adds one rectangular skirt face (2 triangles, 4 vertices).
        /// The face runs from <paramref name="topLeft"/> to <paramref name="topRight"/>
        /// at <paramref name="tileY"/> and drops to <paramref name="neighbourHeight"/>
        /// (or <paramref name="overrideBottom"/> if &gt;= 0).
        /// </summary>
        private static void AddSkirtFace(
            List<Vector3> verts, List<int> tris,
            List<Vector2> uvs,   List<Color32> colors,
            Vector3 topLeft, Vector3 topRight,
            int neighbourHeight,
            Color32 col,
            TileType type,
            float overrideBottom = -1f)
        {
            float bottomY = overrideBottom >= 0f
                ? overrideBottom
                : neighbourHeight * Constants.HeightStep;

            if (topLeft.y <= bottomY) return; // nothing to fill

            int baseIdx = verts.Count;

            Vector3 botLeft  = new Vector3(topLeft.x,  bottomY, topLeft.z);
            Vector3 botRight = new Vector3(topRight.x, bottomY, topRight.z);

            verts.Add(topLeft);   // 0
            verts.Add(topRight);  // 1
            verts.Add(botLeft);   // 2
            verts.Add(botRight);  // 3

            // UVs: map the height drop to V
            float uvRow    = (int)type;
            float uvBottom = uvRow / AtlasRows;
            float uvTop    = (uvRow + 1f) / AtlasRows;

            uvs.Add(new Vector2(0f, uvTop));
            uvs.Add(new Vector2(1f, uvTop));
            uvs.Add(new Vector2(0f, uvBottom));
            uvs.Add(new Vector2(1f, uvBottom));

            colors.Add(col);
            colors.Add(col);
            colors.Add(col);
            colors.Add(col);

            tris.Add(baseIdx);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 2);

            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 3);
            tris.Add(baseIdx + 2);
        }

        // -------------------------------------------------------
        // Vertex colour helpers
        // -------------------------------------------------------

        private Color32 GetVertexColor(in Tile tile)
        {
            switch (tile.Type)
            {
                case TileType.Water:
                {
                    float t = Mathf.InverseLerp(0f, Constants.WaterLevel, tile.Height);
                    return Color32.Lerp(ColorWaterDeep, ColorWaterShallow, t);
                }
                case TileType.Sand:    return ColorSand;
                case TileType.Rock:    return ColorRock;
                case TileType.Rail:    return ColorRail;
                case TileType.Station: return ColorStation;
                default: // Grass
                {
                    float t = Mathf.InverseLerp(0f, Constants.MaxHeight, tile.Height);
                    if (t < 0.33f) return Color32.Lerp(ColorGrassLow, ColorGrassMid,  t / 0.33f);
                    if (t < 0.66f) return Color32.Lerp(ColorGrassMid, ColorGrassHigh, (t - 0.33f) / 0.33f);
                    return ColorRock; // very high grass becomes rocky
                }
            }
        }

        private static Color32 Darken(Color32 c, float factor)
        {
            return new Color32(
                (byte)(c.r * factor),
                (byte)(c.g * factor),
                (byte)(c.b * factor),
                c.a);
        }
    }
}
