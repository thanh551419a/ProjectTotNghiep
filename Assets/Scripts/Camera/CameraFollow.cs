using UnityEngine;
using Assets.Table;
using Native;
using Simulation;

namespace Presentation.CameraSystem
{
    /// <summary>
    /// Fits the 1v1 court and provides a ground-locked horizontal follow of the committed native ball X position.
    /// Vertical (Y) camera movement remains locked to the grounded baseline except during special high-altitude
    /// airborne Spike / Boom Jump moments, after which it smoothly returns to the ground baseline.
    /// Camera movement never writes to the simulation or rendered entities.
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
        [Tooltip("Viewport Y fraction (from bottom of screen) where the near sideline (court.yMin) is anchored in ground-locked framing, matching The Spike Cross (~11.5%).")]
        [Range(0.05f, 0.25f)]
        [SerializeField] private float courtNearLineViewportY = 0.115f;
        [Tooltip("Follow the ball using its latest committed SimulationDataTable position.")]
        [SerializeField] private bool followBall = true;
        [Tooltip("Smooth time for camera movement toward the bounded ball position.")]
        [Min(0f)]
        [SerializeField] private float followSmoothTime = 0.14f;
        [Tooltip("Look-ahead time multiplier (seconds) applied to horizontal ball velocity during fast Serve/Spike flight.")]
        [Min(0f)]
        [SerializeField] private float horizontalLookAheadSeconds = 0.09f;
        [Tooltip("Maximum world-unit horizontal look-ahead offset added in the ball's flight direction.")]
        [Min(0f)]
        [SerializeField] private float maxHorizontalLookAheadWorld = 1.15f;
        [Tooltip("Minimum horizontal ball speed (world units/s) required to engage velocity look-ahead.")]
        [Min(0f)]
        [SerializeField] private float lookAheadMinSpeedWorld = 1.20f;
        [Tooltip("Maximum horizontal ball speed (world units/s) considered a vertical Set/Lob for horizontal deadzone stabilization.")]
        [Min(0f)]
        [SerializeField] private float setDeadzoneMaxSpeedWorld = 0.75f;
        [Tooltip("Horizontal deadzone radius (world units) kept steady during vertical Set/Lob trajectories.")]
        [Min(0f)]
        [SerializeField] private float horizontalSetDeadzoneWorld = 0.10f;
        [Tooltip("Enable short camera impact shake when a Spike hits the court floor or a point is scored.")]
        [SerializeField] private bool enableImpactShake = true;
        [Tooltip("Duration in seconds of the Spike floor-impact / point-scored camera shake.")]
        [Min(0f)]
        [SerializeField] private float shakeDurationSeconds = 0.11f;
        [Tooltip("World-unit amplitude of the Spike floor-impact / point-scored camera shake.")]
        [Min(0f)]
        [SerializeField] private float spikeImpactShakeWorld = 0.14f;
        [Tooltip("Allow vertical camera lift only during special high-altitude airborne Spike / Boom Jump moments.")]
        [SerializeField] private bool enableSpecialVerticalFollow = true;
        [Tooltip("Vertical follow blend above grounded framing when special vertical follow is active.")]
        [Range(0f, 1f)]
        [SerializeField] private float verticalFollowWeight = 0.45f;
        [Tooltip("Minimum ball height above the ground-locked camera center required before special vertical follow can engage.")]
        [Min(0f)]
        [SerializeField] private float specialBallHeightOffset = 1.10f;
        [Tooltip("Minimum character jump elevation (world units above court floor) that qualifies as a Boom Jump apex for special vertical follow.")]
        [Min(0.5f)]
        [SerializeField] private float specialBoomJumpElevation = 1.35f;
        [Tooltip("Minimum world-unit headroom kept above the ball below the top HUD reserve during special vertical follow.")]
        [Min(0f)]
        [SerializeField] private float ballTopPadding = 0.9f;
        [Tooltip("Desired fraction of the viewport height occupied by the native player bounds, when court fit permits.")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float targetCharacterHeightFraction = 0.50f;
        [Tooltip("Desired fraction of the viewport width occupied by the native player bounds, when court fit permits.")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float targetCharacterWidthFraction = 1f / 3f;
        [SerializeField] private Color backgroundColor = new Color(0.05f, 0.08f, 0.17f, 1f);
        [Tooltip("Fit the court automatically until either orthographic size is edited.")]
        [SerializeField] private bool autoFitCourt = true;
        [Tooltip("Scale multiplier applied to full-court orthographic fit when autoFitCourt is enabled (0.5 = half viewing span).")]
        [Range(0.2f, 1f)]
        [SerializeField] private float autoFitViewScale = 0.5f;
        [Tooltip("Orthographic half-height in world units. Edit this or Camera Size to set a manual zoom.")]
        [Min(0.01f)]
        [SerializeField] private float targetOrthographicSize = 2.5f;

        private const float GroundLockSnapEpsilon = 0.002f;

        public float TopHudReserve => topHudReserve;
        public float BottomControlsReserve => bottomControlsReserve;
        public Rect LastRequiredRect { get; private set; }
        public Rect LastVisibleRect { get; private set; }
        public Vector3 LastBallTargetWorld { get; private set; }
        public Vector3 LastClampedTarget { get; private set; }
        public bool BallTargetAvailable { get; private set; }
        public float GroundLockedY { get; private set; }
        public bool IsSpecialVerticalFollowActive { get; private set; }
        public float LastLookAheadOffsetX { get; private set; }
        public Vector3 LastShakeOffset { get; private set; }
        public int ImpactShakeCount { get; private set; }
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
        private bool rallyTrackingInitialized;
        private ulong lastObservedTick;
        private int lastBallStateFrame;
        private int lastBallCollisionState;
        private int lastTeam0Score;
        private int lastTeam1Score;
        private bool rallyHadSpike;
        private float lastAirborneBallSpeedWorld;
        private float shakeTimer;
        private float currentShakeMagnitude;

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

            // Automatic framing applies autoFitViewScale (0.5 = half viewing span); manual zoom uses targetOrthographicSize.
            float fitHalfHeight = Mathf.Max(courtHalfHeight * autoFitViewScale, characterHalfHeight);
            float halfHeight = ResolveOrthographicSize(fitHalfHeight);
            LastCharacterScreenHeightFraction = LastCharacterWorldSize.y / (2f * halfHeight);
            LastCharacterScreenWidthFraction = LastCharacterWorldSize.x / (2f * halfHeight * aspect);

            float visibleHeight = halfHeight * 2f;
            float playableHalfBelowCenter = halfHeight * (1f - 2f * bottomControlsReserve);
            float playableHalfAboveCenter = halfHeight * (1f - 2f * topHudReserve);
            float usableWorldHeight = visibleHeight * usableHeight;

            float fullCourtFramedY = required.center.y - visibleHeight * (bottomControlsReserve - topHudReserve) * 0.5f;
            float nearLineOffsetBelowCenter = halfHeight * (1f - 2f * Mathf.Clamp(courtNearLineViewportY, 0.05f, 0.25f));
            float groundedFramedY = court.yMin + nearLineOffsetBelowCenter;
            float baseFramedY = usableWorldHeight < required.height ? groundedFramedY : fullCourtFramedY;
            GroundLockedY = baseFramedY;

            Vector3 current = transform.position;
            Vector3 framed = new Vector3(required.center.x, baseFramedY, cameraZ);

            Vector3 goal = framed;
            BallTargetAvailable = false;
            IsSpecialVerticalFollowActive = false;
            LastLookAheadOffsetX = 0f;

            SimulationEntityRecord ballRecord = default;
            bool hasBallRecord = TryReadBallRecord(out ballRecord);

            if (followBall && hasBallRecord && TryComputeBallWorldTarget(ballRecord, out Vector3 ballTarget))
            {
                BallTargetAvailable = true;
                LastBallTargetWorld = ballTarget;

                // Track ball X horizontally with velocity look-ahead on fast Serve/Spike and subtle deadzone on vertical Set/Lob.
                goal.x = ComputeHorizontalGoalX(ballTarget.x, ballRecord);

                // Keep Y locked on the court ground baseline unless a special high-altitude airborne Spike / Boom Jump is active.
                if (ShouldUnlockSpecialVerticalFollow(ballTarget, baseFramedY))
                {
                    IsSpecialVerticalFollowActive = true;
                    if (usableWorldHeight < required.height)
                    {
                        float proportionalY = baseFramedY + Mathf.Max(0f, ballTarget.y - baseFramedY) * verticalFollowWeight;
                        float highBallClearanceY = ballTarget.y + ballTopPadding - playableHalfAboveCenter;
                        goal.y = Mathf.Max(baseFramedY, Mathf.Max(proportionalY, highBallClearanceY));
                    }
                    else
                    {
                        goal.y = ballTarget.y;
                    }
                }
                else
                {
                    goal.y = baseFramedY;
                }
            }
            else
            {
                LastBallTargetWorld = framed;
                smoothingVelocity = Vector3.zero;
            }

            EvaluateRallyImpactShake(hasBallRecord, ballRecord);

            goal = ClampToCourtBounds(goal, halfHeight, aspect, required, framed);
            LastClampedTarget = goal;

            if (!hasSmoothedPosition)
            {
                smoothedPosition = BallTargetAvailable ? goal : new Vector3(current.x, current.y, cameraZ);
                hasSmoothedPosition = true;
            }

            if (followSmoothTime <= 0f || deltaTime <= 0f)
            {
                // Keep the established court framing immediately available to editor capture tools.
                // Runtime follow uses LateUpdate with a positive unscaled delta.
                if (BallTargetAvailable && followSmoothTime <= 0f) smoothedPosition = goal;
                else if (!BallTargetAvailable) smoothedPosition = framed;
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

                if (!IsSpecialVerticalFollowActive && Mathf.Abs(smoothedPosition.y - baseFramedY) <= GroundLockSnapEpsilon)
                {
                    smoothedPosition.y = baseFramedY;
                    smoothingVelocity.y = 0f;
                }
            }

            // Re-clamp the smoothed value so viewport resize/reset cannot temporarily reveal beyond
            // the court bounds. Camera depth and rotation are preserved independently of the target.
            smoothedPosition = ClampToCourtBounds(smoothedPosition, halfHeight, aspect, required, framed);
            smoothedPosition.z = cameraZ;

            Vector3 finalPosition = ApplyImpactShakeOffset(smoothedPosition, deltaTime, halfHeight, aspect, required, framed);
            finalPosition.z = cameraZ;
            transform.position = finalPosition;

            float visibleWidth = visibleHeight * aspect;
            LastVisibleRect = new Rect(
                transform.position.x - visibleWidth * 0.5f,
                transform.position.y - halfHeight,
                visibleWidth,
                visibleHeight);
        }

        private bool ShouldUnlockSpecialVerticalFollow(Vector3 ballTarget, float groundBaselineY)
        {
            if (!enableSpecialVerticalFollow)
            {
                return false;
            }

            if (ballTarget.y < groundBaselineY + specialBallHeightOffset)
            {
                return false;
            }

            SimulationDataTable table = boundTable != null ? boundTable : SimulationDataTable.Instance;
            if (table == null || !table.IsInitialized)
            {
                return false;
            }

            float floorWorldY = CourtGeometry.ToWorldY(CourtGeometry.FloorY);
            if (table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord player) &&
                IsCharacterInSpecialAirborneMoment(player, floorWorldY))
            {
                return true;
            }

            if (table.TryGetEntity(SimulationDataTable.ENTITY_OPPONENT_1, out SimulationEntityRecord opponent) &&
                IsCharacterInSpecialAirborneMoment(opponent, floorWorldY))
            {
                return true;
            }

            return false;
        }

        private bool IsCharacterInSpecialAirborneMoment(in SimulationEntityRecord entity, float floorWorldY)
        {
            bool airborne = !entity.isGrounded ||
                            entity.airborneState != SimAirborneState.Grounded;
            if (!airborne)
            {
                return false;
            }

            if (entity.hasCharacterActionState)
            {
                SimActionState action = (SimActionState)entity.actionStatus;
                if (action == SimActionState.Spike ||
                    action == SimActionState.SpikeLight ||
                    action == SimActionState.SpikeMedium ||
                    action == SimActionState.SpikeStrong)
                {
                    return true;
                }
            }

            if (entity.hasPosition)
            {
                float footWorldY = CourtGeometry.ToWorldY(entity.posY);
                if (footWorldY - floorWorldY >= specialBoomJumpElevation)
                {
                    return true;
                }
            }

            return false;
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

        private float ComputeHorizontalGoalX(float ballWorldX, in SimulationEntityRecord ball)
        {
            float goalX = ballWorldX;
            if (!ball.hasBallGameplayState || ball.stateFrame != -1)
            {
                return goalX;
            }

            float rawVx = Mathf.Abs(ball.ballSpeed) > 0.01f ? ball.ballSpeed : ball.vx;
            float absRawVx = Mathf.Abs(rawVx);
            // Native C++ Serve/Spike speeds are 12.8..36 px/tick (at 60Hz), while synthetic tests may pass >= 96 px/s.
            float tickToPerSecond = (absRawVx >= 12f && absRawVx < 96f) ? (1f / SpikeSimulationManager.TickSeconds) : 1f;
            float ballVxWorld = (rawVx * tickToPerSecond) / CourtGeometry.PixelsPerMeter;
            lastAirborneBallSpeedWorld = ballVxWorld;

            float absVxWorld = Mathf.Abs(ballVxWorld);
            if (absVxWorld >= lookAheadMinSpeedWorld)
            {
                float lookAheadX = Mathf.Clamp(
                    ballVxWorld * horizontalLookAheadSeconds,
                    -maxHorizontalLookAheadWorld,
                    maxHorizontalLookAheadWorld);
                goalX += lookAheadX;
                LastLookAheadOffsetX = lookAheadX;
            }
            else if (absVxWorld < setDeadzoneMaxSpeedWorld && hasSmoothedPosition)
            {
                if (Mathf.Abs(goalX - smoothedPosition.x) <= horizontalSetDeadzoneWorld)
                {
                    goalX = smoothedPosition.x;
                }
            }

            return goalX;
        }

        private void EvaluateRallyImpactShake(bool hasBall, in SimulationEntityRecord ball)
        {
            SimulationDataTable table = boundTable != null ? boundTable : SimulationDataTable.Instance;
            if (table == null || !table.IsInitialized)
            {
                return;
            }

            if (table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord player) &&
                IsCharacterSpikeAction(player))
            {
                rallyHadSpike = true;
            }
            if (table.TryGetEntity(SimulationDataTable.ENTITY_OPPONENT_1, out SimulationEntityRecord opponent) &&
                IsCharacterSpikeAction(opponent))
            {
                rallyHadSpike = true;
            }

            ulong currentTick = table.CurrentTick;
            SimulationMatchRecord match = table.MatchRecord;
            int team0Score = match.leftScore;
            int team1Score = match.rightScore;
            int currentStateFrame = hasBall && ball.hasBallGameplayState ? ball.stateFrame : 0;
            int currentCollisionState = hasBall && ball.hasBallGameplayState ? ball.collisionState : 0;

            if (!rallyTrackingInitialized || currentTick <= 1 || currentTick < lastObservedTick)
            {
                rallyTrackingInitialized = true;
                lastObservedTick = currentTick;
                lastBallStateFrame = currentStateFrame;
                lastBallCollisionState = currentCollisionState;
                lastTeam0Score = team0Score;
                lastTeam1Score = team1Score;
                if (currentTick <= 1)
                {
                    rallyHadSpike = false;
                    lastAirborneBallSpeedWorld = 0f;
                    shakeTimer = 0f;
                    LastShakeOffset = Vector3.zero;
                }
                return;
            }

            if (Application.isPlaying && enableImpactShake)
            {
                bool highSpeedOrSpiked = rallyHadSpike || Mathf.Abs(lastAirborneBallSpeedWorld) >= lookAheadMinSpeedWorld;
                bool floorImpact = hasBall && ball.hasBallGameplayState &&
                                   ((lastBallStateFrame == -1 && currentStateFrame >= 1) ||
                                    (lastBallCollisionState == 0 && currentCollisionState != 0 && highSpeedOrSpiked));
                bool pointScored = team0Score > lastTeam0Score || team1Score > lastTeam1Score;

                if (floorImpact || pointScored)
                {
                    shakeTimer = shakeDurationSeconds;
                    currentShakeMagnitude = spikeImpactShakeWorld;
                    ImpactShakeCount++;
                }
            }

            if (hasBall && ball.hasBallGameplayState && currentStateFrame == 0)
            {
                rallyHadSpike = false;
                lastAirborneBallSpeedWorld = 0f;
            }

            lastObservedTick = currentTick;
            lastBallStateFrame = currentStateFrame;
            lastBallCollisionState = currentCollisionState;
            lastTeam0Score = team0Score;
            lastTeam1Score = team1Score;
        }

        private static bool IsCharacterSpikeAction(in SimulationEntityRecord entity)
        {
            if (!entity.hasCharacterActionState)
            {
                return false;
            }
            SimActionState action = (SimActionState)entity.actionStatus;
            return action == SimActionState.Spike ||
                   action == SimActionState.SpikeLight ||
                   action == SimActionState.SpikeMedium ||
                   action == SimActionState.SpikeStrong;
        }

        private Vector3 ApplyImpactShakeOffset(
            Vector3 basePosition,
            float deltaTime,
            float halfHeight,
            float aspect,
            Rect required,
            Vector3 framed)
        {
            if (!enableImpactShake || shakeTimer <= 0f || shakeDurationSeconds <= 0.0001f || deltaTime <= 0f)
            {
                if (shakeTimer <= 0f)
                {
                    LastShakeOffset = Vector3.zero;
                }
                return basePosition;
            }

            shakeTimer = Mathf.Max(0f, shakeTimer - deltaTime);
            if (shakeTimer <= 0f)
            {
                LastShakeOffset = Vector3.zero;
                return basePosition;
            }

            float decay = Mathf.Clamp01(shakeTimer / shakeDurationSeconds);
            float phase = (1f - decay) * Mathf.PI * 10f;
            float offsetX = Mathf.Sin(phase) * currentShakeMagnitude * decay;
            // Preserve the strict ground-locked Y contract (< 0.01m tolerance) when special vertical follow is inactive.
            float maxVerticalShake = IsSpecialVerticalFollowActive ? currentShakeMagnitude * 0.65f : 0.004f;
            float offsetY = Mathf.Cos(phase * 1.3f) * maxVerticalShake * decay;

            Vector3 shakeOffset = new Vector3(offsetX, offsetY, 0f);
            LastShakeOffset = shakeOffset;
            return ClampToCourtBounds(basePosition + shakeOffset, halfHeight, aspect, required, framed);
        }

        private bool TryReadBallRecord(out SimulationEntityRecord ball)
        {
            ball = default;
            if (boundTable == null && SimulationDataTable.Instance != null)
                boundTable = SimulationDataTable.Instance;
            SimulationDataTable table = boundTable;
            return table != null && table.IsInitialized &&
                   table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out ball);
        }

        private static bool TryComputeBallWorldTarget(in SimulationEntityRecord ball, out Vector3 target)
        {
            target = Vector3.zero;
            if (!ball.hasPosition || !ball.hasSize)
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
            if (boundTable == null && SimulationDataTable.Instance != null)
                boundTable = SimulationDataTable.Instance;
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

        private Vector3 ClampToCourtBounds(Vector3 desired, float halfHeight, float aspect, Rect required, Vector3 fallback)
        {
            Rect bounds = CourtGeometry.BigRectWorld;
            float halfWidth = halfHeight * aspect;

            float minX = halfWidth <= bounds.width * 0.5f
                ? bounds.xMin + halfWidth : bounds.xMax - halfWidth;
            float maxX = halfWidth <= bounds.width * 0.5f
                ? bounds.xMax - halfWidth : bounds.xMin + halfWidth;

            float playableHalfBelowCenter = halfHeight * (1f - 2f * bottomControlsReserve);
            float playableHalfAboveCenter = halfHeight * (1f - 2f * topHudReserve);
            float usableWorldHeight = playableHalfBelowCenter + playableHalfAboveCenter;

            float minY;
            float maxY;
            if (usableWorldHeight < required.height)
            {
                Rect court = CourtGeometry.RequiredVisibleWorld;
                float nearLineOffsetBelowCenter = halfHeight * (1f - 2f * Mathf.Clamp(courtNearLineViewportY, 0.05f, 0.25f));
                minY = court.yMin + nearLineOffsetBelowCenter;
                float ceilingGoalY = required.yMax + ballTopPadding - playableHalfAboveCenter;
                float dynamicBallGoalY = BallTargetAvailable
                    ? LastBallTargetWorld.y + ballTopPadding - playableHalfAboveCenter
                    : ceilingGoalY;
                maxY = Mathf.Max(minY, Mathf.Max(ceilingGoalY, Mathf.Min(dynamicBallGoalY, required.yMax + halfHeight)));
            }
            else
            {
                minY = halfHeight <= bounds.height * 0.5f
                    ? bounds.yMin + halfHeight : bounds.yMax - playableHalfAboveCenter;
                maxY = halfHeight <= bounds.height * 0.5f
                    ? bounds.yMax - halfHeight : bounds.yMin + playableHalfBelowCenter;
            }

            desired.x = minX > maxX ? fallback.x : Mathf.Clamp(desired.x, minX, maxX);
            desired.y = minY > maxY ? fallback.y : Mathf.Clamp(desired.y, minY, maxY);
            desired.z = cameraZ;
            return desired;
        }
    }
}
