using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Generates initial terrain height values using fractional Brownian motion
    /// (fBm) built on top of Unity's <see cref="Mathf.PerlinNoise"/>.
    ///
    /// After generating heights this component:
    ///   1. Applies them to <see cref="GridManager"/> in bulk.
    ///   2. Classifies tile types (Water, Sand, Grass, Rock) based on height.
    ///   3. Flattens candidate areas for industry placement and records them.
    ///
    /// Call <see cref="Generate"/> once per new game from <see cref="GameManager"/>.
    /// </summary>
    public class TerrainGenerator : MonoBehaviour
    {
        // -------------------------------------------------------
        // Inspector fields
        // -------------------------------------------------------

        [Header("Noise Settings")]
        [SerializeField, Tooltip("Random seed. 0 = use random seed.")]
        private int _seed = 0;

        [SerializeField, Tooltip("Base noise frequency. Smaller = smoother/flatter terrain.")]
        private float _scale = Constants.NoiseScale;

        [SerializeField, Tooltip("Number of noise octaves for fractal detail.")]
        [Range(1, 8)]
        private int _octaves = Constants.NoiseOctaves;

        [SerializeField, Tooltip("Amplitude multiplier per octave (0–1).")]
        [Range(0f, 1f)]
        private float _persistence = Constants.NoisePersistence;

        [SerializeField, Tooltip("Frequency multiplier per octave.")]
        [Range(1f, 4f)]
        private float _lacunarity = Constants.NoiseLacunarity;

        [Header("Height Distribution")]
        [SerializeField, Tooltip("Exponent applied to the 0-1 noise value. >1 = more flat land; <1 = more mountains.")]
        [Range(0.5f, 3f)]
        private float _heightCurveExponent = 1.5f;

        [Header("Water / Shore")]
        [SerializeField, Tooltip("Noise value at or below which the tile becomes water.")]
        [Range(0f, 0.5f)]
        private float _waterThreshold = 0.30f;

        [SerializeField, Tooltip("Noise value at or below which coastal sand appears (must be > waterThreshold).")]
        [Range(0.1f, 0.6f)]
        private float _sandThreshold = 0.36f;

        [Header("Rock")]
        [SerializeField, Tooltip("Normalised height at or above which the surface is rock.")]
        [Range(0.6f, 1f)]
        private float _rockHeightFraction = 0.80f;

        [Header("Industry Flats")]
        [SerializeField, Tooltip("How many flat areas to carve for industry placement.")]
        [Range(0, 20)]
        private int _industryFlatCount = 8;

        [SerializeField, Tooltip("Radius (tiles) of each flattened area.")]
        [Range(1, 6)]
        private int _flatRadius = 3;

        // -------------------------------------------------------
        // Output
        // -------------------------------------------------------

        /// <summary>
        /// After <see cref="Generate"/>, contains the world positions of industry-ready
        /// flat tiles (one per flattened zone), for <see cref="IndustryManager"/> to use.
        /// </summary>
        public List<Vector2Int> IndustrySpawnPoints { get; private set; } = new List<Vector2Int>();

        // -------------------------------------------------------
        // State
        // -------------------------------------------------------

        private int _resolvedSeed;
        private float _offsetX;
        private float _offsetZ;

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /// <summary>
        /// Generates terrain heights and types, applies them to the
        /// <see cref="GridManager"/>, and records industry spawn points.
        /// Must be called after <see cref="GridManager.Initialize"/>.
        /// </summary>
        public void Generate()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null)
            {
                Debug.LogError("[TerrainGenerator] GridManager.Instance is null. Cannot generate terrain.");
                return;
            }

            ResolveRandomSeed();

            int w = grid.Width;
            int h = grid.Height;

            // Step 1 — build normalised noise map (0…1)
            float[,] noiseMap  = BuildNoiseMap(w, h);

            // Step 2 — convert to integer heights
            int[,] heights = NoiseToHeights(noiseMap, w, h);

            // Step 3 — flatten industry locations BEFORE applying to grid
            IndustrySpawnPoints.Clear();
            FlattenIndustryAreas(heights, noiseMap, w, h);

            // Step 4 — push heights to GridManager in bulk
            grid.BulkSetHeights(heights);

            // Step 5 — classify tile types based on height + noise thresholds
            grid.BulkSetTypes(tile =>
            {
                float n = noiseMap[tile.X, tile.Z];
                if (n <= _waterThreshold) return TileType.Water;
                if (n <= _sandThreshold)  return TileType.Sand;

                float heightFrac = (float)tile.Height / Constants.MaxHeight;
                if (heightFrac >= _rockHeightFraction) return TileType.Rock;
                return TileType.Grass;
            });

            Debug.Log($"[TerrainGenerator] Terrain generated. Seed={_resolvedSeed}, " +
                      $"Industry flats={IndustrySpawnPoints.Count}.");
        }

        // -------------------------------------------------------
        // Seed
        // -------------------------------------------------------

        private void ResolveRandomSeed()
        {
            _resolvedSeed = _seed == 0 ? Random.Range(1, int.MaxValue) : _seed;
            // Use the seed to generate stable noise offsets
            Random.State prevState = Random.state;
            Random.InitState(_resolvedSeed);
            _offsetX = Random.Range(0f, 100000f);
            _offsetZ = Random.Range(0f, 100000f);
            Random.state = prevState;
        }

        // -------------------------------------------------------
        // Noise map
        // -------------------------------------------------------

        /// <summary>
        /// Builds a 2-D noise map using fBm (fractional Brownian motion).
        /// Returns values in [0, 1].
        /// </summary>
        private float[,] BuildNoiseMap(int w, int h)
        {
            float[,] map   = new float[w, h];
            float maxNoise = float.MinValue;
            float minNoise = float.MaxValue;

            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    float amplitude = 1f;
                    float frequency = 1f;
                    float value     = 0f;

                    for (int o = 0; o < _octaves; o++)
                    {
                        float sampleX = (_offsetX + x) * _scale * frequency;
                        float sampleZ = (_offsetZ + z) * _scale * frequency;
                        // PerlinNoise returns [0,1]; map to [-1,1] for signed sum
                        float p = Mathf.PerlinNoise(sampleX, sampleZ) * 2f - 1f;
                        value     += p * amplitude;
                        amplitude *= _persistence;
                        frequency *= _lacunarity;
                    }

                    map[x, z] = value;
                    if (value > maxNoise) maxNoise = value;
                    if (value < minNoise) minNoise = value;
                }
            }

            // Normalise to [0, 1]
            float range = maxNoise - minNoise;
            if (range < 0.0001f) range = 0.0001f;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                    map[x, z] = (map[x, z] - minNoise) / range;

            return map;
        }

        /// <summary>
        /// Maps normalised noise values to integer height levels.
        /// A power curve biases the distribution toward flat/low terrain.
        /// </summary>
        private int[,] NoiseToHeights(float[,] noiseMap, int w, int h)
        {
            int[,] heights = new int[w, h];
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    float n = noiseMap[x, z];
                    // Apply power curve
                    float curved = Mathf.Pow(n, _heightCurveExponent);
                    int height   = Mathf.RoundToInt(curved * Constants.MaxHeight);
                    heights[x, z] = Mathf.Clamp(height, Constants.MinHeight, Constants.MaxHeight);
                }
            }
            return heights;
        }

        // -------------------------------------------------------
        // Industry area flattening
        // -------------------------------------------------------

        /// <summary>
        /// Selects random tile positions in the mid-height range and flattens
        /// a circular area around them so that industry buildings can be placed.
        /// Respects <see cref="Constants.MinIndustrySpacing"/> between sites.
        /// </summary>
        private void FlattenIndustryAreas(int[,] heights, float[,] noiseMap, int w, int h)
        {
            int attempts = 0;
            int maxAttempts = _industryFlatCount * 20;

            while (IndustrySpawnPoints.Count < _industryFlatCount && attempts < maxAttempts)
            {
                attempts++;

                // Pick a random tile not too close to map edges
                int margin = _flatRadius + 2;
                int cx = Random.Range(margin, w - margin);
                int cz = Random.Range(margin, h - margin);

                float n = noiseMap[cx, cz];

                // Only on land, above sand, but not on rocky peaks
                if (n <= _sandThreshold) continue;
                float hFrac = (float)heights[cx, cz] / Constants.MaxHeight;
                if (hFrac >= _rockHeightFraction) continue;

                // Respect minimum spacing between industry sites
                bool tooClose = false;
                foreach (Vector2Int pt in IndustrySpawnPoints)
                {
                    int dx = cx - pt.x;
                    int dz = cz - pt.y;
                    if (dx * dx + dz * dz < Constants.MinIndustrySpacing * Constants.MinIndustrySpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                // Flatten the area to the centre tile's height
                int flatHeight = heights[cx, cz];
                for (int dz = -_flatRadius; dz <= _flatRadius; dz++)
                {
                    for (int dx = -_flatRadius; dx <= _flatRadius; dx++)
                    {
                        int tx = cx + dx;
                        int tz = cz + dz;
                        if (tx < 0 || tx >= w || tz < 0 || tz >= h) continue;
                        if (dx * dx + dz * dz <= _flatRadius * _flatRadius)
                        {
                            heights[tx, tz] = flatHeight;
                            // Also adjust noise map so type classification is correct
                            noiseMap[tx, tz] = Mathf.Max(noiseMap[tx, tz], _sandThreshold + 0.01f);
                        }
                    }
                }

                IndustrySpawnPoints.Add(new Vector2Int(cx, cz));
            }
        }

        // -------------------------------------------------------
        // Debug
        // -------------------------------------------------------

        /// <summary>
        /// Re-generates terrain with a new random seed.
        /// Useful for editor tooling and testing.
        /// </summary>
        [ContextMenu("Regenerate Terrain")]
        public void RegenerateTerrain()
        {
            int old = _seed;
            _seed   = 0; // Force random seed
            Generate();
            _seed   = old;
        }
    }
}
