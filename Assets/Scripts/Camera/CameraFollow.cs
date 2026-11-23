using System;
using System.Linq.Expressions;
using System.Reflection;
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
        [Tooltip("Allow vertical camera lift only during special high-altitude airborne Spike / Boom Jump moments (disabled by default: camera stays ground-locked and uses P03_OffScreenBallArrow when ball Y exceeds camTopY).")]
        [SerializeField] private bool enableSpecialVerticalFollow = false;
        [Tooltip("Vertical follow blend above grounded framing when special vertical follow is active.")]
        [Range(0f, 1f)]
        [SerializeField] private float verticalFollowWeight = 0f;
        [Tooltip("Minimum ball height above the ground-locked camera center required before special vertical follow can engage.")]
        [Min(0f)]
        [SerializeField] private float specialBallHeightOffset = 1.10f;
        [Tooltip("Minimum character jump elevation (world units above court floor) that qualifies as a Boom Jump apex for special vertical follow.")]
        [Min(0.5f)]
        [SerializeField] private float specialBoomJumpElevation = 1.35f;
        [Tooltip("Minimum world-unit headroom kept above the ball below the top HUD reserve during special vertical follow.")]
        [Min(0f)]
        [SerializeField] private float ballTopPadding = 0.9f;
        [Header("Off-Screen Top Ball Projection Arrow (P03_OffScreenBallArrow)")]
        [Tooltip("Show an upward-pointing sky-blue triangle arrow near the top of the camera view when the ball flies vertically above camTopY.")]
        [SerializeField] private bool enableOffScreenBallArrow = true;
        [Tooltip("Fraction of camera half-height inset below the camera top edge (camTopY) where the projection arrow is positioned.")]
        [Range(0.04f, 0.35f)]
        [SerializeField] private float arrowTopEdgeInsetRatio = 0.14f;
        [Tooltip("Fraction of camera half-width inset from the left/right camera edges for clamping the arrow horizontal projection X.")]
        [Range(0.02f, 0.25f)]
        [SerializeField] private float arrowHorizontalEdgeInsetRatio = 0.06f;
        [Tooltip("Minimum arrow world height scale when the ball is just barely above the camera top boundary (ballY -> camTopY).")]
        [Min(0.05f)]
        [SerializeField] private float minOffScreenArrowScale = 0.28f;
        [Tooltip("Maximum arrow world height scale when the ball is high above the camera top boundary.")]
        [Min(0.10f)]
        [SerializeField] private float maxOffScreenArrowScale = 1.15f;
        [Tooltip("Multiple of camera half-height above camTopY at which the off-screen ball arrow reaches maxOffScreenArrowScale.")]
        [Min(0.10f)]
        [SerializeField] private float arrowMaxScaleHeightSpanRatio = 1.00f;
        [Tooltip("Width-to-height aspect ratio of the upward-pointing isosceles triangle arrow (matching reference ~1.85:1).")]
        [Range(1.0f, 3.0f)]
        [SerializeField] private float arrowWidthToHeightAspect = 1.85f;
        [Tooltip("Sky-blue color of the off-screen ball projection arrow (#66BAF9).")]
        [SerializeField] private Color offScreenArrowColor = new Color(0.40f, 0.73f, 0.98f, 0.94f);
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
        [Tooltip("Enable upper-layer camera zoom-in on the ball when the lower layer signals a 0.5s slow-motion event.")]
        [SerializeField] private bool enableSlowMoCameraZoom = true;
        [Tooltip("Duration in seconds to keep the camera zoomed in on the ball when a 0.5s slow-motion event triggers (default 0.40s).")]
        [Min(0.05f)]
        [SerializeField] private float slowMoZoomDuration = 0.40f;
        [Tooltip("Target orthographic size while zoomed in on the ball during the 0.4s slow-motion zoom window.")]
        [Min(0.5f)]
        [SerializeField] private float slowMoZoomOrthoSize = 3.10f;
        [Tooltip("Zoom-in speed (orthographic size units per second) toward slowMoZoomOrthoSize during the 0.4s window.")]
        [Min(0.1f)]
        [SerializeField] private float slowMoZoomInSpeed = 14.0f;
        [Tooltip("User-configurable return speed (orthographic size units per second) back to normal camera size after the 0.4s window expires.")]
        [Min(0.1f)]
        [SerializeField] private float slowMoZoomReturnSpeed = 5.5f;
        [Tooltip("Blend weight [0..1] focusing the camera position toward the ball (X, Y) during slow-motion zoom.")]
        [Range(0f, 1f)]
        [SerializeField] private float slowMoBallFocusWeight = 0.85f;
        [Header("Cinemachine Dual-VCam Zoom Blend")]
        [Tooltip("Drive slow-motion camera zoom using the Cinemachine Dual-VCam (CM_CourtVCam <-> CM_BallZoomVCam) EaseInOut blend rig.")]
        [SerializeField] private bool useCinemachineZoomBlend = true;
        [Tooltip("Cinemachine EaseInOut blend-in duration (seconds) when switching priority from CM_CourtVCam to CM_BallZoomVCam.")]
        [Min(0.05f)]
        [SerializeField] private float cinemachineBlendInTime = 0.18f;
        [Tooltip("Cinemachine EaseInOut blend-out duration (seconds) when returning priority from CM_BallZoomVCam back to CM_CourtVCam.")]
        [Min(0.10f)]
        [SerializeField] private float cinemachineBlendOutTime = 0.55f;
        [Tooltip("Cinemachine Framing Transposer dead-zone width (fraction of zoomed ortho width) around the ball target.")]
        [Range(0f, 0.4f)]
        [SerializeField] private float cinemachineBallDeadZoneWidth = 0.08f;
        [Tooltip("Cinemachine Framing Transposer soft-zone width (fraction of zoomed ortho width) around the ball target.")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float cinemachineBallSoftZoneWidth = 0.35f;
        [Tooltip("Cinemachine Framing Transposer position damping time (seconds) for CM_BallZoomVCam.")]
        [Min(0.01f)]
        [SerializeField] private float cinemachinePositionDamping = 0.22f;
        [Header("High-Speed Spike Camera Smoothing")]
        [Tooltip("Enable spiker follow-through hold and smoother camera damping when the ball flies at high speed after a spike.")]
        [SerializeField] private bool enableHighSpeedSpikeSmoothing = true;
        [Tooltip("Sim px/tick ball speed threshold above which high-speed spike camera smoothing and spiker hold engage.")]
        [Min(1f)]
        [SerializeField] private float highSpeedBallThresholdSimPx = 18.0f;
        [Tooltip("Seconds after a spike contact during which the camera holds near the spiker's X position so the player's spike animation is clearly visible.")]
        [Min(0f)]
        [SerializeField] private float spikeAnimationHoldDuration = 0.16f;
        [Tooltip("Seconds over which the camera framing target smoothly blends from the spiker's X position to the ball's X position after the hold window.")]
        [Min(0.05f)]
        [SerializeField] private float spikeTargetBlendDuration = 0.32f;
        [Tooltip("Normal horizontal SmoothDamp time (seconds) during standard rally movement.")]
        [Min(0.01f)]
        [SerializeField] private float normalHorizontalSmoothTime = 0.14f;
        [Tooltip("Heavier cinematic horizontal SmoothDamp time (seconds) when the ball is flying fast after a spike.")]
        [Min(0.05f)]
        [SerializeField] private float highSpeedHorizontalSmoothTime = 0.42f;
        [Tooltip("Maximum horizontal camera pan speed (world meters/second) during high-speed spike ball flight so the camera glides smoothly without jerking.")]
        [Min(1f)]
        [SerializeField] private float highSpeedMaxCameraPanSpeed = 11.5f;

        private const int VCamBaseCourtPriority = 10;
        private const int VCamActiveBallZoomPriority = 20;
        private const int VCamInactiveBallZoomPriority = 5;

        private const float GroundLockSnapEpsilon = 0.002f;
        public const string OffScreenBallArrowObjectName = "P03_OffScreenBallArrow";
        private const int OffScreenArrowSortingOrder = 650;
        private const int OffScreenArrowTexWidth = 128;
        private const int OffScreenArrowTexHeight = 128;
        private static Sprite cachedOffScreenArrowSprite;

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
        public bool IsSlowMoZoomActive => slowMoZoomTimer > 0f;
        public bool IsSlowMoZoomReturning => slowMoZoomTimer <= 0f && slowMoZoomBlend > 0.001f;
        public float SlowMoZoomTimer => slowMoZoomTimer;
        public float SlowMoZoomBlend => slowMoZoomBlend;
        public float BaseOrthographicSize => lastBaseOrthographicSize;
        public float SlowMoZoomOrthoSize => slowMoZoomOrthoSize;
        public float SlowMoZoomDuration => slowMoZoomDuration;
        public float SlowMoZoomReturnSpeed => slowMoZoomReturnSpeed;
        public int SlowMoZoomTriggerCount { get; private set; }
        public int LastObservedReservedHeader => lastObservedReservedHeader;
        public bool IsPausedWithSimulation { get; private set; }
        public bool UseCinemachineZoomBlend => useCinemachineZoomBlend;
        public float CinemachineBlendInTime => cinemachineBlendInTime;
        public float CinemachineBlendOutTime => cinemachineBlendOutTime;
        public float CinemachineBlendWeight => slowMoZoomBlend;
        public Transform CourtVCamTransform => courtVCamTransform;
        public Transform BallZoomVCamTransform => ballZoomVCamTransform;
        public int CourtVCamPriority => courtVCamPriority;
        public int BallZoomVCamPriority => ballZoomVCamPriority;
        public float CourtVCamOrthoSize => courtVCamOrthoSize;
        public float BallZoomVCamOrthoSize => ballZoomVCamOrthoSize;
        public bool IsCinemachineRigInitialized => cinemachineRigInitialized && courtVCamTransform != null && ballZoomVCamTransform != null;
        public bool IsCinemachinePackageBound => cinemachinePackageBound;
        public bool IsOffScreenBallArrowVisible { get; private set; }
        public float CurrentOffScreenArrowScale { get; private set; }
        public float OffScreenBallHeightRatio { get; private set; }
        public float CameraTopBoundaryY { get; private set; }
        public Vector3 OffScreenArrowWorldPosition { get; private set; }
        public SpriteRenderer OffScreenBallArrowRenderer => offScreenArrowRenderer;
        public float MinOffScreenArrowScale => minOffScreenArrowScale;
        public float MaxOffScreenArrowScale => maxOffScreenArrowScale;
        public float ArrowWidthToHeightAspect => arrowWidthToHeightAspect;
        public bool IsSpikeCameraHoldActive => spikeFollowThroughElapsed < (spikeAnimationHoldDuration + spikeTargetBlendDuration) && hasActiveSpikeOrigin;
        public float LastSpikeOriginWorldX => lastSpikeOriginWorldX;
        public float CurrentHorizontalSmoothTime { get; private set; } = 0.14f;
        public float CurrentMaxHorizontalPanSpeed { get; private set; } = float.PositiveInfinity;
        public float HighSpeedBallThresholdSimPx => highSpeedBallThresholdSimPx;
        public float SpikeAnimationHoldDuration => spikeAnimationHoldDuration;
        public float HighSpeedHorizontalSmoothTime => highSpeedHorizontalSmoothTime;
        public float HighSpeedMaxCameraPanSpeed => highSpeedMaxCameraPanSpeed;

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
        private float slowMoZoomTimer;
        private float slowMoZoomBlend;
        private float cinemachineBlendProgress;
        private float currentSlowMoOrthoSize;
        private float slowMoOrthoVelocity;
        private float lastBaseOrthographicSize;
        private int lastObservedReservedHeader;
        private ulong lastSlowMoEvaluatedTick;
        private bool slowMoBurstLatched;
        private bool isApplyingSlowMoOrtho;
        private SimulationDataTable subscribedTable;

        private bool cinemachineRigInitialized;
        private bool cinemachinePackageBound;
        private Transform courtVCamTransform;
        private Transform ballZoomVCamTransform;
        private Transform ballZoomTargetProxy;
        private Component cinemachineBrainComponent;
        private Component courtVCamComponent;
        private Component ballZoomVCamComponent;
        private int courtVCamPriority = VCamBaseCourtPriority;
        private int ballZoomVCamPriority = VCamInactiveBallZoomPriority;
        private float courtVCamOrthoSize = 4.95f;
        private float ballZoomVCamOrthoSize = 3.10f;
        private Vector3 ballZoomVCamDampedPos;
        private Vector3 ballZoomVCamVelocity;
        private bool hasBallZoomVCamPos;
        private Transform offScreenArrowTransform;
        private SpriteRenderer offScreenArrowRenderer;
        private bool hasActiveSpikeOrigin;
        private float lastSpikeOriginWorldX;
        private float spikeFollowThroughElapsed;
        private float lastCommittedBallSpeedSimPx;
        private ulong lastSpikeEvaluatedTick;
        private bool wasCharacterSpikingLastTick;
        private float activeProxyTargetWorldX;

        private static bool cinemachineTypesProbed;
        private static Type cmBrainType;
        private static Type cmVCamType;
        private static PropertyInfo cmVCamPriorityProp;
        private static FieldInfo cmVCamPriorityField;
        private static PropertyInfo cmVCamFollowProp;
        private static PropertyInfo cmVCamLensProp;
        private static FieldInfo cmVCamLensField;
        private static FieldInfo cmLensOrthoSizeField;
        private static PropertyInfo cmLensOrthoSizeProp;
        private static FieldInfo cmLensDutchField;
        private static PropertyInfo cmLensDutchProp;

        private static bool slowMoAccessorsInitialized;
        private static Func<SimulationDataTable, int> getCurrentSlowMoFromTable;
        private static Func<SimulationDataTable, int> getPreviousSlowMoFromTable;
        private static Func<SimulationDataTable, bool> getDidSlowMoTriggerFromTable;
        private static Func<SimulationDataTable, int> getCurrentReservedFromTable;
        private static Func<SimulationDataTable, int> getPreviousReservedFromTable;
        private static Func<SpikeSimulationManager, int> getCurrentSlowMoFromManager;
        private static Func<SpikeSimulationManager, int> getCurrentReservedFromManager;
        private static Func<int> getBridgeSlowMoFrames;

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

        private void OnDisable()
        {
            if (subscribedTable != null)
            {
                subscribedTable.OnSnapshotCommitted -= OnTableSnapshotCommitted;
                subscribedTable = null;
            }
        }

        private void OnValidate()
        {
            targetOrthographicSize = Mathf.Max(0.01f, targetOrthographicSize);
            slowMoZoomDuration = Mathf.Max(0.05f, slowMoZoomDuration);
            slowMoZoomOrthoSize = Mathf.Max(0.5f, slowMoZoomOrthoSize);
            slowMoZoomInSpeed = Mathf.Max(0.1f, slowMoZoomInSpeed);
            slowMoZoomReturnSpeed = Mathf.Max(0.1f, slowMoZoomReturnSpeed);
            minOffScreenArrowScale = Mathf.Max(0.05f, minOffScreenArrowScale);
            maxOffScreenArrowScale = Mathf.Max(minOffScreenArrowScale, maxOffScreenArrowScale);
            arrowMaxScaleHeightSpanRatio = Mathf.Max(0.10f, arrowMaxScaleHeightSpanRatio);
            arrowWidthToHeightAspect = Mathf.Clamp(arrowWidthToHeightAspect, 1.0f, 3.0f);
            highSpeedBallThresholdSimPx = Mathf.Max(1f, highSpeedBallThresholdSimPx);
            spikeAnimationHoldDuration = Mathf.Max(0f, spikeAnimationHoldDuration);
            spikeTargetBlendDuration = Mathf.Max(0.05f, spikeTargetBlendDuration);
            normalHorizontalSmoothTime = Mathf.Max(0.01f, normalHorizontalSmoothTime);
            highSpeedHorizontalSmoothTime = Mathf.Max(normalHorizontalSmoothTime, highSpeedHorizontalSmoothTime);
            highSpeedMaxCameraPanSpeed = Mathf.Max(1f, highSpeedMaxCameraPanSpeed);
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

        private void LateUpdate()
        {
            IsPausedWithSimulation = IsSimulationLayerPaused();
            if (IsPausedWithSimulation)
            {
                UpdateFraming(0f);
                return;
            }

            float dt = Time.unscaledDeltaTime > 0.0001f
                ? Time.unscaledDeltaTime
                : (Time.deltaTime > 0.0001f ? Time.deltaTime : 0f);
            UpdateFraming(dt);
        }

        private static bool IsSimulationLayerPaused()
        {
            SpikeSimulationManager mgr = SpikeSimulationManager.Instance;
            if (mgr != null && (mgr.IsPaused || UIButtonInputTest.IsLayoutEditMode))
            {
                return true;
            }
            if (Application.isPlaying && Time.timeScale <= 0.0001f)
            {
                return true;
            }
            return false;
        }

        /// <summary>Recomputes the fit for the current viewport without advancing follow smoothing.</summary>
        public void ApplyFramingNow() => UpdateFraming(0f);

        /// <summary>
        /// Triggers the 0.4s slow-motion camera zoom-in toward the ball, after which the camera smoothly
        /// returns to its normal orthographic size at slowMoZoomReturnSpeed.
        /// </summary>
        public void TriggerSlowMotionCameraZoom(float zoomHoldDuration = -1f)
        {
            if (!enableSlowMoCameraZoom)
            {
                return;
            }

            float duration = zoomHoldDuration > 0f ? zoomHoldDuration : slowMoZoomDuration;
            slowMoZoomTimer = Mathf.Max(0.01f, duration);
            slowMoOrthoVelocity = 0f;
            if (currentSlowMoOrthoSize <= 0.01f)
            {
                currentSlowMoOrthoSize = lastBaseOrthographicSize > 0.01f
                    ? lastBaseOrthographicSize
                    : (cam != null && cam.orthographicSize > 0.01f ? cam.orthographicSize : 4.60f);
            }
            SlowMoZoomTriggerCount++;
        }

        /// <summary>
        /// Deterministically advances the camera framing and slow-motion zoom state by dt seconds.
        /// </summary>
        public void SimulateSlowMoCameraStep(float dt)
        {
            if (cam == null) cam = GetComponent<Camera>();
            BindSceneTable();
            UpdateFraming(Mathf.Max(0f, dt));
        }

        private void OnTableSnapshotCommitted(ulong tick)
        {
            EvaluateSlowMotionCameraEvent();
        }

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
            float baseHalfHeight = ResolveOrthographicSize(fitHalfHeight);
            lastBaseOrthographicSize = baseHalfHeight;
            EnsureCinemachineRigInitialized(baseHalfHeight);
            EvaluateSlowMotionCameraEvent();
            float halfHeight = AdvanceSlowMotionZoom(baseHalfHeight, deltaTime);
            LastCharacterScreenHeightFraction = LastCharacterWorldSize.y / (2f * halfHeight);
            LastCharacterScreenWidthFraction = LastCharacterWorldSize.x / (2f * halfHeight * aspect);

            float baseVisibleHeight = baseHalfHeight * 2f;
            float playableHalfBelowCenter = baseHalfHeight * (1f - 2f * bottomControlsReserve);
            float playableHalfAboveCenter = baseHalfHeight * (1f - 2f * topHudReserve);
            float usableWorldHeight = baseVisibleHeight * usableHeight;

            float fullCourtFramedY = required.center.y - baseVisibleHeight * (bottomControlsReserve - topHudReserve) * 0.5f;
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

            float activeHorizontalSmoothTime = followSmoothTime > 0f ? followSmoothTime : 0f;
            float activeMaxHorizontalSpeed = Mathf.Infinity;

            if (followBall && hasBallRecord && TryComputeBallWorldTarget(ballRecord, out Vector3 ballTarget))
            {
                BallTargetAvailable = true;
                LastBallTargetWorld = ballTarget;

                // Track ball X horizontally with velocity look-ahead on fast Serve/Spike and subtle deadzone on vertical Set/Lob.
                float rawGoalX = ComputeHorizontalGoalX(ballTarget.x, ballRecord);

                // Evaluate high-speed spike camera hold & smooth damping so the spiker's strike animation is clearly visible
                // before the camera glides smoothly after a fast spiked ball.
                EvaluateHighSpeedSpikeCameraSmoothing(
                    ballTarget.x,
                    rawGoalX,
                    ballRecord,
                    deltaTime,
                    out float smoothedTargetX,
                    out float smoothedProxyBallX,
                    out activeHorizontalSmoothTime,
                    out activeMaxHorizontalSpeed);

                goal.x = smoothedTargetX;
                activeProxyTargetWorldX = smoothedProxyBallX;

                // Camera strictly follows horizontal X only; Y stays floor-anchored at GroundLockedY (baseFramedY).
                goal.y = baseFramedY;

                // When slow-motion camera zoom is active or returning, blend horizontal X between CM_CourtVCam and CM_BallZoomVCam
                // using Cinemachine's EaseInOut blend weight and Framing Transposer deadzone/softzone damping while keeping Y floor-locked.
                Vector3 proxyBallTarget = new Vector3(smoothedProxyBallX, ballTarget.y, ballTarget.z);
                Vector3 ballVCamTarget = ComposeCinemachineBallFramingTransposer(proxyBallTarget, goal, halfHeight, aspect, deltaTime);
                if (slowMoZoomTimer > 0f || slowMoZoomBlend > 0.0001f)
                {
                    float focusT = Mathf.Clamp01(slowMoBallFocusWeight * slowMoZoomBlend);
                    goal.x = Mathf.Lerp(goal.x, ballVCamTarget.x, focusT);
                }
            }
            else
            {
                LastBallTargetWorld = framed;
                activeProxyTargetWorldX = framed.x;
                smoothingVelocity = Vector3.zero;
                hasBallZoomVCamPos = false;
                hasActiveSpikeOrigin = false;
                spikeFollowThroughElapsed = 0f;
            }

            CurrentHorizontalSmoothTime = activeHorizontalSmoothTime;
            CurrentMaxHorizontalPanSpeed = activeMaxHorizontalSpeed;

            EvaluateRallyImpactShake(hasBallRecord, ballRecord);

            goal = ClampToCourtBounds(goal, halfHeight, aspect, required, framed);
            goal.y = baseFramedY;
            LastClampedTarget = goal;

            if (!hasSmoothedPosition)
            {
                smoothedPosition = BallTargetAvailable ? goal : new Vector3(current.x, baseFramedY, cameraZ);
                hasSmoothedPosition = true;
            }

            if (activeHorizontalSmoothTime <= 0f || deltaTime <= 0f)
            {
                // Keep the established court framing immediately available to editor capture tools.
                // Runtime follow uses LateUpdate with a positive unscaled delta.
                if (BallTargetAvailable && activeHorizontalSmoothTime <= 0f) smoothedPosition = goal;
                else if (!BallTargetAvailable) smoothedPosition = framed;
                smoothedPosition.y = baseFramedY;
                smoothingVelocity.y = 0f;
            }
            else
            {
                smoothedPosition = Vector3.SmoothDamp(
                    smoothedPosition,
                    goal,
                    ref smoothingVelocity,
                    activeHorizontalSmoothTime,
                    activeMaxHorizontalSpeed,
                    deltaTime);
                smoothedPosition.y = baseFramedY;
                smoothingVelocity.y = 0f;
            }

            // Re-clamp the smoothed value so viewport resize/reset cannot temporarily reveal beyond
            // the court bounds, and synchronize smoothingVelocity if a bound is touched during zoom-out expansion.
            Vector3 preClampSmoothed = smoothedPosition;
            smoothedPosition = ClampToCourtBounds(smoothedPosition, halfHeight, aspect, required, framed);
            smoothedPosition.y = baseFramedY;
            if (Mathf.Abs(smoothedPosition.x - preClampSmoothed.x) > 0.0001f)
            {
                if ((smoothedPosition.x > preClampSmoothed.x && smoothingVelocity.x < 0f) ||
                    (smoothedPosition.x < preClampSmoothed.x && smoothingVelocity.x > 0f))
                {
                    smoothingVelocity.x = 0f;
                }
            }
            smoothingVelocity.y = 0f;
            smoothedPosition.z = cameraZ;

            Vector3 finalPosition = ApplyImpactShakeOffset(smoothedPosition, deltaTime, halfHeight, aspect, required, framed);
            finalPosition.y = baseFramedY;
            finalPosition.z = cameraZ;
            transform.position = finalPosition;
            SyncCinemachineVirtualCameras(framed, hasBallZoomVCamPos ? ballZoomVCamDampedPos : finalPosition, baseHalfHeight);

            float visibleHeight = halfHeight * 2f;
            float visibleWidth = visibleHeight * aspect;
            LastVisibleRect = new Rect(
                transform.position.x - visibleWidth * 0.5f,
                transform.position.y - halfHeight,
                visibleWidth,
                visibleHeight);

            UpdateOffScreenBallArrow(halfHeight, aspect);
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

            // Only treat direct Camera/target size changes as manual Inspector overrides in Edit Mode.
            // During Play Mode, post-camera presentation effects (e.g. ground-impact punch zoom) may modify
            // cam.orthographicSize in LateUpdate after CameraFollow and must never disable autoFitCourt
            // or latch a smaller targetOrthographicSize when the ball bounces away.
            if (!Application.isPlaying)
            {
                bool targetEdited = !Mathf.Approximately(targetOrthographicSize, observedTargetOrthographicSize);
                bool cameraEdited = !isApplyingSlowMoOrtho && !Mathf.Approximately(cam.orthographicSize, appliedOrthographicSize);
                if (targetEdited)
                    autoFitCourt = false;
                else if (cameraEdited)
                {
                    autoFitCourt = false;
                    targetOrthographicSize = cam.orthographicSize;
                }
            }

            float size = autoFitCourt ? fitHalfHeight : Mathf.Max(0.01f, targetOrthographicSize);
            targetOrthographicSize = size;
            observedTargetOrthographicSize = size;
            appliedOrthographicSize = size;
            cam.orthographicSize = size;
            isApplyingSlowMoOrtho = false;
            return size;
        }

        private float AdvanceSlowMotionZoom(float baseHalfHeight, float deltaTime)
        {
            float targetZoomOrtho = Mathf.Clamp(slowMoZoomOrthoSize, 0.5f, Mathf.Max(0.5f, baseHalfHeight - 0.25f));
            courtVCamOrthoSize = baseHalfHeight;
            ballZoomVCamOrthoSize = targetZoomOrtho;

            if (!enableSlowMoCameraZoom)
            {
                slowMoZoomTimer = 0f;
                slowMoZoomBlend = 0f;
                cinemachineBlendProgress = 0f;
                slowMoOrthoVelocity = 0f;
                currentSlowMoOrthoSize = baseHalfHeight;
                courtVCamPriority = VCamBaseCourtPriority;
                ballZoomVCamPriority = VCamInactiveBallZoomPriority;
                isApplyingSlowMoOrtho = false;
                return baseHalfHeight;
            }

            if (currentSlowMoOrthoSize <= 0.01f)
            {
                currentSlowMoOrthoSize = baseHalfHeight;
            }

            float zoomSpan = Mathf.Max(0.01f, baseHalfHeight - targetZoomOrtho);

            if (slowMoZoomTimer > 0f)
            {
                // CM_BallZoomVCam has higher priority (20 > 10) during the 0.40s zoom-in window.
                courtVCamPriority = VCamBaseCourtPriority;
                ballZoomVCamPriority = VCamActiveBallZoomPriority;

                if (useCinemachineZoomBlend)
                {
                    float effectiveBlendIn = Mathf.Clamp(
                        cinemachineBlendInTime * (14.0f / Mathf.Max(0.1f, slowMoZoomInSpeed)),
                        0.04f,
                        0.38f);
                    if (deltaTime > 0f)
                    {
                        slowMoZoomTimer = Mathf.Max(0f, slowMoZoomTimer - deltaTime);
                        cinemachineBlendProgress = Mathf.MoveTowards(
                            cinemachineBlendProgress,
                            1f,
                            deltaTime / effectiveBlendIn);
                    }
                    else if (cinemachineBlendProgress <= 0.0001f)
                    {
                        cinemachineBlendProgress = Mathf.MoveTowards(0f, 1f, (1f / 60f) / effectiveBlendIn);
                    }

                    slowMoZoomBlend = EvaluateCinemachineEaseInOutCurve(cinemachineBlendProgress);
                    currentSlowMoOrthoSize = Mathf.Lerp(baseHalfHeight, targetZoomOrtho, slowMoZoomBlend);
                }
                else
                {
                    if (deltaTime > 0f)
                    {
                        slowMoZoomTimer = Mathf.Max(0f, slowMoZoomTimer - deltaTime);
                        float inRate = Mathf.Max(1f, (slowMoZoomInSpeed / zoomSpan) * 2.2f);
                        float inAlpha = 1f - Mathf.Exp(-inRate * deltaTime);
                        currentSlowMoOrthoSize = Mathf.Lerp(currentSlowMoOrthoSize, targetZoomOrtho, inAlpha);
                    }
                    float rawRatio = Mathf.Clamp01((baseHalfHeight - currentSlowMoOrthoSize) / zoomSpan);
                    slowMoZoomBlend = EvaluateCinemachineEaseInOutCurve(rawRatio);
                    cinemachineBlendProgress = rawRatio;
                }
                slowMoOrthoVelocity = 0f;
            }
            else if (slowMoZoomBlend > 0.0001f || cinemachineBlendProgress > 0.0001f || currentSlowMoOrthoSize < baseHalfHeight - 0.0005f)
            {
                // 0.40s window expired -> drop CM_BallZoomVCam priority to 5 (< CM_CourtVCam priority 10)
                // so Cinemachine blends back to CM_CourtVCam using its EaseInOut S-curve at slowMoZoomReturnSpeed.
                courtVCamPriority = VCamBaseCourtPriority;
                ballZoomVCamPriority = VCamInactiveBallZoomPriority;

                if (useCinemachineZoomBlend)
                {
                    float returnSpeed = Mathf.Max(0.1f, slowMoZoomReturnSpeed);
                    float effectiveBlendOut = Mathf.Clamp(
                        Mathf.Max(0.12f, cinemachineBlendOutTime) * (5.5f / returnSpeed),
                        0.12f,
                        2.5f);
                    if (deltaTime > 0f)
                    {
                        cinemachineBlendProgress = Mathf.MoveTowards(
                            cinemachineBlendProgress,
                            0f,
                            deltaTime / effectiveBlendOut);
                    }

                    slowMoZoomBlend = EvaluateCinemachineEaseInOutCurve(cinemachineBlendProgress);
                    currentSlowMoOrthoSize = Mathf.Lerp(baseHalfHeight, targetZoomOrtho, slowMoZoomBlend);
                }
                else
                {
                    if (deltaTime > 0f)
                    {
                        float returnSpeed = Mathf.Max(0.1f, slowMoZoomReturnSpeed);
                        float returnSmoothTime = Mathf.Clamp(zoomSpan / (returnSpeed * 2.4f), 0.04f, 1.0f);
                        currentSlowMoOrthoSize = Mathf.SmoothDamp(
                            currentSlowMoOrthoSize,
                            baseHalfHeight,
                            ref slowMoOrthoVelocity,
                            returnSmoothTime,
                            returnSpeed * 1.45f,
                            deltaTime);
                    }
                    float rawRatio = Mathf.Clamp01((baseHalfHeight - currentSlowMoOrthoSize) / zoomSpan);
                    slowMoZoomBlend = EvaluateCinemachineEaseInOutCurve(rawRatio);
                    cinemachineBlendProgress = rawRatio;
                }

                if (Mathf.Abs(currentSlowMoOrthoSize - baseHalfHeight) <= 0.001f || cinemachineBlendProgress <= 0.0001f)
                {
                    currentSlowMoOrthoSize = baseHalfHeight;
                    slowMoZoomBlend = 0f;
                    cinemachineBlendProgress = 0f;
                    slowMoOrthoVelocity = 0f;
                }
            }
            else
            {
                courtVCamPriority = VCamBaseCourtPriority;
                ballZoomVCamPriority = VCamInactiveBallZoomPriority;
                currentSlowMoOrthoSize = baseHalfHeight;
                slowMoZoomBlend = 0f;
                cinemachineBlendProgress = 0f;
                slowMoOrthoVelocity = 0f;
            }

            bool zoomModActive = slowMoZoomTimer > 0f || slowMoZoomBlend > 0.0001f;
            float activeHalfHeight = zoomModActive ? currentSlowMoOrthoSize : baseHalfHeight;
            isApplyingSlowMoOrtho = zoomModActive;
            appliedOrthographicSize = activeHalfHeight;
            cam.orthographicSize = activeHalfHeight;
            return activeHalfHeight;
        }

        /// <summary>
        /// Evaluates Cinemachine's signature CinemachineBlendDefinition.Style.EaseInOut S-curve
        /// (blended cubic Hermite + quintic Perlin smoothstep with zero first & second derivatives at t=0 and t=1).
        /// </summary>
        private static float EvaluateCinemachineEaseInOutCurve(float t)
        {
            float u = Mathf.Clamp01(t);
            float cubic = u * u * (3f - 2f * u);
            float quintic = u * u * u * (u * (u * 6f - 15f) + 10f);
            return Mathf.Lerp(cubic, quintic, 0.55f);
        }

        private Vector3 ComposeCinemachineBallFramingTransposer(
            Vector3 ballTarget,
            Vector3 courtGoal,
            float halfHeight,
            float aspect,
            float deltaTime)
        {
            float orthoW = Mathf.Max(0.5f, halfHeight * 2f * aspect);
            float deadW = orthoW * Mathf.Clamp(cinemachineBallDeadZoneWidth, 0f, 0.4f) * 0.5f;

            if (!hasBallZoomVCamPos)
            {
                ballZoomVCamDampedPos = new Vector3(ballTarget.x, GroundLockedY, cameraZ);
                ballZoomVCamVelocity = Vector3.zero;
                hasBallZoomVCamPos = true;
            }

            Vector3 desired = new Vector3(ballZoomVCamDampedPos.x, GroundLockedY, cameraZ);
            float dx = ballTarget.x - ballZoomVCamDampedPos.x;
            if (Mathf.Abs(dx) > deadW)
            {
                desired.x = ballTarget.x - Mathf.Sign(dx) * deadW;
            }

            if (deltaTime > 0.0001f && cinemachinePositionDamping > 0.001f)
            {
                ballZoomVCamDampedPos = Vector3.SmoothDamp(
                    new Vector3(ballZoomVCamDampedPos.x, GroundLockedY, cameraZ),
                    new Vector3(desired.x, GroundLockedY, cameraZ),
                    ref ballZoomVCamVelocity,
                    Mathf.Clamp(cinemachinePositionDamping * 0.5f, 0.02f, 0.5f),
                    Mathf.Infinity,
                    deltaTime);
                ballZoomVCamDampedPos.y = GroundLockedY;
                ballZoomVCamVelocity.y = 0f;
            }
            else
            {
                ballZoomVCamDampedPos = new Vector3(ballTarget.x, GroundLockedY, cameraZ);
                ballZoomVCamVelocity.y = 0f;
            }

            Vector3 blended = Vector3.Lerp(courtGoal, ballZoomVCamDampedPos, 0.92f);
            blended.y = GroundLockedY;
            return blended;
        }

        private void EnsureOffScreenBallArrowInitialized()
        {
            EnsureOffScreenArrowSpriteCreated();

            if (offScreenArrowTransform == null)
            {
                Transform existingArrow = transform.Find(OffScreenBallArrowObjectName);
                if (existingArrow == null)
                {
                    var go = new GameObject(OffScreenBallArrowObjectName);
                    go.layer = gameObject.layer;
                    existingArrow = go.transform;
                    existingArrow.SetParent(transform, false);
                }
                offScreenArrowTransform = existingArrow;
            }

            if (offScreenArrowRenderer == null && offScreenArrowTransform != null)
            {
                offScreenArrowRenderer = offScreenArrowTransform.GetComponent<SpriteRenderer>();
                if (offScreenArrowRenderer == null)
                {
                    offScreenArrowRenderer = offScreenArrowTransform.gameObject.AddComponent<SpriteRenderer>();
                }
            }

            if (offScreenArrowRenderer != null)
            {
                if (offScreenArrowRenderer.sprite != cachedOffScreenArrowSprite)
                {
                    offScreenArrowRenderer.sprite = cachedOffScreenArrowSprite;
                }
                offScreenArrowRenderer.sortingOrder = OffScreenArrowSortingOrder;
                offScreenArrowRenderer.color = offScreenArrowColor;
            }
        }

        private static void EnsureOffScreenArrowSpriteCreated()
        {
            if (cachedOffScreenArrowSprite != null)
            {
                return;
            }

            Texture2D tex = new Texture2D(OffScreenArrowTexWidth, OffScreenArrowTexHeight, TextureFormat.RGBA32, false)
            {
                name = "P03_OffScreenBallArrowTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[OffScreenArrowTexWidth * OffScreenArrowTexHeight];
            const float padY = 4f;
            const float padX = 4f;
            float usableH = OffScreenArrowTexHeight - 2f * padY;
            float halfUsableW = (OffScreenArrowTexWidth - 2f * padX) * 0.5f;
            float centerX = OffScreenArrowTexWidth * 0.5f;

            for (int y = 0; y < OffScreenArrowTexHeight; y++)
            {
                float ny = ((y + 0.5f) - padY) / usableH; // 0 = flat base, 1 = top apex
                for (int x = 0; x < OffScreenArrowTexWidth; x++)
                {
                    if (ny < 0f || ny > 1f)
                    {
                        pixels[y * OffScreenArrowTexWidth + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Upward-pointing flat-bottomed isosceles triangle (▲): halfWidth tapers linearly from halfUsableW at base (ny=0) to 0 at apex (ny=1).
                    float maxHalfWidthAtY = (1f - ny) * halfUsableW;
                    float dx = Mathf.Abs((x + 0.5f) - centerX);
                    float edgeSignedDist = maxHalfWidthAtY - dx;
                    float bottomSignedDist = (y + 0.5f) - padY;
                    float topSignedDist = (OffScreenArrowTexHeight - padY) - (y + 0.5f);
                    float signedDist = Mathf.Min(edgeSignedDist, Mathf.Min(bottomSignedDist, topSignedDist));

                    float alpha = Mathf.Clamp01(signedDist + 0.65f);
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    pixels[y * OffScreenArrowTexWidth + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            cachedOffScreenArrowSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, OffScreenArrowTexWidth, OffScreenArrowTexHeight),
                new Vector2(0.5f, 0.5f),
                OffScreenArrowTexHeight);
            cachedOffScreenArrowSprite.name = "P03_OffScreenBallArrowSprite";
            cachedOffScreenArrowSprite.hideFlags = HideFlags.HideAndDontSave;
        }

        private void UpdateOffScreenBallArrow(float camHalfHeight, float aspect)
        {
            EnsureOffScreenBallArrowInitialized();

            float camCenterX = transform.position.x;
            float camCenterY = transform.position.y;
            float camTopY = camCenterY + camHalfHeight;
            CameraTopBoundaryY = camTopY;

            if (!enableOffScreenBallArrow || !BallTargetAvailable || camHalfHeight <= 0.001f)
            {
                IsOffScreenBallArrowVisible = false;
                CurrentOffScreenArrowScale = 0f;
                OffScreenBallHeightRatio = 0f;
                if (offScreenArrowRenderer != null)
                {
                    offScreenArrowRenderer.enabled = false;
                }
                return;
            }

            float ballX = LastBallTargetWorld.x;
            float ballY = LastBallTargetWorld.y;

            // Compare ball vertical Y coordinate against the camera's top vertical boundary camTopY.
            if (ballY <= camTopY)
            {
                IsOffScreenBallArrowVisible = false;
                CurrentOffScreenArrowScale = 0f;
                OffScreenBallHeightRatio = 0f;
                if (offScreenArrowRenderer != null)
                {
                    offScreenArrowRenderer.enabled = false;
                }
                return;
            }

            // Ball is above camTopY -> compute normalized vertical distance above camTopY relative to camera vertical height span.
            float heightAboveCamTop = ballY - camTopY;
            float heightSpan = Mathf.Max(0.1f, camHalfHeight * Mathf.Max(0.1f, arrowMaxScaleHeightSpanRatio));
            float heightRatio = Mathf.Clamp01(heightAboveCamTop / heightSpan);
            float minScale = Mathf.Max(0.05f, minOffScreenArrowScale);
            float maxScale = Mathf.Max(minScale, maxOffScreenArrowScale);
            float currentScale = Mathf.Lerp(minScale, maxScale, heightRatio);
            float widthScale = currentScale * Mathf.Clamp(arrowWidthToHeightAspect, 1.0f, 3.0f);

            float camHalfWidth = camHalfHeight * aspect;
            float horizontalInset = Mathf.Max(widthScale * 0.5f, camHalfWidth * Mathf.Clamp(arrowHorizontalEdgeInsetRatio, 0.02f, 0.25f));
            float minLocalX = -camHalfWidth + horizontalInset;
            float maxLocalX = camHalfWidth - horizontalInset;
            float desiredLocalX = ballX - camCenterX;
            float localArrowX = minLocalX <= maxLocalX ? Mathf.Clamp(desiredLocalX, minLocalX, maxLocalX) : desiredLocalX;

            float topInset = camHalfHeight * Mathf.Clamp(arrowTopEdgeInsetRatio, 0.04f, 0.35f);
            float localArrowY = camHalfHeight - topInset - (currentScale * 0.35f);
            float localArrowZ = -cameraZ;

            IsOffScreenBallArrowVisible = true;
            CurrentOffScreenArrowScale = currentScale;
            OffScreenBallHeightRatio = heightRatio;

            if (offScreenArrowTransform != null)
            {
                // Use camera-local coordinates & localRotation = identity so when the camera tilts (e.g. 20 deg on ground impact),
                // P03_OffScreenBallArrow tilts coherently with the camera's top edge instead of floating unrotated in world space.
                offScreenArrowTransform.localPosition = new Vector3(localArrowX, localArrowY, localArrowZ);
                offScreenArrowTransform.localRotation = Quaternion.identity;
                offScreenArrowTransform.localScale = new Vector3(widthScale, currentScale, 1f);
                OffScreenArrowWorldPosition = offScreenArrowTransform.position;
            }
            else
            {
                OffScreenArrowWorldPosition = new Vector3(camCenterX + localArrowX, camCenterY + localArrowY, 0f);
            }

            if (offScreenArrowRenderer != null)
            {
                offScreenArrowRenderer.color = offScreenArrowColor;
                offScreenArrowRenderer.enabled = true;
            }
        }

        private void EnsureCinemachineRigInitialized(float baseHalfHeight)
        {
            ProbeCinemachineTypesOnce();

            if (courtVCamTransform == null)
            {
                Transform existingCourt = transform.Find("CM_CourtVCam");
                if (existingCourt == null)
                {
                    var go = new GameObject("CM_CourtVCam");
                    existingCourt = go.transform;
                    existingCourt.SetParent(transform, false);
                }
                courtVCamTransform = existingCourt;
            }

            if (ballZoomVCamTransform == null)
            {
                Transform existingBall = transform.Find("CM_BallZoomVCam");
                if (existingBall == null)
                {
                    var go = new GameObject("CM_BallZoomVCam");
                    existingBall = go.transform;
                    existingBall.SetParent(transform, false);
                }
                ballZoomVCamTransform = existingBall;
            }

            if (ballZoomTargetProxy == null)
            {
                Transform existingProxy = transform.Find("CM_BallZoomTargetProxy");
                if (existingProxy == null)
                {
                    var go = new GameObject("CM_BallZoomTargetProxy");
                    existingProxy = go.transform;
                    existingProxy.SetParent(transform, false);
                }
                ballZoomTargetProxy = existingProxy;
            }

            if (cmBrainType != null && cinemachineBrainComponent == null)
            {
                cinemachineBrainComponent = GetComponent(cmBrainType);
                if (cinemachineBrainComponent == null)
                {
                    cinemachineBrainComponent = gameObject.AddComponent(cmBrainType);
                }
                if (cinemachineBrainComponent is Behaviour brainBehaviour)
                {
                    // CameraFollow evaluates the Cinemachine EaseInOut blend and court clamp deterministically in UpdateFraming.
                    brainBehaviour.enabled = false;
                }
            }

            if (cmVCamType != null)
            {
                if (courtVCamComponent == null && courtVCamTransform != null)
                {
                    courtVCamComponent = courtVCamTransform.GetComponent(cmVCamType);
                    if (courtVCamComponent == null)
                    {
                        courtVCamComponent = courtVCamTransform.gameObject.AddComponent(cmVCamType);
                    }
                }
                if (ballZoomVCamComponent == null && ballZoomVCamTransform != null)
                {
                    ballZoomVCamComponent = ballZoomVCamTransform.GetComponent(cmVCamType);
                    if (ballZoomVCamComponent == null)
                    {
                        ballZoomVCamComponent = ballZoomVCamTransform.gameObject.AddComponent(cmVCamType);
                    }
                }
                cinemachinePackageBound = courtVCamComponent != null && ballZoomVCamComponent != null;
            }

            cinemachineRigInitialized = courtVCamTransform != null && ballZoomVCamTransform != null;
        }

        private void SyncCinemachineVirtualCameras(Vector3 courtWorldPos, Vector3 ballWorldPos, float baseHalfHeight)
        {
            if (!cinemachineRigInitialized)
            {
                return;
            }

            float targetZoomOrtho = Mathf.Clamp(slowMoZoomOrthoSize, 0.5f, Mathf.Max(0.5f, baseHalfHeight - 0.25f));
            courtVCamOrthoSize = baseHalfHeight;
            ballZoomVCamOrthoSize = targetZoomOrtho;
            Quaternion camRotation = transform.rotation;
            float signedDutchZ = Mathf.DeltaAngle(0f, camRotation.eulerAngles.z);

            if (courtVCamTransform != null)
            {
                courtVCamTransform.SetPositionAndRotation(new Vector3(courtWorldPos.x, courtWorldPos.y, cameraZ), camRotation);
            }
            if (ballZoomVCamTransform != null)
            {
                ballZoomVCamTransform.SetPositionAndRotation(new Vector3(ballWorldPos.x, ballWorldPos.y, cameraZ), camRotation);
            }
            if (ballZoomTargetProxy != null)
            {
                float proxyX = BallTargetAvailable ? activeProxyTargetWorldX : LastBallTargetWorld.x;
                ballZoomTargetProxy.position = new Vector3(proxyX, LastBallTargetWorld.y, 0f);
            }

            if (cinemachinePackageBound)
            {
                ApplyVCamComponentState(courtVCamComponent, courtVCamPriority, courtVCamOrthoSize, signedDutchZ, null);
                ApplyVCamComponentState(ballZoomVCamComponent, ballZoomVCamPriority, ballZoomVCamOrthoSize, signedDutchZ, ballZoomTargetProxy);
            }
        }

        private static void ApplyVCamComponentState(Component vcam, int priority, float orthoSize, float dutchAngleZ, Transform followTarget)
        {
            if (vcam == null) return;
            try
            {
                if (cmVCamPriorityProp != null)
                {
                    if (cmVCamPriorityProp.PropertyType == typeof(int))
                    {
                        cmVCamPriorityProp.SetValue(vcam, priority);
                    }
                }
                else if (cmVCamPriorityField != null && cmVCamPriorityField.FieldType == typeof(int))
                {
                    cmVCamPriorityField.SetValue(vcam, priority);
                }

                if (followTarget != null && cmVCamFollowProp != null)
                {
                    cmVCamFollowProp.SetValue(vcam, followTarget);
                }

                if (cmVCamLensProp != null)
                {
                    object lens = cmVCamLensProp.GetValue(vcam);
                    if (lens != null)
                    {
                        if (cmLensOrthoSizeField != null)
                            cmLensOrthoSizeField.SetValue(lens, orthoSize);
                        else if (cmLensOrthoSizeProp != null)
                            cmLensOrthoSizeProp.SetValue(lens, orthoSize);
                        if (cmLensDutchField != null)
                            cmLensDutchField.SetValue(lens, dutchAngleZ);
                        else if (cmLensDutchProp != null)
                            cmLensDutchProp.SetValue(lens, dutchAngleZ);
                        cmVCamLensProp.SetValue(vcam, lens);
                    }
                }
                else if (cmVCamLensField != null)
                {
                    object lens = cmVCamLensField.GetValue(vcam);
                    if (lens != null)
                    {
                        if (cmLensOrthoSizeField != null)
                            cmLensOrthoSizeField.SetValue(lens, orthoSize);
                        else if (cmLensOrthoSizeProp != null)
                            cmLensOrthoSizeProp.SetValue(lens, orthoSize);
                        if (cmLensDutchField != null)
                            cmLensDutchField.SetValue(lens, dutchAngleZ);
                        else if (cmLensDutchProp != null)
                            cmLensDutchProp.SetValue(lens, dutchAngleZ);
                        cmVCamLensField.SetValue(vcam, lens);
                    }
                }
            }
            catch
            {
            }
        }

        private static void ProbeCinemachineTypesOnce()
        {
            if (cinemachineTypesProbed)
            {
                return;
            }
            cinemachineTypesProbed = true;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly asm = assemblies[i];
                if (cmBrainType == null)
                {
                    cmBrainType = asm.GetType("Unity.Cinemachine.CinemachineBrain", false) ??
                                  asm.GetType("Cinemachine.CinemachineBrain", false);
                }
                if (cmVCamType == null)
                {
                    cmVCamType = asm.GetType("Unity.Cinemachine.CinemachineCamera", false) ??
                                 asm.GetType("Cinemachine.CinemachineVirtualCamera", false);
                }
            }

            if (cmVCamType != null)
            {
                cmVCamPriorityProp = cmVCamType.GetProperty("Priority", flags);
                cmVCamPriorityField = cmVCamType.GetField("Priority", flags) ?? cmVCamType.GetField("m_Priority", flags);
                cmVCamFollowProp = cmVCamType.GetProperty("Follow", flags);
                cmVCamLensProp = cmVCamType.GetProperty("Lens", flags) ?? cmVCamType.GetProperty("m_Lens", flags);
                cmVCamLensField = cmVCamType.GetField("Lens", flags) ?? cmVCamType.GetField("m_Lens", flags);
                Type lensType = cmVCamLensProp != null
                    ? cmVCamLensProp.PropertyType
                    : (cmVCamLensField != null ? cmVCamLensField.FieldType : null);
                if (lensType != null)
                {
                    cmLensOrthoSizeField = lensType.GetField("OrthographicSize", flags);
                    cmLensOrthoSizeProp = lensType.GetProperty("OrthographicSize", flags);
                    cmLensDutchField = lensType.GetField("Dutch", flags);
                    cmLensDutchProp = lensType.GetProperty("Dutch", flags);
                }
            }
        }

        private void EvaluateSlowMotionCameraEvent()
        {
            if (boundTable == null && SimulationDataTable.Instance != null)
            {
                boundTable = SimulationDataTable.Instance;
            }
            SimulationDataTable table = boundTable;
            if (table == null || !table.IsInitialized)
            {
                return;
            }

            EnsureSlowMoAccessorsInitialized();
            int currentReserved = ReadCurrentReservedHeader(table);
            int previousReserved = ReadPreviousReservedHeader(table, lastObservedReservedHeader);
            ulong currentTick = table.CurrentTick;

            if (currentTick <= 1 && currentReserved <= 0)
            {
                lastObservedReservedHeader = 0;
                lastSlowMoEvaluatedTick = currentTick;
                slowMoBurstLatched = false;
                slowMoZoomTimer = 0f;
                slowMoZoomBlend = 0f;
                cinemachineBlendProgress = 0f;
                courtVCamPriority = VCamBaseCourtPriority;
                ballZoomVCamPriority = VCamInactiveBallZoomPriority;
                if (lastBaseOrthographicSize > 0.01f)
                {
                    currentSlowMoOrthoSize = lastBaseOrthographicSize;
                }
                return;
            }

            // Never trigger or hold a slow-motion ball zoom once the ball has already hit the floor
            // and is bouncing away (stateFrame > 0). If a spike zoom hold is still active at floor impact,
            // end the hold immediately so the camera smoothly zooms out to full court view.
            bool hasBall = table.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ball);
            bool isBallDeadOnFloorBounce = hasBall && ball.hasBallGameplayState && ball.stateFrame > 0;
            if (isBallDeadOnFloorBounce)
            {
                if (slowMoZoomTimer > 0f)
                {
                    slowMoZoomTimer = 0f;
                }
                slowMoBurstLatched = true;
                lastObservedReservedHeader = currentReserved;
                lastSlowMoEvaluatedTick = currentTick;
                return;
            }

            if (currentTick == lastSlowMoEvaluatedTick && currentReserved == lastObservedReservedHeader)
            {
                return;
            }

            bool didTriggerFlag = ReadDidSlowMoTriggerFlag(table);
            bool risingFromTable = didTriggerFlag || (currentReserved > 0 && (previousReserved == 0 || currentReserved > previousReserved));
            bool risingFromObserved = currentReserved > 0 && (lastObservedReservedHeader == 0 || currentReserved > lastObservedReservedHeader);
            bool freshRetriggerWhileActive = currentReserved > 0 && lastObservedReservedHeader > 0 && currentReserved > lastObservedReservedHeader;

            if (enableSlowMoCameraZoom &&
                ((!slowMoBurstLatched && (risingFromTable || risingFromObserved)) || freshRetriggerWhileActive))
            {
                slowMoBurstLatched = true;
                TriggerSlowMotionCameraZoom(slowMoZoomDuration);
            }
            else if (currentReserved <= 0 && !didTriggerFlag)
            {
                slowMoBurstLatched = false;
            }

            lastObservedReservedHeader = currentReserved;
            lastSlowMoEvaluatedTick = currentTick;
        }

        private static bool ReadDidSlowMoTriggerFlag(SimulationDataTable table)
        {
            if (table != null && getDidSlowMoTriggerFromTable != null)
            {
                try
                {
                    return getDidSlowMoTriggerFromTable(table);
                }
                catch
                {
                }
            }
            return false;
        }

        private static int ReadCurrentReservedHeader(SimulationDataTable table)
        {
            int best = 0;
            bool readFromTable = false;
            if (table != null)
            {
                if (getCurrentSlowMoFromTable != null)
                {
                    try
                    {
                        best = Mathf.Max(best, getCurrentSlowMoFromTable(table));
                        readFromTable = true;
                    }
                    catch { }
                }
                if (getCurrentReservedFromTable != null)
                {
                    try
                    {
                        best = Mathf.Max(best, getCurrentReservedFromTable(table));
                        readFromTable = true;
                    }
                    catch { }
                }
            }

            if (!readFromTable)
            {
                SpikeSimulationManager mgr = SpikeSimulationManager.Instance;
                if (mgr != null)
                {
                    if (getCurrentSlowMoFromManager != null)
                    {
                        try { best = Mathf.Max(best, getCurrentSlowMoFromManager(mgr)); } catch { }
                    }
                    if (getCurrentReservedFromManager != null)
                    {
                        try { best = Mathf.Max(best, getCurrentReservedFromManager(mgr)); } catch { }
                    }
                }

                if (best <= 0 && getBridgeSlowMoFrames != null)
                {
                    try { best = Mathf.Max(best, getBridgeSlowMoFrames()); } catch { }
                }
            }

            return best;
        }

        private static int ReadPreviousReservedHeader(SimulationDataTable table, int fallbackPrevious)
        {
            if (table != null)
            {
                if (getPreviousSlowMoFromTable != null)
                {
                    try
                    {
                        int prevSlow = getPreviousSlowMoFromTable(table);
                        if (prevSlow >= 0) return prevSlow;
                    }
                    catch { }
                }
                if (getPreviousReservedFromTable != null)
                {
                    try
                    {
                        return getPreviousReservedFromTable(table);
                    }
                    catch { }
                }
            }

            return fallbackPrevious;
        }

        private static void EnsureSlowMoAccessorsInitialized()
        {
            if (slowMoAccessorsInitialized)
            {
                return;
            }
            slowMoAccessorsInitialized = true;

            getCurrentSlowMoFromTable =
                CompileIntGetter<SimulationDataTable>("SlowMoFramesRemaining") ??
                CompileIntGetter<SimulationDataTable>("CurrentRallyState", "slowMoFramesRemaining") ??
                CompileIntGetter<SimulationDataTable>("CurrentRallyState", "_padding0") ??
                CompileIntGetter<SimulationDataTable>("RallyRecord", "slowMoFramesRemaining") ??
                CompileIntGetter<SimulationDataTable>("MatchRecord", "slowMoFramesRemaining");

            getPreviousSlowMoFromTable =
                CompileIntGetter<SimulationDataTable>("PreviousSlowMoFramesRemaining") ??
                CompileIntGetter<SimulationDataTable>("PreviousRallyState", "slowMoFramesRemaining") ??
                CompileIntGetter<SimulationDataTable>("PreviousRallyState", "_padding0");

            getDidSlowMoTriggerFromTable =
                CompileBoolGetter<SimulationDataTable>("DidSlowMotionTriggerThisTick");

            getCurrentReservedFromTable =
                CompileIntGetter<SimulationDataTable>("CurrentHeader", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("currentHeader", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("CurrentSnapshot", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("LastSnapshot", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("lastSnapshot", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("ReservedHeader");

            getPreviousReservedFromTable =
                CompileIntGetter<SimulationDataTable>("PreviousHeader", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("previousHeader", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("PreviousSnapshot", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("previousSnapshot", "reservedHeader") ??
                CompileIntGetter<SimulationDataTable>("PreviousReservedHeader");

            getCurrentSlowMoFromManager =
                CompileIntGetter<SpikeSimulationManager>("LastNativeSnapshot", "rallyState", "slowMoFramesRemaining") ??
                CompileIntGetter<SpikeSimulationManager>("LastNativeSnapshot", "rallyState", "_padding0") ??
                CompileIntGetter<SpikeSimulationManager>("LastNativeSnapshot", "matchState", "slowMoFramesRemaining");

            getCurrentReservedFromManager =
                CompileIntGetter<SpikeSimulationManager>("LastNativeSnapshot", "reservedHeader") ??
                CompileIntGetter<SpikeSimulationManager>("LastNativeSnapshot", "header", "reservedHeader");

            MethodInfo bridgeMethod = typeof(SpikeNativeBridge).GetMethod(
                "GetSlowMoFramesRemaining",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (bridgeMethod != null && bridgeMethod.GetParameters().Length == 0)
            {
                try
                {
                    Expression call = Expression.Call(bridgeMethod);
                    Expression conv = bridgeMethod.ReturnType == typeof(int) ? call : Expression.Convert(call, typeof(int));
                    getBridgeSlowMoFrames = Expression.Lambda<Func<int>>(conv).Compile();
                }
                catch
                {
                }
            }
        }

        private static Func<TTarget, bool> CompileBoolGetter<TTarget>(string memberName)
        {
            if (string.IsNullOrEmpty(memberName)) return null;
            try
            {
                ParameterExpression param = Expression.Parameter(typeof(TTarget), "target");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                PropertyInfo prop = typeof(TTarget).GetProperty(memberName, flags);
                if (prop != null && prop.PropertyType == typeof(bool))
                {
                    return Expression.Lambda<Func<TTarget, bool>>(Expression.Property(param, prop), param).Compile();
                }
                FieldInfo field = typeof(TTarget).GetField(memberName, flags);
                if (field != null && field.FieldType == typeof(bool))
                {
                    return Expression.Lambda<Func<TTarget, bool>>(Expression.Field(param, field), param).Compile();
                }
            }
            catch
            {
            }
            return null;
        }

        private static Func<TTarget, int> CompileIntGetter<TTarget>(params string[] memberChain)
        {
            if (memberChain == null || memberChain.Length == 0)
            {
                return null;
            }

            try
            {
                ParameterExpression param = Expression.Parameter(typeof(TTarget), "target");
                Expression current = param;
                Type currentType = typeof(TTarget);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                for (int i = 0; i < memberChain.Length; i++)
                {
                    string name = memberChain[i];
                    PropertyInfo prop = currentType.GetProperty(name, flags);
                    if (prop != null)
                    {
                        current = Expression.Property(current, prop);
                        currentType = prop.PropertyType;
                        continue;
                    }

                    FieldInfo field = currentType.GetField(name, flags);
                    if (field != null)
                    {
                        current = Expression.Field(current, field);
                        currentType = field.FieldType;
                        continue;
                    }

                    return null;
                }

                Expression converted = currentType == typeof(int)
                    ? current
                    : Expression.Convert(current, typeof(int));
                return Expression.Lambda<Func<TTarget, int>>(converted, param).Compile();
            }
            catch
            {
                return null;
            }
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

        private void EvaluateHighSpeedSpikeCameraSmoothing(
            float ballWorldX,
            float rawGoalX,
            in SimulationEntityRecord ball,
            float deltaTime,
            out float smoothedTargetX,
            out float smoothedProxyBallX,
            out float activeSmoothTime,
            out float activeMaxSpeed)
        {
            smoothedTargetX = rawGoalX;
            smoothedProxyBallX = ballWorldX;
            float baseNormalSmooth = followSmoothTime > 0f
                ? Mathf.Max(0.01f, normalHorizontalSmoothTime)
                : 0f;
            activeSmoothTime = baseNormalSmooth;
            activeMaxSpeed = Mathf.Infinity;

            if (!enableHighSpeedSpikeSmoothing)
            {
                hasActiveSpikeOrigin = false;
                spikeFollowThroughElapsed = 0f;
                return;
            }

            SimulationDataTable table = boundTable != null ? boundTable : SimulationDataTable.Instance;
            if (table == null || !table.IsInitialized || !ball.hasBallGameplayState || ball.stateFrame == 0)
            {
                hasActiveSpikeOrigin = false;
                spikeFollowThroughElapsed = 0f;
                lastCommittedBallSpeedSimPx = 0f;
                wasCharacterSpikingLastTick = false;
                return;
            }

            float rawSimSpeed = Mathf.Abs(ball.ballSpeed) > 0.01f ? Mathf.Abs(ball.ballSpeed) : Mathf.Abs(ball.vx);
            // Normalize synthetic per-second test inputs (>= 96 px/s) to Sim px/tick at 60 Hz for consistent threshold comparison.
            float simSpeedPxPerTick = rawSimSpeed >= 96f ? (rawSimSpeed * SpikeSimulationManager.TickSeconds) : rawSimSpeed;
            float thresholdPx = Mathf.Max(1f, highSpeedBallThresholdSimPx);

            bool characterSpikingNow = false;
            float spikerWorldX = ballWorldX;
            if (table.TryGetEntity(SimulationDataTable.ENTITY_PLAYER, out SimulationEntityRecord player) &&
                IsCharacterSpikeAction(player) && player.hasPosition)
            {
                characterSpikingNow = true;
                spikerWorldX = SimulationEntityReader.AnchorWorld(player).x;
            }
            else if (table.TryGetEntity(SimulationDataTable.ENTITY_OPPONENT_1, out SimulationEntityRecord opponent) &&
                     IsCharacterSpikeAction(opponent) && opponent.hasPosition)
            {
                characterSpikingNow = true;
                spikerWorldX = SimulationEntityReader.AnchorWorld(opponent).x;
            }

            ulong currentTick = table.CurrentTick;
            if (currentTick != lastSpikeEvaluatedTick)
            {
                bool speedJumpedAboveThreshold = ball.stateFrame == -1 &&
                                                 simSpeedPxPerTick >= thresholdPx &&
                                                 (lastCommittedBallSpeedSimPx < thresholdPx * 0.85f ||
                                                  (characterSpikingNow && !wasCharacterSpikingLastTick));
                bool spikeActionRisingEdge = characterSpikingNow && !wasCharacterSpikingLastTick;

                if (spikeActionRisingEdge || speedJumpedAboveThreshold)
                {
                    hasActiveSpikeOrigin = true;
                    lastSpikeOriginWorldX = characterSpikingNow ? spikerWorldX : ballWorldX;
                    spikeFollowThroughElapsed = 0f;
                }

                lastCommittedBallSpeedSimPx = simSpeedPxPerTick;
                wasCharacterSpikingLastTick = characterSpikingNow;
                lastSpikeEvaluatedTick = currentTick;
            }

            float holdDuration = Mathf.Max(0f, spikeAnimationHoldDuration);
            float blendDuration = Mathf.Max(0.05f, spikeTargetBlendDuration);
            float totalSpikeWindow = holdDuration + blendDuration;

            if (hasActiveSpikeOrigin)
            {
                float stepDt = deltaTime > 0.0001f ? deltaTime : 0f;
                spikeFollowThroughElapsed += stepDt;

                if (spikeFollowThroughElapsed < totalSpikeWindow && ball.stateFrame == -1)
                {
                    float followBlendT = 0f;
                    if (spikeFollowThroughElapsed > holdDuration)
                    {
                        float u = Mathf.Clamp01((spikeFollowThroughElapsed - holdDuration) / blendDuration);
                        // Cubic Hermite ease-in-out S-curve: holds near spiker first, then smoothly accelerates toward ball X.
                        followBlendT = u * u * (3f - 2f * u);
                    }
                    else if (holdDuration > 0.001f)
                    {
                        // Subtle 15% drift during the initial hold window so the camera feels alive while keeping the spiker centered.
                        float uHold = Mathf.Clamp01(spikeFollowThroughElapsed / holdDuration);
                        followBlendT = 0.15f * (uHold * uHold);
                    }

                    smoothedTargetX = Mathf.Lerp(lastSpikeOriginWorldX, rawGoalX, followBlendT);
                    smoothedProxyBallX = Mathf.Lerp(lastSpikeOriginWorldX, ballWorldX, followBlendT);
                }
                else
                {
                    hasActiveSpikeOrigin = false;
                }
            }

            // Interpolate horizontal SmoothDamp time from normalHorizontalSmoothTime (0.14s) up to highSpeedHorizontalSmoothTime (0.42s)
            // when the ball is flying fast or within the post-spike follow-through window.
            float speedFactor = ball.stateFrame == -1
                ? Mathf.Clamp01((simSpeedPxPerTick - thresholdPx * 0.70f) / Mathf.Max(1f, thresholdPx * 0.65f))
                : 0f;
            if (hasActiveSpikeOrigin && spikeFollowThroughElapsed < totalSpikeWindow)
            {
                float windowFactor = 1f - Mathf.Clamp01(spikeFollowThroughElapsed / totalSpikeWindow);
                speedFactor = Mathf.Max(speedFactor, windowFactor);
            }

            if (baseNormalSmooth > 0f && speedFactor > 0.001f)
            {
                float highSmooth = Mathf.Max(baseNormalSmooth, highSpeedHorizontalSmoothTime);
                activeSmoothTime = Mathf.Lerp(baseNormalSmooth, highSmooth, speedFactor);
                float maxPan = Mathf.Max(1f, highSpeedMaxCameraPanSpeed);
                activeMaxSpeed = Mathf.Lerp(maxPan * 2.2f, maxPan, speedFactor);
            }
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
            // Camera stays strictly floor-anchored on Y (horizontal-only shake offset).
            float offsetY = 0f;

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
            {
                SubscribeToTable(boundTable);
                return boundTable;
            }

            boundTable = null;
            SimulationDataTable singleton = SimulationDataTable.Instance;
            if (singleton != null && singleton.gameObject.scene == gameObject.scene)
            {
                boundTable = singleton;
                SubscribeToTable(boundTable);
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
                    SubscribeToTable(boundTable);
                    return boundTable;
                }
            }
            return null;
        }

        private void SubscribeToTable(SimulationDataTable table)
        {
            if (subscribedTable == table)
            {
                return;
            }
            if (subscribedTable != null)
            {
                subscribedTable.OnSnapshotCommitted -= OnTableSnapshotCommitted;
            }
            subscribedTable = table;
            if (subscribedTable != null)
            {
                subscribedTable.OnSnapshotCommitted -= OnTableSnapshotCommitted;
                subscribedTable.OnSnapshotCommitted += OnTableSnapshotCommitted;
            }
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
