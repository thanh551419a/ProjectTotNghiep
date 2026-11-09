using UnityEngine;
using Assets.Table;
using Simulation;

namespace Presentation.CameraSystem
{
    /// <summary>
    /// Fits the 1v1 court and adds a bounded, smooth presentation-only follow of the committed
    /// native ball position. Camera movement never writes to the simulation or rendered entities.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(200)]
    public class CameraFollow : MonoBehaviour
    {
        [Tooltip("World-unit margin added left/right of the play area.")]
        [SerializeField] private float horizontalMargin = 0.6f;
        [Tooltip("World-unit margin added above/below the play area.")]
        [SerializeField] private float verticalMargin = 0.4f;
        [Tooltip("Fraction of viewport height reserved for the score HUD.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float topHudReserve = 0.10f;
        [Tooltip("Fraction of viewport height reserved for touch controls.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float bottomControlsReserve = 0.16f;
        [Tooltip("Follow the ball using its latest committed SimulationDataTable position.")]
        [SerializeField] private bool followBall = true;
        [Tooltip("Smooth time for camera movement toward the bounded ball position.")]
        [Min(0f)]
        [SerializeField] private float followSmoothTime = 0.22f;
        [SerializeField] private Color backgroundColor = new Color(0.05f, 0.08f, 0.17f, 1f);

        public float TopHudReserve => topHudReserve;
        public float BottomControlsReserve => bottomControlsReserve;
        public Rect LastRequiredRect { get; private set; }
        public Rect LastVisibleRect { get; private set; }
        public Vector3 LastBallTargetWorld { get; private set; }
        public Vector3 LastClampedTarget { get; private set; }
        public bool BallTargetAvailable { get; private set; }

        private Camera cam;
        private float cameraZ;
        private bool cameraZInitialized;
        private Vector3 smoothingVelocity;
        private Vector3 smoothedPosition;
        private bool hasSmoothedPosition;
        private SimulationDataTable boundTable;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            cameraZ = transform.position.z;
            cameraZInitialized = true;
            smoothedPosition = new Vector3(transform.position.x, transform.position.y, cameraZ);
            hasSmoothedPosition = true;
            UpdateFraming(0f);
        }

        private void LateUpdate() => UpdateFraming(Time.unscaledDeltaTime);

        /// <summary>Recomputes the fit for the current viewport without advancing follow smoothing.</summary>
        public void ApplyFramingNow() => UpdateFraming(0f);

        private void UpdateFraming(float deltaTime)
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;
            if (!cameraZInitialized)
            {
                cameraZ = transform.position.z;
                cameraZInitialized = true;
            }

            float aspect = cam.aspect;
            if (aspect <= 0f || float.IsNaN(aspect) || float.IsInfinity(aspect)) return;

            Rect court = CourtGeometry.RequiredVisibleWorld;
            Rect required = Rect.MinMaxRect(
                court.xMin - horizontalMargin,
                court.yMin - verticalMargin,
                court.xMax + horizontalMargin,
                court.yMax + verticalMargin);
            LastRequiredRect = required;

            float usableHeight = 1f - topHudReserve - bottomControlsReserve;
            float halfHeight = Mathf.Max(
                required.height * 0.5f / usableHeight,
                required.width * 0.5f / aspect);
            cam.orthographicSize = halfHeight;

            float visibleHeight = halfHeight * 2f;
            Vector3 current = transform.position;
            Vector3 framed = new Vector3(
                required.center.x,
                required.center.y - visibleHeight * (bottomControlsReserve - topHudReserve) * 0.5f,
                cameraZ);

            Vector3 goal = framed;
            BallTargetAvailable = false;
            if (followBall && TryReadBallTarget(out Vector3 ballTarget))
            {
                BallTargetAvailable = true;
                LastBallTargetWorld = ballTarget;
                goal.x = ballTarget.x;
                goal.y = ballTarget.y;
            }
            else
            {
                LastBallTargetWorld = framed;
                smoothingVelocity = Vector3.zero;
            }

            goal = ClampToCourtBounds(goal, halfHeight, aspect, framed);
            LastClampedTarget = goal;

            if (!hasSmoothedPosition)
            {
                smoothedPosition = new Vector3(current.x, current.y, cameraZ);
                hasSmoothedPosition = true;
            }

            if (followSmoothTime <= 0f || deltaTime <= 0f)
            {
                // Keep the established court framing immediately available to editor capture tools.
                // Runtime follow uses LateUpdate with a positive unscaled delta.
                if (!BallTargetAvailable) smoothedPosition = framed;
            }
            else
            {
                smoothedPosition = Vector3.SmoothDamp(
                    smoothedPosition,
                    goal,
                    ref smoothingVelocity,
                    followSmoothTime,
                    Mathf.Infinity,
                    deltaTime);
            }

            // Re-clamp the smoothed value so viewport resize/reset cannot temporarily reveal beyond
            // the court bounds. Camera depth and rotation are preserved independently of the target.
            smoothedPosition = ClampToCourtBounds(smoothedPosition, halfHeight, aspect, framed);
            smoothedPosition.z = cameraZ;
            transform.position = smoothedPosition;

            float visibleWidth = visibleHeight * aspect;
            LastVisibleRect = new Rect(
                transform.position.x - visibleWidth * 0.5f,
                transform.position.y - halfHeight,
                visibleWidth,
                visibleHeight);
        }

        private bool TryReadBallTarget(out Vector3 target)
        {
            target = Vector3.zero;
            SimulationDataTable table = ResolveSceneTable();
            if (table == null || !table.IsInitialized ||
                !table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ball) ||
                !ball.hasPosition || !ball.hasSize)
            {
                return false;
            }

            target = SimulationEntityReader.AnchorWorld(ball);
            return !float.IsNaN(target.x) && !float.IsInfinity(target.x) &&
                   !float.IsNaN(target.y) && !float.IsInfinity(target.y);
        }

        private SimulationDataTable ResolveSceneTable()
        {
            if (boundTable != null && boundTable.gameObject.scene == gameObject.scene)
                return boundTable;

            boundTable = null;
            SimulationDataTable singleton = SimulationDataTable.Instance;
            if (singleton != null && singleton.gameObject.scene == gameObject.scene)
            {
                boundTable = singleton;
                return boundTable;
            }

            SimulationDataTable[] tables = FindObjectsByType<SimulationDataTable>(FindObjectsInactive.Include);
            foreach (SimulationDataTable candidate in tables)
            {
                if (candidate != null && candidate.gameObject.scene == gameObject.scene)
                {
                    boundTable = candidate;
                    return boundTable;
                }
            }
            return null;
        }

        private Vector3 ClampToCourtBounds(Vector3 desired, float halfHeight, float aspect, Vector3 fallback)
        {
            Rect bounds = CourtGeometry.BigRectWorld;
            float halfWidth = halfHeight * aspect;

            float minX = bounds.xMax - halfWidth;
            float maxX = bounds.xMin + halfWidth;
            float playableHalfBelowCenter = halfHeight * (1f - 2f * bottomControlsReserve);
            float playableHalfAboveCenter = halfHeight * (1f - 2f * topHudReserve);
            float minY = bounds.yMax - playableHalfAboveCenter;
            float maxY = bounds.yMin + playableHalfBelowCenter;

            if (minX > maxX || minY > maxY)
                return new Vector3(fallback.x, fallback.y, cameraZ);

            desired.x = Mathf.Clamp(desired.x, minX, maxX);
            desired.y = Mathf.Clamp(desired.y, minY, maxY);
            desired.z = cameraZ;
            return desired;
        }
    }
}
