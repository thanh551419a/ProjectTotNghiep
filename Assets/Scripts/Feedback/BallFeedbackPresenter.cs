using System;
using Assets.Table;
using Native;
using Simulation;
using UnityEngine;

namespace Presentation.Feedback
{
    /// <summary>
    /// Presentation-only feedback for the rendered native ball (Medieval Iron Sphere visual,
    /// forged-iron trail, ground spark burst, and synthesized metallic audio).
    /// It observes committed DataTable snapshots and never calls native APIs or writes simulation/entity state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallFeedbackPresenter : MonoBehaviour
    {
        public const string IronSphereSpriteName = "P03_MedievalIronSphereSprite";

        // Verified against MyGame/Source/Config/Match/MatchRuleConfig.h. Used only to avoid
        // emitting a landing cue for the native max-touch terminal branch.
        private const int NativeMaxTouchesPerTeam = 3;
        private const int NativeCharactersPerSide = 3;
        private const int ImpactParticleCount = 9;
        private const float TeleportClearDistance = 3.0f;
        private const float SpherePixelRadiusFraction = 0.5f;

        // RcTdc--AdaI & KIQbyAcdmSM Spike Contact Burst, Laser Beam Trail, Sonic Ring & Point-Score constants
        private const int ProceduralVfxTextureSize = 128;
        private const float NormalRallyTrailWidthScale = 0.65f;
        private const float SonicRingShaftFraction = 0.35f;
        private const float SpikeContactMaxDistanceWorld = 2.5f;
        private const float MidAirBurstDuration = 0.26f;
        private const float OriginPuffDuration = 0.36f;
        private const float SpikeBeamMaxDuration = 0.42f;
        private const float GroundCrossRingDuration = 0.38f;
        private const float SpikeSpeedPopupDuration = 0.55f;
        private const float SpikeSpeedPopupRiseHeight = 0.35f;
        private const float PointScoreRingDuration = 0.48f;
        private const float CrescentStartScale = 0.48f;
        private const float CrescentEndScale = 1.18f;
        private const float SpeedLinesStartScale = 0.42f;
        private const float SpeedLinesEndScale = 1.48f;
        private const float XSlashStartScale = 0.36f;
        private const float XSlashEndScale = 1.05f;
        private const float OriginPuffStartScale = 0.30f;
        private const float OriginPuffEndScale = 0.92f;
        private const float GroundRingStartScale = 0.38f;
        private const float GroundRingEndScale = 1.52f;
        private const float PointScoreRingStartScale = 0.45f;
        private const float PointScoreRingEndScale = 2.35f;
        private const int PointScoreParticleCount = 14;
        private const int MinSpikePopupKmh = 72;
        private const int MaxSpikePopupKmh = 162;
        private const float BeamWidthLight = 0.085f;
        private const float BeamWidthMedium = 0.135f;
        private const float BeamWidthStrong = 0.195f;

        [Header("Trail")]
        [SerializeField, Min(0.05f)] private float slowTrailDuration = 0.14f;
        [SerializeField, Min(0.05f)] private float fastTrailDuration = 0.32f;
        [SerializeField, Min(0.001f)] private float slowTrailWidth = 0.045f;
        [SerializeField, Min(0.001f)] private float fastTrailWidth = 0.12f;
        [SerializeField, Min(0f)] private float minimumTrailSpeed = 0.02f;

        [Header("Impact")]
        [SerializeField] private Color impactColor = new Color(1f, 0.78f, 0.36f, 0.96f);

        [Header("Audio")]
        [SerializeField, Range(0f, 1f)] private float spikeVolume = 0.34f;
        [SerializeField, Range(0f, 1f)] private float bumpVolume = 0.26f;
        [SerializeField, Range(0f, 1f)] private float groundImpactVolume = 0.40f;
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
        private static string[] cachedKmhStrings;

        private TrailRenderer trail;
        private ParticleSystem impactParticles;
        private GameObject impactObject;
        private AudioSource audioSource;
        private AudioSource bgmAudioSource;
        private AudioClip spikeClip;
        private AudioClip bumpClip;
        private AudioClip groundClip;
        private AudioClip arenaBgmClip;
        private Material feedbackMaterial;
        private SimulationEntityReader entityReader;
        private SimulationDataTable boundTable;

        // Pre-allocated RcTdc--AdaI & KIQbyAcdmSM VFX child renderers & cached gradients (zero hot-loop allocations)
        private Gradient defaultTrailGradient;
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
        private Transform groundCrossRingTransform;
        private SpriteRenderer groundCrossRingRenderer;
        private Transform spikeSpeedPopupTransform;
        private TextMesh spikeSpeedPopupText;
        private MeshRenderer spikeSpeedPopupRenderer;
        private Transform pointScoreRingTransform;
        private SpriteRenderer pointScoreRingRenderer;

        private int activeSpikeTier;
        private float midAirBurstTimer;
        private float originPuffTimer;
        private float spikeBeamTimer;
        private float groundCrossRingTimer;
        private float spikeSpeedPopupTimer;
        private float pointScoreRingTimer;
        private Vector3 spikeOriginWorldPos;
        private Vector3 currentBallWorldPos;
        private Vector3 spikeSpeedPopupOrigin;
        private Vector3 pointScoreWorldPos;
        private float spikeDirectionSign = 1f;
        private float lastGroundRingTierScale = 1f;
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
        public int SpikeSoundCount { get; private set; }
        public int BumpSoundCount { get; private set; }
        public int PointScoredBurstCount { get; private set; }
        public int LastSpikeImpactKmh { get; private set; }
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
            entityReader = GetComponent<SimulationEntityReader>();
            ApplyMedievalIronSphere(GetComponent<SpriteRenderer>());
            CreateTrail();
            CreateImpactParticles();
            CreateSpikeAndImpactOverlays();
            CreateAudioSource();
            CreatePlaceholderClips();
            CreateArenaAmbience();
            BindTable();
        }

        private void OnEnable()
        {
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
            DestroyOwned(arenaBgmClip);
            DestroyOwned(feedbackMaterial);
        }

        private void LateUpdate()
        {
            BindTable();
            if (midAirBurstTimer > 0f || originPuffTimer > 0f || spikeBeamTimer > 0f ||
                groundCrossRingTimer > 0f || spikeSpeedPopupTimer > 0f || pointScoreRingTimer > 0f)
            {
                UpdateOverlayVisuals(Time.deltaTime);
            }
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
            if (hasPreviousBall && IsGroundImpactTransition(previousBall, ball, maxTouchEndedThisTick,
                    CourtGeometry.FloorY))
            {
                EmitGroundImpact(ball, tick);
            }

            bool serveOrReset = !hasPreviousBall ||
                                (previousBall.stateFrame == 0 && ball.stateFrame == -1) ||
                                (previousBall.stateFrame > 0 && ball.stateFrame == 0);
            bool teleported = hasPreviousBall &&
                              Vector2.Distance(new Vector2(previousBall.posX + previousBall.sizeX * 0.5f,
                                      previousBall.posY + previousBall.sizeY * 0.5f),
                                  new Vector2(ball.posX + ball.sizeX * 0.5f,
                                      ball.posY + ball.sizeY * 0.5f)) /
                              CourtGeometry.PixelsPerMeter > TeleportClearDistance;

            if (serveOrReset || teleported)
            {
                ClearTrail();
                StopAudio();
                DisableOverlayVisuals();
                if (impactParticles != null)
                    impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            ProcessScoreCelebration(boundTable, anchor, serveOrReset);

            bool activeBall = ball.stateFrame == -1;
            UpdateTrail(ball, anchor, activeBall);

            if (spikeBeamTimer > 0f && activeBall && spikeLaserBeam != null)
            {
                spikeLaserBeam.SetPosition(0, spikeOriginWorldPos);
                spikeLaserBeam.SetPosition(1, anchor);
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
            bool withinContactProximity = !character.hasPosition || !ball.hasPosition ||
                                          Vector2.Distance(SimulationEntityReader.AnchorWorld(character),
                                              SimulationEntityReader.AnchorWorld(ball)) <= SpikeContactMaxDistanceWorld;
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
                bool spikeWhiffed = false;
                if (started)
                {
                    SimActionState state = (SimActionState)character.actionStatus;
                    if (state == SimActionState.Bump)
                    {
                        if (acceptedTouchEntity < 0 || IsSameTeam(previousLastTouchEntity, entityId))
                            acceptedTouchEntity = entityId;
                        activeSpikeTier = 0;
                        spikeBeamTimer = 0f;
                        if (spikeLaserBeam != null) spikeLaserBeam.enabled = false;
                        if (trail != null && defaultTrailGradient != null)
                            trail.colorGradient = defaultTrailGradient;
                        if (PlayOneShot(bumpClip, bumpVolume))
                        {
                            BumpSoundCount++;
                            bumpSoundCountsByEntity[entityId]++;
                        }
                    }
                    else if (state == SimActionState.Set)
                    {
                        if (acceptedTouchEntity < 0 || IsSameTeam(previousLastTouchEntity, entityId))
                            acceptedTouchEntity = entityId;
                        activeSpikeTier = 0;
                        spikeBeamTimer = 0f;
                        if (spikeLaserBeam != null) spikeLaserBeam.enabled = false;
                        if (trail != null && defaultTrailGradient != null)
                            trail.colorGradient = defaultTrailGradient;
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
            float speed = Mathf.Abs(ball.ballSpeed) / CourtGeometry.PixelsPerMeter;
            bool emit = active && speed >= minimumTrailSpeed;
            trail.emitting = emit;
            if (!emit) return;

            float speedWeight = Mathf.InverseLerp(0.15f, 1.25f, speed);
            trail.time = Mathf.Lerp(slowTrailDuration, fastTrailDuration, speedWeight);
            float baseWidth = Mathf.Lerp(slowTrailWidth, fastTrailWidth, speedWeight);
            trail.widthMultiplier = activeSpikeTier == 0 ? baseWidth * NormalRallyTrailWidthScale : baseWidth;

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
                spikeLaserBeam.SetPosition(0, spikeOriginWorldPos);
                spikeLaserBeam.SetPosition(1, currentBallWorldPos);
                spikeLaserBeam.enabled = true;
            }

            UpdateOverlayVisuals(0f);

            if (impactParticles == null) return;
            Color sparkColor = new Color(0.70f, 0.96f, 1.00f, 0.98f);
            Color goldSpark = new Color(1.00f, 0.86f, 0.32f, 0.98f);
            impactParticles.Play(true);
            for (int i = 0; i < 8; i++)
            {
                float angle = (i / 8f) * Mathf.PI * 2f;
                var emit = new ParticleSystem.EmitParams
                {
                    position = burstPos,
                    velocity = new Vector3(Mathf.Cos(angle) * 1.35f, Mathf.Sin(angle) * 1.35f, 0f),
                    startLifetime = 0.24f,
                    startSize = 0.17f,
                    startColor = (i % 2 == 0) ? sparkColor : goldSpark
                };
                impactParticles.Emit(emit, 1);
            }
        }

        private void EmitGroundImpact(SimulationEntityRecord ball, ulong tick)
        {
            Vector3 center = SimulationEntityReader.AnchorWorld(ball);
            Vector3 contact = new Vector3(center.x, CourtGeometry.ToWorldY(CourtGeometry.FloorY), center.z);
            LastImpactWorldPosition = contact;

            // Trigger RcTdc--AdaI Golden Cross-Star + Double Shockwave Ring at ground contact
            groundCrossRingTimer = GroundCrossRingDuration;
            lastGroundRingTierScale = activeSpikeTier >= 3 ? 1.26f : (activeSpikeTier == 2 ? 1.12f : 1.0f);
            if (spikeBeamTimer > 0f && spikeLaserBeam != null)
            {
                spikeLaserBeam.SetPosition(1, contact);
                spikeBeamTimer = Mathf.Min(spikeBeamTimer, 0.09f);
            }

            // Floating spike speed readout (km/h) near floor impact point (KIQbyAcdmSM @ t=345s, 405s, 440s)
            float horizMps = Mathf.Abs(ball.ballSpeed) / CourtGeometry.PixelsPerMeter;
            float stepMps = hasPreviousBall ? CenterDistanceWorld(previousBall, ball) * 60f : horizMps;
            float impactSpeedMps = Mathf.Max(horizMps, stepMps);
            if (lastGroundRingTierScale > 1.0f || activeSpikeTier > 0 || impactSpeedMps >= 8.0f)
            {
                int kmh = Mathf.RoundToInt(Mathf.Clamp(impactSpeedMps * 5.2f, MinSpikePopupKmh, MaxSpikePopupKmh));
                if (activeSpikeTier >= 3) kmh = Mathf.Max(kmh, 136);
                else if (activeSpikeTier == 2) kmh = Mathf.Max(kmh, 120);
                else if (activeSpikeTier == 1) kmh = Mathf.Clamp(kmh, 76, 102);

                LastSpikeImpactKmh = kmh;
                spikeSpeedPopupOrigin = contact + new Vector3(0f, 0.24f, 0f);
                spikeSpeedPopupTimer = SpikeSpeedPopupDuration;
                if (spikeSpeedPopupText != null && spikeSpeedPopupRenderer != null)
                {
                    spikeSpeedPopupText.text = GetCachedKmhString(kmh);
                    spikeSpeedPopupRenderer.enabled = true;
                }
            }

            UpdateOverlayVisuals(0f);

            if (impactParticles != null)
            {
                impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                impactParticles.transform.position = contact;
                impactParticles.Play(true);
                for (int i = 0; i < ImpactParticleCount; i++)
                {
                    float t = i / (float)(ImpactParticleCount - 1);
                    float angle = Mathf.Lerp(Mathf.PI, 0f, t);
                    var emit = new ParticleSystem.EmitParams
                    {
                        position = contact,
                        velocity = new Vector3(Mathf.Cos(angle) * 0.95f, 0.38f + Mathf.Sin(angle) * 0.64f, 0f),
                        startLifetime = Mathf.Lerp(0.24f, 0.40f, 1f - Mathf.Abs(t - 0.5f) * 2f),
                        startSize = Mathf.Lerp(0.11f, 0.21f, 1f - Mathf.Abs(t - 0.5f) * 2f),
                        startColor = impactColor
                    };
                    impactParticles.Emit(emit, 1);
                }
            }

            bool soundPlayed = PlayOneShot(groundClip, groundImpactVolume);
            GroundImpactEventCount++;
            if (soundPlayed) GroundImpactSoundCount++;
            LastGroundImpactTick = tick;
            Debug.Log($"[P04_BALL_FEEDBACK] ground impact tick={tick} entity={ball.entityId} nativePos=({ball.posX:F2},{ball.posY:F2}) world=({contact.x:F3},{contact.y:F3})");
        }

        private void CreateTrail()
        {
            trail = GetComponent<TrailRenderer>();
            if (trail == null) trail = gameObject.AddComponent<TrailRenderer>();
            feedbackMaterial = CreateFeedbackMaterial();
            if (feedbackMaterial != null) trail.sharedMaterial = feedbackMaterial;
            defaultTrailGradient = CreateTrailGradient();
            spikeLightTrailGradient = CreateSpikeLightTrailGradient();
            spikeMediumTrailGradient = CreateSpikeMediumTrailGradient();
            spikeStrongTrailGradient = CreateSpikeStrongTrailGradient();
            trail.emitting = false;
            trail.time = slowTrailDuration;
            trail.minVertexDistance = 0.03f;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.numCornerVertices = 3;
            trail.numCapVertices = 3;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1.0f),
                new Keyframe(0.35f, 0.72f),
                new Keyframe(1f, 0.0f));
            trail.widthMultiplier = slowTrailWidth;
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
                const int sampleRate = 22050;
                const float duration = 8.0f; // 16 beats at 120 BPM (0.5s per beat, 0.25s per eighth note)
                int count = Mathf.CeilToInt(duration * sampleRate);
                var samples = new float[count];
                // Root bass frequencies per 2-second bar: D2 -> Bb1 -> C2 -> A1
                float[] bassRoots = { 73.42f, 58.27f, 65.41f, 55.00f };
                // 16-step eighth-note synth lead arpeggio (Hz) in D minor (energetic arcade sports theme)
                float[] leadNotes =
                {
                    293.66f, 349.23f, 440.00f, 587.33f, 523.25f, 440.00f, 349.23f, 440.00f,
                    233.08f, 293.66f, 349.23f, 466.16f, 440.00f, 349.23f, 293.66f, 349.23f,
                    261.63f, 329.63f, 392.00f, 523.25f, 440.00f, 392.00f, 329.63f, 392.00f,
                    220.00f, 277.18f, 329.63f, 440.00f, 523.25f, 440.00f, 349.23f, 329.63f
                };
                var hihatRng = new System.Random(1337);
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)sampleRate;
                    int bar = Mathf.Clamp((int)(t / 2.0f), 0, 3);
                    int eighthIndex = Mathf.Clamp((int)(t / 0.25f), 0, 31);
                    float beatLocal = Mathf.Repeat(t, 0.5f);   // 0..0.5s quarter-note beat
                    float eighthLocal = Mathf.Repeat(t, 0.25f); // 0..0.25s eighth-note step

                    // 1. Punchy 4-on-the-floor kick drum on every beat
                    float kickEnv = Mathf.Exp(-beatLocal * 18f);
                    float kickFreq = Mathf.Lerp(130f, 46f, Mathf.Clamp01(beatLocal * 14f));
                    float kick = Mathf.Sin(2f * Mathf.PI * kickFreq * beatLocal) * kickEnv * 0.50f;

                    // 2. Crisp off-beat hi-hat & backbeat snare
                    int beatIndex = (int)(t / 0.5f);
                    float noise = (float)(hihatRng.NextDouble() * 2.0 - 1.0);
                    float snare = (beatIndex % 2 == 1) ? noise * Mathf.Exp(-beatLocal * 22f) * 0.22f : 0f;
                    float hat = (eighthIndex % 2 == 1) ? noise * Mathf.Exp(-eighthLocal * 42f) * 0.12f : 0f;

                    // 3. Driving octave bassline (root on down-eighth, octave on up-eighth)
                    float bassHz = bassRoots[bar] * ((eighthIndex % 2 == 1) ? 2f : 1f);
                    float bassEnv = Mathf.Exp(-eighthLocal * 6.5f);
                    float bass = (Mathf.Sin(2f * Mathf.PI * bassHz * t) +
                                  0.45f * Mathf.Sin(2f * Mathf.PI * (bassHz * 2f) * t)) * bassEnv * 0.34f;

                    // 4. Arcade sports synth lead melody
                    float leadHz = leadNotes[eighthIndex];
                    float leadEnv = Mathf.Exp(-eighthLocal * 5.2f);
                    float lead = (Mathf.Sin(2f * Mathf.PI * leadHz * t) +
                                  0.35f * Mathf.Sin(2f * Mathf.PI * (leadHz * 2f) * t) +
                                  0.18f * Mathf.Sin(2f * Mathf.PI * (leadHz * 3f) * t)) * leadEnv * 0.26f;

                    samples[i] = Mathf.Clamp((kick + snare + hat + bass + lead) * 0.78f, -1f, 1f);
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
            // Synthesized medieval metallic SFX (iron spike clang, armored bump clink, heavy floor impact).
            spikeClip = CreateProceduralClip("P04 Iron Spike Clang", 0.12f, 820f, 410f, 0.14f);
            bumpClip = CreateProceduralClip("P04 Armored Bump Deflect", 0.08f, 380f, 220f, 0.09f);
            groundClip = CreateProceduralClip("P04 Heavy Iron Ground Impact", 0.17f, 165f, 52f, 0.28f);
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
                Debug.LogWarning("[P04_BALL_FEEDBACK] No compatible sprite shader found; trail/effect material uses Unity's default renderer material.", this);
                return null;
            }
            return new Material(shader) { name = "P04_RuntimeBallFeedbackMaterial" };
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

        private static Gradient CreateSpikeLightTrailGradient()
        {
            // SpikeLight: Crisp bright gold-white aero ribbon (#FFF9D2 -> #FFD24C)
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.98f, 0.82f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.82f, 0.30f), 0.65f),
                    new GradientColorKey(new Color(1.00f, 0.68f, 0.18f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.96f, 0f),
                    new GradientAlphaKey(0.68f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Gradient CreateSpikeMediumTrailGradient()
        {
            // SpikeMedium: Blazing golden-amber laser ribbon (#FFFFFF core -> #FFC828 -> #FF6A00)
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 1.00f, 1.00f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.78f, 0.16f), 0.45f),
                    new GradientColorKey(new Color(1.00f, 0.42f, 0.00f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.98f, 0f),
                    new GradientAlphaKey(0.76f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static Gradient CreateSpikeStrongTrailGradient()
        {
            // SpikeStrong / Spike: Hyper-gold & fiery orange-crimson beam (#FFFFE0 -> #FFB800 -> #FF2A00)
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 1.00f, 0.88f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.72f, 0.00f), 0.42f),
                    new GradientColorKey(new Color(1.00f, 0.16f, 0.00f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1.00f, 0f),
                    new GradientAlphaKey(0.84f, 0.60f),
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
            pointScoreRingRenderer = GetOrCreateChildOverlayRenderer(parentRoot, "P04_PointScoreBurstRing",
                GetOrCreateGoldenCrossRingSprite(), sortingLayerId, baseOrder + 4, out pointScoreRingTransform);

            Transform existingPopup = parentRoot.Find("P04_SpikeSpeedPopup");
            GameObject popupObj = existingPopup != null ? existingPopup.gameObject : new GameObject("P04_SpikeSpeedPopup");
            spikeSpeedPopupTransform = popupObj.transform;
            spikeSpeedPopupTransform.SetParent(parentRoot, false);
            spikeSpeedPopupText = popupObj.GetComponent<TextMesh>();
            if (spikeSpeedPopupText == null) spikeSpeedPopupText = popupObj.AddComponent<TextMesh>();
            spikeSpeedPopupText.text = "135 km/h";
            spikeSpeedPopupText.anchor = TextAnchor.MiddleCenter;
            spikeSpeedPopupText.alignment = TextAlignment.Center;
            spikeSpeedPopupText.fontSize = 36;
            spikeSpeedPopupText.characterSize = 0.085f;
            spikeSpeedPopupText.fontStyle = FontStyle.Bold;
            spikeSpeedPopupText.color = new Color(1f, 0.96f, 0.78f, 1f);
            spikeSpeedPopupRenderer = popupObj.GetComponent<MeshRenderer>();
            if (spikeSpeedPopupRenderer != null)
            {
                spikeSpeedPopupRenderer.sortingLayerID = sortingLayerId;
                spikeSpeedPopupRenderer.sortingOrder = baseOrder + 5;
                spikeSpeedPopupRenderer.enabled = false;
            }

            Transform existingBeam = parentRoot.Find("P04_SpikeLaserBeam");
            GameObject beamObj = existingBeam != null ? existingBeam.gameObject : new GameObject("P04_SpikeLaserBeam");
            beamObj.transform.SetParent(parentRoot, false);
            spikeLaserBeam = beamObj.GetComponent<LineRenderer>();
            if (spikeLaserBeam == null) spikeLaserBeam = beamObj.AddComponent<LineRenderer>();
            if (feedbackMaterial != null) spikeLaserBeam.sharedMaterial = feedbackMaterial;
            spikeLaserBeam.useWorldSpace = true;
            spikeLaserBeam.positionCount = 2;
            spikeLaserBeam.numCapVertices = 3;
            spikeLaserBeam.numCornerVertices = 2;
            spikeLaserBeam.alignment = LineAlignment.View;
            spikeLaserBeam.textureMode = LineTextureMode.Stretch;
            spikeLaserBeam.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.42f),
                new Keyframe(0.65f, 0.85f),
                new Keyframe(1f, 1.00f));
            spikeLaserBeam.widthMultiplier = BeamWidthStrong;
            spikeLaserBeam.colorGradient = spikeStrongTrailGradient;
            spikeLaserBeam.sortingLayerID = sortingLayerId;
            spikeLaserBeam.sortingOrder = baseOrder;
            spikeLaserBeam.enabled = false;
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
                    if (beamDelta.sqrMagnitude > 0.0016f)
                    {
                        // Position tilted elliptical sonic boom ring 35% along the straight spike beam shaft
                        sonicCrescentTransform.position = Vector3.Lerp(spikeOriginWorldPos, currentBallWorldPos, SonicRingShaftFraction);
                        float beamAngleDeg = Mathf.Atan2(beamDelta.y, beamDelta.x) * Mathf.Rad2Deg;
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
                    radialSpeedLinesTransform.localScale = new Vector3(scale / parentScaleX, scale / parentScaleY, 1f);
                    radialSpeedLinesRenderer.color = new Color(0.92f, 0.99f, 1f, alpha * 0.94f);
                }

                if (xSlashSparkRenderer != null && xSlashSparkTransform != null)
                {
                    xSlashSparkRenderer.enabled = midAirBurstTimer > 0f;
                    float scale = Mathf.Lerp(XSlashStartScale, XSlashEndScale, Mathf.Sqrt(progress));
                    xSlashSparkTransform.position = spikeOriginWorldPos;
                    xSlashSparkTransform.localScale = new Vector3(scale / parentScaleX, scale / parentScaleY, 1f);
                    xSlashSparkRenderer.color = new Color(0.84f, 0.98f, 1f, alpha);
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
                    originPuffRingRenderer.color = new Color(0.96f, 0.98f, 1f, alpha * 0.82f);
                }
            }
            else if (originPuffRingRenderer != null)
            {
                originPuffRingRenderer.enabled = false;
            }

            if (spikeBeamTimer > 0f)
            {
                spikeBeamTimer = Mathf.Max(0f, spikeBeamTimer - dt);
                if (spikeLaserBeam != null)
                {
                    spikeLaserBeam.enabled = spikeBeamTimer > 0f;
                    float lifeRatio = Mathf.Clamp01(spikeBeamTimer / SpikeBeamMaxDuration);
                    float baseWidth = activeSpikeTier == 1
                        ? BeamWidthLight
                        : (activeSpikeTier == 2 ? BeamWidthMedium : BeamWidthStrong);
                    spikeLaserBeam.widthMultiplier = baseWidth * Mathf.Lerp(0.25f, 1f, lifeRatio);
                }
            }
            else if (spikeLaserBeam != null)
            {
                spikeLaserBeam.enabled = false;
            }

            if (groundCrossRingTimer > 0f)
            {
                groundCrossRingTimer = Mathf.Max(0f, groundCrossRingTimer - dt);
                float progress = 1f - (groundCrossRingTimer / GroundCrossRingDuration);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - progress), 1.2f);
                if (groundCrossRingRenderer != null && groundCrossRingTransform != null)
                {
                    groundCrossRingRenderer.enabled = groundCrossRingTimer > 0f;
                    float scale = Mathf.Lerp(GroundRingStartScale, GroundRingEndScale, Mathf.Sqrt(progress)) *
                                  lastGroundRingTierScale;
                    groundCrossRingTransform.position = LastImpactWorldPosition + new Vector3(0f, 0.04f, 0f);
                    // Slight perspective flattening on the court floor while keeping bold X-star readability
                    groundCrossRingTransform.localScale = new Vector3(scale / parentScaleX, (scale * 0.72f) / parentScaleY, 1f);
                    groundCrossRingRenderer.color = new Color(1f, 0.95f, 0.76f, alpha);
                }
            }
            else if (groundCrossRingRenderer != null)
            {
                groundCrossRingRenderer.enabled = false;
            }

            if (spikeSpeedPopupTimer > 0f)
            {
                spikeSpeedPopupTimer = Mathf.Max(0f, spikeSpeedPopupTimer - dt);
                float progress = 1f - (spikeSpeedPopupTimer / SpikeSpeedPopupDuration);
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - progress), 1.15f);
                if (spikeSpeedPopupRenderer != null && spikeSpeedPopupTransform != null && spikeSpeedPopupText != null)
                {
                    spikeSpeedPopupRenderer.enabled = spikeSpeedPopupTimer > 0f;
                    spikeSpeedPopupTransform.position = spikeSpeedPopupOrigin +
                                                        new Vector3(0f, Mathf.Lerp(0f, SpikeSpeedPopupRiseHeight, progress), 0f);
                    spikeSpeedPopupTransform.localScale = new Vector3(1f / parentScaleX, 1f / parentScaleY, 1f);
                    spikeSpeedPopupText.color = activeSpikeTier >= 3
                        ? new Color(1f, 0.88f, 0.32f, alpha)
                        : new Color(1f, 0.98f, 0.90f, alpha);
                }
            }
            else if (spikeSpeedPopupRenderer != null)
            {
                spikeSpeedPopupRenderer.enabled = false;
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
            spikeSpeedPopupTimer = 0f;
            if (sonicCrescentRenderer != null) sonicCrescentRenderer.enabled = false;
            if (radialSpeedLinesRenderer != null) radialSpeedLinesRenderer.enabled = false;
            if (xSlashSparkRenderer != null) xSlashSparkRenderer.enabled = false;
            if (originPuffRingRenderer != null) originPuffRingRenderer.enabled = false;
            if (spikeLaserBeam != null) spikeLaserBeam.enabled = false;
            if (groundCrossRingRenderer != null) groundCrossRingRenderer.enabled = false;
            if (spikeSpeedPopupRenderer != null) spikeSpeedPopupRenderer.enabled = false;
            if (trail != null && defaultTrailGradient != null)
                trail.colorGradient = defaultTrailGradient;
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

            Color cyanGlow = new Color(0.68f, 0.95f, 1.00f, 1f);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);

                    // 1. Tilted elliptical Sonic Boom Ring wrapping perpendicular to the spike beam (KIQbyAcdmSM @ t=345s, 385s, 470s)
                    float ex = nx / 0.38f;
                    float ey = ny / 0.84f;
                    float er = Mathf.Sqrt(ex * ex + ey * ey);
                    float ellipseRing = Mathf.Clamp01(1f - Mathf.Abs(er - 1f) / 0.22f);

                    // 2. Curved '(' crescent shockwave edge
                    float arcCenterX = 0.22f;
                    float dx = nx - arcCenterX;
                    float r = Mathf.Sqrt(dx * dx + ny * ny);
                    float angle = Mathf.Atan2(ny, -dx);
                    float angleMask = Mathf.Clamp01(1f - Mathf.Abs(angle) / 1.32f);
                    float thickness = Mathf.Lerp(0.02f, 0.16f, angleMask * angleMask);
                    float distToArc = Mathf.Abs(r - 0.64f);
                    float crescent = angleMask > 0f ? Mathf.Clamp01(1f - distToArc / Mathf.Max(0.005f, thickness)) : 0f;

                    float intensity = Mathf.Clamp01(Mathf.Max(ellipseRing * 0.92f, crescent));
                    float core = Mathf.Pow(intensity, 2.0f);
                    Color c = Color.Lerp(cyanGlow, Color.white, core);
                    c.a = intensity * intensity;
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

            Color cyanRay = new Color(0.62f, 0.94f, 1.00f, 1f);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.5f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.5f);
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    if (r > 1f)
                    {
                        cachedRadialSpeedLinesTexture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    float angle = Mathf.Atan2(ny, nx);
                    // 24 sharp radial speed-lines + bright white core flash
                    float rayWave = Mathf.Abs(Mathf.Cos(angle * 12f));
                    float secondaryRay = Mathf.Abs(Mathf.Cos((angle + 0.13f) * 20f));
                    float raySharp = Mathf.Pow(rayWave, 14f) * Mathf.Clamp01((1f - r) * 1.45f) * Mathf.Clamp01((r - 0.10f) * 6f);
                    float raySub = Mathf.Pow(secondaryRay, 18f) * Mathf.Clamp01((0.88f - r) * 1.6f) * Mathf.Clamp01((r - 0.16f) * 5f);
                    float coreFlash = Mathf.Pow(Mathf.Clamp01(1f - r / 0.28f), 2.0f);
                    float alpha = Mathf.Clamp01(coreFlash + raySharp * 0.95f + raySub * 0.70f);
                    Color c = Color.Lerp(cyanRay, Color.white, Mathf.Clamp01(coreFlash + raySharp * 0.6f));
                    c.a = alpha;
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

            Color cyanSpark = new Color(0.50f, 0.94f, 1.00f, 1f);
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

                    // 4-point diagonal X-slash blades
                    float d1 = Mathf.Abs(nx - ny) * 0.7071f;
                    float d2 = Mathf.Abs(nx + ny) * 0.7071f;
                    float bladeWidth = Mathf.Lerp(0.09f, 0.006f, r);
                    float blade1 = Mathf.Clamp01(1f - d1 / Mathf.Max(0.004f, bladeWidth)) * Mathf.Clamp01(1f - r);
                    float blade2 = Mathf.Clamp01(1f - d2 / Mathf.Max(0.004f, bladeWidth)) * Mathf.Clamp01(1f - r);
                    float centerCore = Mathf.Pow(Mathf.Clamp01(1f - r / 0.25f), 2.2f);
                    float intensity = Mathf.Clamp01(Mathf.Max(blade1, blade2) + centerCore);
                    Color c = Color.Lerp(cyanSpark, Color.white, Mathf.Pow(intensity, 1.5f));
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

                    float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.68f) / 0.14f);
                    float innerHaze = Mathf.Clamp01(1f - r / 0.68f) * 0.22f;
                    float alpha = Mathf.Clamp01(ring * ring * 0.88f + innerHaze);
                    cachedOriginPuffRingTexture.SetPixel(x, y, new Color(0.95f, 0.98f, 1f, alpha));
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

            Color goldenRingColor = new Color(1.00f, 0.80f, 0.16f, 1f);
            Color deepAmberColor = new Color(1.00f, 0.52f, 0.06f, 1f);
            Color hotWhiteColor = new Color(1.00f, 0.99f, 0.92f, 1f);

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

                    // 1. Bold golden shockwave circle at r ~ 0.68
                    float goldenRing = Mathf.Clamp01(1f - Mathf.Abs(r - 0.68f) / 0.11f);
                    // 2. White outer shockwave halo ring at r ~ 0.89
                    float whiteOuterHalo = Mathf.Clamp01(1f - Mathf.Abs(r - 0.89f) / 0.07f);
                    // 3. Bold 4-pointed diagonal X Cross-Star flare right inside the ring
                    float d1 = Mathf.Abs(nx - ny) * 0.7071f;
                    float d2 = Mathf.Abs(nx + ny) * 0.7071f;
                    float crossWidth = Mathf.Lerp(0.13f, 0.012f, Mathf.Clamp01(r / 0.86f));
                    float cross1 = r < 0.88f ? Mathf.Clamp01(1f - d1 / Mathf.Max(0.005f, crossWidth)) * Mathf.Clamp01(1f - r / 0.88f) : 0f;
                    float cross2 = r < 0.88f ? Mathf.Clamp01(1f - d2 / Mathf.Max(0.005f, crossWidth)) * Mathf.Clamp01(1f - r / 0.88f) : 0f;
                    float crossStar = Mathf.Max(cross1, cross2);
                    // 4. White-gold center flash
                    float centerCore = Mathf.Pow(Mathf.Clamp01(1f - r / 0.30f), 2.0f);

                    float totalAlpha = Mathf.Clamp01(goldenRing * 0.95f + whiteOuterHalo * 0.88f + crossStar * 0.98f + centerCore);
                    Color baseCol = Color.Lerp(deepAmberColor, goldenRingColor, Mathf.Clamp01(goldenRing + crossStar * 0.6f));
                    float whiteWeight = Mathf.Clamp01(whiteOuterHalo * 0.92f + Mathf.Pow(crossStar, 2.2f) * 0.85f + centerCore);
                    Color finalCol = Color.Lerp(baseCol, hotWhiteColor, whiteWeight);
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
