using UnityEngine;

namespace OpenTTDUnity
{
    /// <summary>
    /// Isometric camera controller for an orthographic camera.
    ///
    /// Rotation is always a multiple of 90° (North, East, South, West view)
    /// and is interpolated smoothly using <see cref="Mathf.LerpAngle"/>.
    /// Pan direction is relative to the current camera facing so WASD always
    /// moves in screen-space terms, not world-space.
    ///
    /// Attach this component to the Camera rig root GameObject.
    /// The Camera component should be a child (or this same GameObject).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class IsoCameraController : MonoBehaviour
    {
        // -------------------------------------------------------
        // Inspector-exposed configuration
        // -------------------------------------------------------

        [Header("References")]
        [SerializeField, Tooltip("The orthographic camera. Defaults to Camera on this GameObject.")]
        private Camera _camera;

        [Header("Pan")]
        [SerializeField, Tooltip("World-unit pan speed at 1× zoom.")]
        private float _panSpeed = Constants.CameraPanSpeed;

        [SerializeField, Tooltip("Multiplier applied when holding Shift to pan faster.")]
        private float _panShiftMultiplier = 2f;

        [Header("Zoom")]
        [SerializeField, Tooltip("Orthographic size change per scroll-wheel unit.")]
        private float _zoomSpeed = Constants.CameraZoomSpeed;

        [SerializeField]
        private float _minOrthoSize = Constants.CameraMinOrthoSize;

        [SerializeField]
        private float _maxOrthoSize = Constants.CameraMaxOrthoSize;

        [Header("Rotation")]
        [SerializeField, Tooltip("Lerp factor per frame for rotation smoothing (higher = snappier).")]
        private float _rotationLerpSpeed = 8f;

        [Header("Isometric Angles")]
        [SerializeField, Tooltip("Pitch of the camera from horizontal (classic TTD uses ~30°).")]
        private float _pitchAngle = 30f;

        // -------------------------------------------------------
        // State
        // -------------------------------------------------------

        // Target Y rotation (in 90° increments: 0, 90, 180, 270)
        private float _targetYaw;
        // Current smoothed Y rotation
        private float _currentYaw;

        // Middle-mouse drag
        private bool  _isDragging;
        private Vector3 _dragOriginScreen;
        private Vector3 _dragOriginWorld;

        // Grid world bounds for clamping
        private float _worldWidth;
        private float _worldDepth;

        // Smooth zoom target
        private float _targetOrthoSize;

        // -------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------

        private void Awake()
        {
            if (_camera == null) _camera = GetComponent<Camera>();

            if (!_camera.orthographic)
            {
                Debug.LogWarning("[IsoCameraController] Camera is not orthographic — forcing it on.");
                _camera.orthographic = true;
            }
        }

        private void Start()
        {
            // World size from constants
            _worldWidth = Constants.GridWidth  * Constants.TileSize;
            _worldDepth = Constants.GridHeight * Constants.TileSize;

            // Position camera at the centre of the map
            float cx = _worldWidth  * 0.5f;
            float cz = _worldDepth  * 0.5f;
            transform.position = new Vector3(cx, 0f, cz);

            // Initial angles — face from the North-East (classic TTD default)
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

        // -------------------------------------------------------
        // Input handlers
        // -------------------------------------------------------

        private void HandleKeyboardPan()
        {
            float dx = 0f, dz = 0f;

            // WASD + Arrow key support
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    dz += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  dz -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dx += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  dx -= 1f;

            if (dx == 0f && dz == 0f) return;

            float speed = _panSpeed;
            // Scale pan speed proportionally to zoom level so the map doesn't fly
            // past when zoomed out
            speed *= _camera.orthographicSize / Constants.CameraDefaultOrthoSize;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                speed *= _panShiftMultiplier;

            // Rotate the pan direction by the current camera yaw so WASD is
            // always relative to the screen, not world axes.
            float yawRad = _currentYaw * Mathf.Deg2Rad;
            float cos    = Mathf.Cos(yawRad);
            float sin    = Mathf.Sin(yawRad);

            float worldDx = dx * cos - dz * sin;
            float worldDz = dx * sin + dz * cos;

            Vector3 pos = transform.position;
            pos.x += worldDx * speed * Time.unscaledDeltaTime;
            pos.z += worldDz * speed * Time.unscaledDeltaTime;
            pos = ClampPosition(pos);
            transform.position = pos;
        }

        private void HandleMiddleMousePan()
        {
            // Start drag
            if (Input.GetMouseButtonDown(2))
            {
                _isDragging = true;
                _dragOriginScreen = Input.mousePosition;
                _dragOriginWorld  = ScreenToWorldXZ(_dragOriginScreen);
            }

            // End drag
            if (Input.GetMouseButtonUp(2))
            {
                _isDragging = false;
            }

            // Continue drag
            if (_isDragging)
            {
                Vector3 currentWorldPos = ScreenToWorldXZ(Input.mousePosition);
                Vector3 delta = _dragOriginWorld - currentWorldPos;
                Vector3 pos   = transform.position + delta;
                pos = ClampPosition(pos);
                transform.position = pos;
                // Update origin to current position to avoid jumpy acceleration
                _dragOriginWorld = ScreenToWorldXZ(Input.mousePosition);
            }
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.0001f) return;

            _targetOrthoSize -= scroll * _zoomSpeed;
            _targetOrthoSize  = Mathf.Clamp(_targetOrthoSize, _minOrthoSize, _maxOrthoSize);

            // Smooth zoom towards target
            _camera.orthographicSize = Mathf.Lerp(
                _camera.orthographicSize,
                _targetOrthoSize,
                Time.unscaledDeltaTime * 12f);
        }

        private void HandleRotation()
        {
            // Q rotates counter-clockwise (add 90°), E clockwise (subtract 90°)
            if (Input.GetKeyDown(KeyCode.Q)) _targetYaw = NormalizeAngle(_targetYaw + 90f);
            if (Input.GetKeyDown(KeyCode.E)) _targetYaw = NormalizeAngle(_targetYaw - 90f);

            // Smooth interpolation — DOTween-free, plain Mathf.LerpAngle
            _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw,
                Time.unscaledDeltaTime * _rotationLerpSpeed);

            // Snap to final angle when very close to avoid infinite lerp
            if (Mathf.Abs(Mathf.DeltaAngle(_currentYaw, _targetYaw)) < 0.1f)
                _currentYaw = _targetYaw;
        }

        // -------------------------------------------------------
        // Transform application
        // -------------------------------------------------------

        /// <summary>
        /// Rebuilds the camera rig transform from the current pivot (transform.position)
        /// plus the desired yaw and pitch.  The Camera is offset along its local -Z
        /// so it looks down at the pivot point.
        /// </summary>
        private void ApplyTransform(bool snap)
        {
            float yaw = snap ? _targetYaw : _currentYaw;
            // Smoothly advance ortho size even outside zoom input
            _camera.orthographicSize = Mathf.Lerp(
                _camera.orthographicSize, _targetOrthoSize,
                snap ? 1f : Time.unscaledDeltaTime * 12f);

            // Build the rotation: first pitch down, then yaw around world-Y
            Quaternion rot = Quaternion.Euler(_pitchAngle, yaw, 0f);
            _camera.transform.rotation = rot;

            // Position the camera above and behind the pivot
            // The offset distance is chosen so that at the default ortho size
            // the pivot sits comfortably in frame.
            float dist = _camera.orthographicSize * 4f;
            Vector3 pivotPos = transform.position;
            // Add a fixed world-Y height so the camera is always above terrain
            pivotPos.y = Constants.MaxHeight * Constants.HeightStep;
            _camera.transform.position = pivotPos + rot * new Vector3(0f, 0f, -dist);
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /// <summary>
        /// Projects a screen point onto the XZ world plane (at world Y = 0),
        /// using the camera's current inverse view-projection.
        /// </summary>
        private Vector3 ScreenToWorldXZ(Vector3 screenPos)
        {
            Ray ray = _camera.ScreenPointToRay(screenPos);
            // Intersect with the Y=0 plane
            if (Mathf.Abs(ray.direction.y) > 0.0001f)
            {
                float t = -ray.origin.y / ray.direction.y;
                if (t > 0f) return ray.origin + ray.direction * t;
            }
            // Fallback: just use the ray origin projected flat
            return new Vector3(ray.origin.x, 0f, ray.origin.z);
        }

        private Vector3 ClampPosition(Vector3 pos)
        {
            float margin = 5f * Constants.TileSize;
            pos.x = Mathf.Clamp(pos.x, -margin, _worldWidth  + margin);
            pos.z = Mathf.Clamp(pos.z, -margin, _worldDepth  + margin);
            return pos;
        }

        private static float NormalizeAngle(float a)
        {
            a %= 360f;
            if (a < 0f) a += 360f;
            return a;
        }

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /// <summary>
        /// Instantly moves the camera pivot to look at a world-space position.
        /// </summary>
        public void FocusOn(Vector3 worldPos)
        {
            Vector3 pos   = transform.position;
            pos.x = worldPos.x;
            pos.z = worldPos.z;
            transform.position = ClampPosition(pos);
        }

        /// <summary>
        /// Instantly moves the camera pivot to look at a tile grid coordinate.
        /// </summary>
        public void FocusOnTile(int tx, int tz)
        {
            if (GridManager.Instance != null)
                FocusOn(GridManager.Instance.GridToWorld(tx, tz));
        }

        /// <summary>
        /// Returns the current camera orthographic size.
        /// </summary>
        public float OrthoSize => _camera.orthographicSize;
    }
}
