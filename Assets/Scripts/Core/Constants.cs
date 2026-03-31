namespace OpenTTDUnity
{
    /// <summary>
    /// Game-wide compile-time and runtime constants used by every system.
    /// </summary>
    public static class Constants
    {
        // ── Grid ────────────────────────────────────────────────────────────

        /// <summary>Default grid width in tiles.</summary>
        public const int GridSize   = 128;
        public const int GridWidth  = GridSize;
        public const int GridHeight = GridSize;

        /// <summary>Chunk size in tiles (chunk-based mesh rendering).</summary>
        public const int ChunkSize = 16;

        /// <summary>Number of chunks along X axis.</summary>
        public const int ChunksX = GridWidth  / ChunkSize;

        /// <summary>Number of chunks along Z axis.</summary>
        public const int ChunksZ = GridHeight / ChunkSize;

        /// <summary>World-space size of one tile (all axes).</summary>
        public const float TileSize = 1f;

        // ── Height ──────────────────────────────────────────────────────────

        /// <summary>Minimum terrain height level.</summary>
        public const int MinHeight = 0;

        /// <summary>Maximum terrain height level.</summary>
        public const int MaxHeight = 15;

        /// <summary>World-space vertical offset per height level.</summary>
        public const float HeightStep = 0.5f;

        /// <summary>Height threshold for water tiles.</summary>
        public const int WaterLevel = 2;

        // ── Economy ─────────────────────────────────────────────────────────

        /// <summary>Player starting balance.</summary>
        public const long StartingMoney = 100_000;

        /// <summary>Cost to place one rail segment.</summary>
        public const int RailCostPerSegment = 100;

        /// <summary>Cost to place one station.</summary>
        public const int StationCost = 500;

        /// <summary>Cost to purchase a basic train.</summary>
        public const int TrainPurchaseCost = 5_000;

        /// <summary>Cost per tile of terrain modification (raise / lower).</summary>
        public const int TerrainModifyCost = 50;

        // ── Trains ──────────────────────────────────────────────────────────

        /// <summary>Default train speed in tiles per second.</summary>
        public const float DefaultTrainSpeed = 3f;

        /// <summary>Default cargo capacity in units.</summary>
        public const int DefaultCargoCapacity = 40;

        // ── Camera ──────────────────────────────────────────────────────────

        /// <summary>Default camera pan speed.</summary>
        public const float CameraPanSpeed = 20f;

        /// <summary>Camera zoom (scroll) speed.</summary>
        public const float CameraZoomSpeed = 5f;

        /// <summary>Minimum orthographic size (closest zoom).</summary>
        public const float CameraMinOrthoSize = 4f;

        /// <summary>Maximum orthographic size (farthest zoom).</summary>
        public const float CameraMaxOrthoSize = 60f;

        /// <summary>Starting orthographic size.</summary>
        public const float CameraDefaultOrthoSize = 20f;

        // ── Date / Time ─────────────────────────────────────────────────────

        /// <summary>Starting year.</summary>
        public const int StartYear  = 1950;

        /// <summary>Starting month (1-12).</summary>
        public const int StartMonth = 1;

        /// <summary>Starting day (1-31).</summary>
        public const int StartDay   = 1;

        /// <summary>Real-time seconds per in-game day at 1× speed.</summary>
        public const float SecondsPerDay = 2f;

        // ── Terrain Noise ───────────────────────────────────────────────────

        /// <summary>Perlin noise scale for terrain generation.</summary>
        public const float NoiseScale = 0.04f;

        /// <summary>Number of Perlin noise octaves.</summary>
        public const int NoiseOctaves = 4;

        /// <summary>Perlin noise persistence.</summary>
        public const float NoisePersistence = 0.45f;

        /// <summary>Perlin noise lacunarity.</summary>
        public const float NoiseLacunarity = 2.2f;

        // ── Industry ────────────────────────────────────────────────────────

        /// <summary>Minimum tile distance between industries of the same type.</summary>
        public const int MinIndustrySpacing = 12;

        // ── Layer Names ─────────────────────────────────────────────────────

        public const string LayerTerrain  = "Terrain";
        public const string LayerRail     = "Rail";
        public const string LayerBuilding = "Building";
        public const string LayerUI       = "UI";
    }
}
