using UnityEngine;

namespace OpenTTDUnity
{
    // ── Tile type ───────────────────────────────────────────────────────────

    /// <summary>Visual / logical type of a tile.</summary>
    public enum TileType
    {
        Grass,
        Water,
        Sand,
        Rock,
        Rail,
        Station
    }

    /// <summary>Kept for backward-compat with any code referencing TerrainType.</summary>
    public enum TerrainType
    {
        Grass   = TileType.Grass,
        Water   = TileType.Water,
        Sand    = TileType.Sand,
        Rock    = TileType.Rock,
    }

    // ── Neighbour flags (bitmask) ───────────────────────────────────────────

    /// <summary>Bitmask for which neighbours exist (4-direction).</summary>
    [System.Flags]
    public enum TileNeighborFlags
    {
        None  = 0,
        North = 1 << 0,
        East  = 1 << 1,
        South = 1 << 2,
        West  = 1 << 3,
    }

    // ── Tile data ───────────────────────────────────────────────────────────

    /// <summary>
    /// Data container for a single tile in the world grid.
    /// Stored by <see cref="GridManager"/> in a flat array.
    /// Uses class (not struct) so references can be shared/mutated by terrain,
    /// rail, station, and industry systems.
    /// </summary>
    [System.Serializable]
    public class Tile
    {
        /// <summary>Grid X coordinate.</summary>
        public int X;

        /// <summary>Grid Z coordinate (depth).</summary>
        public int Z;

        /// <summary>Terrain height level (0–15).</summary>
        public int Height;

        /// <summary>Visual / logical tile type.</summary>
        public TileType Type = TileType.Grass;

        /// <summary>Legacy accessor — maps to Type for terrain systems.</summary>
        public TerrainType Terrain
        {
            get => (TerrainType)(int)Type;
            set => Type = (TileType)(int)value;
        }

        /// <summary>Rail segment occupying this tile (null if none).</summary>
        public RailSegment Rail;

        /// <summary>Station occupying this tile (null if none).</summary>
        public Station Station;

        /// <summary>Industry occupying this tile (null if none).</summary>
        public Industry Building;

        // ── Computed properties ──────────────────────────────────────────

        /// <summary>True if a rail segment is placed here.</summary>
        public bool HasRail => Rail != null;

        /// <summary>True if a station is placed here.</summary>
        public bool HasStation => Station != null;

        /// <summary>True if an industry/building is placed here.</summary>
        public bool HasBuilding => Building != null;

        /// <summary>True if the tile has no rail, station, or building.</summary>
        public bool IsBuildable => !HasRail && !HasStation && !HasBuilding && Type != TileType.Water;

        /// <summary>World-space Y position of this tile's top surface.</summary>
        public float WorldY => Height * Constants.HeightStep;

        /// <summary>Grid coordinate as Vector2Int.</summary>
        public Vector2Int GridPos => new Vector2Int(X, Z);

        // ── Constructors ─────────────────────────────────────────────────

        public Tile() { }

        public Tile(int x, int z, int height = 0, TileType type = TileType.Grass)
        {
            X      = x;
            Z      = z;
            Height = height;
            Type   = type;
        }

        /// <summary>Overload accepting TerrainType for backward compat.</summary>
        public Tile(int x, int z, int height, TerrainType terrain)
            : this(x, z, height, (TileType)(int)terrain) { }

        // ── Factory ──────────────────────────────────────────────────────

        /// <summary>Creates a default grass tile at the given position.</summary>
        public static Tile Create(int x, int z) => new Tile(x, z);

        /// <summary>Human-readable representation.</summary>
        public override string ToString() =>
            $"Tile({X},{Z}) h={Height} type={Type}";
    }
}
