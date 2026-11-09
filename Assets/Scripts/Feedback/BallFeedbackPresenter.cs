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
        [SerializeField, Range(0f, 0.2f)] private float arenaBgmVolume = 0.05f;

        private static Sprite cachedIronSphereSprite;
        private static Texture2D cachedIronSphereTexture;

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
                    float nx = (x - (N - 1) * 0.5f) / (N * 0.47f);
                    float ny = (y - (N - 1) * 0.5f) / (N * 0.47f);
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
                return;
            }

            SimulationMatchRecord match = boundTable.MatchRecord;
            bool hasMatchBefore = hasPreviousMatch;
            SimulationMatchRecord matchBefore = previousMatch;
            int acceptedTouchEntity = ProcessAcceptedActionSounds(boundTable, matchBefore.lastTouchEntity);
            bool maxTouchEndedThisTick = hasMatchBefore &&
                                         IsSameSideFourthTouch(matchBefore.touchCount,
                                             matchBefore.lastTouchEntity, acceptedTouchEntity);
            if (hasPreviousBall && IsGroundImpactTransition(previousBall, ball, maxTouchEndedThisTick,
                    CourtGeometry.FloorY))
            {
                EmitGroundImpact(ball, tick);
            }

            Vector3 anchor = SimulationEntityReader.AnchorWorld(ball);
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
                if (impactParticles != null)
                    impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            bool activeBall = ball.stateFrame == -1;
            UpdateTrail(ball, anchor, activeBall);

            previousBall = ball;
            hasPreviousBall = true;
            previousMatch = match;
            hasPreviousMatch = true;
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

        private int ProcessAcceptedActionSounds(SimulationDataTable table, int previousLastTouchEntity)
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
                if (started)
                {
                    SimActionState state = (SimActionState)character.actionStatus;
                    if (IsTouchAction(state) &&
                        (acceptedTouchEntity < 0 || IsSameTeam(previousLastTouchEntity, entityId)))
                        acceptedTouchEntity = entityId;
                    if (state == SimActionState.Bump)
                    {
                        if (PlayOneShot(bumpClip, bumpVolume))
                        {
                            BumpSoundCount++;
                            bumpSoundCountsByEntity[entityId]++;
                        }
                    }
                    else if (state == SimActionState.Spike || state == SimActionState.SpikeLight ||
                             state == SimActionState.SpikeMedium || state == SimActionState.SpikeStrong)
                    {
                        if (PlayOneShot(spikeClip, spikeVolume))
                        {
                            SpikeSoundCount++;
                            spikeSoundCountsByEntity[entityId]++;
                        }
                    }
                }

                previousCharacters[entityId] = character;
                hasPreviousCharacters[entityId] = true;
            }
            return acceptedTouchEntity;
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
            trail.widthMultiplier = Mathf.Lerp(slowTrailWidth, fastTrailWidth, speedWeight);

            // The SimulationEntityReader owns the rendered transform; this observer never writes it.
            if (entityReader == null) entityReader = GetComponent<SimulationEntityReader>();
            if (entityReader != null && entityReader.EntityId != SimulationDataTable.ENTITY_BALL)
                trail.emitting = false;
        }

        private void EmitGroundImpact(SimulationEntityRecord ball, ulong tick)
        {
            Vector3 center = SimulationEntityReader.AnchorWorld(ball);
            Vector3 contact = new Vector3(center.x, CourtGeometry.ToWorldY(CourtGeometry.FloorY), center.z);
            LastImpactWorldPosition = contact;
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
                        velocity = new Vector3(Mathf.Cos(angle) * 0.85f, 0.34f + Mathf.Sin(angle) * 0.58f, 0f),
                        startLifetime = Mathf.Lerp(0.24f, 0.40f, 1f - Mathf.Abs(t - 0.5f) * 2f),
                        startSize = Mathf.Lerp(0.10f, 0.19f, 1f - Mathf.Abs(t - 0.5f) * 2f),
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
            trail.emitting = false;
            trail.time = slowTrailDuration;
            trail.minVertexDistance = 0.04f;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.numCornerVertices = 2;
            trail.numCapVertices = 2;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = slowTrailWidth;
            trail.colorGradient = CreateTrailGradient();
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
                const float duration = 2.0f;
                int count = Mathf.CeilToInt(duration * sampleRate);
                var samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)sampleRate;
                    // Seamless 2.0s medieval arena Fifth drone (D2=73.42Hz, A2=110.0Hz) + subtle pulse
                    float drone = 0.45f * Mathf.Sin(2f * Mathf.PI * 73.5f * t) +
                                  0.30f * Mathf.Sin(2f * Mathf.PI * 110.0f * t);
                    float pulse = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 1.0f * t);
                    samples[i] = Mathf.Clamp(drone * (0.6f + 0.4f * pulse) * 0.35f, -1f, 1f);
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
            // Forged steel-silver wake transitioning to warm ember-orange tail
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.84f, 0.90f, 0.98f), 0f),
                    new GradientColorKey(new Color(0.98f, 0.66f, 0.30f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.85f, 0f),
                    new GradientAlphaKey(0.42f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
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
            if (impactParticles != null)
                impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!resetHistory) return;
            hasProcessedTick = false;
            hasPreviousBall = false;
            hasPreviousMatch = false;
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
