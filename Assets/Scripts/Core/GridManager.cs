using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Singleton that owns the flat tile grid and exposes tile queries,
    /// mutation helpers, and world ↔ grid coordinate conversions.
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        // ── Singleton ───────────────────────────────────────────────────────

        /// <summary>Singleton instance.</summary>
        public static GridManager Instance { get; private set; }

        // ── Inspector fields ────────────────────────────────────────────────

        [SerializeField, Tooltip("Grid width in tiles")]
        private int width = Constants.GridWidth;

        [SerializeField, Tooltip("Grid depth (Z) in tiles")]
        private int height = Constants.GridHeight;

        [SerializeField, Tooltip("World-space size of one tile")]
        private float tileSize = Constants.TileSize;

        // ── Internal grid ───────────────────────────────────────────────────

        private Tile[] tiles;

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>
        /// Fired when one or more tiles have been modified.
        /// Listeners receive the list of changed (x, z) coords.
        /// </summary>
        public event Action<List<Vector2Int>> OnTilesChanged;

        // ── Properties ──────────────────────────────────────────────────────

        /// <summary>Grid width in tiles.</summary>
        public int Width  => width;

        /// <summary>Grid depth (Z) in tiles.</summary>
        public int Height => height;

        /// <summary>World-space tile size.</summary>
        public float TileSize => tileSize;

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            InitialiseGrid();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Initialisation ──────────────────────────────────────────────────

        /// <summary>Creates the flat tile array.</summary>
        public void InitialiseGrid()
        {
            tiles = new Tile[width * height];
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width;  x++)
                tiles[z * width + x] = new Tile(x, z);
        }

        // ── Core queries ────────────────────────────────────────────────────

        /// <summary>Returns the tile at (x, z) or null if out of bounds.</summary>
        public Tile GetTile(int x, int z)
        {
            if (!IsValidCoord(x, z)) return null;
            return tiles[z * width + x];
        }

        /// <summary>Flat-index accessor.</summary>
        public Tile GetTileByIndex(int index)
        {
            if (index < 0 || index >= tiles.Length) return null;
            return tiles[index];
        }

        /// <summary>Returns true if (x, z) is within grid bounds.</summary>
        public bool IsValidCoord(int x, int z) =>
            x >= 0 && x < width && z >= 0 && z < height;

        /// <summary>Alias kept for compat.</summary>
        public bool IsInBounds(int x, int z) => IsValidCoord(x, z);

        /// <summary>Total tile count.</summary>
        public int TileCount => width * height;

        // ── Coordinate conversions ──────────────────────────────────────────

        /// <summary>Converts a grid position to the world-space centre of that tile.</summary>
        public Vector3 GridToWorld(int x, int z)
        {
            float worldY = 0f;
            var tile = GetTile(x, z);
            if (tile != null)
                worldY = tile.Height * Constants.HeightStep;

            return new Vector3(
                x * tileSize + tileSize * 0.5f,
                worldY,
                z * tileSize + tileSize * 0.5f);
        }

        /// <summary>Converts a world-space position to the nearest grid (x, z).</summary>
        public Vector2Int WorldToGrid(Vector3 worldPos) =>
            new Vector2Int(
                Mathf.FloorToInt(worldPos.x / tileSize),
                Mathf.FloorToInt(worldPos.z / tileSize));

        /// <summary>Returns the chunk coordinate for a tile position.</summary>
        public Vector2Int TileToChunk(int tileX, int tileZ) =>
            new Vector2Int(tileX / Constants.ChunkSize, tileZ / Constants.ChunkSize);

        // ── Neighbour queries ───────────────────────────────────────────────

        private static readonly Vector2Int[] Dir4 =
        {
            new Vector2Int( 0,  1), // North (+Z)
            new Vector2Int( 1,  0), // East  (+X)
            new Vector2Int( 0, -1), // South (-Z)
            new Vector2Int(-1,  0), // West  (-X)
        };

        private static readonly Vector2Int[] Dir8 =
        {
            new Vector2Int( 0,  1),
            new Vector2Int( 1,  1),
            new Vector2Int( 1,  0),
            new Vector2Int( 1, -1),
            new Vector2Int( 0, -1),
            new Vector2Int(-1, -1),
            new Vector2Int(-1,  0),
            new Vector2Int(-1,  1),
        };

        /// <summary>Returns 4-direction adjacent tiles (non-null, in-bounds only).</summary>
        public List<Tile> GetNeighbors(int x, int z)
        {
            var result = new List<Tile>(4);
            foreach (var d in Dir4)
            {
                var t = GetTile(x + d.x, z + d.y);
                if (t != null) result.Add(t);
            }
            return result;
        }

        /// <summary>Returns 8-direction adjacent tiles (non-null, in-bounds only).</summary>
        public List<Tile> GetNeighbors8(int x, int z)
        {
            var result = new List<Tile>(8);
            foreach (var d in Dir8)
            {
                var t = GetTile(x + d.x, z + d.y);
                if (t != null) result.Add(t);
            }
            return result;
        }

        // ── Mutators ────────────────────────────────────────────────────────

        /// <summary>Sets the height of a single tile and fires event.</summary>
        public void SetTileHeight(int x, int z, int h)
        {
            var tile = GetTile(x, z);
            if (tile == null) return;
            tile.Height = Mathf.Clamp(h, Constants.MinHeight, Constants.MaxHeight);
            OnTilesChanged?.Invoke(new List<Vector2Int> { new Vector2Int(x, z) });
        }

        /// <summary>Sets the type of a single tile and fires event.</summary>
        public void SetTileType(int x, int z, TileType type)
        {
            var tile = GetTile(x, z);
            if (tile == null) return;
            tile.Type = type;
            OnTilesChanged?.Invoke(new List<Vector2Int> { new Vector2Int(x, z) });
        }

        /// <summary>Bulk-set heights without firing per-tile events. Call NotifyTilesChanged after.</summary>
        public void BulkSetHeights(int[] xCoords, int[] zCoords, int[] heights)
        {
            int count = Mathf.Min(xCoords.Length, Mathf.Min(zCoords.Length, heights.Length));
            for (int i = 0; i < count; i++)
            {
                var tile = GetTile(xCoords[i], zCoords[i]);
                if (tile != null)
                    tile.Height = Mathf.Clamp(heights[i], Constants.MinHeight, Constants.MaxHeight);
            }
        }

        /// <summary>Bulk-set types without firing per-tile events.</summary>
        public void BulkSetTypes(int[] xCoords, int[] zCoords, TileType[] types)
        {
            int count = Mathf.Min(xCoords.Length, Mathf.Min(zCoords.Length, types.Length));
            for (int i = 0; i < count; i++)
            {
                var tile = GetTile(xCoords[i], zCoords[i]);
                if (tile != null)
                    tile.Type = types[i];
            }
        }

        /// <summary>Fire the changed event manually after bulk operations.</summary>
        public void NotifyTilesChanged(List<Vector2Int> changedCoords)
        {
            OnTilesChanged?.Invoke(changedCoords);
        }

        // ── Rail / Station / Building setters ───────────────────────────────

        /// <summary>Registers a rail segment on the given tile.</summary>
        public void SetRailOnTile(int x, int z, RailSegment segment)
        {
            var tile = GetTile(x, z);
            if (tile != null)
            {
                tile.Rail = segment;
                tile.Type = TileType.Rail;
            }
        }

        /// <summary>Clears the rail reference on the given tile.</summary>
        public void ClearRailOnTile(int x, int z)
        {
            var tile = GetTile(x, z);
            if (tile != null)
            {
                tile.Rail = null;
                if (tile.Type == TileType.Rail)
                    tile.Type = TileType.Grass;
            }
        }

        /// <summary>Registers a station on the given tile.</summary>
        public void SetStationOnTile(int x, int z, Station station)
        {
            var tile = GetTile(x, z);
            if (tile != null)
            {
                tile.Station = station;
                tile.Type = TileType.Station;
            }
        }

        /// <summary>Clears the station reference on the given tile.</summary>
        public void ClearStationOnTile(int x, int z)
        {
            var tile = GetTile(x, z);
            if (tile != null)
            {
                tile.Station = null;
                if (tile.Type == TileType.Station)
                    tile.Type = TileType.Grass;
            }
        }

        /// <summary>Sets a building/industry reference on a tile.</summary>
        public void SetBuildingOnTile(int x, int z, Industry building)
        {
            var tile = GetTile(x, z);
            if (tile != null)
                tile.Building = building;
        }
    }
}
