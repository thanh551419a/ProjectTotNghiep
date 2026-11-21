using System;
using Assets.Table;
using Native;
using Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Presentation.Feedback
{
    /// <summary>
    /// Presentation-only feedback for the rendered native ball (Medieval Iron Sphere visual,
    /// forged-iron trail, ground spark burst, and synthesized metallic audio).
    /// It observes committed DataTable snapshots and never calls native APIs or writes simulation/entity state.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public sealed class BallFeedbackPresenter : MonoBehaviour
    {
        public const string IronSphereSpriteName = "P03_MedievalIronSphereSprite";

        // Verified against MyGame/Source/Config/Match/MatchRuleConfig.h. Used only to avoid
        // emitting a landing cue for the native max-touch terminal branch.
        private const int NativeMaxTouchesPerTeam = 3;
        private const int NativeCharactersPerSide = 3;
        private const int ImpactParticleCount = 11;
        private const int GroundExplosionSpikeParticleCount = 15;
        private const float TeleportClearDistance = 3.0f;
        private const float TrailTeleportThresholdWorld = 1.15f;
        private const float MaxLaserBeamTailLength = 0.85f;
        private const float SpherePixelRadiusFraction = 0.5f;

        // RcTdc--AdaI, KIQbyAcdmSM & F_ds3BmnfSA (>= 80 km/h 0.5s Connecting Trail & Spike-Only Width Scaling) constants
        public const float NonSpikeBaselineTrailWidth = 0.08f;
        public const float SpikeMinTrailWidth = 0.21f;
        public const float SpikeMaxTrailWidth = 1.65f;
        public const float DefaultSimTrailBaseWidthPx = 6.40f;
        public const float DefaultSimTrailBaseV0 = 160.0f;
        public const float DefaultSimTrailBaseSpeed = 160.0f;
        public const float SimV0ToSpeedScale = 0.04f;
        public const float DefaultSimSlowTrailWidthPx = 6.40f;
        public const float DefaultSimFastTrailWidthPx = 19.20f;
        private const int ProceduralVfxTextureSize = 128;
        private const float HighSpeedConnectingThresholdSim = 6.40f;
        private const float HighSpeedConnectingTrailDuration = 0.50f;
        private const float LowSpeedMinTrailDuration = 0.24f;
        private const float LowSpeedMaxTrailDuration = 0.38f;
        private const float LowSpeedMinTrailWidth = 0.080f;
        private const float LowSpeedMaxTrailWidth = 0.120f;
        private const float HighSpeedMinTrailWidth = 0.210f;
        private const float HighSpeedMaxTrailWidth = 1.650f;
        private const float NormalRallyTrailWidthScale = 0.65f;
        private const float SonicRingShaftFraction = 0.35f;
        private const float SpikeContactMaxDistanceWorld = 2.5f;
        private const float MidAirBurstDuration = 0.28f;
        private const float OriginPuffDuration = 0.36f;
        private const float SpikeBeamMaxDuration = 0.56f;
        private const int SpikeLightningVertexCount = 7;
        private const float GroundCrossRingDuration = 0.58f;
        private const float GroundExplosionDuration = 0.58f;
        private const float GroundSonicWaveStartRadius = 1.35f;
        private const float SingleBoomRingVideoTargetMaxRadius = 3.45f;
        private const float BoomJumpRingDuration = 0.52f;
        private const float GroundSonicWaveStagger = 0.09f;
        private const float GroundSonicWaveSingleLifetime = 0.40f;
        private const int GroundSonicWaveCount = 3;
        private const int ActiveSingleBoomRingCount = 1;
        private const float GroundEllipseAspectY = 0.36f;
        private const float DefaultGroundTiltAngleDeg = 20.0f;
        private const float DefaultGroundTiltPercent = DefaultGroundTiltAngleDeg / 90.0f;
        private const float DefaultGroundTiltDuration = 0.90f;
        private const float GroundTiltHoldPlateauFraction = 0.25f;
        private const float GroundExplosionStartScale = 1.26f;
        private const float GroundExplosionEndScale = 3.45f;
        private const float SpikeSpeedPopupDuration = 0.95f;
        private const float SpikeSpeedPopupRiseHeight = 0.06f;
        private const int SpikeSpeedPopupFontSize = 72;
        private const int SpikeSpeedPopupUnitFontSize = 49;
        private const float SpikeSpeedPopupCharacterSize = 0.0425f;
        private const float SpikeSpeedPopupTargetScale = 2.0f;
        private const float SpikeSpeedPopupPeakScale = 3.2f;
        private const float SpikeSpeedPopupPopUpPhaseEnd = 0.18f;
        private const float SpikeSpeedPopupShrinkPhaseEnd = 0.48f;
        private const float SpikeSpeedPopupFadeStartProgress = 0.75f;
        private const float SpikeSpeedPopupOutlineOffset = 0.032f;
        private const int SpikeSpeedPopupOutlineCount = 8;
        private const float SpikeSpeedPopupTopCenterViewportOffsetRatio = 0.62f;
        private const float SimFloorReferencePx = 265.0f;
        private const float PointScoreRingDuration = 0.48f;
        private const float CrescentStartScale = 0.48f;
        private const float CrescentEndScale = 1.18f;
        private const float SpeedLinesStartScale = 0.42f;
        private const float SpeedLinesEndScale = 1.48f;
        private const float XSlashStartScale = 0.36f;
        private const float XSlashEndScale = 1.05f;
        private const float OriginPuffStartScale = 0.30f;
        private const float OriginPuffEndScale = 0.92f;
        private const float GroundRingStartScale = 1.14f;
        private const float GroundRingEndScale = 4.56f;
        private const float PointScoreRingStartScale = 0.45f;
        private const float PointScoreRingEndScale = 2.35f;
        private const int PointScoreParticleCount = 14;
        private const int MinSpikePopupKmh = 0;
        private const int MaxSpikePopupKmh = 2000;
        private const float SimSpeedToKmhScale = 10f;
        private const float JumpSpikeScreenV0BoostThreshold = 32.0f;
        private const float JumpSpikeScreenV0BoostFactor = 4.0f;
        private const float FullPathSpeedToDisplayKmhScale = 7.85f;
        private const float DefaultSpikeHorizontalSpanPx = 380.0f;
        private const float SpikeSpeedPopupOffsetY = 0.24f;
        private const float BeamWidthLight = 0.085f;
        private const float BeamWidthMedium = 0.135f;
        private const float BeamWidthStrong = 0.195f;

        [Header("Trail (Input in C++ Sim Units: px & v0 Index)")]
        [Tooltip("Base trail width in C++ Sim pixels (px). Unity converts px -> meters via / CourtGeometry.PixelsPerMeter (80).")]
        [SerializeField, Min(0.1f)] private float trailBaseWidth = DefaultSimTrailBaseWidthPx;
        [Tooltip("Base ball v0 index in C++ Sim layer (chi so v0 cuoi cung, e.g. 160.0 in TrajectoryConfig.h). Unity converts v0 -> traj->speed via v0 * 0.04 (4/100).")]
        [SerializeField, Min(0.01f)] private float trailBaseSpeed = DefaultSimTrailBaseV0;
        [Tooltip("Dimensionless multiplier applied to (ballFinalV0 / trailBaseSpeed).")]
        [SerializeField, Min(0.01f)] private float trailSpeedMultiplier = 1.0f;
        [Tooltip("Low-speed trail duration in seconds.")]
        [SerializeField, Min(0.05f)] private float slowTrailDuration = 0.24f;
        [Tooltip("High-speed (>= 6.40 Sim speed / v0 >= 160) connecting trail duration in seconds.")]
        [SerializeField, Min(0.05f)] private float fastTrailDuration = 0.50f;
        [Tooltip("Minimum ball speed in C++ Sim units (|traj->speed|) required to emit trail.")]
        [SerializeField, Min(0f)] private float minimumTrailSpeed = 0.50f;

        private float slowTrailWidth = DefaultSimSlowTrailWidthPx;
        private float fastTrailWidth = DefaultSimFastTrailWidthPx;

        [Header("Impact & Ground Sonic Waves")]
        [SerializeField] private Color impactColor = new Color(0.88f, 0.97f, 1.00f, 0.46f);
        [SerializeField, Min(0.2f)] private float groundSonicWaveMaxRadius = SingleBoomRingVideoTargetMaxRadius;
        [SerializeField, Min(0.2f)] private float jumpSpikeGroundSonicWaveMaxRadius = SingleBoomRingVideoTargetMaxRadius;
        [SerializeField, Min(0.1f)] private float groundSonicWaveDuration = 0.58f;
        [SerializeField, Range(0.05f, 0.85f)] private float groundSonicWavePeakAlpha = 0.62f;
        [SerializeField, Range(0f, 100f)] private float slowMoTriggerChancePercent = 100f;
        [SerializeField, Range(0f, 0.50f)] private float jumpSpikeGroundTiltPercent = DefaultGroundTiltPercent;
        [SerializeField, Min(0.1f)] private float jumpSpikeGroundTiltDuration = DefaultGroundTiltDuration;
        [SerializeField] private bool showSimSpeedPopup = true;

        [Header("Audio")]
        [SerializeField, Range(0f, 1f)] private float spikeVolume = 0.34f;
        [SerializeField, Range(0f, 1f)] private float bumpVolume = 0.26f;
        [SerializeField, Range(0f, 1f)] private float groundImpactVolume = 0.56f;
        [SerializeField, Range(0f, 1f)] private float jumpSqueakBoomVolume = 0.44f;
        [SerializeField, Range(0f, 0.5f)] private float arenaBgmVolume = 0.16f;

        private static Sprite cachedIronSphereSprite;
        private static Texture2D cachedIronSphereTexture;
        private static Sprite cachedSonicCrescentSprite;
        private static Texture2D cachedSonicCrescentTexture;
        private static Sprite cachedRadialSpeedLinesSprite;
        private static Texture2D cachedRadialSpeedLinesTexture;
        private static Sprite cachedXSlashSparkSprite;
        private static Texture2D cachedXSlashSparkTexture;
        private static Sprite cachedOriginPuffRingSprite;
        private static Texture2D cachedOriginPuffRingTexture;
        private static Sprite cachedGoldenCrossRingSprite;
        private static Texture2D cachedGoldenCrossRingTexture;
        private static Sprite cachedGroundExplosionBurstSprite;
        private static Texture2D cachedGroundExplosionBurstTexture;
        private static Sprite cachedGroundSonicWaveSprite;
        private static Texture2D cachedGroundSonicWaveTexture;
        private static string[] cachedKmhStrings;

        private TrailRenderer trail;
        private ParticleSystem impactParticles;
        private GameObject impactObject;
        private AudioSource audioSource;
        private AudioSource bgmAudioSource;
        private AudioClip spikeClip;
        private AudioClip bumpClip;
        private AudioClip groundClip;
        private AudioClip jumpSqueakBoomClip;
        private AudioClip arenaBgmClip;
        private Material feedbackMaterial;
        private SimulationEntityReader entityReader;
        private SimulationDataTable boundTable;

        // Pre-allocated RcTdc--AdaI, KIQbyAcdmSM & F_ds3BmnfSA VFX child renderers & cached gradients
        private Gradient defaultTrailGradient;
        private Gradient highSpeedServeTrailGradient;
        private Gradient spikeLightTrailGradient;
        private Gradient spikeMediumTrailGradient;
        private Gradient spikeStrongTrailGradient;
        private Transform sonicCrescentTransform;
        private SpriteRenderer sonicCrescentRenderer;
        private Transform radialSpeedLinesTransform;
        private SpriteRenderer radialSpeedLinesRenderer;
        private Transform xSlashSparkTransform;
        private SpriteRenderer xSlashSparkRenderer;
        private Transform originPuffRingTransform;
        private SpriteRenderer originPuffRingRenderer;
        private LineRenderer spikeLaserBeam;
        private LineRenderer spikeLaserBeamShadow;
        private Gradient spikeLightningShadowGradient;
        private Vector3 spikeLightningEndWorldPos;
        private Transform groundCrossRingTransform;
        private SpriteRenderer groundCrossRingRenderer;
        private Transform groundExplosionBurstTransform;
        private SpriteRenderer groundExplosionBurstRenderer;
        private readonly Transform[] groundSonicWaveTransforms = new Transform[GroundSonicWaveCount];
        private readonly SpriteRenderer[] groundSonicWaveRenderers = new SpriteRenderer[GroundSonicWaveCount];
        private Transform boomJumpRingTransform;
        private SpriteRenderer boomJumpRingRenderer;
        private Transform[] cachedTiltableTransforms;
        private Transform spikeSpeedPopupTransform;
        private TextMesh spikeSpeedPopupText;
        private MeshRenderer spikeSpeedPopupRenderer;
        private readonly TextMesh[] spikeSpeedPopupOutlineTexts = new TextMesh[SpikeSpeedPopupOutlineCount];
        private readonly MeshRenderer[] spikeSpeedPopupOutlineRenderers = new MeshRenderer[SpikeSpeedPopupOutlineCount];
        private Transform pointScoreRingTransform;
        private SpriteRenderer pointScoreRingRenderer;

        private int activeSpikeTier;
        private float midAirBurstTimer;
        private float originPuffTimer;
        private float spikeBeamTimer;
        private float groundCrossRingTimer;
        private float groundExplosionTimer;
        private float groundSonicWaveTimer;
        private float boomJumpRingTimer;
        private float activeGroundSonicMaxRadius = SingleBoomRingVideoTargetMaxRadius;
        private float jumpSpikeGroundTiltTimer;
        private float spikeSpeedPopupTimer;
        private float pointScoreRingTimer;
        private Vector3 spikeOriginWorldPos;
        private Vector3 currentBallWorldPos;
        private Vector3 previousBallWorldPos;
        private Vector3 currentBallVelocityDir;
        private Vector3 spikeSpeedPopupOrigin;
        private Vector3 pointScoreWorldPos;
        private Vector3 boomJumpWorldPos;
        private float spikeDirectionSign = 1f;
        private float lastGroundRingTierScale = 1f;
        private float lastActiveBallSpeed;
        private float baseTrailTime = HighSpeedConnectingTrailDuration;
        private float pausedTrailLifetimeExtension;
        private bool didTickAdvanceThisFrame;
        private bool isLastTickSimFrozen;
        private bool hadSlowMoDuringCurrentFlight;
        private bool pendingTrailClearAfterMove;
        private bool hasPreviousScore;
        private int lastTeam0Score;
        private int lastTeam1Score;

        private bool hasProcessedTick;
        private ulong lastProcessedTick;
        private bool hasPreviousBall;
        private SimulationEntityRecord previousBall;
        private bool hasPreviousMatch;
        private SimulationMatchRecord previousMatch;
        private readonly SimulationEntityRecord[] previousCharacters = new SimulationEntityRecord[6];
        private readonly bool[] hasPreviousCharacters = new bool[6];
        private readonly int[] spikeSoundCountsByEntity = new int[6];
        private readonly int[] bumpSoundCountsByEntity = new int[6];

        public int GroundImpactEventCount { get; private set; }
        public int GroundImpactSoundCount { get; private set; }
        public int GroundExplosionBurstCount { get; private set; }
        public int BoomJumpEventCount { get; private set; }
        public int BoomJumpSoundCount { get; private set; }
        public int SpikeSoundCount { get; private set; }
        public int BumpSoundCount { get; private set; }
        public int PointScoredBurstCount { get; private set; }
        public int LastSpikeImpactKmh { get; private set; }
        public float LastSpikeImpactSimSpeed { get; private set; }
        public float LastSpikeHorizontalV0Speed { get; private set; }
        public float LastSpikeFullPathSpeedSim { get; private set; }
        public float LastSpikeDisplayedPathSpeedKmh { get; private set; }
        public bool ShowSimSpeedPopup { get => showSimSpeedPopup; set => showSimSpeedPopup = value; }
        public bool IsSpikeFlightActive => activeSpikeTier > 0;
        public float TrailBaseWidth { get => trailBaseWidth; set => trailBaseWidth = Mathf.Max(0.001f, value); }
        public float TrailBaseSpeed { get => trailBaseSpeed; set => trailBaseSpeed = Mathf.Max(0.001f, value); }
        public float TrailSpeedMultiplier { get => trailSpeedMultiplier; set => trailSpeedMultiplier = Mathf.Max(0.01f, value); }
        public float SlowTrailDuration { get => slowTrailDuration; set => slowTrailDuration = Mathf.Max(0.05f, value); }
        public float FastTrailDuration { get => fastTrailDuration; set => fastTrailDuration = Mathf.Max(0.05f, value); }
        public float LastEffectiveBallSpeed => lastActiveBallSpeed;
        public float PausedTrailLifetimeExtension => pausedTrailLifetimeExtension;
        public bool IsSimLayerPausedOrFrozen { get; private set; }
        public float GroundSonicWaveMaxRadius { get => groundSonicWaveMaxRadius; set => groundSonicWaveMaxRadius = Mathf.Max(0.2f, value); }
        public float JumpSpikeGroundSonicWaveMaxRadius { get => jumpSpikeGroundSonicWaveMaxRadius; set => jumpSpikeGroundSonicWaveMaxRadius = Mathf.Max(0.2f, value); }
        public float LastGroundSonicWaveTargetMaxRadius => activeGroundSonicMaxRadius;
        public float SlowMoTriggerChancePercent { get => slowMoTriggerChancePercent; set => slowMoTriggerChancePercent = Mathf.Clamp(value, 0f, 100f); }
        public float JumpSpikeGroundTiltPercent { get => jumpSpikeGroundTiltPercent; set => jumpSpikeGroundTiltPercent = Mathf.Clamp(value, 0f, 0.50f); }
        public float LastJumpSpikeGroundTiltAngleDeg { get; private set; }
        public float CurrentJumpSpikeGroundTiltAngleDeg { get; private set; }
        public int JumpSpikeGroundTiltCount { get; private set; }
        public int ActiveGroundSonicWaveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < GroundSonicWaveCount; i++)
                {
                    if (groundSonicWaveRenderers[i] != null && groundSonicWaveRenderers[i].enabled)
                        count++;
                }
                return count;
            }
        }
        public float ComputeSpikeTrailWidthSimPx(float simBallSpeed) =>
            trailBaseWidth * (simBallSpeed / Mathf.Max(0.0001f, trailBaseSpeed * SimV0ToSpeedScale)) * trailSpeedMultiplier;
        public float ComputeSpikeTrailWidth(float simBallSpeed) =>
            CourtGeometry.ToWorldLength(ComputeSpikeTrailWidthSimPx(simBallSpeed));
        public int SpikeSoundCountFor(uint entityId) => entityId < (uint)spikeSoundCountsByEntity.Length ? spikeSoundCountsByEntity[entityId] : 0;
        public int BumpSoundCountFor(uint entityId) => entityId < (uint)bumpSoundCountsByEntity.Length ? bumpSoundCountsByEntity[entityId] : 0;
        public int TrailClearCount { get; private set; }
        public ulong LastGroundImpactTick { get; private set; }
        public Vector3 LastImpactWorldPosition { get; private set; }
        public TrailRenderer Trail => trail;
        public ParticleSystem ImpactParticles => impactParticles;
        public AudioSource AudioSource => audioSource;
        public bool HasBoundTable => boundTable != null;
        public bool ProceduralAudioReady => spikeClip != null && bumpClip != null && groundClip != null;
        public float SpikeVolume => spikeVolume;
        public float BumpVolume => bumpVolume;
        public float GroundImpactVolume => groundImpactVolume;

        /// <summary>
        /// Assigns the procedural Medieval Iron Sphere sprite onto the target SpriteRenderer.
        /// </summary>
        public static void ApplyMedievalIronSphere(SpriteRenderer renderer)
        {
            if (renderer == null) return;
            renderer.sprite = GetOrCreateIronSphereSprite();
            Color c = renderer.color;
            renderer.color = new Color(1f, 1f, 1f, c.a > 0f ? c.a : 1f);
        }

        public static Sprite GetOrCreateIronSphereSprite()
        {
            if (cachedIronSphereSprite != null) return cachedIronSphereSprite;

            const int N = 64;
            cachedIronSphereTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "P03_MedievalIronSphereTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color darkRim = new Color(0.10f, 0.12f, 0.16f, 1f);
            Color baseIron = new Color(0.32f, 0.36f, 0.43f, 1f);
            Color midSteel = new Color(0.54f, 0.60f, 0.69f, 1f);
            Color specular = new Color(0.94f, 0.97f, 1.00f, 1f);
            Color bandSeam = new Color(0.20f, 0.23f, 0.28f, 1f);
            Color rivetBright = new Color(0.80f, 0.85f, 0.92f, 1f);
            Color emberRim = new Color(0.86f, 0.46f, 0.20f, 1f);

            Vector2 lightDir = new Vector2(-0.38f, 0.42f);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * SpherePixelRadiusFraction);
                    float ny = (y - (N - 1) * 0.5f) / (N * SpherePixelRadiusFraction);
                    float r2 = nx * nx + ny * ny;
                    if (r2 > 1.0f)
                    {
                        cachedIronSphereTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float r = Mathf.Sqrt(r2);
                    float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - r2));
                    float edgeAlpha = Mathf.Clamp01((1f - r) * 18f);

                    float lambert = Mathf.Clamp01((nx * lightDir.x + ny * lightDir.y + nz * 0.82f));
                    Color col = Color.Lerp(baseIron, midSteel, lambert);

                    // Forged iron Equatorial + Meridian seam bands
                    bool onBand = Mathf.Abs(ny) < 0.11f || Mathf.Abs(nx) < 0.11f;
                    if (onBand)
                    {
                        col = Color.Lerp(col, bandSeam, 0.62f);
                        // Rivet studs along the iron bands
                        float rx = Mathf.Repeat((nx + 1f) * 3.5f, 1f) - 0.5f;
                        float ry = Mathf.Repeat((ny + 1f) * 3.5f, 1f) - 0.5f;
                        if ((Mathf.Abs(ny) < 0.08f && rx * rx + (ny * 6f) * (ny * 6f) < 0.06f) ||
                            (Mathf.Abs(nx) < 0.08f && ry * ry + (nx * 6f) * (nx * 6f) < 0.06f))
                        {
                            col = Color.Lerp(col, rivetBright, 0.75f);
                        }
                    }

                    // Upper-left metallic specular highlight
                    float specDist = Vector2.Distance(new Vector2(nx, ny), new Vector2(-0.32f, 0.34f));
                    float spec = Mathf.Pow(Mathf.Clamp01(1f - specDist * 2.1f), 2.4f);
                    col = Color.Lerp(col, specular, spec * 0.85f);

                    // Lower-right warm forge-ember bounce light & dark forged outer rim
                    if (r > 0.76f)
                    {
                        float rimT = (r - 0.76f) / 0.24f;
                        float emberWeight = Mathf.Clamp01((nx * 0.6f - ny * 0.6f));
                        Color rimTarget = Color.Lerp(darkRim, emberRim, emberWeight * 0.45f);
                        col = Color.Lerp(col, rimTarget, rimT * 0.88f);
                    }

                    col.a = edgeAlpha;
                    cachedIronSphereTexture.SetPixel(x, y, col);
                }
            }

            cachedIronSphereTexture.Apply();
            cachedIronSphereSprite = Sprite.Create(cachedIronSphereTexture, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cachedIronSphereSprite.name = IronSphereSpriteName;
            cachedIronSphereSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedIronSphereSprite;
        }

        private void Awake()
        {
            UpgradeLegacyGroundSonicRadius();
            entityReader = GetComponent<SimulationEntityReader>();
            ApplyMedievalIronSphere(GetComponent<SpriteRenderer>());
            CreateTrail();
            CreateImpactParticles();
            CreateSpikeAndImpactOverlays();
            CacheTiltableSceneObjects();
            CreateAudioSource();
            CreatePlaceholderClips();
            CreateArenaAmbience();
            BindTable();
        }

        private void OnValidate()
        {
            UpgradeLegacyGroundSonicRadius();
        }

        private void UpgradeLegacyGroundSonicRadius()
        {
            // Enforce single Boom Ring court-relative radius (3.45m = 51% of half-court width matching Qo0kCZF0NWA)
            if (groundSonicWaveMaxRadius <= 2.0f || Mathf.Abs(groundSonicWaveMaxRadius - 5.55f) < 0.05f || groundSonicWaveMaxRadius > 4.5f)
                groundSonicWaveMaxRadius = SingleBoomRingVideoTargetMaxRadius;
            if (jumpSpikeGroundSonicWaveMaxRadius <= 2.0f || Mathf.Abs(jumpSpikeGroundSonicWaveMaxRadius - 9.75f) < 0.05f || jumpSpikeGroundSonicWaveMaxRadius > 4.5f)
                jumpSpikeGroundSonicWaveMaxRadius = SingleBoomRingVideoTargetMaxRadius;
            // Enforce 20.0 deg screen/object tilt (20 / 90 = 0.2222f) and longer 0.90s sustain duration
            if (jumpSpikeGroundTiltPercent < 0.21f)
                jumpSpikeGroundTiltPercent = DefaultGroundTiltPercent;
            if (jumpSpikeGroundTiltDuration < 0.80f)
                jumpSpikeGroundTiltDuration = DefaultGroundTiltDuration;
            if (slowMoTriggerChancePercent < 100f)
                slowMoTriggerChancePercent = 100f;
            showSimSpeedPopup = true;
            // Upgrade legacy Unity-unit or raw traj->speed trail parameters (<= 20.0f or old 2.2x multiplier) to C++ Sim units (px & v0 = 160.0f)
            if (trailBaseWidth < 1.0f)
                trailBaseWidth = DefaultSimTrailBaseWidthPx;
            if (trailBaseSpeed <= 20.0f || Mathf.Abs(trailSpeedMultiplier - 2.2f) < 0.01f)
            {
                trailBaseSpeed = DefaultSimTrailBaseV0;
                trailSpeedMultiplier = 1.0f;
            }
            if (slowTrailWidth < 1.0f)
                slowTrailWidth = DefaultSimSlowTrailWidthPx;
            if (fastTrailWidth < 1.0f)
                fastTrailWidth = DefaultSimFastTrailWidthPx;
            if (minimumTrailSpeed < 0.1f)
            {
                minimumTrailSpeed = 0.50f;
            }
            if (trail != null)
            {
                trail.numCornerVertices = 0;
                trail.numCapVertices = 0;
                trail.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            }
        }

        private void OnEnable()
        {
            Camera.onPreCull -= OnCameraPreCullTilt;
            Camera.onPreCull += OnCameraPreCullTilt;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRenderingTilt;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRenderingTilt;
            ApplyMedievalIronSphere(GetComponent<SpriteRenderer>());
            BindTable();
            if (Application.isPlaying && bgmAudioSource != null && arenaBgmClip != null && !bgmAudioSource.isPlaying && arenaBgmVolume > 0f)
            {
                bgmAudioSource.volume = arenaBgmVolume;
                bgmAudioSource.Play();
            }
        }

        private void OnDisable()
        {
            Camera.onPreCull -= OnCameraPreCullTilt;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRenderingTilt;
            if (boundTable != null)
                boundTable.OnSnapshotCommitted -= OnSnapshotCommitted;
            boundTable = null;
            ResetTransientFeedback(true);
            if (bgmAudioSource != null) bgmAudioSource.Stop();
        }

        private void OnDestroy()
        {
            DestroyOwned(spikeClip);
            DestroyOwned(bumpClip);
            DestroyOwned(groundClip);
            DestroyOwned(jumpSqueakBoomClip);
            DestroyOwned(arenaBgmClip);
            DestroyOwned(feedbackMaterial);
        }

        private void LateUpdate()
        {
            BindTable();
            if (pendingTrailClearAfterMove && trail != null)
            {
                trail.Clear();
                pendingTrailClearAfterMove = false;
                if (hasPreviousBall && previousBall.stateFrame != 0 &&
                    Mathf.Max(Mathf.Abs(previousBall.ballSpeed), lastActiveBallSpeed) >= minimumTrailSpeed)
                {
                    trail.emitting = true;
                }
            }

            // Synchronize Pause & 1000ms Slow-Motion between Sim layer and Presentation layer:
            // When the Sim layer is paused (!didTickAdvanceThisFrame), on a frozen slow-motion tick, or while SlowMoFramesRemaining > 0
            // during active flight / post-score bounce (stateFrame != 0), extend trail.time by Time.deltaTime so the trail NEVER disappears!
            bool inActiveSlowMoWindow = hasPreviousBall && previousBall.stateFrame != 0 &&
                                        boundTable != null && boundTable.SlowMoFramesRemaining > 0;
            bool simPausedInActiveFlight = hasPreviousBall && previousBall.stateFrame != 0 &&
                                           (!didTickAdvanceThisFrame || isLastTickSimFrozen);
            if (simPausedInActiveFlight || inActiveSlowMoWindow)
            {
                if (simPausedInActiveFlight)
                    IsSimLayerPausedOrFrozen = true;
                pausedTrailLifetimeExtension += Time.deltaTime;
                if (trail != null)
                {
                    trail.time = baseTrailTime + pausedTrailLifetimeExtension;
                }
            }

            float overlayDt = simPausedInActiveFlight ? 0f : Time.deltaTime;
            if (midAirBurstTimer > 0f || originPuffTimer > 0f || spikeBeamTimer > 0f ||
                groundCrossRingTimer > 0f || groundExplosionTimer > 0f || groundSonicWaveTimer > 0f ||
                boomJumpRingTimer > 0f || jumpSpikeGroundTiltTimer > 0f || spikeSpeedPopupTimer > 0f ||
                pointScoreRingTimer > 0f)
            {
                UpdateOverlayVisuals(overlayDt);
            }

            didTickAdvanceThisFrame = false;
        }

        private void BindTable()
        {
            SimulationDataTable table = SimulationDataTable.Instance;
            if (table == boundTable) return;

            if (boundTable != null)
                boundTable.OnSnapshotCommitted -= OnSnapshotCommitted;

            boundTable = table;
            ResetTransientFeedback(true);
            if (boundTable != null)
                boundTable.OnSnapshotCommitted += OnSnapshotCommitted;
        }

        private void OnSnapshotCommitted(ulong tick)
        {
            if (boundTable == null || !boundTable.IsInitialized) return;

            // A duplicate callback cannot replay a one-shot. A lower tick is a simulation reset.
            if (hasProcessedTick && tick == lastProcessedTick) return;
            if (hasProcessedTick && tick < lastProcessedTick)
                ResetTransientFeedback(true);

            lastProcessedTick = tick;
            hasProcessedTick = true;
            didTickAdvanceThisFrame = true;

            if (!boundTable.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ball) ||
                !ball.hasPosition || !ball.hasSize || !ball.hasBallGameplayState)
            {
                hasPreviousBall = false;
                hasPreviousMatch = false;
                Array.Clear(hasPreviousCharacters, 0, hasPreviousCharacters.Length);
                ClearTrail();
                StopAudio();
                DisableOverlayVisuals();
                return;
            }

            Vector3 anchor = SimulationEntityReader.AnchorWorld(ball);
            Vector3 previousAnchor = hasPreviousBall ? SimulationEntityReader.AnchorWorld(previousBall) : anchor;
            Vector3 frameDelta = anchor - previousAnchor;
            if (hasPreviousBall && frameDelta.sqrMagnitude > 0.00001f)
            {
                currentBallVelocityDir = frameDelta.normalized;
            }
            previousBallWorldPos = previousAnchor;
            currentBallWorldPos = anchor;

            SimulationMatchRecord match = boundTable.MatchRecord;
            bool hasMatchBefore = hasPreviousMatch;
            SimulationMatchRecord matchBefore = previousMatch;
            bool ballTrajectoryChangedThisTick = hasPreviousBall && DidBallTrajectoryChange(previousBall, ball);
            int acceptedTouchEntity = ProcessAcceptedActionSounds(boundTable, matchBefore.lastTouchEntity, ball,
                ballTrajectoryChangedThisTick);
            bool maxTouchEndedThisTick = hasMatchBefore &&
                                         IsSameSideFourthTouch(matchBefore.touchCount,
                                             matchBefore.lastTouchEntity, acceptedTouchEntity);

            bool serveStartedThisTick = hasPreviousBall && previousBall.stateFrame == 0 && ball.stateFrame == -1;
            bool enteredResetThisTick = !hasPreviousBall || (previousBall.stateFrame != 0 && ball.stateFrame == 0);
            bool serveOrReset = enteredResetThisTick || (previousBall.stateFrame != -1 && ball.stateFrame == -1);
            // Only treat a large step as a teleport when entering/leaving Reset (stateFrame == 0) and step exceeds threshold.
            // During active high-speed flight (FIXED_V0_MULTIPLIER = 7.20f, > 1.15m/tick), NEVER clear the trail!
            float stepDistanceWorld = hasPreviousBall ? Vector2.Distance(previousAnchor, anchor) : 0f;
            bool teleported = hasPreviousBall &&
                              (ball.stateFrame == 0 || previousBall.stateFrame == 0) &&
                              stepDistanceWorld > TrailTeleportThresholdWorld;

            bool activeBall = ball.stateFrame != 0;
            bool frozenSlowMoTick = activeBall && hasPreviousBall && !serveOrReset && !teleported &&
                                    acceptedTouchEntity < 0 &&
                                    (frameDelta.sqrMagnitude < 0.000001f ||
                                     (Mathf.Abs(ball.ballSpeed) < 0.0001f && lastActiveBallSpeed >= minimumTrailSpeed));
            isLastTickSimFrozen = frozenSlowMoTick;
            if (frozenSlowMoTick)
            {
                hadSlowMoDuringCurrentFlight = true;
                IsSimLayerPausedOrFrozen = true;
                pausedTrailLifetimeExtension += Mathf.Max(Time.deltaTime, 1f / 60f);
            }
            else if (enteredResetThisTick || teleported)
            {
                pausedTrailLifetimeExtension = 0f;
                IsSimLayerPausedOrFrozen = false;
                hadSlowMoDuringCurrentFlight = false;
            }
            else
            {
                // Preserve pausedTrailLifetimeExtension for the entire remaining flight and post-impact bounce
                // so trail vertices emitted during the 1000ms slow-motion window are NEVER prematurely culled!
                if (activeBall && boundTable != null && boundTable.SlowMoFramesRemaining > 0)
                {
                    hadSlowMoDuringCurrentFlight = true;
                }
                IsSimLayerPausedOrFrozen = false;
            }

            if (hasPreviousBall && IsGroundImpactTransition(previousBall, ball, maxTouchEndedThisTick,
                    CourtGeometry.FloorY))
            {
                EmitGroundImpact(ball, tick);
            }

            if (serveOrReset || teleported)
            {
                if (ball.stateFrame == 0)
                    lastActiveBallSpeed = 0f;
                ClearTrail();
                // Only defer a post-move trail clear when entering Reset (ball.stateFrame == 0) or teleporting.
                // When launching a serve/spike (stateFrame == -1), keep emitting immediately from tick 0 so the first high-speed segment is never erased in LateUpdate!
                if (ball.stateFrame == 0 || teleported)
                {
                    pendingTrailClearAfterMove = true;
                }
                bool spikeTriggeredThisTick = activeSpikeTier > 0 && acceptedTouchEntity >= 0 && ball.stateFrame == -1;
                if (!spikeTriggeredThisTick)
                {
                    StopAudio();
                    DisableOverlayVisuals();
                    if (impactParticles != null)
                        impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (serveStartedThisTick && !spikeTriggeredThisTick)
                {
                    float serveSimSpeed = Mathf.Abs(ball.ballSpeed);
                    if (serveSimSpeed >= HighSpeedConnectingThresholdSim)
                    {
                        spikeOriginWorldPos = anchor;
                        spikeDirectionSign = ball.ballSpeed >= 0f ? 1f : -1f;
                        midAirBurstTimer = MidAirBurstDuration;
                        originPuffTimer = OriginPuffDuration;
                        UpdateOverlayVisuals(0f);
                    }
                }
            }

            ProcessScoreCelebration(boundTable, anchor, serveOrReset);

            // Keep the trail active whenever the ball is flying or bouncing (including after death stateFrame > 0!).
            // The trail is only disabled when the ball is held in Reset (stateFrame == 0).
            UpdateTrail(ball, anchor, activeBall);
            if ((ball.stateFrame == 0 || teleported) && trail != null)
            {
                trail.emitting = false;
            }

            if (spikeBeamTimer > 0f && activeBall && spikeLaserBeam != null && acceptedTouchEntity < 0)
            {
                // Keep the jagged golden-yellow lightning bolt & dark burnt-amber shadow connected from
                // the mid-air spike contact point down to the flying ball / floor impact point (matching reference image).
                if (ball.stateFrame == -1)
                {
                    spikeLightningEndWorldPos = anchor;
                }
                UpdateSpikeLightningPolyline(spikeOriginWorldPos, spikeLightningEndWorldPos);
            }

            previousBall = ball;
            hasPreviousBall = true;
            previousMatch = match;
            hasPreviousMatch = true;
        }

        private void ProcessScoreCelebration(SimulationDataTable table, Vector3 ballWorldAnchor, bool serveOrReset)
        {
            SimulationMatchRecord match = table.MatchRecord;
            int team0 = match.leftScore;
            int team1 = match.rightScore;

            if (!hasPreviousScore || table.CurrentTick <= 1 ||
                (team0 == 0 && team1 == 0) ||
                team0 < lastTeam0Score || team1 < lastTeam1Score)
            {
                lastTeam0Score = team0;
                lastTeam1Score = team1;
                hasPreviousScore = true;
                return;
            }

            if (team0 > lastTeam0Score || team1 > lastTeam1Score)
            {
                lastTeam0Score = team0;
                lastTeam1Score = team1;
                EmitPointScoredBurst(ballWorldAnchor);
            }
            else if (serveOrReset)
            {
                lastTeam0Score = team0;
                lastTeam1Score = team1;
            }
        }

        private void EmitPointScoredBurst(Vector3 ballWorldAnchor)
        {
            pointScoreWorldPos = GroundImpactEventCount > 0
                ? LastImpactWorldPosition
                : new Vector3(ballWorldAnchor.x, CourtGeometry.ToWorldY(CourtGeometry.FloorY), ballWorldAnchor.z);
            pointScoreRingTimer = PointScoreRingDuration;
            PointScoredBurstCount++;
            UpdateOverlayVisuals(0f);

            if (impactParticles == null) return;
            Color goldSpark = new Color(1.00f, 0.88f, 0.30f, 0.98f);
            Color cyanSpark = new Color(0.62f, 0.96f, 1.00f, 0.98f);
            impactParticles.Play(true);
            for (int i = 0; i < PointScoreParticleCount; i++)
            {
                float angle = (i / (float)PointScoreParticleCount) * Mathf.PI * 2f;
                float speed = (i % 2 == 0) ? 1.55f : 1.10f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = pointScoreWorldPos + new Vector3(0f, 0.08f, 0f),
                    velocity = new Vector3(Mathf.Cos(angle) * speed, Mathf.Abs(Mathf.Sin(angle)) * speed + 0.32f, 0f),
                    startLifetime = 0.34f,
                    startSize = (i % 2 == 0) ? 0.18f : 0.13f,
                    startColor = (i % 2 == 0) ? goldSpark : cyanSpark
                };
                impactParticles.Emit(emit, 1);
            }
        }

        /// <summary>
        /// Existing native output confirms a floor landing only on Alive -> terminal-frame while
        /// the committed ball AABB is at/below the authoritative floor. A terminal frame caused
        /// by an accepted max-touch action is explicitly excluded.
        /// </summary>
        public static bool IsGroundImpactTransition(SimulationEntityRecord previous,
            SimulationEntityRecord current, bool maxTouchEndedThisTick, float floorY)
        {
            return !maxTouchEndedThisTick &&
                   previous.hasBallGameplayState && current.hasBallGameplayState &&
                   previous.stateFrame == -1 && current.stateFrame > 0 &&
                   previous.hasPosition && current.hasPosition &&
                   previous.posY > floorY && current.posY <= floorY &&
                   CenterDistanceWorld(previous, current) <= TeleportClearDistance;
        }

        private static float CenterDistanceWorld(SimulationEntityRecord previous, SimulationEntityRecord current)
        {
            float previousX = previous.posX + previous.sizeX * 0.5f;
            float previousY = previous.posY + previous.sizeY * 0.5f;
            float currentX = current.posX + current.sizeX * 0.5f;
            float currentY = current.posY + current.sizeY * 0.5f;
            return Vector2.Distance(new Vector2(previousX, previousY), new Vector2(currentX, currentY)) /
                   CourtGeometry.PixelsPerMeter;
        }

        /// <summary>Matches native UpdateRallyTouch: only a fourth consecutive touch by that side ends the rally.</summary>
        public static bool IsSameSideFourthTouch(uint previousTouchCount, int previousLastTouchEntity,
            int acceptedTouchEntity)
        {
            return previousTouchCount >= NativeMaxTouchesPerTeam &&
                   previousLastTouchEntity >= 0 && acceptedTouchEntity >= 0 &&
                   previousLastTouchEntity / NativeCharactersPerSide == acceptedTouchEntity / NativeCharactersPerSide;
        }

        /// <summary>Recognizes only action-state edges/reset transitions returned by native output.</summary>
        public static bool IsAcceptedActionStart(SimulationEntityRecord previous,
            SimulationEntityRecord current, bool hasPrevious)
        {
            if (!current.hasCharacterActionState || current.actionStatus == (int)SimActionState.None)
                return false;

            // A first snapshot can be observed after an action already began (scene/table bind).
            // It establishes history; only a later committed transition is an event.
            if (!hasPrevious || !previous.hasCharacterActionState)
                return false;

            if (previous.actionStatus == (int)SimActionState.None)
                return true;

            return previous.actionStatus != current.actionStatus ||
                   (current.actionStatus == previous.actionStatus &&
                    current.actionRemainTime > previous.actionRemainTime + 0.001f);
        }

        private static bool DidBallTrajectoryChange(SimulationEntityRecord previous, SimulationEntityRecord current)
        {
            return !Mathf.Approximately(current.ballSpeed, previous.ballSpeed) ||
                   current.trajectoryType != previous.trajectoryType ||
                   !Mathf.Approximately(current.trajA, previous.trajA) ||
                   !Mathf.Approximately(current.trajB, previous.trajB) ||
                   !Mathf.Approximately(current.trajC, previous.trajC);
        }

        private bool DidSpikeContactBall(SimulationEntityRecord character, SimulationEntityRecord ball,
            bool ballTrajectoryChangedThisTick)
        {
            Vector3 charWorld = character.hasPosition ? SimulationEntityReader.AnchorWorld(character) : Vector3.zero;
            bool withinCurrentProximity = !character.hasPosition || !ball.hasPosition ||
                                          Vector2.Distance(charWorld,
                                              SimulationEntityReader.AnchorWorld(ball)) <= SpikeContactMaxDistanceWorld;
            bool withinPreviousProximity = hasPreviousBall && character.hasPosition && previousBall.hasPosition &&
                                           Vector2.Distance(charWorld,
                                               SimulationEntityReader.AnchorWorld(previousBall)) <= SpikeContactMaxDistanceWorld;
            bool withinContactProximity = withinCurrentProximity || withinPreviousProximity;
            if (ballTrajectoryChangedThisTick)
                return withinContactProximity;

            if (hasPreviousBall && (Mathf.Abs(ball.ballSpeed) > 0.001f || Mathf.Abs(previousBall.ballSpeed) > 0.001f))
                return false;

            return withinContactProximity;
        }

        private int ProcessAcceptedActionSounds(SimulationDataTable table, int previousLastTouchEntity,
            SimulationEntityRecord currentBall, bool ballTrajectoryChangedThisTick)
        {
            int acceptedTouchEntity = -1;
            for (int entityId = 0; entityId < previousCharacters.Length; entityId++)
            {
                if (!table.TryGetEntity((uint)entityId, out SimulationEntityRecord character))
                {
                    hasPreviousCharacters[entityId] = false;
                    continue;
                }

                bool started = IsAcceptedActionStart(previousCharacters[entityId], character,
                    hasPreviousCharacters[entityId]);

                // Detect character jump takeoff from the court floor -> emit Single Boom Jump Ring + Sneaker Shoe Squeak & Boom Jump SFX
                if (hasPreviousCharacters[entityId] &&
                    previousCharacters[entityId].hasPosition && character.hasPosition)
                {
                    float floorBaseline = CourtGeometry.FloorY;
                    bool wasOnFloor = previousCharacters[entityId].posY <= floorBaseline + 2.5f;
                    bool tookOffAirborne = character.posY > floorBaseline + 3.0f &&
                                           (character.posY - previousCharacters[entityId].posY) > 1.2f;
                    if (wasOnFloor && tookOffAirborne)
                    {
                        EmitCharacterBoomJump(character);
                    }
                }

                bool spikeWhiffed = false;
                if (started)
                {
                    SimActionState state = (SimActionState)character.actionStatus;
                    if (state == SimActionState.Bump)
                    {
                        if (DidSpikeContactBall(character, currentBall, ballTrajectoryChangedThisTick))
                        {
                            if (acceptedTouchEntity < 0 || IsSameTeam(previousLastTouchEntity, entityId))
                                acceptedTouchEntity = entityId;
                            activeSpikeTier = 0;
                            spikeBeamTimer = 0f;
                            if (spikeLaserBeam != null) spikeLaserBeam.enabled = false;
                            if (spikeLaserBeamShadow != null) spikeLaserBeamShadow.enabled = false;
                            if (trail != null && defaultTrailGradient != null)
                                trail.colorGradient = defaultTrailGradient;
                            if (PlayOneShot(bumpClip, bumpVolume))
                            {
                                BumpSoundCount++;
                                bumpSoundCountsByEntity[entityId]++;
                            }
                        }
                    }
                    else if (state == SimActionState.Set)
                    {
                        if (DidSpikeContactBall(character, currentBall, ballTrajectoryChangedThisTick))
                        {
                            if (acceptedTouchEntity < 0 || IsSameTeam(previousLastTouchEntity, entityId))
                                acceptedTouchEntity = entityId;
                            activeSpikeTier = 0;
                            spikeBeamTimer = 0f;
                            if (spikeLaserBeam != null) spikeLaserBeam.enabled = false;
                            if (spikeLaserBeamShadow != null) spikeLaserBeamShadow.enabled = false;
                            if (trail != null && defaultTrailGradient != null)
                                trail.colorGradient = defaultTrailGradient;
                        }
                    }
                    else if (state == SimActionState.Spike || state == SimActionState.SpikeLight ||
                             state == SimActionState.SpikeMedium || state == SimActionState.SpikeStrong)
                    {
                        if (DidSpikeContactBall(character, currentBall, ballTrajectoryChangedThisTick))
                        {
                            if (acceptedTouchEntity < 0 || IsSameTeam(previousLastTouchEntity, entityId))
                                acceptedTouchEntity = entityId;
                            if (PlayOneShot(spikeClip, spikeVolume))
                            {
                                SpikeSoundCount++;
                                spikeSoundCountsByEntity[entityId]++;
                            }
                            ApplySpikeTierTrail(state);
                            EmitSpikeShockwave(character, currentBall, state);
                        }
                        else
                        {
                            spikeWhiffed = true;
                        }
                    }
                }

                previousCharacters[entityId] = character;
                if (spikeWhiffed)
                    previousCharacters[entityId].actionStatus = (int)SimActionState.None;
                hasPreviousCharacters[entityId] = true;
            }
            return acceptedTouchEntity;
        }

        private void EmitCharacterBoomJump(SimulationEntityRecord character)
        {
            Vector3 charAnchor = SimulationEntityReader.AnchorWorld(character);
            boomJumpWorldPos = new Vector3(charAnchor.x, CourtGeometry.ToWorldY(CourtGeometry.FloorY) + 0.02f, 0f);
            boomJumpRingTimer = BoomJumpRingDuration;
            BoomJumpEventCount++;
            if (audioSource != null && jumpSqueakBoomClip != null && jumpSqueakBoomVolume > 0f)
            {
                audioSource.PlayOneShot(jumpSqueakBoomClip, Mathf.Clamp01(jumpSqueakBoomVolume));
                BoomJumpSoundCount++;
            }
            UpdateOverlayVisuals(0f);
        }

        private void ApplySpikeTierTrail(SimActionState state)
        {
            if (state == SimActionState.SpikeLight)
            {
                activeSpikeTier = 1;
                if (trail != null && spikeLightTrailGradient != null)
                    trail.colorGradient = spikeLightTrailGradient;
            }
            else if (state == SimActionState.SpikeMedium)
            {
                activeSpikeTier = 2;
                if (trail != null && spikeMediumTrailGradient != null)
                    trail.colorGradient = spikeMediumTrailGradient;
            }
            else
            {
                activeSpikeTier = 3;
                if (trail != null && spikeStrongTrailGradient != null)
                    trail.colorGradient = spikeStrongTrailGradient;
            }
        }

        private static bool IsSameTeam(int firstEntityId, int secondEntityId) =>
            firstEntityId >= 0 && secondEntityId >= 0 &&
            firstEntityId / NativeCharactersPerSide == secondEntityId / NativeCharactersPerSide;

        private static bool IsTouchAction(SimActionState state)
        {
            return state == SimActionState.Bump || state == SimActionState.Set ||
                   state == SimActionState.Spike || state == SimActionState.SpikeLight ||
                   state == SimActionState.SpikeMedium || state == SimActionState.SpikeStrong;
        }

        private void UpdateTrail(SimulationEntityRecord ball, Vector3 anchor, bool active)
        {
            if (trail == null) return;
            // 100% direct synchronization with C++ Sim layer ball.ballSpeed (|traj->speed|, ZERO Unity unit conversion!)
            float rawSimSpeed = Mathf.Abs(ball.ballSpeed);
            float fallbackSimDelta = (!ball.hasBallTrajectory && hasPreviousBall &&
                                      Vector2.Distance(previousBallWorldPos, anchor) <= TrailTeleportThresholdWorld)
                ? Vector2.Distance(new Vector2(previousBall.posX, previousBall.posY), new Vector2(ball.posX, ball.posY))
                : 0f;
            float candidateSpeed = ball.hasBallTrajectory ? rawSimSpeed : Mathf.Max(rawSimSpeed, fallbackSimDelta);
            if (active && candidateSpeed >= minimumTrailSpeed)
            {
                lastActiveBallSpeed = candidateSpeed;
            }

            // Preserve latched C++ Sim flight speed during slow-motion frozen ticks or post-impact bounces
            float simSpeed = (active && candidateSpeed < minimumTrailSpeed && lastActiveBallSpeed >= minimumTrailSpeed)
                ? lastActiveBallSpeed
                : candidateSpeed;

            bool emit = active && simSpeed >= minimumTrailSpeed;
            trail.emitting = emit;
            if (!emit)
            {
                if (!active && ball.stateFrame == 0 && trail.positionCount > 0)
                    trail.Clear();
                return;
            }

            // Direct synchronous trail width in C++ Sim speed units ("van toc bao nhieu, trail rong bay nhieu"):
            // trailWidth = trailBaseWidth * (simBallSpeed / trailBaseSpeed) * trailSpeedMultiplier
            trail.widthMultiplier = ComputeSpikeTrailWidth(simSpeed);

            if (activeSpikeTier > 0 || simSpeed >= HighSpeedConnectingThresholdSim)
            {
                baseTrailTime = Mathf.Max(0.05f, fastTrailDuration);
                if (activeSpikeTier == 0 && highSpeedServeTrailGradient != null)
                    trail.colorGradient = highSpeedServeTrailGradient;
            }
            else
            {
                float lowSpeedT = Mathf.Clamp01(simSpeed / HighSpeedConnectingThresholdSim);
                float minLowTime = Mathf.Max(0.05f, slowTrailDuration);
                float maxLowTime = Mathf.Max(minLowTime, fastTrailDuration * 0.76f);
                baseTrailTime = Mathf.Lerp(minLowTime, maxLowTime, lowSpeedT);
                if (defaultTrailGradient != null)
                    trail.colorGradient = defaultTrailGradient;
            }

            trail.time = baseTrailTime + pausedTrailLifetimeExtension;

            // The SimulationEntityReader owns the rendered transform; this observer never writes it.
            if (entityReader == null) entityReader = GetComponent<SimulationEntityReader>();
            if (entityReader != null && entityReader.EntityId != SimulationDataTable.ENTITY_BALL)
                trail.emitting = false;
        }

        private void EmitSpikeShockwave(SimulationEntityRecord character, SimulationEntityRecord ball,
            SimActionState state)
        {
            if (!character.hasPosition || !character.hasSize) return;
            Vector3 charAnchor = SimulationEntityReader.AnchorWorld(character);
            Vector3 charHitPos = new Vector3(charAnchor.x,
                charAnchor.y + SimulationEntityReader.ToWorldLength(character.sizeY) * 0.85f, 0f);
            Vector3 burstPos = (ball.hasPosition && ball.hasSize)
                ? Vector3.Lerp(charHitPos, SimulationEntityReader.AnchorWorld(ball), 0.65f)
                : charHitPos;

            spikeOriginWorldPos = burstPos;
            currentBallWorldPos = (ball.hasPosition && ball.hasSize)
                ? SimulationEntityReader.AnchorWorld(ball)
                : burstPos;
            spikeDirectionSign = character.facingDirection != 0
                ? Mathf.Sign(character.facingDirection)
                : (character.entityId < NativeCharactersPerSide ? 1f : -1f);

            midAirBurstTimer = MidAirBurstDuration;
            originPuffTimer = OriginPuffDuration;
            spikeBeamTimer = SpikeBeamMaxDuration;
            spikeLightningEndWorldPos = currentBallWorldPos;

            if (spikeLaserBeam != null)
            {
                Gradient beamGrad = activeSpikeTier == 1
                    ? spikeLightTrailGradient
                    : (activeSpikeTier == 2 ? spikeMediumTrailGradient : spikeStrongTrailGradient);
                if (beamGrad != null) spikeLaserBeam.colorGradient = beamGrad;
                float beamW = activeSpikeTier == 1
                    ? BeamWidthLight
                    : (activeSpikeTier == 2 ? BeamWidthMedium : BeamWidthStrong);
                spikeLaserBeam.widthMultiplier = beamW;
                UpdateSpikeLightningPolyline(spikeOriginWorldPos, spikeLightningEndWorldPos);
                spikeLaserBeam.enabled = true;
                if (spikeLaserBeamShadow != null)
                {
                    spikeLaserBeamShadow.widthMultiplier = beamW * 1.35f;
                    spikeLaserBeamShadow.enabled = true;
                }
            }

            // Decouple Horizontal Velocity v0 (Quantity 1: dx/tick along X) from Displayed Full-Path Velocity (Quantity 2: 2D trajectory path dS/dt in km/h):
            // 1. Horizontal v0 from DLL: strictly horizontal movement speed along X (boosted 4x for JumpSpike to match video screen flight speed).
            float dllHorizontalV0 = ball.ballSpeed;
            float simHorizontalV0Speed = Mathf.Abs(dllHorizontalV0) > 0.0001f ? Mathf.Abs(dllHorizontalV0) : lastActiveBallSpeed;
            LastSpikeHorizontalV0Speed = simHorizontalV0Speed;
            LastSpikeImpactSimSpeed = simHorizontalV0Speed;

            float simContactHeight = (boundTable != null && boundTable.SpikeContactHeight > 0.001f)
                ? boundTable.SpikeContactHeight
                : ball.posY;
            float baselineFloorPx = CourtGeometry.FloorY > 0.001f ? CourtGeometry.FloorY : SimFloorReferencePx;
            float heightMeters = simContactHeight > baselineFloorPx
                ? (simContactHeight - baselineFloorPx) / CourtGeometry.PixelsPerMeter
                : Mathf.Max(0f, simContactHeight / CourtGeometry.PixelsPerMeter);

            // 2. Full 2D Trajectory Path Velocity: V_path = |v_x| * sqrt(dX^2 + dY_total^2) / dX = sqrt(v_x^2 + v_y^2)
            float contactX = ball.posX;
            float rawDeltaX = (ball.landingX > 0.001f && Mathf.Abs(ball.landingX - contactX) > 1.0f)
                ? Mathf.Abs(ball.landingX - contactX)
                : DefaultSpikeHorizontalSpanPx;
            float deltaX = Mathf.Max(1.0f, rawDeltaX);
            float apexY = ball.hasBallTrajectory && ball.trajC > simContactHeight ? ball.trajC : simContactHeight;
            float deltaYTotal = Mathf.Max(0f, apexY - simContactHeight) + Mathf.Max(0f, apexY - baselineFloorPx);
            float fullPathDistancePx = Mathf.Sqrt(deltaX * deltaX + deltaYTotal * deltaYTotal);
            float pathToHorizontalRatio = Mathf.Clamp(fullPathDistancePx / deltaX, 1.0f, 2.25f);

            LastSpikeFullPathSpeedSim = simHorizontalV0Speed * pathToHorizontalRatio;

            // Normalize out the 4.0x screen-flight v0 multiplier when converting to displayed HUD km/h so displayed speed
            // reflects the true 2D trajectory path (horizontal + vertical drop) in the realistic 140..230 km/h range.
            float unboostedHorizontalRef = simHorizontalV0Speed > JumpSpikeScreenV0BoostThreshold
                ? (simHorizontalV0Speed / JumpSpikeScreenV0BoostFactor)
                : simHorizontalV0Speed;
            float displayedSpeedKmh = unboostedHorizontalRef * pathToHorizontalRatio * FullPathSpeedToDisplayKmhScale;
            LastSpikeDisplayedPathSpeedKmh = displayedSpeedKmh;
            int displayedBallSpeed = Mathf.RoundToInt(displayedSpeedKmh);
            LastSpikeImpactKmh = displayedBallSpeed;

            Debug.Log($"[DLL Ball Velocity (Horizontal v0)] {dllHorizontalV0:F2}");
            Debug.Log($"[Unity Displayed Ball Velocity (Full 2D Path km/h)] {displayedSpeedKmh:F2}");

            if (showSimSpeedPopup)
            {
                spikeSpeedPopupOrigin = ComputeTopCenterPopupWorldPosition();
                spikeSpeedPopupTimer = SpikeSpeedPopupDuration;
                SetSpikePopupText(FormatSpikeHudPopupText(displayedSpeedKmh, heightMeters), true);
            }

            UpdateOverlayVisuals(0f);

            if (impactParticles == null) return;
            // Directional golden-yellow lightning & soft white glow motes along the spike streak (replacing radial boom explosion)
            Color lightningGold = new Color(1.00f, 0.86f, 0.16f, 0.92f);
            Color softWhiteGlow = new Color(1.00f, 0.99f, 0.92f, 0.78f);
            Vector3 streakDir = new Vector3(spikeDirectionSign * 0.82f, -0.57f, 0f).normalized;
            Vector3 perpDir = new Vector3(-streakDir.y, streakDir.x, 0f);
            impactParticles.Play(true);
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                float side = (i % 2 == 0 ? 1f : -1f) * 0.08f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = burstPos + streakDir * (t * 0.28f) + perpDir * side,
                    velocity = streakDir * Mathf.Lerp(0.95f, 2.10f, t) + perpDir * (side * 1.5f),
                    startLifetime = 0.22f,
                    startSize = (i % 2 == 0) ? 0.15f : 0.11f,
                    startColor = (i % 2 == 0) ? lightningGold : softWhiteGlow
                };
                impactParticles.Emit(emit, 1);
            }
        }

        private void EmitGroundImpact(SimulationEntityRecord ball, ulong tick)
        {
            Vector3 center = SimulationEntityReader.AnchorWorld(ball);
            Vector3 contact = new Vector3(center.x, CourtGeometry.ToWorldY(CourtGeometry.FloorY), center.z);
            LastImpactWorldPosition = contact;

            // 100% spawn rate on every ball ground impact:
            // Spawn the smooth perspective-flattened glowing white + cyan-halo elliptical shockwave rings (9.75m)
            // AND always (100% probability) rotate the screen (Camera.main) & scene objects by 15% (13.5 deg)!
            bool isJumpSpikeOrSpikeLanding = activeSpikeTier > 0 || hadSlowMoDuringCurrentFlight;
            activeGroundSonicMaxRadius = Mathf.Max(groundSonicWaveMaxRadius, jumpSpikeGroundSonicWaveMaxRadius);
            groundSonicWaveTimer = groundSonicWaveDuration;
            groundCrossRingTimer = groundSonicWaveDuration;
            groundExplosionTimer = groundSonicWaveDuration;
            GroundExplosionBurstCount++;
            lastGroundRingTierScale = activeGroundSonicMaxRadius / Mathf.Max(0.2f, groundSonicWaveMaxRadius);

            float tiltDir = spikeDirectionSign >= 0f ? -1f : 1f;
            float tiltDeg = jumpSpikeGroundTiltPercent * 90.0f * tiltDir;
            LastJumpSpikeGroundTiltAngleDeg = tiltDeg;
            CurrentJumpSpikeGroundTiltAngleDeg = tiltDeg;
            jumpSpikeGroundTiltTimer = jumpSpikeGroundTiltDuration;
            JumpSpikeGroundTiltCount++;
            ApplyWorldObjectsTilt(CurrentJumpSpikeGroundTiltAngleDeg);

            if (spikeLaserBeam != null)
            {
                if (isJumpSpikeOrSpikeLanding)
                {
                    // Latch the jagged golden-yellow lightning bolt endpoint to the floor impact center
                    // so the mid-air-to-floor lightning bolt + dark shadow remains visible during the ground ellipse ring!
                    spikeLightningEndWorldPos = contact;
                    spikeBeamTimer = Mathf.Max(spikeBeamTimer, groundSonicWaveDuration * 0.82f);
                    UpdateSpikeLightningPolyline(spikeOriginWorldPos, spikeLightningEndWorldPos);
                    spikeLaserBeam.enabled = true;
                    if (spikeLaserBeamShadow != null)
                        spikeLaserBeamShadow.enabled = true;
                }
                else
                {
                    spikeBeamTimer = 0f;
                    spikeLaserBeam.enabled = false;
                    if (spikeLaserBeamShadow != null)
                        spikeLaserBeamShadow.enabled = false;
                }
            }

            // Speed is 100% computed from C++ Sim layer ball.ballSpeed (|traj->speed|);
            // Top-center HUD popup is triggered on eventSpike (when spike hits the ball in EmitSpikeShockwave).
            float simImpactSpeed = Mathf.Abs(ball.ballSpeed) > 0.0001f ? Mathf.Abs(ball.ballSpeed) : lastActiveBallSpeed;
            LastSpikeImpactSimSpeed = simImpactSpeed;
            LastSpikeImpactKmh = Mathf.RoundToInt(simImpactSpeed * SimSpeedToKmhScale);

            UpdateOverlayVisuals(0f);

            if (impactParticles != null)
            {
                impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                impactParticles.transform.position = contact;
                impactParticles.Play(true);
                int burstCount = isJumpSpikeOrSpikeLanding ? GroundExplosionSpikeParticleCount : ImpactParticleCount;
                float burstSpeedScale = isJumpSpikeOrSpikeLanding ? 1.25f : 0.90f;
                // Focused V-chevron golden-yellow & soft white rebound streak motes + horizontal ellipse rim glow (zero chaotic boom debris)
                Color goldenReboundCol = new Color(1.00f, 0.86f, 0.22f, 0.62f);
                Color whiteFloorGlowCol = new Color(0.96f, 0.99f, 1.00f, 0.52f);
                for (int i = 0; i < burstCount; i++)
                {
                    float t = i / (float)Mathf.Max(1, burstCount - 1);
                    bool isVReboundBranch = (i % 2 == 0);
                    Vector3 vel;
                    Vector3 spawnPos;
                    if (isVReboundBranch)
                    {
                        // V-chevron rebound arms angled upward along incoming & outgoing bounce directions
                        float armSign = (i % 4 == 0) ? spikeDirectionSign : -spikeDirectionSign * 0.75f;
                        Vector3 armDir = new Vector3(armSign * 1.15f, 0.85f, 0f).normalized;
                        vel = armDir * (Mathf.Lerp(1.10f, 2.85f, t) * burstSpeedScale);
                        spawnPos = contact + armDir * (t * 0.22f);
                    }
                    else
                    {
                        // Low horizontal floor-hugging white ring mist along the elliptical shockwave plane
                        float sideSign = (t < 0.5f) ? -1f : 1f;
                        vel = new Vector3(sideSign * Mathf.Lerp(1.20f, 2.90f, Mathf.Abs(t - 0.5f) * 2f) * burstSpeedScale, 0.14f, 0f);
                        spawnPos = contact + new Vector3(sideSign * 0.18f, 0.03f, 0f);
                    }
                    var emit = new ParticleSystem.EmitParams
                    {
                        position = spawnPos,
                        velocity = vel,
                        startLifetime = Mathf.Lerp(0.24f, 0.38f, 1f - Mathf.Abs(t - 0.5f) * 2f),
                        startSize = isVReboundBranch ? 0.22f : 0.34f,
                        startColor = isVReboundBranch ? goldenReboundCol : whiteFloorGlowCol
                    };
                    impactParticles.Emit(emit, 1);
                }
            }

            bool soundPlayed = PlayOneShot(groundClip, groundImpactVolume);
            GroundImpactEventCount++;
            if (soundPlayed) GroundImpactSoundCount++;
            LastGroundImpactTick = tick;
            // Debug.Log($"[P04_BALL_FEEDBACK] ground impact tick={tick} entity={ball.entityId} nativePos=({ball.posX:F2},{ball.posY:F2}) world=({contact.x:F3},{contact.y:F3})");
        }

        private void CreateTrail()
        {
            trail = GetComponent<TrailRenderer>();
            if (trail == null) trail = gameObject.AddComponent<TrailRenderer>();
            feedbackMaterial = CreateFeedbackMaterial();
            if (feedbackMaterial != null) trail.sharedMaterial = feedbackMaterial;
            defaultTrailGradient = CreateTrailGradient();
            highSpeedServeTrailGradient = CreateHighSpeedServeTrailGradient();
            spikeLightTrailGradient = CreateSpikeLightTrailGradient();
            spikeMediumTrailGradient = CreateSpikeMediumTrailGradient();
            spikeStrongTrailGradient = CreateSpikeStrongTrailGradient();
            trail.emitting = false;
            trail.time = slowTrailDuration;
            trail.minVertexDistance = 0.025f;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            // Rectangular-corner ribbon profile (0 cap vertices, 0 corner vertices, constant 1.0f width from head to tail instead of meteor cone)
            trail.numCornerVertices = 0;
            trail.numCapVertices = 0;
            trail.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            trail.widthMultiplier = CourtGeometry.ToWorldLength(slowTrailWidth);
            trail.colorGradient = defaultTrailGradient;
            trail.autodestruct = false;
            SpriteRenderer sprite = GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                trail.sortingLayerID = sprite.sortingLayerID;
                trail.sortingOrder = sprite.sortingOrder - 1;
            }
            trail.Clear();
        }

        private void CreateImpactParticles()
        {
            Transform existing = transform.Find("P04_GroundImpactParticles");
            impactObject = existing != null ? existing.gameObject : new GameObject("P04_GroundImpactParticles");
            impactObject.transform.SetParent(transform, false);
            impactParticles = impactObject.GetComponent<ParticleSystem>();
            if (impactParticles == null) impactParticles = impactObject.AddComponent<ParticleSystem>();

            impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = impactParticles.main;
            main.duration = 0.45f;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.40f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.19f);
            main.startColor = impactColor;
            main.gravityModifier = 0.12f;
            main.scalingMode = ParticleSystemScalingMode.Local;

            var emission = impactParticles.emission;
            emission.enabled = false;
            var shape = impactParticles.shape;
            shape.enabled = false;
            var colorOverLifetime = impactParticles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            var renderer = impactParticles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingLayerID = trail != null ? trail.sortingLayerID : 0;
            renderer.sortingOrder = trail != null ? trail.sortingOrder + 2 : 2;
            if (feedbackMaterial != null) renderer.sharedMaterial = feedbackMaterial;
            impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void CreateAudioSource()
        {
            Transform existing = transform.Find("P04_BallFeedbackAudio");
            GameObject audioObject = existing != null ? existing.gameObject : new GameObject("P04_BallFeedbackAudio");
            audioObject.transform.SetParent(transform, false);
            audioSource = audioObject.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = audioObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 1f;
            audioSource.pitch = 1f;
        }

        private void CreateArenaAmbience()
        {
            Transform existing = transform.Find("P04_ArenaAmbienceAudio");
            GameObject bgmObject = existing != null ? existing.gameObject : new GameObject("P04_ArenaAmbienceAudio");
            bgmObject.transform.SetParent(transform, false);
            bgmAudioSource = bgmObject.GetComponent<AudioSource>();
            if (bgmAudioSource == null) bgmAudioSource = bgmObject.AddComponent<AudioSource>();
            bgmAudioSource.playOnAwake = false;
            bgmAudioSource.loop = true;
            bgmAudioSource.spatialBlend = 0f;
            bgmAudioSource.volume = arenaBgmVolume;

            if (arenaBgmClip == null)
            {
                // High-Tempo 152 BPM Arcade Sports Electronic/Rock Driving BGM ("nhac nen dang nhip do cao"):
                // 32 beats (8 bars) at 152 BPM (beatPeriod = 0.394737s, sixteenthPeriod = 0.098684s -> duration = 12.6316s)
                const int sampleRate = 22050;
                const float bpm = 152.0f;
                const float beatPeriod = 60.0f / bpm;
                const float eighthPeriod = beatPeriod * 0.5f;
                const float sixteenthPeriod = beatPeriod * 0.25f;
                const float duration = beatPeriod * 32.0f;
                int count = Mathf.CeilToInt(duration * sampleRate);
                var samples = new float[count];
                // Driving minor-key anime sports progression: E2 -> C2 -> D2 -> B1 (2 bars each)
                float[] bassRoots = { 82.41f, 65.41f, 73.42f, 61.74f };
                // High-energy 32-step eighth-note synth rock lead riff in E minor
                float[] leadNotes =
                {
                    329.63f, 392.00f, 493.88f, 659.25f, 587.33f, 493.88f, 392.00f, 493.88f,
                    261.63f, 329.63f, 392.00f, 523.25f, 493.88f, 392.00f, 329.63f, 392.00f,
                    293.66f, 369.99f, 440.00f, 587.33f, 523.25f, 440.00f, 369.99f, 440.00f,
                    246.94f, 311.13f, 369.99f, 493.88f, 587.33f, 493.88f, 392.00f, 369.99f
                };
                var hihatRng = new System.Random(1520);
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)sampleRate;
                    int bar = Mathf.Clamp((int)(t / (beatPeriod * 8.0f)), 0, 3);
                    int beatIndex = (int)(t / beatPeriod);
                    int eighthIndex = (int)(t / eighthPeriod) % 32;
                    int sixteenthIndex = (int)(t / sixteenthPeriod);
                    float beatLocal = Mathf.Repeat(t, beatPeriod);
                    float eighthLocal = Mathf.Repeat(t, eighthPeriod);
                    float sixteenthLocal = Mathf.Repeat(t, sixteenthPeriod);

                    // 1. High-impact 152 BPM four-on-the-floor kick drum
                    float kickEnv = Mathf.Exp(-beatLocal * 22f);
                    float kickFreq = Mathf.Lerp(150f, 48f, Mathf.Clamp01(beatLocal * 18f));
                    float kick = Mathf.Sin(2f * Mathf.PI * kickFreq * beatLocal) * kickEnv * 0.54f;

                    // 2. Snappy backbeat snare & driving 16th-note hi-hat groove
                    float noise = (float)(hihatRng.NextDouble() * 2.0 - 1.0);
                    float snareTone = Mathf.Sin(2f * Mathf.PI * 195f * beatLocal) * Mathf.Exp(-beatLocal * 30f) * 0.16f;
                    float snare = (beatIndex % 2 == 1) ? (noise * Mathf.Exp(-beatLocal * 25f) * 0.25f + snareTone) : 0f;
                    float hatAccent = (sixteenthIndex % 2 == 1) ? 0.14f : 0.07f;
                    float hat = noise * Mathf.Exp(-sixteenthLocal * 56f) * hatAccent;

                    // 3. Fast 16th-note rolling octave bassline
                    float bassMul = (sixteenthIndex % 4 == 2) ? 2.0f : ((sixteenthIndex % 4 == 3) ? 1.5f : 1.0f);
                    float bassHz = bassRoots[bar] * bassMul;
                    float bassEnv = Mathf.Exp(-sixteenthLocal * 10.5f);
                    float bass = (Mathf.Sin(2f * Mathf.PI * bassHz * t) +
                                  0.52f * Mathf.Sin(2f * Mathf.PI * (bassHz * 2f) * t)) * bassEnv * 0.36f;

                    // 4. High-tempo saw/pulse synth lead melody + power chord fifth
                    float leadHz = leadNotes[eighthIndex];
                    float fifthHz = leadHz * 1.4983f;
                    float leadEnv = Mathf.Exp(-eighthLocal * 6.8f);
                    float lead = (Mathf.Sin(2f * Mathf.PI * leadHz * t) +
                                  0.42f * Mathf.Sin(2f * Mathf.PI * (leadHz * 2f) * t) +
                                  0.26f * Mathf.Sin(2f * Mathf.PI * fifthHz * t)) * leadEnv * 0.28f;

                    samples[i] = Mathf.Clamp((kick + snare + hat + bass + lead) * 0.80f, -1f, 1f);
                }
                arenaBgmClip = AudioClip.Create("P04_MedievalArenaAmbience", count, 1, sampleRate, false);
                arenaBgmClip.SetData(samples, 0);
            }
            bgmAudioSource.clip = arenaBgmClip;
            if (Application.isPlaying && arenaBgmVolume > 0f)
                bgmAudioSource.Play();
        }

        private void CreatePlaceholderClips()
        {
            // Synthesized SFX matched to https://www.youtube.com/watch?v=Qo0kCZF0NWA:
            // spike clang, bump deflect, sneaker shoe friction squeak + Boom Jump, and Ground Impact Boom.
            spikeClip = CreateProceduralClip("P04 Iron Spike Clang", 0.14f, 925f, 410f, 0.16f);
            bumpClip = CreateProceduralClip("P04 Armored Bump Deflect", 0.08f, 380f, 220f, 0.09f);
            groundClip = CreateVideoStyleGroundSlamClip("P04 Heavy Iron Ground Impact");
            jumpSqueakBoomClip = CreateVideoStyleJumpSqueakAndBoomClip("P04 Shoe Squeak And Boom Jump");
        }

        private AudioClip CreateVideoStyleJumpSqueakAndBoomClip(string clipName)
        {
            // Synthesized from FFT analysis of Boom Jump + Sneaker Shoe Friction Squeak in https://www.youtube.com/watch?v=Qo0kCZF0NWA:
            // 1. Rubber Sneaker Sole Friction Squeak on Hardwood Court (0..82ms):
            //    High-Q frequency-modulated chirp sweeping 2300 Hz -> 3080 Hz -> 2380 Hz (FFT ShoeSqueakChirp dom=2300..3078Hz)
            // 2. Boom Jump Sub-Bass Air Explosion (18..360ms):
            //    Deep sub-boom 125 Hz -> 50 Hz (FFT SubBoom dom=72Hz->50Hz, E=390.36) + LowPunch 125 Hz + MidBody 458 Hz
            const int sampleRate = 44100;
            const float duration = 0.36f;
            int frameCount = Mathf.CeilToInt(duration * sampleRate);
            var samples = new float[frameCount];
            var rng = new System.Random(4110);

            float squeakPhase = 0f;
            float boomSubPhase = 0f;
            float boomPunchPhase = 0f;
            float lpNoise = 0f;

            for (int i = 0; i < frameCount; i++)
            {
                float t = i / (float)sampleRate;

                // Part A: Gym Shoe Rubber-on-Hardwood Friction Squeak (double-chirp slip-stick profile in 0..85ms)
                float squeakSignal = 0f;
                if (t < 0.088f)
                {
                    float st = t / 0.088f;
                    // Slip-stick rubber vibration curve peaking around 3080 Hz
                    float chirpCurve = Mathf.Sin(st * Mathf.PI);
                    float microStick = Mathf.Sin(st * Mathf.PI * 6.0f) * 0.08f;
                    float squeakFreq = Mathf.Lerp(2300f, 3080f, Mathf.Clamp01(chirpCurve + microStick));
                    squeakPhase += 2f * Mathf.PI * squeakFreq / sampleRate;
                    float squeakEnv = Mathf.Sin(st * Mathf.PI) * Mathf.Exp(-st * 1.4f);
                    squeakSignal = (Mathf.Sin(squeakPhase) * 0.78f +
                                    Mathf.Sin(squeakPhase * 2.0f) * 0.24f) * squeakEnv * 0.46f;
                }

                // Part B: Explosive Boom Jump Sub-Bass & Air Shockwave (peaking right as feet leave the floor)
                float bt = Mathf.Max(0f, t - 0.014f);
                float subFreq = Mathf.Lerp(48f, 128f, Mathf.Exp(-bt * 16.0f));
                boomSubPhase += 2f * Mathf.PI * subFreq / sampleRate;
                float subEnv = (t >= 0.014f) ? Mathf.Exp(-bt * 8.8f) : (t / 0.014f);
                float subBoom = Mathf.Sin(boomSubPhase) * subEnv * 0.92f;

                float punchFreq = Mathf.Lerp(72f, 145f, Mathf.Exp(-bt * 24.0f));
                boomPunchPhase += 2f * Mathf.PI * punchFreq / sampleRate;
                float punchEnv = Mathf.Exp(-bt * 15.0f);
                float midBody = Mathf.Sin(2f * Mathf.PI * 458f * bt) * Mathf.Exp(-bt * 36.0f) * 0.34f;
                float punchBoom = (Mathf.Sin(boomPunchPhase) * 0.65f + midBody) * punchEnv;

                float rawNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpNoise = Mathf.Lerp(lpNoise, rawNoise, 0.20f);
                float airBurst = lpNoise * Mathf.Exp(-bt * 22.0f) * 0.42f;

                float tailFade = Mathf.Clamp01((duration - t) / 0.06f);
                float mixed = squeakSignal + (float)Math.Tanh((subBoom + punchBoom + airBurst) * 1.55f) * 0.85f;
                samples[i] = Mathf.Clamp(mixed * tailFade, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(clipName, frameCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private AudioClip CreateVideoStyleGroundSlamClip(string clipName)
        {
            // Synthesized from FFT & 25ms RMS envelope analysis of https://www.youtube.com/watch?v=2by5rx-O7_E:
            // - SubBass (40-110Hz, dom=108Hz->40Hz, E=338.49): deep chest-thumping boom drop
            // - LowPunch (110-280Hz, dom=110Hz, E=142.77): tight hardwood gym-floor thud
            // - MidSlap (280-900Hz, dom=525Hz & 782Hz, E=61.75): sharp leather-on-court slap
            // - HighCrack (900-3200Hz, dom=925Hz & 1112Hz & 3155Hz, E=27.06): sonic impact whip crack
            // - Stadium Hall Reverb Tail (60..420ms multi-tap early reflections @ 65ms, 130ms, 205ms, 280ms)
            const int sampleRate = 44100;
            const float duration = 0.42f;
            int frameCount = Mathf.CeilToInt(duration * sampleRate);
            var drySamples = new float[frameCount];
            var finalSamples = new float[frameCount];
            var random = new System.Random(2086);

            float subPhase = 0f;
            float punchPhase = 0f;
            float lpNoise = 0f;
            float bpNoise = 0f;

            for (int i = 0; i < frameCount; i++)
            {
                float t = i / (float)sampleRate;

                // 1. Sub-Bass Thunder Drop (112 Hz -> 40 Hz exponential sweep over 220ms)
                float subFreq = Mathf.Lerp(40f, 112f, Mathf.Exp(-t * 14.5f));
                subPhase += 2f * Mathf.PI * subFreq / sampleRate;
                float subEnv = Mathf.Exp(-t * 7.8f);
                float subWave = Mathf.Sin(subPhase) * subEnv * 0.95f;

                // 2. Low-Punch Court Resonance (185 Hz -> 108 Hz over 95ms)
                float punchFreq = Mathf.Lerp(108f, 185f, Mathf.Exp(-t * 28f));
                punchPhase += 2f * Mathf.PI * punchFreq / sampleRate;
                float punchEnv = Mathf.Exp(-t * 16.5f);
                float punchWave = (Mathf.Sin(punchPhase) + 0.35f * Mathf.Sin(punchPhase * 1.5f)) * punchEnv * 0.68f;

                // 3. Hardwood Court Slap (dominant FFT peaks at 525 Hz & 782 Hz)
                float slapEnv = Mathf.Exp(-t * 42f);
                float slapWave = (Mathf.Sin(2f * Mathf.PI * 525f * t) * 0.62f +
                                  Mathf.Sin(2f * Mathf.PI * 782f * t) * 0.48f) * slapEnv * 0.52f;

                // 4. High-Velocity Sonic Crack (925 Hz, 1112 Hz, 3155 Hz + filtered transient burst)
                float crackEnv = Mathf.Exp(-t * 85f);
                float rawNoise = (float)(random.NextDouble() * 2.0 - 1.0);
                lpNoise = Mathf.Lerp(lpNoise, rawNoise, 0.24f);
                bpNoise = Mathf.Lerp(bpNoise, rawNoise - lpNoise, 0.45f);
                float crackTone = (Mathf.Sin(2f * Mathf.PI * 925f * t) * 0.50f +
                                   Mathf.Sin(2f * Mathf.PI * 1112f * t) * 0.35f +
                                   Mathf.Sin(2f * Mathf.PI * 3155f * t) * 0.20f) * crackEnv;
                float impactNoise = (lpNoise * 0.65f * Mathf.Exp(-t * 26f)) + (bpNoise * 0.85f * crackEnv);

                // Soft-clipping saturation for punchy arcade compression matching 2by5rx-O7_E
                float combined = subWave + punchWave + slapWave + crackTone * 0.42f + impactNoise * 0.58f;
                drySamples[i] = (float)Math.Tanh(combined * 1.65f);
            }

            // 5. Multi-Tap Stadium Hall Reverb Tail (matching the 25ms RMS echo peaks at +62ms, +128ms, +195ms, +265ms)
            int[] tapDelays =
            {
                (int)(0.028f * sampleRate),
                (int)(0.062f * sampleRate),
                (int)(0.128f * sampleRate),
                (int)(0.195f * sampleRate),
                (int)(0.265f * sampleRate)
            };
            float[] tapGains = { 0.42f, 0.36f, 0.28f, 0.20f, 0.14f };

            float reverbLp = 0f;
            for (int i = 0; i < frameCount; i++)
            {
                float t = i / (float)sampleRate;
                float echoSum = 0f;
                for (int tap = 0; tap < tapDelays.Length; tap++)
                {
                    int srcIdx = i - tapDelays[tap];
                    if (srcIdx >= 0)
                    {
                        echoSum += drySamples[srcIdx] * tapGains[tap];
                    }
                }
                // Warm low-pass filter on the stadium hall echo tail
                reverbLp = Mathf.Lerp(reverbLp, echoSum, 0.16f);
                float tailFade = Mathf.Clamp01((duration - t) / 0.08f);
                finalSamples[i] = Mathf.Clamp((drySamples[i] * 0.88f + reverbLp * 0.64f) * tailFade, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(clipName, frameCount, 1, sampleRate, false);
            clip.SetData(finalSamples, 0);
            return clip;
        }

        private AudioClip CreateProceduralClip(string clipName, float duration, float startHz, float endHz, float noiseMix)
        {
            const int sampleRate = 22050;
            int frameCount = Mathf.CeilToInt(duration * sampleRate);
            var samples = new float[frameCount];
            var random = new System.Random(clipName.GetHashCode());
            for (int i = 0; i < frameCount; i++)
            {
                float t = i / (float)sampleRate;
                float progress = t / duration;
                float frequency = Mathf.Lerp(startHz, endHz, progress);
                float envelope = (1f - progress) * (1f - progress);
                // Fundamental + metallic inharmonic overtones for forged-iron character
                float fundamental = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float metallicOvertone = 0.35f * Mathf.Sin(2f * Mathf.PI * (frequency * 2.41f) * t) +
                                         0.18f * Mathf.Sin(2f * Mathf.PI * (frequency * 3.83f) * t);
                float tone = (fundamental + metallicOvertone) * 0.72f;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                samples[i] = Mathf.Clamp((tone * (1f - noiseMix) + noise * noiseMix) * envelope, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(clipName, frameCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private Material CreateFeedbackMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
            {
                // Debug.LogWarning("[P04_BALL_FEEDBACK] No compatible sprite shader found; trail/effect material uses Unity's default renderer material.", this);
                return null;
            }
            var mat = new Material(shader)
            {
                name = "P04_RuntimeBallFeedbackMaterial",
                mainTexture = Texture2D.whiteTexture
            };
            return mat;
        }

        private static Gradient CreateTrailGradient()
        {
            // Normal Rally (Serve / Receive / Set): Clean, semi-transparent Silver-Cyan/White air ribbon (KIQbyAcdmSM)
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.88f, 0.96f, 1.00f), 0f),
                    new GradientColorKey(new Color(0.72f, 0.90f, 1.00f), 0.50f),
                    new GradientColorKey(new Color(0.60f, 0.84f, 1.00f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.45f, 0f),
                    new GradientAlphaKey(0.20f, 0.50f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Gradient CreateHighSpeedServeTrailGradient()
        {
            // High-speed Golden-Yellow Lightning Connecting Ribbon (#FFF68F -> #FFD82E -> #C87A08)
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.96f, 0.56f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.85f, 0.18f), 0.58f),
                    new GradientColorKey(new Color(0.78f, 0.48f, 0.03f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.96f, 0f),
                    new GradientAlphaKey(0.86f, 0.70f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Gradient CreateSpikeLightTrailGradient()
        {
            // SpikeLight: Bright White-Gold -> Golden-Yellow connecting ribbon (#FFFAA8 -> #FFD82E -> #B86E05)
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.98f, 0.66f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.85f, 0.18f), 0.55f),
                    new GradientColorKey(new Color(0.72f, 0.43f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.84f, 0.70f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Gradient CreateSpikeMediumTrailGradient()
        {
            // SpikeMedium: Electric Golden-Yellow -> Warm Amber connecting ribbon (#FFF066 -> #FFCC14 -> #9E5804)
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.94f, 0.40f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.80f, 0.08f), 0.52f),
                    new GradientColorKey(new Color(0.62f, 0.35f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.97f, 0f),
                    new GradientAlphaKey(0.86f, 0.70f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Gradient CreateSpikeStrongTrailGradient()
        {
            // SpikeStrong / Spike (matching reference image Golden-Yellow Lightning Streak #FFD82E -> #F5B800 -> #7A4204)
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.94f, 0.36f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.82f, 0.08f), 0.58f),
                    new GradientColorKey(new Color(0.56f, 0.30f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.98f, 0f),
                    new GradientAlphaKey(0.90f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Gradient CreateSpikeLightningShadowGradient()
        {
            // Dark Burnt-Amber / Smoky Brown-Orange shadow streak offset behind the golden lightning bolt
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.42f, 0.21f, 0.04f), 0f),
                    new GradientColorKey(new Color(0.32f, 0.15f, 0.03f), 0.60f),
                    new GradientColorKey(new Color(0.20f, 0.09f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.58f, 0f),
                    new GradientAlphaKey(0.48f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static void EnsureKmhStringCache()
        {
            if (cachedKmhStrings != null) return;
            int count = MaxSpikePopupKmh - MinSpikePopupKmh + 1;
            cachedKmhStrings = new string[count];
            for (int i = 0; i < count; i++)
            {
                cachedKmhStrings[i] = (MinSpikePopupKmh + i) + " km/h";
            }
        }

        private static string GetCachedKmhString(int kmh)
        {
            EnsureKmhStringCache();
            int idx = Mathf.Clamp(kmh - MinSpikePopupKmh, 0, cachedKmhStrings.Length - 1);
            return cachedKmhStrings[idx];
        }

        private void CreateSpikeAndImpactOverlays()
        {
            EnsureKmhStringCache();
            Transform parentRoot = impactObject != null ? impactObject.transform : transform;
            int sortingLayerId = trail != null ? trail.sortingLayerID : 0;
            int baseOrder = trail != null ? trail.sortingOrder : 1;

            sonicCrescentRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_SonicCrescentArc",
                GetOrCreateSonicCrescentSprite(), sortingLayerId, baseOrder + 3, out sonicCrescentTransform);
            radialSpeedLinesRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_RadialSpeedLines",
                GetOrCreateRadialSpeedLinesSprite(), sortingLayerId, baseOrder + 2, out radialSpeedLinesTransform);
            xSlashSparkRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_XSlashSpark",
                GetOrCreateXSlashSparkSprite(), sortingLayerId, baseOrder + 4, out xSlashSparkTransform);
            originPuffRingRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_OriginPuffRing",
                GetOrCreateOriginPuffRingSprite(), sortingLayerId, baseOrder + 1, out originPuffRingTransform);
            groundCrossRingRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_GroundGoldenCrossRing",
                GetOrCreateGoldenCrossRingSprite(), sortingLayerId, baseOrder + 3, out groundCrossRingTransform);
            groundExplosionBurstRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_GroundExplosionBurst",
                GetOrCreateGroundExplosionBurstSprite(), sortingLayerId, baseOrder + 6, out groundExplosionBurstTransform);
            for (int i = 0; i < GroundSonicWaveCount; i++)
            {
                groundSonicWaveRenderers[i] = GetOrCreateChildOverlayRenderer(parentRoot,
                    "P04_GroundSonicWave_" + i, GetOrCreateGroundSonicWaveSprite(),
                    sortingLayerId, baseOrder + 3 + i, out groundSonicWaveTransforms[i]);
            }
            boomJumpRingRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_BoomJumpRing",
                GetOrCreateGroundSonicWaveSprite(), sortingLayerId, baseOrder + 3, out boomJumpRingTransform);
            pointScoreRingRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_PointScoreBurstRing",
                GetOrCreateGoldenCrossRingSprite(), sortingLayerId, baseOrder + 4, out pointScoreRingTransform);

            Transform existingPopup = parentRoot.Find("P04_SpikeSpeedPopup");
            GameObject popupObj = existingPopup != null ? existingPopup.gameObject : new GameObject("P04_SpikeSpeedPopup");
            spikeSpeedPopupTransform = popupObj.transform;
            spikeSpeedPopupTransform.SetParent(parentRoot, false);
            spikeSpeedPopupText = popupObj.GetComponent<TextMesh>();
            if (spikeSpeedPopupText == null) spikeSpeedPopupText = popupObj.AddComponent<TextMesh>();
            spikeSpeedPopupText.text = FormatSpikeHudPopupText(119.12f, 3.69f);
            spikeSpeedPopupText.anchor = TextAnchor.MiddleCenter;
            spikeSpeedPopupText.alignment = TextAlignment.Center;
            spikeSpeedPopupText.fontSize = SpikeSpeedPopupFontSize;
            spikeSpeedPopupText.characterSize = SpikeSpeedPopupCharacterSize;
            spikeSpeedPopupText.lineSpacing = 0.88f;
            spikeSpeedPopupText.fontStyle = FontStyle.Bold;
            spikeSpeedPopupText.richText = true;
            spikeSpeedPopupText.color = Color.white;
            spikeSpeedPopupRenderer = popupObj.GetComponent<MeshRenderer>();
            if (spikeSpeedPopupRenderer != null)
            {
                spikeSpeedPopupRenderer.sortingLayerID = sortingLayerId;
                spikeSpeedPopupRenderer.sortingOrder = baseOrder + 8;
                spikeSpeedPopupRenderer.enabled = false;
            }

            Vector2[] outlineDirections =
            {
                new Vector2(-1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(0f, 1f),
                new Vector2(-0.7071f, -0.7071f),
                new Vector2(0.7071f, -0.7071f),
                new Vector2(-0.7071f, 0.7071f),
                new Vector2(0.7071f, 0.7071f)
            };
            for (int i = 0; i < SpikeSpeedPopupOutlineCount; i++)
            {
                string outlineName = "P04_SpikeSpeedPopup_Outline_" + i;
                Transform existingOutline = spikeSpeedPopupTransform.Find(outlineName);
                GameObject outlineObj = existingOutline != null ? existingOutline.gameObject : new GameObject(outlineName);
                Transform outlineTransform = outlineObj.transform;
                outlineTransform.SetParent(spikeSpeedPopupTransform, false);
                outlineTransform.localPosition = new Vector3(
                    outlineDirections[i].x * SpikeSpeedPopupOutlineOffset,
                    outlineDirections[i].y * SpikeSpeedPopupOutlineOffset,
                    0.001f);
                outlineTransform.localRotation = Quaternion.identity;
                outlineTransform.localScale = Vector3.one;

                TextMesh outlineText = outlineObj.GetComponent<TextMesh>();
                if (outlineText == null) outlineText = outlineObj.AddComponent<TextMesh>();
                outlineText.text = spikeSpeedPopupText.text;
                outlineText.anchor = TextAnchor.MiddleCenter;
                outlineText.alignment = TextAlignment.Center;
                outlineText.fontSize = SpikeSpeedPopupFontSize;
                outlineText.characterSize = SpikeSpeedPopupCharacterSize;
                outlineText.lineSpacing = 0.88f;
                outlineText.fontStyle = FontStyle.Bold;
                outlineText.richText = true;
                outlineText.color = Color.black;
                spikeSpeedPopupOutlineTexts[i] = outlineText;

                MeshRenderer outlineRenderer = outlineObj.GetComponent<MeshRenderer>();
                if (outlineRenderer != null)
                {
                    outlineRenderer.sortingLayerID = sortingLayerId;
                    outlineRenderer.sortingOrder = baseOrder + 7;
                    outlineRenderer.enabled = false;
                }
                spikeSpeedPopupOutlineRenderers[i] = outlineRenderer;
            }

            spikeLightningShadowGradient = CreateSpikeLightningShadowGradient();

            Transform existingBeamShadow = parentRoot.Find("P04_SpikeLaserBeam_Shadow");
            GameObject beamShadowObj = existingBeamShadow != null ? existingBeamShadow.gameObject : new GameObject("P04_SpikeLaserBeam_Shadow");
            beamShadowObj.transform.SetParent(parentRoot, false);
            spikeLaserBeamShadow = beamShadowObj.GetComponent<LineRenderer>();
            if (spikeLaserBeamShadow == null) spikeLaserBeamShadow = beamShadowObj.AddComponent<LineRenderer>();
            if (feedbackMaterial != null) spikeLaserBeamShadow.sharedMaterial = feedbackMaterial;
            spikeLaserBeamShadow.useWorldSpace = true;
            spikeLaserBeamShadow.positionCount = SpikeLightningVertexCount;
            spikeLaserBeamShadow.numCapVertices = 0;
            spikeLaserBeamShadow.numCornerVertices = 0;
            spikeLaserBeamShadow.alignment = LineAlignment.View;
            spikeLaserBeamShadow.textureMode = LineTextureMode.Stretch;
            spikeLaserBeamShadow.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.85f),
                new Keyframe(0.55f, 1.00f),
                new Keyframe(1f, 0.55f));
            spikeLaserBeamShadow.widthMultiplier = BeamWidthStrong * 1.35f;
            spikeLaserBeamShadow.colorGradient = spikeLightningShadowGradient;
            spikeLaserBeamShadow.sortingLayerID = sortingLayerId;
            spikeLaserBeamShadow.sortingOrder = baseOrder + 4;
            spikeLaserBeamShadow.enabled = false;

            Transform existingBeam = parentRoot.Find("P04_SpikeLaserBeam");
            GameObject beamObj = existingBeam != null ? existingBeam.gameObject : new GameObject("P04_SpikeLaserBeam");
            beamObj.transform.SetParent(parentRoot, false);
            spikeLaserBeam = beamObj.GetComponent<LineRenderer>();
            if (spikeLaserBeam == null) spikeLaserBeam = beamObj.AddComponent<LineRenderer>();
            if (feedbackMaterial != null) spikeLaserBeam.sharedMaterial = feedbackMaterial;
            spikeLaserBeam.useWorldSpace = true;
            spikeLaserBeam.positionCount = SpikeLightningVertexCount;
            spikeLaserBeam.numCapVertices = 0;
            spikeLaserBeam.numCornerVertices = 0;
            spikeLaserBeam.alignment = LineAlignment.View;
            spikeLaserBeam.textureMode = LineTextureMode.Stretch;
            spikeLaserBeam.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.72f),
                new Keyframe(0.50f, 1.00f),
                new Keyframe(1f, 0.48f));
            spikeLaserBeam.widthMultiplier = BeamWidthStrong;
            spikeLaserBeam.colorGradient = spikeStrongTrailGradient;
            spikeLaserBeam.sortingLayerID = sortingLayerId;
            spikeLaserBeam.sortingOrder = baseOrder + 5;
            spikeLaserBeam.enabled = false;
        }

        private void UpdateSpikeLightningPolyline(Vector3 startWorld, Vector3 endWorld)
        {
            if (spikeLaserBeam == null) return;
            if (spikeLaserBeam.positionCount != SpikeLightningVertexCount)
                spikeLaserBeam.positionCount = SpikeLightningVertexCount;
            if (spikeLaserBeamShadow != null && spikeLaserBeamShadow.positionCount != SpikeLightningVertexCount)
                spikeLaserBeamShadow.positionCount = SpikeLightningVertexCount;

            Vector3 delta = endWorld - startWorld;
            float length = delta.magnitude;
            Vector3 dir = length > 0.001f ? (delta / length) : new Vector3(spikeDirectionSign, -0.7f, 0f).normalized;
            Vector3 perp = new Vector3(-dir.y, dir.x, 0f);
            float kinkAmp = Mathf.Clamp(length * 0.065f, 0.05f, 0.24f);
            // Jagged zig-zag offsets matching the reference screenshot's golden lightning bolt
            float[] zigOffsets = { 0f, 0.55f, -0.70f, 0.85f, -0.60f, 0.35f, 0f };
            Vector3 shadowOffset = new Vector3(-spikeDirectionSign * 0.11f, -0.16f, 0f);

            for (int i = 0; i < SpikeLightningVertexCount; i++)
            {
                float t = i / (float)(SpikeLightningVertexCount - 1);
                Vector3 pt = Vector3.Lerp(startWorld, endWorld, t) + perp * (zigOffsets[i] * kinkAmp * spikeDirectionSign);
                spikeLaserBeam.SetPosition(i, pt);
                if (spikeLaserBeamShadow != null)
                {
                    spikeLaserBeamShadow.SetPosition(i, pt + shadowOffset);
                }
            }
        }

        private static Vector3 ComputeTopCenterPopupWorldPosition()
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 camPos = mainCam.transform.position;
                float ortho = mainCam.orthographic ? mainCam.orthographicSize : 4.95f;
                Vector3 upDir = mainCam.transform.up;
                return new Vector3(camPos.x, camPos.y, 0f) + upDir * (ortho * SpikeSpeedPopupTopCenterViewportOffsetRatio);
            }
            return new Vector3(0f, 3.10f, 0f);
        }

        private static string FormatSpikeHudPopupText(float speedKmh, float heightMeters)
        {
            float clampedSpeed = Mathf.Max(0f, speedKmh);
            int speedWhole = Mathf.FloorToInt(clampedSpeed);
            int speedFrac = Mathf.Clamp(Mathf.RoundToInt((clampedSpeed - speedWhole) * 100f), 0, 99);
            if (speedFrac == 100)
            {
                speedWhole++;
                speedFrac = 0;
            }

            float clampedHeight = Mathf.Max(0f, heightMeters);
            int heightWhole = Mathf.FloorToInt(clampedHeight);
            int heightFrac = Mathf.Clamp(Mathf.RoundToInt((clampedHeight - heightWhole) * 100f), 0, 99);
            if (heightFrac == 100)
            {
                heightWhole++;
                heightFrac = 0;
            }

            return $"{speedWhole}<size={SpikeSpeedPopupUnitFontSize}>.{speedFrac:D2} km/h</size>   {heightWhole}<size={SpikeSpeedPopupUnitFontSize}>.{heightFrac:D2} m</size>";
        }

        private SpriteRenderer GetOrCreateChildOverlayRenderer(Transform parentRoot, string childName,
            Sprite sprite, int sortingLayerId, int sortingOrder, out Transform childTransform)
        {
            Transform existing = parentRoot.Find(childName);
            GameObject childObj = existing != null ? existing.gameObject : new GameObject(childName);
            childTransform = childObj.transform;
            childTransform.SetParent(parentRoot, false);
            SpriteRenderer sr = childObj.GetComponent<SpriteRenderer>();
            if (sr == null) sr = childObj.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (feedbackMaterial != null) sr.sharedMaterial = feedbackMaterial;
            sr.sortingLayerID = sortingLayerId;
            sr.sortingOrder = sortingOrder;
            sr.enabled = false;
            return sr;
        }

        private void CacheTiltableSceneObjects()
        {
            // Unified Camera-View Tilt:
            // Do NOT rotate individual world objects around separate local pivots (which detaches characters from the floor,
            // leaves useWorldSpace=true CourtLineRenderer and Boom Rings at 0 deg, and cancels out camera rotation!).
            // Instead, reset any stale localRotation on world objects to identity and cache all Camera / VCam transforms
            // so tilting the camera view tilts 100% of every element on screen (background, court lines, net, characters,
            // ball, shadow, trail, lightning bolt, boom rings, and popup HUD) coherently around the screen center!
            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            SimulationEntityReader[] readers = UnityEngine.Object.FindObjectsByType<SimulationEntityReader>(FindObjectsInactive.Include);
            SpriteRenderer[] renderers = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include);
            var list = new System.Collections.Generic.List<Transform>(8);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].gameObject.scene.IsValid() && !list.Contains(cameras[i].transform))
                {
                    list.Add(cameras[i].transform);
                }
            }
            for (int i = 0; i < readers.Length; i++)
            {
                if (readers[i] != null && readers[i].gameObject.scene.IsValid())
                {
                    readers[i].transform.localRotation = Quaternion.identity;
                }
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].gameObject.scene.IsValid())
                {
                    Transform root = renderers[i].transform.root;
                    if (root != null && root.GetComponent<Camera>() == null)
                    {
                        root.localRotation = Quaternion.identity;
                    }
                }
            }
            cachedTiltableTransforms = list.ToArray();
        }

        private void OnBeginCameraRenderingTilt(ScriptableRenderContext context, Camera cam)
        {
            OnCameraPreCullTilt(cam);
        }

        private void OnCameraPreCullTilt(Camera cam)
        {
            if (cam == null) return;
            if (Mathf.Abs(CurrentJumpSpikeGroundTiltAngleDeg) > 0.0001f)
            {
                cam.transform.localRotation = Quaternion.Euler(0f, 0f, CurrentJumpSpikeGroundTiltAngleDeg);
            }
        }

        private void ApplyWorldObjectsTilt(float tiltAngleDeg)
        {
            CurrentJumpSpikeGroundTiltAngleDeg = tiltAngleDeg;
            Quaternion rot = Quaternion.Euler(0f, 0f, tiltAngleDeg);
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.transform.localRotation = rot;
            }
            if (cachedTiltableTransforms != null)
            {
                for (int i = 0; i < cachedTiltableTransforms.Length; i++)
                {
                    Transform t = cachedTiltableTransforms[i];
                    if (t != null)
                    {
                        t.localRotation = rot;
                    }
                }
            }
        }

        private void UpdateOverlayVisuals(float dt)
        {
            float parentScaleX = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x));
            float parentScaleY = Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.y));

            if (midAirBurstTimer > 0f)
            {
                midAirBurstTimer = Mathf.Max(0f, midAirBurstTimer - dt);
                float progress = 1f - (midAirBurstTimer / MidAirBurstDuration);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - progress), 1.35f);

                if (sonicCrescentRenderer != null && sonicCrescentTransform != null)
                {
                    sonicCrescentRenderer.enabled = midAirBurstTimer > 0f;
                    float scale = Mathf.Lerp(CrescentStartScale, CrescentEndScale, progress);
                    Vector3 beamDelta = currentBallWorldPos - spikeOriginWorldPos;
                    if (beamDelta.sqrMagnitude > 0.0016f && currentBallWorldPos.y <= spikeOriginWorldPos.y + 0.05f)
                    {
                        float ringDist = Mathf.Min(beamDelta.magnitude * SonicRingShaftFraction, MaxLaserBeamTailLength * 0.65f);
                        Vector3 ringDir = currentBallVelocityDir.sqrMagnitude > 0.001f ? currentBallVelocityDir : beamDelta.normalized;
                        sonicCrescentTransform.position = currentBallWorldPos - ringDir * ringDist;
                        float beamAngleDeg = Mathf.Atan2(ringDir.y, ringDir.x) * Mathf.Rad2Deg;
                        sonicCrescentTransform.localRotation = Quaternion.Euler(0f, 0f, beamAngleDeg);
                    }
                    else
                    {
                        Vector3 offset = new Vector3(-spikeDirectionSign * Mathf.Lerp(0.06f, 0.22f, progress), 0.04f, 0f);
                        sonicCrescentTransform.position = spikeOriginWorldPos + offset;
                        sonicCrescentTransform.localRotation = Quaternion.Euler(0f, 0f, spikeDirectionSign >= 0f ? -18f : 162f);
                    }
                    sonicCrescentTransform.localScale = new Vector3(scale / parentScaleX, scale / parentScaleY, 1f);
                    sonicCrescentRenderer.color = new Color(1f, 1f, 1f, alpha * 0.96f);
                }

                if (radialSpeedLinesRenderer != null && radialSpeedLinesTransform != null)
                {
                    radialSpeedLinesRenderer.enabled = midAirBurstTimer > 0f;
                    float scale = Mathf.Lerp(SpeedLinesStartScale, SpeedLinesEndScale, progress);
                    radialSpeedLinesTransform.position = spikeOriginWorldPos;
                    radialSpeedLinesTransform.localRotation = Quaternion.Euler(0f, 0f, spikeDirectionSign >= 0f ? -26f : 206f);
                    radialSpeedLinesTransform.localScale = new Vector3(scale / parentScaleX, scale / parentScaleY, 1f);
                    radialSpeedLinesRenderer.color = new Color(1.00f, 0.96f, 0.72f, alpha * 0.95f);
                }

                if (xSlashSparkRenderer != null && xSlashSparkTransform != null)
                {
                    xSlashSparkRenderer.enabled = midAirBurstTimer > 0f;
                    float scale = Mathf.Lerp(XSlashStartScale, XSlashEndScale, Mathf.Sqrt(progress));
                    xSlashSparkTransform.position = spikeOriginWorldPos;
                    xSlashSparkTransform.localRotation = Quaternion.Euler(0f, 0f, spikeDirectionSign >= 0f ? -22f : 202f);
                    xSlashSparkTransform.localScale = new Vector3(scale / parentScaleX, scale / parentScaleY, 1f);
                    xSlashSparkRenderer.color = new Color(1.00f, 0.94f, 0.56f, alpha);
                }
            }
            else
            {
                if (sonicCrescentRenderer != null) sonicCrescentRenderer.enabled = false;
                if (radialSpeedLinesRenderer != null) radialSpeedLinesRenderer.enabled = false;
                if (xSlashSparkRenderer != null) xSlashSparkRenderer.enabled = false;
            }

            if (originPuffTimer > 0f)
            {
                originPuffTimer = Mathf.Max(0f, originPuffTimer - dt);
                float progress = 1f - (originPuffTimer / OriginPuffDuration);
                float alpha = Mathf.Clamp01(1f - progress * progress);
                if (originPuffRingRenderer != null && originPuffRingTransform != null)
                {
                    originPuffRingRenderer.enabled = originPuffTimer > 0f;
                    float scale = Mathf.Lerp(OriginPuffStartScale, OriginPuffEndScale, progress);
                    originPuffRingTransform.position = spikeOriginWorldPos;
                    originPuffRingTransform.localScale = new Vector3(scale / parentScaleX, scale / parentScaleY, 1f);
                    originPuffRingRenderer.color = new Color(1.00f, 0.98f, 0.88f, alpha * 0.76f);
                }
            }
            else if (originPuffRingRenderer != null)
            {
                originPuffRingRenderer.enabled = false;
            }

            if (spikeBeamTimer > 0f)
            {
                spikeBeamTimer = Mathf.Max(0f, spikeBeamTimer - dt);
                bool beamActive = spikeBeamTimer > 0f;
                float lifeRatio = Mathf.Clamp01(spikeBeamTimer / SpikeBeamMaxDuration);
                float baseWidth = activeSpikeTier == 1
                    ? BeamWidthLight
                    : (activeSpikeTier == 2 ? BeamWidthMedium : BeamWidthStrong);
                float currentBeamWidth = baseWidth * Mathf.Lerp(0.32f, 1.05f, lifeRatio);
                if (spikeLaserBeam != null)
                {
                    spikeLaserBeam.enabled = beamActive;
                    spikeLaserBeam.widthMultiplier = currentBeamWidth;
                }
                if (spikeLaserBeamShadow != null)
                {
                    spikeLaserBeamShadow.enabled = beamActive;
                    spikeLaserBeamShadow.widthMultiplier = currentBeamWidth * 1.35f;
                }
            }
            else
            {
                if (spikeLaserBeam != null) spikeLaserBeam.enabled = false;
                if (spikeLaserBeamShadow != null) spikeLaserBeamShadow.enabled = false;
            }

            if (groundSonicWaveTimer > 0f || groundCrossRingTimer > 0f || groundExplosionTimer > 0f)
            {
                groundSonicWaveTimer = Mathf.Max(0f, groundSonicWaveTimer - dt);
                groundCrossRingTimer = groundSonicWaveTimer;
                groundExplosionTimer = groundSonicWaveTimer;
                bool anyWaveActive = groundSonicWaveTimer > 0f;
                float elapsed = Mathf.Max(0f, groundSonicWaveDuration - groundSonicWaveTimer);
                float localProgress = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, groundSonicWaveDuration));

                // Render ONLY 1 single Boom Ring (i == 0) matching https://www.youtube.com/watch?v=Qo0kCZF0NWA (3.45m max diameter = 51% half-court)
                for (int i = 0; i < GroundSonicWaveCount; i++)
                {
                    SpriteRenderer waveRenderer = groundSonicWaveRenderers[i];
                    Transform waveTransform = groundSonicWaveTransforms[i];
                    if (waveRenderer == null || waveTransform == null) continue;

                    if (i >= ActiveSingleBoomRingCount || !anyWaveActive || localProgress >= 1.0f)
                    {
                        waveRenderer.enabled = false;
                        continue;
                    }

                    float expandCurve = 1f - Mathf.Pow(1f - localProgress, 2.10f);
                    float radius = Mathf.Lerp(GroundSonicWaveStartRadius, activeGroundSonicMaxRadius, expandCurve);
                    float fadeOut = Mathf.Pow(Mathf.Clamp01(1f - localProgress), 0.95f);
                    float waveAlpha = Mathf.Clamp01(groundSonicWavePeakAlpha * 1.55f * fadeOut);

                    waveRenderer.enabled = waveAlpha > 0.005f;
                    waveTransform.position = LastImpactWorldPosition + new Vector3(0f, 0.02f, 0f);
                    waveTransform.localRotation = Quaternion.identity;
                    waveTransform.localScale = new Vector3(
                        radius / parentScaleX,
                        (radius * GroundEllipseAspectY) / parentScaleY,
                        1f);
                    waveRenderer.color = new Color(1.00f, 1.00f, 1.00f, waveAlpha);
                }

                // Soft circular white floor glow pad (P04_GroundGoldenCrossRing) & V-shaped golden-yellow rebound flash (P04_GroundExplosionBurst)
                float normalizedImpactElapsed = localProgress;
                float centerFade = Mathf.Pow(Mathf.Clamp01(1f - normalizedImpactElapsed), 1.25f);
                float floorGlowScale = Mathf.Lerp(1.15f, activeGroundSonicMaxRadius * 0.45f, normalizedImpactElapsed);
                if (groundCrossRingRenderer != null && groundCrossRingTransform != null)
                {
                    groundCrossRingRenderer.enabled = anyWaveActive;
                    groundCrossRingTransform.position = LastImpactWorldPosition + new Vector3(0f, 0.015f, 0f);
                    groundCrossRingTransform.localRotation = Quaternion.identity;
                    groundCrossRingTransform.localScale = new Vector3(
                        floorGlowScale / parentScaleX,
                        (floorGlowScale * GroundEllipseAspectY) / parentScaleY,
                        1f);
                    groundCrossRingRenderer.color = new Color(1.00f, 1.00f, 1.00f, centerFade * 0.42f);
                }
                if (groundExplosionBurstRenderer != null && groundExplosionBurstTransform != null)
                {
                    float vStreakScale = Mathf.Lerp(1.50f, 2.55f, Mathf.Sqrt(normalizedImpactElapsed));
                    groundExplosionBurstRenderer.enabled = anyWaveActive;
                    groundExplosionBurstTransform.position = LastImpactWorldPosition + new Vector3(0f, 0.22f, 0f);
                    groundExplosionBurstTransform.localRotation = Quaternion.identity;
                    groundExplosionBurstTransform.localScale = new Vector3(
                        (vStreakScale * spikeDirectionSign) / parentScaleX,
                        (vStreakScale * 0.72f) / parentScaleY,
                        1f);
                    groundExplosionBurstRenderer.color = new Color(1.00f, 0.95f, 0.68f, centerFade * 0.72f);
                }
            }
            else
            {
                if (groundCrossRingRenderer != null) groundCrossRingRenderer.enabled = false;
                if (groundExplosionBurstRenderer != null) groundExplosionBurstRenderer.enabled = false;
                for (int i = 0; i < GroundSonicWaveCount; i++)
                {
                    if (groundSonicWaveRenderers[i] != null)
                        groundSonicWaveRenderers[i].enabled = false;
                }
            }

            // Single Boom Jump Ring on the floor under a jumping character (matching Qo0kCZF0NWA Boom Jump)
            if (boomJumpRingTimer > 0f)
            {
                boomJumpRingTimer = Mathf.Max(0f, boomJumpRingTimer - dt);
                float progress = 1f - (boomJumpRingTimer / BoomJumpRingDuration);
                float expandCurve = 1f - Mathf.Pow(1f - progress, 2.15f);
                float radius = Mathf.Lerp(GroundSonicWaveStartRadius, SingleBoomRingVideoTargetMaxRadius, expandCurve);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - progress), 1.05f) * 0.95f;
                if (boomJumpRingRenderer != null && boomJumpRingTransform != null)
                {
                    boomJumpRingRenderer.enabled = boomJumpRingTimer > 0f && alpha > 0.005f;
                    boomJumpRingTransform.position = boomJumpWorldPos;
                    boomJumpRingTransform.localRotation = Quaternion.identity;
                    boomJumpRingTransform.localScale = new Vector3(
                        radius / parentScaleX,
                        (radius * GroundEllipseAspectY) / parentScaleY,
                        1f);
                    boomJumpRingRenderer.color = new Color(1.00f, 1.00f, 1.00f, alpha);
                }
            }
            else if (boomJumpRingRenderer != null)
            {
                boomJumpRingRenderer.enabled = false;
            }

            if (jumpSpikeGroundTiltTimer > 0f)
            {
                jumpSpikeGroundTiltTimer = Mathf.Max(0f, jumpSpikeGroundTiltTimer - dt);
                float totalTiltDur = Mathf.Max(0.01f, jumpSpikeGroundTiltDuration);
                float tiltElapsedFraction = 1f - Mathf.Clamp01(jumpSpikeGroundTiltTimer / totalTiltDur);
                // Hold the full 20.0 deg screen rotation for the initial plateau (25% of 0.90s duration) before smoothly damping back to 0 deg
                float decayRatio = tiltElapsedFraction <= GroundTiltHoldPlateauFraction
                    ? 1.0f
                    : Mathf.Clamp01(1f - ((tiltElapsedFraction - GroundTiltHoldPlateauFraction) /
                                          Mathf.Max(0.01f, 1f - GroundTiltHoldPlateauFraction)));
                float dampedTilt = LastJumpSpikeGroundTiltAngleDeg * (decayRatio * decayRatio);
                ApplyWorldObjectsTilt(jumpSpikeGroundTiltTimer > 0f ? dampedTilt : 0f);
            }
            else if (Mathf.Abs(CurrentJumpSpikeGroundTiltAngleDeg) > 0.001f)
            {
                ApplyWorldObjectsTilt(0f);
            }

            if (spikeSpeedPopupTimer > 0f)
            {
                float popupDt = dt > 0f ? dt : Time.deltaTime;
                spikeSpeedPopupTimer = Mathf.Max(0f, spikeSpeedPopupTimer - popupDt);
                float progress = 1f - (spikeSpeedPopupTimer / SpikeSpeedPopupDuration);
                float fadeProgress = progress <= SpikeSpeedPopupFadeStartProgress
                    ? 0f
                    : (progress - SpikeSpeedPopupFadeStartProgress) / Mathf.Max(0.01f, 1f - SpikeSpeedPopupFadeStartProgress);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - fadeProgress), 1.15f);

                // Pop-in scale animation on eventSpike ("phong to len luc dap bong va thu nho lai bang kich thuoc quy dinh o tren"):
                // Expands rapidly to SpikeSpeedPopupPeakScale (3.2x) right on spike hit, then shrinks smoothly back to SpikeSpeedPopupTargetScale (2.0x = double previous size).
                float currentScale;
                if (progress < SpikeSpeedPopupPopUpPhaseEnd)
                {
                    float popT = Mathf.Clamp01(progress / SpikeSpeedPopupPopUpPhaseEnd);
                    float easeOut = Mathf.Sin(popT * Mathf.PI * 0.5f);
                    currentScale = Mathf.Lerp(SpikeSpeedPopupTargetScale, SpikeSpeedPopupPeakScale, easeOut);
                }
                else if (progress < SpikeSpeedPopupShrinkPhaseEnd)
                {
                    float shrinkT = Mathf.Clamp01((progress - SpikeSpeedPopupPopUpPhaseEnd) /
                                                  Mathf.Max(0.01f, SpikeSpeedPopupShrinkPhaseEnd - SpikeSpeedPopupPopUpPhaseEnd));
                    float easeOut = 1f - (1f - shrinkT) * (1f - shrinkT);
                    currentScale = Mathf.Lerp(SpikeSpeedPopupPeakScale, SpikeSpeedPopupTargetScale, easeOut);
                }
                else
                {
                    currentScale = SpikeSpeedPopupTargetScale;
                }

                bool popupVisible = spikeSpeedPopupTimer > 0f;
                spikeSpeedPopupOrigin = ComputeTopCenterPopupWorldPosition();
                if (spikeSpeedPopupRenderer != null && spikeSpeedPopupTransform != null && spikeSpeedPopupText != null)
                {
                    Camera mainCam = Camera.main;
                    Vector3 upDir = mainCam != null ? mainCam.transform.up : Vector3.up;
                    spikeSpeedPopupRenderer.enabled = popupVisible;
                    spikeSpeedPopupTransform.position = spikeSpeedPopupOrigin +
                                                        upDir * Mathf.Lerp(0f, SpikeSpeedPopupRiseHeight, progress);
                    spikeSpeedPopupTransform.rotation = mainCam != null
                        ? mainCam.transform.rotation
                        : Quaternion.Euler(0f, 0f, CurrentJumpSpikeGroundTiltAngleDeg);
                    spikeSpeedPopupTransform.localScale = new Vector3(
                        currentScale / parentScaleX,
                        currentScale / parentScaleY,
                        1f);
                    spikeSpeedPopupText.color = new Color(1f, 1f, 1f, alpha);
                }
                for (int i = 0; i < SpikeSpeedPopupOutlineCount; i++)
                {
                    if (spikeSpeedPopupOutlineRenderers[i] != null)
                        spikeSpeedPopupOutlineRenderers[i].enabled = popupVisible;
                    if (spikeSpeedPopupOutlineTexts[i] != null)
                        spikeSpeedPopupOutlineTexts[i].color = new Color(0f, 0f, 0f, alpha);
                }
            }
            else if (spikeSpeedPopupRenderer != null)
            {
                SetSpikePopupEnabled(false);
            }

            if (pointScoreRingTimer > 0f)
            {
                pointScoreRingTimer = Mathf.Max(0f, pointScoreRingTimer - dt);
                float progress = 1f - (pointScoreRingTimer / PointScoreRingDuration);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - progress), 1.15f);
                if (pointScoreRingRenderer != null && pointScoreRingTransform != null)
                {
                    pointScoreRingRenderer.enabled = pointScoreRingTimer > 0f;
                    float scale = Mathf.Lerp(PointScoreRingStartScale, PointScoreRingEndScale, Mathf.Sqrt(progress));
                    pointScoreRingTransform.position = pointScoreWorldPos + new Vector3(0f, 0.06f, 0f);
                    pointScoreRingTransform.localScale = new Vector3(scale / parentScaleX, (scale * 0.76f) / parentScaleY, 1f);
                    pointScoreRingRenderer.color = new Color(1f, 0.94f, 0.62f, alpha);
                }
            }
            else if (pointScoreRingRenderer != null)
            {
                pointScoreRingRenderer.enabled = false;
            }
        }

        private void DisableOverlayVisuals()
        {
            activeSpikeTier = 0;
            midAirBurstTimer = 0f;
            originPuffTimer = 0f;
            spikeBeamTimer = 0f;
            groundCrossRingTimer = 0f;
            groundExplosionTimer = 0f;
            groundSonicWaveTimer = 0f;
            boomJumpRingTimer = 0f;
            jumpSpikeGroundTiltTimer = 0f;
            spikeSpeedPopupTimer = 0f;
            if (Mathf.Abs(CurrentJumpSpikeGroundTiltAngleDeg) > 0.001f)
            {
                ApplyWorldObjectsTilt(0f);
            }
            if (sonicCrescentRenderer != null) sonicCrescentRenderer.enabled = false;
            if (radialSpeedLinesRenderer != null) radialSpeedLinesRenderer.enabled = false;
            if (xSlashSparkRenderer != null) xSlashSparkRenderer.enabled = false;
            if (originPuffRingRenderer != null) originPuffRingRenderer.enabled = false;
            if (spikeLaserBeam != null) spikeLaserBeam.enabled = false;
            if (spikeLaserBeamShadow != null) spikeLaserBeamShadow.enabled = false;
            if (groundCrossRingRenderer != null) groundCrossRingRenderer.enabled = false;
            if (groundExplosionBurstRenderer != null) groundExplosionBurstRenderer.enabled = false;
            if (boomJumpRingRenderer != null) boomJumpRingRenderer.enabled = false;
            for (int i = 0; i < GroundSonicWaveCount; i++)
            {
                if (groundSonicWaveRenderers[i] != null)
                    groundSonicWaveRenderers[i].enabled = false;
            }
            SetSpikePopupEnabled(false);
            if (trail != null && defaultTrailGradient != null)
                trail.colorGradient = defaultTrailGradient;
        }

        private void SetSpikePopupText(string content, bool visible)
        {
            if (spikeSpeedPopupText != null)
                spikeSpeedPopupText.text = content;
            for (int i = 0; i < SpikeSpeedPopupOutlineCount; i++)
            {
                if (spikeSpeedPopupOutlineTexts[i] != null)
                    spikeSpeedPopupOutlineTexts[i].text = content;
            }
            SetSpikePopupEnabled(visible);
        }

        private void SetSpikePopupEnabled(bool visible)
        {
            if (spikeSpeedPopupRenderer != null)
                spikeSpeedPopupRenderer.enabled = visible;
            for (int i = 0; i < SpikeSpeedPopupOutlineCount; i++)
            {
                if (spikeSpeedPopupOutlineRenderers[i] != null)
                    spikeSpeedPopupOutlineRenderers[i].enabled = visible;
            }
        }

        public static Sprite GetOrCreateGroundSonicWaveSprite()
        {
            if (cachedGroundSonicWaveSprite != null) return cachedGroundSonicWaveSprite;
            const int N = ProceduralVfxTextureSize;
            cachedGroundSonicWaveTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "P04_GroundSonicWaveTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Single crisp hollow white Boom Ring matching https://www.youtube.com/watch?v=Qo0kCZF0NWA (ref2_boom_jump_full.jpg & ref2_land_18850.jpg):
            // Clean hollow center (r < 0.62), smooth inward white gradient feather (0.62..0.88), and bright pure-white outer rim (0.88..0.95).
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 0.98f || r < 0.58f)
                    {
                        cachedGroundSonicWaveTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    // Smooth inward feather from outer rim (r = 0.91) down to hollow interior (r = 0.58)
                    float innerFeather = r <= 0.91f
                        ? Mathf.Pow(Mathf.Clamp01((r - 0.58f) / 0.33f), 2.15f) * 0.58f
                        : 0f;
                    // Bright crisp outer rim at r ~ 0.91
                    float outerRim = Mathf.Exp(-Mathf.Pow((r - 0.91f) / 0.042f, 2f));
                    float alpha = Mathf.Clamp01(Mathf.Max(innerFeather, outerRim));
                    cachedGroundSonicWaveTexture.SetPixel(x, y, new Color(1.00f, 1.00f, 1.00f, alpha));
                }
            }
            cachedGroundSonicWaveTexture.Apply();
            cachedGroundSonicWaveSprite = Sprite.Create(cachedGroundSonicWaveTexture,
                new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cachedGroundSonicWaveSprite.name = "P04_GroundSonicWaveSprite";
            cachedGroundSonicWaveSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedGroundSonicWaveSprite;
        }

        public static Sprite GetOrCreateGroundExplosionBurstSprite()
        {
            if (cachedGroundExplosionBurstSprite != null) return cachedGroundExplosionBurstSprite;
            const int N = ProceduralVfxTextureSize;
            cachedGroundExplosionBurstTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "P04_GroundExplosionBurstTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Translucent Golden-Yellow & White V-shaped Rebound Streak at the ground impact center (matching reference image)
            Color hotWhiteCore = new Color(1.00f, 1.00f, 0.96f, 1f);
            Color goldenYellowStreak = new Color(1.00f, 0.85f, 0.16f, 1f);
            Color warmAmberEdge = new Color(0.92f, 0.56f, 0.06f, 1f);

            Vector2 vVertex = new Vector2(-0.08f, -0.52f);
            Vector2 leftArmEnd = new Vector2(-0.78f, 0.72f);
            Vector2 rightArmEnd = new Vector2(0.84f, 0.48f);

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    if (nx * nx + ny * ny > 1f)
                    {
                        cachedGroundExplosionBurstTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    Vector2 p = new Vector2(nx, ny);
                    float dLeft = DistanceToSegment(p, vVertex, leftArmEnd, out float tLeft);
                    float dRight = DistanceToSegment(p, vVertex, rightArmEnd, out float tRight);

                    float widthLeft = Mathf.Lerp(0.16f, 0.045f, tLeft);
                    float widthRight = Mathf.Lerp(0.18f, 0.060f, tRight);
                    float leftArm = Mathf.Clamp01(1f - dLeft / widthLeft) * Mathf.Clamp01(1.1f - tLeft * 0.45f);
                    float rightArm = Mathf.Clamp01(1f - dRight / widthRight) * Mathf.Clamp01(1.1f - tRight * 0.35f);

                    float vIntensity = Mathf.Max(leftArm, rightArm);
                    // Soft white circular bounce pad at the V vertex on the floor
                    float vertexDist = Vector2.Distance(p, vVertex);
                    float vertexPad = Mathf.Exp(-Mathf.Pow(vertexDist / 0.28f, 2f)) * 0.85f;

                    float totalAlpha = Mathf.Clamp01(vIntensity * vIntensity * 0.88f + vertexPad * 0.72f);
                    Color streakCol = Color.Lerp(warmAmberEdge, goldenYellowStreak, vIntensity);
                    Color finalCol = Color.Lerp(streakCol, hotWhiteCore, Mathf.Clamp01(vertexPad + Mathf.Pow(vIntensity, 2.4f) * 0.75f));
                    finalCol.a = totalAlpha;
                    cachedGroundExplosionBurstTexture.SetPixel(x, y, finalCol);
                }
            }
            cachedGroundExplosionBurstTexture.Apply();
            cachedGroundExplosionBurstSprite = Sprite.Create(cachedGroundExplosionBurstTexture,
                new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cachedGroundExplosionBurstSprite.name = "P04_GroundExplosionBurstSprite";
            cachedGroundExplosionBurstSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedGroundExplosionBurstSprite;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.00001f)
            {
                t = 0f;
                return Vector2.Distance(p, a);
            }
            t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }

        public static Sprite GetOrCreateSonicCrescentSprite()
        {
            if (cachedSonicCrescentSprite != null) return cachedSonicCrescentSprite;
            const int N = ProceduralVfxTextureSize;
            cachedSonicCrescentTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "P04_SonicCrescentTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Jagged Golden-Yellow Lightning Zig-Zag Streak + Dark Burnt-Amber Shadow (replacing mid-air sonic boom ring)
            Color lightningGold = new Color(1.00f, 0.85f, 0.14f, 1f);
            Color hotWhiteGold = new Color(1.00f, 0.98f, 0.82f, 1f);
            Color darkAmberShadow = new Color(0.38f, 0.18f, 0.03f, 1f);

            Vector2[] boltPts =
            {
                new Vector2(-0.82f, 0.18f),
                new Vector2(-0.34f, -0.16f),
                new Vector2(0.18f, 0.20f),
                new Vector2(0.84f, -0.14f)
            };
            Vector2 shadowShift = new Vector2(-0.08f, -0.16f);

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    if (nx * nx + ny * ny > 1f)
                    {
                        cachedSonicCrescentTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    Vector2 p = new Vector2(nx, ny);
                    float boltVal = 0f;
                    float shadowVal = 0f;
                    for (int i = 0; i < boltPts.Length - 1; i++)
                    {
                        float dBolt = DistanceToSegment(p, boltPts[i], boltPts[i + 1], out _);
                        boltVal = Mathf.Max(boltVal, Mathf.Clamp01(1f - dBolt / 0.11f));

                        float dShadow = DistanceToSegment(p, boltPts[i] + shadowShift, boltPts[i + 1] + shadowShift, out _);
                        shadowVal = Mathf.Max(shadowVal, Mathf.Clamp01(1f - dShadow / 0.14f));
                    }

                    float core = Mathf.Pow(boltVal, 2.2f);
                    Color boltCol = Color.Lerp(lightningGold, hotWhiteGold, core);
                    Color c = boltVal > 0.05f ? boltCol : darkAmberShadow;
                    c.a = Mathf.Clamp01(boltVal * 0.96f + (1f - boltVal) * shadowVal * 0.55f);
                    cachedSonicCrescentTexture.SetPixel(x, y, c);
                }
            }
            cachedSonicCrescentTexture.Apply();
            cachedSonicCrescentSprite = Sprite.Create(cachedSonicCrescentTexture, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cachedSonicCrescentSprite.name = "P04_SonicCrescentSprite";
            cachedSonicCrescentSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedSonicCrescentSprite;
        }

        public static Sprite GetOrCreateRadialSpeedLinesSprite()
        {
            if (cachedRadialSpeedLinesSprite != null) return cachedRadialSpeedLinesSprite;
            const int N = ProceduralVfxTextureSize;
            cachedRadialSpeedLinesTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "P04_RadialSpeedLinesTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Directional Jagged Golden-Yellow Lightning Bolt + Dark Amber Shadow (replacing 24-ray radial starburst boom)
            Color lightningGold = new Color(1.00f, 0.84f, 0.12f, 1f);
            Color whiteGoldCore = new Color(1.00f, 0.98f, 0.80f, 1f);
            Color darkAmberShadow = new Color(0.36f, 0.17f, 0.03f, 1f);

            Vector2[] zigPts =
            {
                new Vector2(-0.85f, 0.42f),
                new Vector2(-0.42f, 0.12f),
                new Vector2(-0.18f, 0.30f),
                new Vector2(0.26f, -0.14f),
                new Vector2(0.46f, 0.04f),
                new Vector2(0.86f, -0.38f)
            };
            Vector2 shadowOffset = new Vector2(-0.09f, -0.15f);

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    if (nx * nx + ny * ny > 1f)
                    {
                        cachedRadialSpeedLinesTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    Vector2 p = new Vector2(nx, ny);
                    float bolt = 0f;
                    float shadow = 0f;
                    for (int i = 0; i < zigPts.Length - 1; i++)
                    {
                        float dB = DistanceToSegment(p, zigPts[i], zigPts[i + 1], out _);
                        bolt = Mathf.Max(bolt, Mathf.Clamp01(1f - dB / 0.095f));

                        float dS = DistanceToSegment(p, zigPts[i] + shadowOffset, zigPts[i + 1] + shadowOffset, out _);
                        shadow = Mathf.Max(shadow, Mathf.Clamp01(1f - dS / 0.125f));
                    }

                    Color boltColor = Color.Lerp(lightningGold, whiteGoldCore, Mathf.Pow(bolt, 2.0f));
                    Color c = bolt > 0.05f ? boltColor : darkAmberShadow;
                    c.a = Mathf.Clamp01(bolt * 0.95f + (1f - bolt) * shadow * 0.54f);
                    cachedRadialSpeedLinesTexture.SetPixel(x, y, c);
                }
            }
            cachedRadialSpeedLinesTexture.Apply();
            cachedRadialSpeedLinesSprite = Sprite.Create(cachedRadialSpeedLinesTexture, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cachedRadialSpeedLinesSprite.name = "P04_RadialSpeedLinesSprite";
            cachedRadialSpeedLinesSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedRadialSpeedLinesSprite;
        }

        public static Sprite GetOrCreateXSlashSparkSprite()
        {
            if (cachedXSlashSparkSprite != null) return cachedXSlashSparkSprite;
            const int N = ProceduralVfxTextureSize;
            cachedXSlashSparkTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "P04_XSlashSparkTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Sleek single directional golden-white lightning contact glint (replacing 4-blade X boom)
            Color goldenSpark = new Color(1.00f, 0.84f, 0.16f, 1f);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 1f)
                    {
                        cachedXSlashSparkTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float dStreak = Mathf.Abs(ny);
                    float streakWidth = Mathf.Lerp(0.085f, 0.008f, Mathf.Abs(nx));
                    float streak = Mathf.Clamp01(1f - dStreak / Mathf.Max(0.004f, streakWidth)) * Mathf.Clamp01(1f - r);
                    float centerCore = Mathf.Exp(-Mathf.Pow(r / 0.22f, 2f)) * 0.65f;
                    float intensity = Mathf.Clamp01(streak + centerCore);
                    Color c = Color.Lerp(goldenSpark, Color.white, Mathf.Pow(intensity, 1.6f));
                    c.a = intensity;
                    cachedXSlashSparkTexture.SetPixel(x, y, c);
                }
            }
            cachedXSlashSparkTexture.Apply();
            cachedXSlashSparkSprite = Sprite.Create(cachedXSlashSparkTexture, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cachedXSlashSparkSprite.name = "P04_XSlashSparkSprite";
            cachedXSlashSparkSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedXSlashSparkSprite;
        }

        public static Sprite GetOrCreateOriginPuffRingSprite()
        {
            if (cachedOriginPuffRingSprite != null) return cachedOriginPuffRingSprite;
            const int N = ProceduralVfxTextureSize;
            cachedOriginPuffRingTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "P04_OriginPuffRingTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 1f)
                    {
                        cachedOriginPuffRingTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    // Soft circular white glow pad (zero hard boom ring)
                    float softPad = Mathf.Exp(-Mathf.Pow(r / 0.58f, 2f)) * 0.55f;
                    cachedOriginPuffRingTexture.SetPixel(x, y, new Color(1.00f, 0.99f, 0.94f, softPad));
                }
            }
            cachedOriginPuffRingTexture.Apply();
            cachedOriginPuffRingSprite = Sprite.Create(cachedOriginPuffRingTexture, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cachedOriginPuffRingSprite.name = "P04_OriginPuffRingSprite";
            cachedOriginPuffRingSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedOriginPuffRingSprite;
        }

        public static Sprite GetOrCreateGoldenCrossRingSprite()
        {
            if (cachedGoldenCrossRingSprite != null) return cachedGoldenCrossRingSprite;
            const int N = ProceduralVfxTextureSize;
            cachedGoldenCrossRingTexture = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "P04_GoldenCrossRingTex",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Soft glowing white floor pad + subtle white-gold rim (matching the clean white floor glow circles in reference image)
            Color pureWhite = new Color(1.00f, 1.00f, 1.00f, 1f);
            Color softCyanWhite = new Color(0.82f, 0.96f, 1.00f, 1f);

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 1f)
                    {
                        cachedGoldenCrossRingTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float centerPad = Mathf.Exp(-Mathf.Pow(r / 0.52f, 2f)) * 0.78f;
                    float softOuterRing = Mathf.Exp(-Mathf.Pow((r - 0.76f) / 0.09f, 2f)) * 0.65f;
                    float totalAlpha = Mathf.Clamp01(centerPad + softOuterRing);
                    Color finalCol = Color.Lerp(softCyanWhite, pureWhite, Mathf.Clamp01(centerPad * 1.25f));
                    finalCol.a = totalAlpha;
                    cachedGoldenCrossRingTexture.SetPixel(x, y, finalCol);
                }
            }
            cachedGoldenCrossRingTexture.Apply();
            cachedGoldenCrossRingSprite = Sprite.Create(cachedGoldenCrossRingTexture, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
            cachedGoldenCrossRingSprite.name = "P04_GoldenCrossRingSprite";
            cachedGoldenCrossRingSprite.hideFlags = HideFlags.HideAndDontSave;
            return cachedGoldenCrossRingSprite;
        }

        private bool PlayOneShot(AudioClip clip, float categoryVolume)
        {
            if (audioSource == null || clip == null || categoryVolume <= 0f) return false;
            audioSource.Stop();
            audioSource.PlayOneShot(clip, Mathf.Clamp01(categoryVolume));
            return true;
        }

        private void ClearTrail()
        {
            if (trail == null) return;
            trail.emitting = false;
            trail.Clear();
            TrailClearCount++;
        }

        private void StopAudio()
        {
            if (audioSource != null) audioSource.Stop();
        }

        private void ResetTransientFeedback(bool resetHistory)
        {
            ClearTrail();
            StopAudio();
            DisableOverlayVisuals();
            if (impactParticles != null)
                impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!resetHistory) return;
            lastActiveBallSpeed = 0f;
            pausedTrailLifetimeExtension = 0f;
            isLastTickSimFrozen = false;
            hadSlowMoDuringCurrentFlight = false;
            IsSimLayerPausedOrFrozen = false;
            pointScoreRingTimer = 0f;
            if (pointScoreRingRenderer != null) pointScoreRingRenderer.enabled = false;
            hasProcessedTick = false;
            hasPreviousBall = false;
            hasPreviousMatch = false;
            hasPreviousScore = false;
            lastTeam0Score = 0;
            lastTeam1Score = 0;
            Array.Clear(hasPreviousCharacters, 0, hasPreviousCharacters.Length);
        }

        private static void DestroyOwned(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
