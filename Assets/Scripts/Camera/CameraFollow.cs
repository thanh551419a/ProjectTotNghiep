using UnityEngine;
using Assets.Table;
using Simulation;

namespace Presentation.CameraSystem
{
    /// <summary>
    /// Fits the 1v1 court and adds a bounded, smooth presentation-only follow of the committed
    /// native ball position. Camera movement never writes to the simulation or rendered entities.
    /// </summary>
    [ExecuteAlways]
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
        [Tooltip("Desired fraction of the viewport height occupied by the native player bounds, when court fit permits.")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float targetCharacterHeightFraction = 0.25f;
        [Tooltip("Desired fraction of the viewport width occupied by the native player bounds, when court fit permits.")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float targetCharacterWidthFraction = 1f / 6f;
        [SerializeField] private Color backgroundColor = new Color(0.05f, 0.08f, 0.17f, 1f);
        [Tooltip("Fit the court automatically until either orthographic size is edited.")]
        [SerializeField] private bool autoFitCourt = true;
        [Tooltip("Orthographic half-height in world units. Edit this or Camera Size to set a manual zoom.")]
        [Min(0.01f)]
        [SerializeField] private float targetOrthographicSize = 5f;

        public float TopHudReserve => topHudReserve;
        public float BottomControlsReserve => bottomControlsReserve;
        public Rect LastRequiredRect { get; private set; }
        public Rect LastVisibleRect { get; private set; }
        public Vector3 LastBallTargetWorld { get; private set; }
        public Vector3 LastClampedTarget { get; private set; }
        public bool BallTargetAvailable { get; private set; }
        public Vector2 LastCharacterWorldSize { get; private set; }
        public float LastCharacterScreenHeightFraction { get; private set; }
        public float LastCharacterScreenWidthFraction { get; private set; }

        private Camera cam;
        private float cameraZ;
        private bool cameraZInitialized;
        private Vector3 smoothingVelocity;
        private Vector3 smoothedPosition;
        private bool hasSmoothedPosition;
        private SimulationDataTable boundTable;
        private bool sceneTableSearchAttempted;
        private bool zoomInitialized;
        private float observedTargetOrthographicSize;
        private float appliedOrthographicSize;

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
            BindSceneTable();
            UpdateFraming(0f);
        }

        private void OnEnable()
        {
            if (cam == null) cam = GetComponent<Camera>();
            BindSceneTable();
            if (!Application.isPlaying) ApplyFramingNow();
        }

        private void OnValidate()
        {
            targetOrthographicSize = Mathf.Max(0.01f, targetOrthographicSize);
            if (!Mathf.Approximately(targetOrthographicSize, observedTargetOrthographicSize) && observedTargetOrthographicSize > 0f)
                autoFitCourt = false;
            if (cam == null) cam = GetComponent<Camera>();
            if (!autoFitCourt && cam != null)
            {
                cam.orthographicSize = targetOrthographicSize;
                observedTargetOrthographicSize = targetOrthographicSize;
                appliedOrthographicSize = targetOrthographicSize;
                zoomInitialized = true;
            }
        }

        private void LateUpdate() => UpdateFraming(Time.unscaledDeltaTime);

        /// <summary>Recomputes the fit for the current viewport without advancing follow smoothing.</summary>
        public void ApplyFramingNow() => UpdateFraming(0f);

        private void UpdateFraming(float deltaTime)
        {
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
            float courtHalfHeight = Mathf.Max(
                required.height * 0.5f / usableHeight,
                required.width * 0.5f / aspect);
            float characterHalfHeight = 0f;
            if (TryReadCharacterWorldSize(out Vector2 characterSize))
            {
                LastCharacterWorldSize = characterSize;
                characterHalfHeight = Mathf.Max(
                    characterSize.y / (2f * targetCharacterHeightFraction),
                    characterSize.x / (2f * aspect * targetCharacterWidthFraction));
            }
            else LastCharacterWorldSize = Vector2.zero;

            // Automatic framing keeps the court visible; a manual zoom may intentionally crop it.
            float fitHalfHeight = Mathf.Max(courtHalfHeight, characterHalfHeight);
            float halfHeight = ResolveOrthographicSize(fitHalfHeight);
            LastCharacterScreenHeightFraction = LastCharacterWorldSize.y / (2f * halfHeight);
            LastCharacterScreenWidthFraction = LastCharacterWorldSize.x / (2f * halfHeight * aspect);

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

        private float ResolveOrthographicSize(float fitHalfHeight)
        {
            if (!zoomInitialized)
            {
                zoomInitialized = true;
                observedTargetOrthographicSize = targetOrthographicSize;
                appliedOrthographicSize = cam.orthographicSize;
            }

            bool targetEdited = !Mathf.Approximately(targetOrthographicSize, observedTargetOrthographicSize);
            bool cameraEdited = !Mathf.Approximately(cam.orthographicSize, appliedOrthographicSize);
            if (targetEdited)
                autoFitCourt = false;
            else if (cameraEdited)
            {
                autoFitCourt = false;
                targetOrthographicSize = cam.orthographicSize;
            }

            float size = autoFitCourt ? fitHalfHeight : Mathf.Max(0.01f, targetOrthographicSize);
            targetOrthographicSize = size;
            observedTargetOrthographicSize = size;
            appliedOrthographicSize = size;
            cam.orthographicSize = size;
            return size;
        }

        private bool TryReadBallTarget(out Vector3 target)
        {
            target = Vector3.zero;
            SimulationDataTable table = boundTable;
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

        private bool TryReadCharacterWorldSize(out Vector2 size)
        {
            size = Vector2.zero;
            SimulationDataTable table = boundTable;
            if (table == null || !table.IsInitialized ||
                !table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord player) ||
                !player.hasSize || player.sizeX <= 0f || player.sizeY <= 0f)
                return false;

            // Use the same authoritative native-to-world mapping as the court and entity reader.
            size.x = Mathf.Abs(CourtGeometry.ToWorldX(player.posX + player.sizeX) - CourtGeometry.ToWorldX(player.posX));
            size.y = Mathf.Abs(CourtGeometry.ToWorldY(player.posY + player.sizeY) - CourtGeometry.ToWorldY(player.posY));
            return size.x > 0f && size.y > 0f &&
                   !float.IsNaN(size.x) && !float.IsInfinity(size.x) &&
                   !float.IsNaN(size.y) && !float.IsInfinity(size.y);
        }

        private SimulationDataTable BindSceneTable()
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

            // The scene-wide fallback is an initialization lookup, never a LateUpdate poll.
            if (sceneTableSearchAttempted) return null;
            sceneTableSearchAttempted = true;
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

            float minX = halfWidth <= bounds.width * 0.5f
                ? bounds.xMin + halfWidth : bounds.xMax - halfWidth;
            float maxX = halfWidth <= bounds.width * 0.5f
                ? bounds.xMax - halfWidth : bounds.xMin + halfWidth;
            float playableHalfBelowCenter = halfHeight * (1f - 2f * bottomControlsReserve);
            float playableHalfAboveCenter = halfHeight * (1f - 2f * topHudReserve);
            float minY = halfHeight <= bounds.height * 0.5f
                ? bounds.yMin + halfHeight : bounds.yMax - playableHalfAboveCenter;
            float maxY = halfHeight <= bounds.height * 0.5f
                ? bounds.yMax - halfHeight : bounds.yMin + playableHalfBelowCenter;

            if (minX > maxX || minY > maxY)
                return new Vector3(fallback.x, fallback.y, cameraZ);

            desired.x = Mathf.Clamp(desired.x, minX, maxX);
            desired.y = Mathf.Clamp(desired.y, minY, maxY);
            desired.z = cameraZ;
            return desired;
        }
    }
}
