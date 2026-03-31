using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Singleton manager for the rail network.
    /// Owns the authoritative dictionary of all placed <see cref="RailSegment"/>s,
    /// handles placement / removal with economy integration, auto-detects rail
    /// directions, and merges junctions when two segments occupy the same tile.
    /// </summary>
    public class RailManager : MonoBehaviour
    {
        // ── Singleton ───────────────────────────────────────────────────────

        /// <summary>Singleton instance; set on Awake.</summary>
        public static RailManager Instance { get; private set; }

        // ── Inspector fields ────────────────────────────────────────────────

        /// <summary>Cost in currency units to place one rail segment.</summary>
        [SerializeField] private int costPerSegment = 100;

        /// <summary>Refund fraction when removing a rail segment (0–1).</summary>
        [SerializeField, Range(0f, 1f)] private float removalRefundFraction = 0.5f;

        /// <summary>Prefab instantiated for each rail segment. Must have a RailSegment component.</summary>
        [SerializeField] private GameObject railSegmentPrefab;

        /// <summary>Parent transform used to organise spawned segment GameObjects.</summary>
        [SerializeField] private Transform railParent;

        // ── Internal state ──────────────────────────────────────────────────

        /// <summary>All placed rail segments keyed by grid position.</summary>
        private readonly Dictionary<Vector2Int, RailSegment> rails =
            new Dictionary<Vector2Int, RailSegment>();

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>
        /// Fired whenever the rail network topology changes (add, remove, or
        /// junction merge).  Subscribers (e.g. pathfinders) should invalidate
        /// cached paths on receipt.
        /// </summary>
        public event Action OnRailNetworkChanged;

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[RailManager] Duplicate singleton destroyed.");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (railParent == null)
            {
                railParent = new GameObject("Rail").transform;
                railParent.SetParent(transform);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Public read API ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the <see cref="RailSegment"/> at the given grid position,
        /// or <c>null</c> if none exists.
        /// </summary>
        public RailSegment GetSegment(int x, int z) =>
            GetSegment(new Vector2Int(x, z));

        /// <summary>
        /// Returns the <see cref="RailSegment"/> at the given grid position,
        /// or <c>null</c> if none exists.
        /// </summary>
        public RailSegment GetSegment(Vector2Int pos) =>
            rails.TryGetValue(pos, out var seg) ? seg : null;

        /// <summary>Returns true if there is a rail segment at the given grid position.</summary>
        public bool HasRail(int x, int z) => rails.ContainsKey(new Vector2Int(x, z));

        /// <summary>Returns true if there is a rail segment at the given grid position.</summary>
        public bool HasRail(Vector2Int pos) => rails.ContainsKey(pos);

        /// <summary>Read-only view of the entire rail dictionary.</summary>
        public IReadOnlyDictionary<Vector2Int, RailSegment> AllRails => rails;

        /// <summary>
        /// Returns a list of grid positions that are reachable from (x, z)
        /// through this tile's rail connections.
        /// </summary>
        /// <param name="x">Grid X.</param>
        /// <param name="z">Grid Z.</param>
        /// <returns>List of connected neighbour positions.</returns>
        public List<Vector2Int> GetConnections(int x, int z)
        {
            var pos = new Vector2Int(x, z);
            if (!rails.TryGetValue(pos, out var seg))
                return new List<Vector2Int>();

            // Only return neighbours that also have rail AND connect back to us
            var raw         = seg.GetConnectedPositions();
            var result      = new List<Vector2Int>(raw.Count);
            var incomingDir = Vector2Int.zero;

            foreach (var neighbour in raw)
            {
                if (!rails.TryGetValue(neighbour, out var neighbourSeg))
                    continue;

                // The neighbour must also connect back in the opposite direction
                incomingDir = pos - neighbour; // offset FROM neighbour TO pos
                if (neighbourSeg.ConnectsIn(incomingDir))
                    result.Add(neighbour);
            }
            return result;
        }

        /// <summary>
        /// Returns connections from the given Vector2Int position.
        /// </summary>
        public List<Vector2Int> GetConnections(Vector2Int pos) =>
            GetConnections(pos.x, pos.y);

        // ── Placement API ───────────────────────────────────────────────────

        /// <summary>
        /// Places a rail segment at (x, z) with the specified direction.
        /// Deducts cost from the <see cref="EconomyManager"/>, spawns the visual
        /// GameObject, generates its mesh, and fires <see cref="OnRailNetworkChanged"/>.
        /// If a segment already exists at this position, <see cref="MergeJunction"/>
        /// is invoked instead.
        /// </summary>
        /// <param name="x">Grid X coordinate.</param>
        /// <param name="z">Grid Z coordinate.</param>
        /// <param name="direction">Rail direction / shape.</param>
        /// <returns>The placed or merged <see cref="RailSegment"/>, or null on failure.</returns>
        public RailSegment AddRail(int x, int z, RailDirection direction)
        {
            var pos = new Vector2Int(x, z);

            // If something is already here, attempt a junction merge
            if (rails.ContainsKey(pos))
                return MergeJunction(x, z);

            // Economy check
            if (EconomyManager.Instance != null &&
                !EconomyManager.Instance.CanAfford(costPerSegment))
            {
                Debug.LogWarning($"[RailManager] Cannot afford rail segment at ({x},{z}). Cost: {costPerSegment}");
                return null;
            }

            // Validate tile availability via GridManager
            if (GridManager.Instance != null)
            {
                var tile = GridManager.Instance.GetTile(x, z);
                if (tile == null)
                {
                    Debug.LogWarning($"[RailManager] Tile ({x},{z}) not found in GridManager.");
                    return null;
                }
                if (tile.HasRail)
                {
                    Debug.LogWarning($"[RailManager] Tile ({x},{z}) already has a rail reference in GridManager.");
                    return null;
                }
            }

            // Spawn GameObject
            RailSegment segment = SpawnSegment(x, z, direction);
            if (segment == null) return null;

            // Register
            rails[pos] = segment;

            // Deduct cost
            EconomyManager.Instance?.Spend(costPerSegment);

            // Register with GridManager
            GridManager.Instance?.SetRailOnTile(x, z, segment);

            // Notify
            OnRailNetworkChanged?.Invoke();

            return segment;
        }

        /// <summary>
        /// Removes the rail segment at (x, z), refunds a partial cost, destroys
        /// the GameObject, and fires <see cref="OnRailNetworkChanged"/>.
        /// </summary>
        /// <param name="x">Grid X coordinate.</param>
        /// <param name="z">Grid Z coordinate.</param>
        /// <returns>True if a segment was removed, false if none existed.</returns>
        public bool RemoveRail(int x, int z)
        {
            var pos = new Vector2Int(x, z);
            if (!rails.TryGetValue(pos, out var segment))
            {
                Debug.LogWarning($"[RailManager] No rail at ({x},{z}) to remove.");
                return false;
            }

            // Refund
            int refund = Mathf.RoundToInt(costPerSegment * removalRefundFraction);
            EconomyManager.Instance?.Earn(refund);

            // Unregister from GridManager
            GridManager.Instance?.ClearRailOnTile(x, z);

            // Destroy visual
            if (segment != null && segment.gameObject != null)
                Destroy(segment.gameObject);

            rails.Remove(pos);

            OnRailNetworkChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Removes the rail segment at the given Vector2Int position.
        /// </summary>
        public bool RemoveRail(Vector2Int pos) => RemoveRail(pos.x, pos.y);

        // ── Direction detection ─────────────────────────────────────────────

        /// <summary>
        /// Determines the best <see cref="RailDirection"/> when placing rail
        /// from one tile to an adjacent tile.  Works for straight (same axis)
        /// and diagonal/curve placements.
        /// </summary>
        /// <param name="from">Source grid tile position.</param>
        /// <param name="to">Target grid tile position (should be adjacent).</param>
        /// <returns>The inferred RailDirection.</returns>
        public static RailDirection AutoDetectDirection(Vector2Int from, Vector2Int to)
        {
            var delta = to - from;
            // Straight segments
            if (delta.x == 0) return RailDirection.North_South;
            if (delta.y == 0) return RailDirection.East_West;

            // Diagonal → pick a curve based on approach vector
            if (delta.x > 0 && delta.y < 0) return RailDirection.Curve_NE;
            if (delta.x < 0 && delta.y < 0) return RailDirection.Curve_NW;
            if (delta.x > 0 && delta.y > 0) return RailDirection.Curve_SE;
            return RailDirection.Curve_SW;
        }

        /// <summary>
        /// Given an entry direction into a tile and an exit direction out of the same
        /// tile, returns the appropriate <see cref="RailDirection"/> for that tile.
        /// Used by the rail placer when auto-routing L-shaped bends.
        /// </summary>
        /// <param name="entry">Normalised direction entering the tile (e.g. (1,0) = from west).</param>
        /// <param name="exit">Normalised direction leaving the tile (e.g. (0,-1) = heading north).</param>
        /// <returns>Best RailDirection for the transition, or North_South as fallback.</returns>
        public static RailDirection DirectionFromEntryExit(Vector2Int entry, Vector2Int exit)
        {
            // Both north–south or east–west → straight
            if (entry.x == 0 && exit.x == 0) return RailDirection.North_South;
            if (entry.y == 0 && exit.y == 0) return RailDirection.East_West;

            // Curve cases: combine the two directions to pick the curve type
            var dirs = new HashSet<Vector2Int> { entry, exit };
            bool hasN = dirs.Contains(new Vector2Int(0, -1));
            bool hasS = dirs.Contains(new Vector2Int(0,  1));
            bool hasE = dirs.Contains(new Vector2Int( 1, 0));
            bool hasW = dirs.Contains(new Vector2Int(-1, 0));

            if (hasN && hasE) return RailDirection.Curve_NE;
            if (hasN && hasW) return RailDirection.Curve_NW;
            if (hasS && hasE) return RailDirection.Curve_SE;
            if (hasS && hasW) return RailDirection.Curve_SW;

            return RailDirection.North_South; // fallback
        }

        // ── Junction merging ────────────────────────────────────────────────

        /// <summary>
        /// When a rail is placed on a tile that already has one, this method
        /// upgrades the existing segment to the appropriate junction type by
        /// examining what connections already exist and adding the new ones.
        /// No additional cost is charged for junction upgrades.
        /// </summary>
        /// <param name="x">Grid X coordinate.</param>
        /// <param name="z">Grid Z coordinate.</param>
        /// <returns>The updated <see cref="RailSegment"/>, or null if no existing rail.</returns>
        public RailSegment MergeJunction(int x, int z)
        {
            var pos = new Vector2Int(x, z);
            if (!rails.TryGetValue(pos, out var existing))
            {
                Debug.LogWarning($"[RailManager] MergeJunction called but no existing rail at ({x},{z}).");
                return null;
            }

            // Determine which cardinal neighbours have rails connecting back to us
            var connectedSides = new HashSet<Vector2Int>();
            Vector2Int[] cardinals =
            {
                new Vector2Int(0, -1),  // N
                new Vector2Int(0,  1),  // S
                new Vector2Int( 1, 0),  // E
                new Vector2Int(-1, 0),  // W
            };

            foreach (var offset in cardinals)
            {
                var neighbour = pos + offset;
                if (!rails.TryGetValue(neighbour, out var nSeg)) continue;
                // Neighbour must connect back toward us
                if (nSeg.ConnectsIn(-offset))
                    connectedSides.Add(offset);
            }

            // Also include directions the existing segment already opens
            foreach (var d in existing.GetConnectedDirections())
                connectedSides.Add(d);

            // Choose the best junction type
            RailDirection newDir = ChooseJunctionType(connectedSides);
            if (newDir == existing.Direction) return existing; // no change needed

            existing.Direction = newDir;

            // Regenerate mesh
            if (existing.MeshFilter != null)
                existing.MeshFilter.mesh = RailMeshGenerator.GenerateRailMesh(newDir);

            OnRailNetworkChanged?.Invoke();
            return existing;
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private RailSegment SpawnSegment(int x, int z, RailDirection direction)
        {
            GameObject go;
            if (railSegmentPrefab != null)
            {
                go = Instantiate(railSegmentPrefab, railParent);
            }
            else
            {
                // Fallback: create a minimal GameObject
                go = new GameObject($"Rail_{x}_{z}");
                go.transform.SetParent(railParent);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.material = RailMeshGenerator.GetOrCreateRailMaterial();
            }

            // Position in world space
            if (GridManager.Instance != null)
            {
                Vector3 worldPos = GridManager.Instance.GridToWorld(x, z);
                go.transform.position = worldPos + Vector3.up * 0.02f; // slight lift above terrain
            }
            else
            {
                go.transform.position = new Vector3(x, 0.02f, z);
            }

            var segment = go.GetComponent<RailSegment>();
            if (segment == null)
                segment = go.AddComponent<RailSegment>();

            segment.Initialise(x, z, direction);

            // Generate procedural mesh
            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null)
                meshFilter.mesh = RailMeshGenerator.GenerateRailMesh(direction);

            go.name = $"Rail_{x}_{z}_{direction}";
            return segment;
        }

        private static RailDirection ChooseJunctionType(HashSet<Vector2Int> sides)
        {
            bool n = sides.Contains(new Vector2Int(0, -1));
            bool s = sides.Contains(new Vector2Int(0,  1));
            bool e = sides.Contains(new Vector2Int( 1, 0));
            bool w = sides.Contains(new Vector2Int(-1, 0));

            int count = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);

            if (count >= 4)                return RailDirection.Junction_Cross;
            if (count == 3)
            {
                if (!n) return RailDirection.Junction_T_S;
                if (!s) return RailDirection.Junction_T_N;
                if (!e) return RailDirection.Junction_T_W;
                if (!w) return RailDirection.Junction_T_E;
            }
            if (count == 2)
            {
                if (n && s) return RailDirection.North_South;
                if (e && w) return RailDirection.East_West;
                if (n && e) return RailDirection.Curve_NE;
                if (n && w) return RailDirection.Curve_NW;
                if (s && e) return RailDirection.Curve_SE;
                if (s && w) return RailDirection.Curve_SW;
            }
            // 0 or 1 side — default
            return RailDirection.North_South;
        }
    }
}
