using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Defines the direction/type of a rail segment on the tile grid.
    /// Straight segments run along one axis; curves connect two perpendicular axes;
    /// junctions allow branching or crossing.
    /// </summary>
    public enum RailDirection
    {
        North_South,      // Straight vertical (Z axis)
        East_West,        // Straight horizontal (X axis)
        Curve_NE,         // Curves from North (–Z) to East (+X)
        Curve_NW,         // Curves from North (–Z) to West (–X)
        Curve_SE,         // Curves from South (+Z) to East (+X)
        Curve_SW,         // Curves from South (+Z) to West (–X)
        Junction_T_N,     // T-junction open toward North
        Junction_T_S,     // T-junction open toward South
        Junction_T_E,     // T-junction open toward East
        Junction_T_W,     // T-junction open toward West
        Junction_Cross    // Full 4-way crossing
    }

    /// <summary>
    /// Represents a single rail piece placed on the tile grid.
    /// Stores its grid position, direction type, and which adjacent tiles
    /// this segment connects to. Holds a reference to its spawned GameObject.
    /// </summary>
    public class RailSegment : MonoBehaviour
    {
        // ── Inspector-exposed fields ────────────────────────────────────────

        /// <summary>Grid X coordinate of this segment.</summary>
        [SerializeField] private int gridX;

        /// <summary>Grid Z coordinate of this segment.</summary>
        [SerializeField] private int gridZ;

        /// <summary>Direction / shape of this rail segment.</summary>
        [SerializeField] private RailDirection direction;

        /// <summary>Optional mesh renderer for visual override from inspector.</summary>
        [SerializeField] private MeshRenderer meshRenderer;

        /// <summary>Optional mesh filter for procedural mesh injection.</summary>
        [SerializeField] private MeshFilter meshFilter;

        // ── Public properties ───────────────────────────────────────────────

        /// <summary>Grid position as a Vector2Int (X, Z).</summary>
        public Vector2Int GridPosition => new Vector2Int(gridX, gridZ);

        /// <summary>Direction / shape type of this segment.</summary>
        public RailDirection Direction
        {
            get => direction;
            internal set => direction = value;
        }

        /// <summary>MeshRenderer for this segment's visual (may be null before generation).</summary>
        public MeshRenderer MeshRenderer => meshRenderer;

        /// <summary>MeshFilter for this segment's procedural mesh (may be null before generation).</summary>
        public MeshFilter MeshFilter => meshFilter;

        // ── Static connection tables ────────────────────────────────────────

        // Cardinal offsets: N = (0,–1), S = (0,+1), E = (+1,0), W = (–1,0)
        private static readonly Vector2Int N = new Vector2Int(0, -1);
        private static readonly Vector2Int S = new Vector2Int(0,  1);
        private static readonly Vector2Int E = new Vector2Int( 1, 0);
        private static readonly Vector2Int W = new Vector2Int(-1, 0);

        /// <summary>
        /// Precomputed connection offsets for every RailDirection.
        /// A connection offset indicates which adjacent tile this segment
        /// "opens" toward — i.e., a train can enter/exit in that direction.
        /// </summary>
        private static readonly Dictionary<RailDirection, Vector2Int[]> ConnectionTable =
            new Dictionary<RailDirection, Vector2Int[]>
        {
            { RailDirection.North_South,   new[] { N, S }         },
            { RailDirection.East_West,     new[] { E, W }         },
            { RailDirection.Curve_NE,      new[] { N, E }         },
            { RailDirection.Curve_NW,      new[] { N, W }         },
            { RailDirection.Curve_SE,      new[] { S, E }         },
            { RailDirection.Curve_SW,      new[] { S, W }         },
            { RailDirection.Junction_T_N,  new[] { N, E, W }      },
            { RailDirection.Junction_T_S,  new[] { S, E, W }      },
            { RailDirection.Junction_T_E,  new[] { N, S, E }      },
            { RailDirection.Junction_T_W,  new[] { N, S, W }      },
            { RailDirection.Junction_Cross,new[] { N, S, E, W }   },
        };

        // ── Initialisation ──────────────────────────────────────────────────

        /// <summary>
        /// Initialises the segment with a grid position and direction.
        /// Called by <see cref="RailManager"/> immediately after instantiation.
        /// </summary>
        /// <param name="x">Grid X coordinate.</param>
        /// <param name="z">Grid Z coordinate.</param>
        /// <param name="dir">Rail direction / shape.</param>
        public void Initialise(int x, int z, RailDirection dir)
        {
            gridX     = x;
            gridZ     = z;
            direction = dir;

            // Cache renderer components if present on this GameObject
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshFilter   == null) meshFilter   = GetComponent<MeshFilter>();
        }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns the list of grid-space offsets that this segment connects to.
        /// Each entry is an (X, Z) direction vector pointing to an adjacent tile
        /// that a train may traverse when on this segment.
        /// </summary>
        /// <returns>Array of Vector2Int offsets.</returns>
        public Vector2Int[] GetConnectedDirections()
        {
            if (ConnectionTable.TryGetValue(direction, out Vector2Int[] dirs))
                return dirs;

            Debug.LogWarning($"[RailSegment] No connection table entry for direction {direction}");
            return System.Array.Empty<Vector2Int>();
        }

        /// <summary>
        /// Returns the absolute grid positions of all tiles this segment connects to.
        /// </summary>
        /// <returns>List of connected grid positions.</returns>
        public List<Vector2Int> GetConnectedPositions()
        {
            Vector2Int[] offsets = GetConnectedDirections();
            var result = new List<Vector2Int>(offsets.Length);
            foreach (var offset in offsets)
                result.Add(GridPosition + offset);
            return result;
        }

        /// <summary>
        /// Returns true if this segment has a connection in the given offset direction.
        /// </summary>
        /// <param name="offset">A cardinal direction offset (N/S/E/W).</param>
        public bool ConnectsIn(Vector2Int offset)
        {
            foreach (var dir in GetConnectedDirections())
                if (dir == offset) return true;
            return false;
        }

        /// <summary>
        /// Returns the opposite connection direction given an entry direction.
        /// Used by TrainMovement to determine exit direction through a segment.
        /// For junctions/crossings with more than 2 connections the caller must
        /// apply signal/switch logic separately.
        /// </summary>
        /// <param name="entryDirection">The direction the train entered from (offset pointing INTO this tile).</param>
        /// <returns>Exit direction offset, or Vector2Int.zero if no valid exit found.</returns>
        public Vector2Int GetExitDirection(Vector2Int entryDirection)
        {
            // entryDirection is the offset from the previous tile to this tile,
            // so the "from" side is the negated entry direction.
            Vector2Int fromSide = -entryDirection;

            var connections = GetConnectedDirections();
            foreach (var c in connections)
            {
                // Return the first connection that is NOT the side we came from
                if (c != fromSide)
                    return c;
            }
            return Vector2Int.zero;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a human-readable description for debugging.
        /// </summary>
        public override string ToString() =>
            $"RailSegment({gridX},{gridZ}) [{direction}]";
    }
}
