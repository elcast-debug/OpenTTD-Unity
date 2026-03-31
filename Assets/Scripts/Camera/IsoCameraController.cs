using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenTTDUnity
{
    /// <summary>
    /// Isometric camera controller for an orthographic camera.
    ///
    /// Tracks a separate <c>_pivot</c> (the world point the camera looks at).
    /// WASD / middle-mouse-drag move the pivot; the camera is always offset
    /// behind and above it based on yaw, pitch, and distance.
    ///
    /// Rotation snaps in 90° increments and is smoothly interpolated.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class IsoCameraController : MonoBehaviour
    {
        // ── Inspector ───────────────────────────────────────────────────

        [Header("References")]
        [SerializeField, Tooltip("The orthographic camera. Auto-assigned if empty.")]
        private Camera _camera;

        [Header("Pan")]
        [SerializeField, Tooltip("World-unit pan speed at 1× zoom.")]
        private float _panSpeed = Constants.CameraPanSpeed;

        [SerializeField, Tooltip("Multiplier when holding Shift.")]
        private float _panShiftMultiplier = 2f;

        [Header("Zoom")]
        [SerializeField, Tooltip("Ortho-size change per scroll unit.")]
        private float _zoomSpeed = Constants.CameraZoomSpeed;

        [SerializeField] private float _minOrthoSize = Constants.CameraMinOrthoSize;
        [SerializeField] private float _maxOrthoSize = Constants.CameraMaxOrthoSize;

        [Header("Rotation")]
        [SerializeField, Tooltip("Lerp speed for rotation smoothing.")]
        private float _rotationLerpSpeed = 8f;

        [Header("Isometric Angles")]
        [SerializeField, Tooltip("Pitch angle (classic TTD ≈ 30°).")]
        private float _pitchAngle = 30f;

        [SerializeField, Tooltip("Distance behind the pivot (for orthographic this just needs to be enough to not clip).")]
        private float _cameraDistance = 100f;

        // ── State ───────────────────────────────────────────────────────

        /// <summary>World-space point the camera looks at (XZ plane).</summary>
        private Vector3 _pivot;

        private float _targetYaw;
        private float _currentYaw;
        private float _targetOrthoSize;

        // Middle-mouse drag
        private bool _isDragging;
        private Vector3 _dragOriginWorld;

        // World bounds for clamping
        private float _worldWidth;
        private float _worldDepth;

        // ── Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            if (!_camera.orthographic)
            {
                Debug.LogWarning("[IsoCameraController] Forcing orthographic projection.");
                _camera.orthographic = true;
            }
        }

        private void Start()
        {
            _worldWidth = Constants.GridWidth  * Constants.TileSize;
            _worldDepth = Constants.GridHeight * Constants.TileSize;

            // Centre of the map
            _pivot = new Vector3(_worldWidth * 0.5f,
                                 Constants.MaxHeight * Constants.HeightStep * 0.5f,
                                 _worldDepth * 0.5f);

            _targetYaw  = 45f;
            _currentYaw = _targetYaw;

            _targetOrthoSize = Constants.CameraDefaultOrthoSize;
            _camera.orthographicSize = _targetOrthoSize;

            ApplyTransform(snap: true);
        }

        private void Update()
        {
            HandleKeyboardPan();
            HandleMiddleMousePan();
            HandleZoom();
            HandleRotation();
            ApplyTransform(snap: false);
        }

        // ── Keyboard pan ────────────────────────────────────────────────

        private void HandleKeyboardPan()
        {
            float dx = 0f, dz = 0f;

            if (InputHelper.GetKey(Key.W) || InputHelper.GetKey(Key.UpArrow))    dz += 1f;
            if (InputHelper.GetKey(Key.S) || InputHelper.GetKey(Key.DownArrow))  dz -= 1f;
            if (InputHelper.GetKey(Key.D) || InputHelper.GetKey(Key.RightArrow)) dx += 1f;
            if (InputHelper.GetKey(Key.A) || InputHelper.GetKey(Key.LeftArrow))  dx -= 1f;

            if (dx == 0f && dz == 0f) return;

            float speed = _panSpeed;
            speed *= _camera.orthographicSize / Constants.CameraDefaultOrthoSize;

            if (InputHelper.GetKey(Key.LeftShift) || InputHelper.GetKey(Key.RightShift))
                speed *= _panShiftMultiplier;

            // Rotate input by current yaw so WASD is screen-relative
            float yawRad = _currentYaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(yawRad);
            float sin = Mathf.Sin(yawRad);

            float worldDx = dx * cos - dz * sin;
            float worldDz = dx * sin + dz * cos;

            _pivot.x += worldDx * speed * Time.unscaledDeltaTime;
            _pivot.z += worldDz * speed * Time.unscaledDeltaTime;

            ClampPivot();
        }

        // ── Middle-mouse drag pan ───────────────────────────────────────

        private void HandleMiddleMousePan()
        {
            if (InputHelper.GetMouseButtonDown(2))
            {
                _isDragging = true;
                _dragOriginWorld = ScreenToWorldXZ(InputHelper.mousePosition);
            }

            if (InputHelper.GetMouseButtonUp(2))
            {
                _isDragging = false;
            }

            if (_isDragging)
            {
                Vector3 current = ScreenToWorldXZ(InputHelper.mousePosition);
                Vector3 delta = _dragOriginWorld - current;
                _pivot.x += delta.x;
                _pivot.z += delta.z;
                ClampPivot();
                _dragOriginWorld = ScreenToWorldXZ(InputHelper.mousePosition);
            }
        }

        // ── Zoom ────────────────────────────────────────────────────────

        private void HandleZoom()
        {
            float scroll = InputHelper.scrollWheel;
            if (Mathf.Abs(scroll) < 0.0001f) return;

            _targetOrthoSize -= scroll * _zoomSpeed;
            _targetOrthoSize  = Mathf.Clamp(_targetOrthoSize, _minOrthoSize, _maxOrthoSize);
        }

        // ── Rotation ────────────────────────────────────────────────────

        private void HandleRotation()
        {
            if (InputHelper.GetKeyDown(Key.Q)) _targetYaw = NormalizeAngle(_targetYaw + 90f);
            if (InputHelper.GetKeyDown(Key.E)) _targetYaw = NormalizeAngle(_targetYaw - 90f);

            _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw,
                Time.unscaledDeltaTime * _rotationLerpSpeed);

            if (Mathf.Abs(Mathf.DeltaAngle(_currentYaw, _targetYaw)) < 0.1f)
                _currentYaw = _targetYaw;
        }

        // ── Apply transform ─────────────────────────────────────────────

        /// <summary>
        /// Positions and rotates the camera based on the pivot, yaw, and pitch.
        /// For an orthographic camera the distance doesn't affect perspective,
        /// but it must be far enough to not clip the terrain.
        /// </summary>
        private void ApplyTransform(bool snap)
        {
            float yaw = snap ? _targetYaw : _currentYaw;

            // Smooth zoom
            _camera.orthographicSize = snap
                ? _targetOrthoSize
                : Mathf.Lerp(_camera.orthographicSize, _targetOrthoSize,
                              Time.unscaledDeltaTime * 12f);

            // Build rotation: pitch down, then yaw
            Quaternion rot = Quaternion.Euler(_pitchAngle, yaw, 0f);
            transform.rotation = rot;

            // Position camera behind the pivot along the view direction
            transform.position = _pivot - rot * Vector3.forward * _cameraDistance;
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private void ClampPivot()
        {
            float margin = 5f * Constants.TileSize;
            _pivot.x = Mathf.Clamp(_pivot.x, -margin, _worldWidth + margin);
            _pivot.z = Mathf.Clamp(_pivot.z, -margin, _worldDepth + margin);
        }

        private Vector3 ScreenToWorldXZ(Vector2 screenPos)
        {
            Ray ray = _camera.ScreenPointToRay(screenPos);
            // Intersect with Y = _pivot.y plane
            if (Mathf.Abs(ray.direction.y) > 0.0001f)
            {
                float t = (_pivot.y - ray.origin.y) / ray.direction.y;
                if (t > 0f) return ray.origin + ray.direction * t;
            }
            return new Vector3(ray.origin.x, _pivot.y, ray.origin.z);
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle < 0f) angle += 360f;
            return angle;
        }

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>Focus the camera on a specific world position.</summary>
        public void FocusOn(Vector3 worldPos)
        {
            _pivot = new Vector3(worldPos.x, _pivot.y, worldPos.z);
            ClampPivot();
        }

        /// <summary>Focus on a tile coordinate.</summary>
        public void FocusOnTile(int tx, int tz)
        {
            if (GridManager.Instance != null)
                FocusOn(GridManager.Instance.GridToWorld(tx, tz));
        }
    }
}
