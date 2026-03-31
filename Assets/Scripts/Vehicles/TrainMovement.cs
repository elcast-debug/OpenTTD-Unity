using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Moves a train smoothly along a path of rail tiles obtained from
    /// <see cref="TrainPathfinder"/>.
    ///
    /// <para>
    /// The train interpolates between tile-centre world positions.  Speed is
    /// slightly reduced on curved segments to simulate real railway physics.
    /// Rotation smoothly tracks the movement direction.
    /// </para>
    ///
    /// <para>
    /// When the train reaches its destination or the rail network changes, it
    /// calls <see cref="RequestNewPath"/> automatically.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Train))]
    public class TrainMovement : MonoBehaviour
    {
        // ── Inspector fields ────────────────────────────────────────────────

        /// <summary>Movement speed in tiles per second (overridden by Train.MaxSpeed).</summary>
        [SerializeField] private float maxSpeed = 3f;

        /// <summary>Speed multiplier applied when traversing curve tiles.</summary>
        [SerializeField, Range(0.1f, 1f)] private float curveSpeedFactor = 0.65f;

        /// <summary>Degrees per second for rotation smoothing.</summary>
        [SerializeField] private float rotationSpeed = 540f;

        /// <summary>
        /// World-space height offset above the tile plane at which the train
        /// is positioned.
        /// </summary>
        [SerializeField] private float heightOffset = 0.15f;

        // ── Runtime state ───────────────────────────────────────────────────

        private List<Vector2Int> currentPath = new List<Vector2Int>();
        private int              pathIndex   = 0;  // next waypoint index

        private Vector3          targetWorldPos;
        private bool             isMoving = false;

        private Vector2Int       currentGridPos;
        private Vector2Int       destinationGridPos;

        private float            totalDistanceTravelled;

        // ── Events ──────────────────────────────────────────────────────────

        /// <summary>Fired when the train reaches the final waypoint of its path.</summary>
        public event Action<Vector2Int> OnDestinationReached;

        /// <summary>Fired when a path could not be found.</summary>
        public event Action OnPathNotFound;

        // ── Properties ──────────────────────────────────────────────────────

        /// <summary>Movement speed in tiles per second.</summary>
        public float MaxSpeed
        {
            get => maxSpeed;
            set => maxSpeed = Mathf.Max(0.1f, value);
        }

        /// <summary>Cumulative distance (in tiles) the train has travelled during its lifetime.</summary>
        public float TotalDistanceTravelled => totalDistanceTravelled;

        /// <summary>Current grid position of the train (updates as tiles are reached).</summary>
        public Vector2Int CurrentGridPosition => currentGridPos;

        /// <summary>True when the train is actively following a path.</summary>
        public bool IsMoving => isMoving;

        // ── Unity lifecycle ─────────────────────────────────────────────────

        private void Start()
        {
            // Snap to initial tile if possible
            if (GridManager.Instance != null)
            {
                targetWorldPos = GetWorldPos(currentGridPos);
                transform.position = targetWorldPos;
            }

            // Subscribe to network changes to invalidate paths
            if (RailManager.Instance != null)
                RailManager.Instance.OnRailNetworkChanged += HandleNetworkChanged;
        }

        private void OnDestroy()
        {
            if (RailManager.Instance != null)
                RailManager.Instance.OnRailNetworkChanged -= HandleNetworkChanged;
        }

        private void Update()
        {
            if (!isMoving) return;
            MoveAlongPath();
        }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Requests that the train move to <paramref name="destination"/> via the
        /// rail network.  Runs A* immediately and begins movement if a path is found.
        /// </summary>
        /// <param name="destination">Target grid position (must have a rail segment).</param>
        public void MoveTo(Vector2Int destination)
        {
            destinationGridPos = destination;
            RequestNewPath();
        }

        /// <summary>
        /// Stops movement immediately.
        /// </summary>
        public void Stop()
        {
            isMoving = false;
            currentPath.Clear();
        }

        /// <summary>
        /// Triggers a fresh A* search from the current position to
        /// <see cref="destinationGridPos"/>.  Called automatically when the network
        /// changes or the train reaches the end of its current path.
        /// </summary>
        public void RequestNewPath()
        {
            if (RailManager.Instance == null)
            {
                Debug.LogWarning("[TrainMovement] RailManager not available.");
                OnPathNotFound?.Invoke();
                return;
            }

            var path = TrainPathfinder.FindPath(currentGridPos, destinationGridPos,
                                                RailManager.Instance);
            if (path == null || path.Count == 0)
            {
                Debug.LogWarning($"[TrainMovement] No path from {currentGridPos} to {destinationGridPos}.");
                isMoving = false;
                OnPathNotFound?.Invoke();
                return;
            }

            currentPath = path;
            pathIndex   = 1; // index 0 is current position — skip it
            isMoving    = pathIndex < currentPath.Count;

            if (isMoving)
                targetWorldPos = GetWorldPos(currentPath[pathIndex]);
        }

        // ── Movement ────────────────────────────────────────────────────────

        private void MoveAlongPath()
        {
            if (pathIndex >= currentPath.Count)
            {
                // Reached the end of the path
                isMoving = false;
                currentGridPos = destinationGridPos;
                OnDestinationReached?.Invoke(currentGridPos);
                return;
            }

            // Determine effective speed (slower on curves)
            float speed = GetEffectiveSpeed(currentPath[pathIndex]);

            // Move toward next waypoint
            Vector3 direction = (targetWorldPos - transform.position);
            float   dist      = direction.magnitude;
            float   step      = speed * Time.deltaTime;

            if (step >= dist)
            {
                // Arrived at waypoint
                transform.position = targetWorldPos;
                totalDistanceTravelled += dist;
                currentGridPos = currentPath[pathIndex];
                pathIndex++;

                if (pathIndex < currentPath.Count)
                    targetWorldPos = GetWorldPos(currentPath[pathIndex]);
            }
            else
            {
                transform.position += direction.normalized * step;
                totalDistanceTravelled += step;
            }

            // Smooth rotation to face movement direction
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(
                    new Vector3(direction.x, 0f, direction.z).normalized,
                    Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }

        private float GetEffectiveSpeed(Vector2Int nextTile)
        {
            if (RailManager.Instance == null) return maxSpeed;
            var seg = RailManager.Instance.GetSegment(nextTile);
            if (seg == null) return maxSpeed;

            bool isCurve = seg.Direction is
                RailDirection.Curve_NE or RailDirection.Curve_NW or
                RailDirection.Curve_SE or RailDirection.Curve_SW;

            return isCurve ? maxSpeed * curveSpeedFactor : maxSpeed;
        }

        // ── Rail network change handler ──────────────────────────────────────

        private void HandleNetworkChanged()
        {
            if (!isMoving) return;

            // Verify the remaining path is still valid
            for (int i = pathIndex; i < currentPath.Count; i++)
            {
                if (!RailManager.Instance.HasRail(currentPath[i]))
                {
                    // Path invalidated — recompute
                    RequestNewPath();
                    return;
                }
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private Vector3 GetWorldPos(Vector2Int gridPos)
        {
            if (GridManager.Instance != null)
                return GridManager.Instance.GridToWorld(gridPos.x, gridPos.y)
                       + Vector3.up * heightOffset;

            return new Vector3(gridPos.x, heightOffset, gridPos.y);
        }
    }
}
