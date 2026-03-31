using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Static A* pathfinder that operates on the rail network graph managed by
    /// <see cref="RailManager"/>.
    ///
    /// <para>
    /// Each tile with a rail segment is treated as a graph node.  Two nodes are
    /// connected only when their respective <see cref="RailSegment"/>s have
    /// bi-directional connections (i.e., both segments acknowledge the link).
    /// </para>
    ///
    /// <para>
    /// Heuristic: Manhattan distance, which is admissible on a 4-connected grid.
    /// </para>
    ///
    /// <para>
    /// A hard node-expansion limit (<see cref="MaxNodes"/>) guards against
    /// excessively long searches on large networks.
    /// </para>
    /// </summary>
    public static class TrainPathfinder
    {
        // ── Constants ───────────────────────────────────────────────────────

        /// <summary>
        /// Maximum number of nodes that may be expanded in a single search.
        /// Prevents frame-rate spikes on degenerate or very long paths.
        /// </summary>
        private const int MaxNodes = 10_000;

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Finds the shortest path through the rail network from
        /// <paramref name="start"/> to <paramref name="end"/> using the A* algorithm.
        /// </summary>
        /// <param name="start">Starting grid position (must have a rail segment).</param>
        /// <param name="end">Target grid position (must have a rail segment).</param>
        /// <param name="railManager">The <see cref="RailManager"/> owning the network.</param>
        /// <returns>
        /// An ordered <see cref="List{T}"/> of grid positions from
        /// <paramref name="start"/> (inclusive) to <paramref name="end"/> (inclusive),
        /// or <c>null</c> if no path exists or the node limit was exceeded.
        /// </returns>
        public static List<Vector2Int> FindPath(Vector2Int start, Vector2Int end,
                                                RailManager railManager)
        {
            if (railManager == null)
            {
                Debug.LogError("[TrainPathfinder] RailManager is null.");
                return null;
            }

            if (!railManager.HasRail(start))
            {
                Debug.LogWarning($"[TrainPathfinder] Start {start} has no rail.");
                return null;
            }

            if (!railManager.HasRail(end))
            {
                Debug.LogWarning($"[TrainPathfinder] End {end} has no rail.");
                return null;
            }

            if (start == end)
                return new List<Vector2Int> { start };

            // ── Data structures ─────────────────────────────────────────────

            // Open set ordered by fCost (min-heap via SortedSet with tie-breaking)
            var openSet   = new SortedSet<Node>(NodeComparer.Instance);
            // Fast lookup: position → node (for open or closed set queries)
            var nodeMap   = new Dictionary<Vector2Int, Node>();
            // Closed set
            var closedSet = new HashSet<Vector2Int>();

            var startNode = new Node(start, gCost: 0, hCost: Heuristic(start, end));
            openSet.Add(startNode);
            nodeMap[start] = startNode;

            int expansions = 0;

            // ── Main loop ───────────────────────────────────────────────────

            while (openSet.Count > 0)
            {
                if (++expansions > MaxNodes)
                {
                    Debug.LogWarning($"[TrainPathfinder] Node limit ({MaxNodes}) exceeded. Path not found.");
                    return null;
                }

                // Pop cheapest node
                Node current = openSet.Min;
                openSet.Remove(current);
                closedSet.Add(current.Position);

                // Reached goal
                if (current.Position == end)
                    return ReconstructPath(current);

                // Expand neighbours
                var neighbours = railManager.GetConnections(current.Position);
                foreach (var neighbourPos in neighbours)
                {
                    if (closedSet.Contains(neighbourPos)) continue;

                    // Movement cost = 1 per tile (could weight curves higher here)
                    int newGCost = current.GCost + TileCost(current.Position, neighbourPos, railManager);

                    if (nodeMap.TryGetValue(neighbourPos, out Node existing))
                    {
                        // If we found a cheaper path to an already-open node, update it
                        if (newGCost < existing.GCost)
                        {
                            openSet.Remove(existing);
                            existing.GCost  = newGCost;
                            existing.Parent = current;
                            openSet.Add(existing);
                        }
                    }
                    else
                    {
                        var newNode = new Node(neighbourPos,
                                               gCost:  newGCost,
                                               hCost:  Heuristic(neighbourPos, end),
                                               parent: current);
                        openSet.Add(newNode);
                        nodeMap[neighbourPos] = newNode;
                    }
                }
            }

            // Open set exhausted — no path
            return null;
        }

        // ── Heuristic ───────────────────────────────────────────────────────

        /// <summary>
        /// Manhattan distance heuristic. Admissible for 4-connected grids.
        /// </summary>
        private static int Heuristic(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        // ── Movement cost ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the cost of moving from <paramref name="from"/> to <paramref name="to"/>.
        /// Curves are slightly more expensive to bias towards straight routes (matches
        /// real railway physics where curves impose speed restrictions).
        /// </summary>
        private static int TileCost(Vector2Int from, Vector2Int to, RailManager rm)
        {
            var seg = rm.GetSegment(to);
            if (seg == null) return 10;

            return seg.Direction switch
            {
                RailDirection.Curve_NE or
                RailDirection.Curve_NW or
                RailDirection.Curve_SE or
                RailDirection.Curve_SW => 12, // curves cost slightly more
                _                      => 10,
            };
        }

        // ── Path reconstruction ──────────────────────────────────────────────

        private static List<Vector2Int> ReconstructPath(Node goalNode)
        {
            var path = new List<Vector2Int>();
            for (Node n = goalNode; n != null; n = n.Parent)
                path.Add(n.Position);
            path.Reverse();
            return path;
        }

        // ── Node class ───────────────────────────────────────────────────────

        /// <summary>
        /// A* search node.  Contains position, cost values, and parent link.
        /// </summary>
        public sealed class Node
        {
            /// <summary>Grid position of this node.</summary>
            public Vector2Int Position { get; }

            /// <summary>Cost from the start node to this node.</summary>
            public int GCost { get; set; }

            /// <summary>Estimated cost from this node to the goal (heuristic).</summary>
            public int HCost { get; }

            /// <summary>Total estimated cost: GCost + HCost.</summary>
            public int FCost => GCost + HCost;

            /// <summary>Parent node in the optimal path tree.</summary>
            public Node Parent { get; set; }

            // Unique id for stable SortedSet ordering when fCosts are equal
            private static int _counter;
            private readonly int _id;

            /// <summary>
            /// Creates a new search node.
            /// </summary>
            public Node(Vector2Int position, int gCost, int hCost, Node parent = null)
            {
                Position = position;
                GCost    = gCost;
                HCost    = hCost;
                Parent   = parent;
                _id      = System.Threading.Interlocked.Increment(ref _counter);
            }

            /// <inheritdoc/>
            public override string ToString() =>
                $"Node({Position}, g={GCost}, h={HCost}, f={FCost})";
        }

        // ── Comparer ─────────────────────────────────────────────────────────

        /// <summary>
        /// Compares nodes for the open-set SortedSet.
        /// Primary sort: FCost ascending.  Tie-breaker: creation order (id).
        /// </summary>
        private sealed class NodeComparer : IComparer<Node>
        {
            public static readonly NodeComparer Instance = new NodeComparer();

            public int Compare(Node x, Node y)
            {
                if (x == null || y == null) return 0;
                int cmp = x.FCost.CompareTo(y.FCost);
                if (cmp != 0) return cmp;
                // Same fCost — break ties by hCost (prefer closer to goal)
                cmp = x.HCost.CompareTo(y.HCost);
                if (cmp != 0) return cmp;
                // Final tie-break by creation order to avoid duplicates in SortedSet
                return x.GetHashCode().CompareTo(y.GetHashCode());
            }
        }
    }
}
