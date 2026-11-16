using UnityEngine;
using Presentation.State;
using Assets.Table;

namespace Presentation.Character
{
    /// <summary>
    /// Binds authoritative C++ simulation state from SimulationDataTable / CharacterStateTable to Animator.
    /// One-shot action animations (Serve, Bump/Set, Spike, SpikeLight, SpikeMedium, SpikeStrong) play ONLY
    /// when C++ commits a non-zero actionState.status rising edge — never from raw button presses or finalIntent.
    /// Also drives Stickman Fall frame playback, Boom Jump court shockwave ellipses, and airborne afterimages.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CharacterAnimationBinder : MonoBehaviour
    {
        private const int ServeStateValue = 90;
        private const CharacterState ServeVisualState = (CharacterState)ServeStateValue;

        // Clip-completion holds (at 60 Hz) after C++ authoritatively enters actionState.status != None.
        private const int ServeHoldTicks = 30;       // 6 frames @ 12 FPS = 0.500s
        private const int SpikeHoldTicks = 30;       // 6 frames @ 12 FPS = 0.500s
        private const int BumpHoldTicks = 25;        // 5 frames @ 12 FPS = 0.417s
        private const int SpikeLightHoldTicks = 27;  // 5 frames @ 11 FPS = 0.455s
        private const int SpikeMediumHoldTicks = 25; // 5 frames @ 12 FPS = 0.417s
        private const int SpikeStrongHoldTicks = 22; // 5 frames @ 14 FPS = 0.357s

        private const float ActionRemainResetEpsilon = 0.001f;
        private const float HorizontalMovePosDeltaMin = 0.05f;
        private const float HorizontalTeleportDeltaMax = 40f;

        // Stickman Fall sprite sheet parameters (Assets/Resources/Stickman/Fall.png: 3328x256, 13 frames of 256x256)
        private const string FallResourcePath = "Stickman/Fall";
        private const int FallFrameCount = 13;
        private const int FallFrameSize = 256;
        private const float FallPixelsPerUnit = 385f;
        private const float FallFrameRate = 24f;
        private static readonly Vector2 StickmanPivot = new Vector2(0.5f, 0.375f);

        // Boom Jump court shockwave ellipses & afterimage ghost pool parameters (RcTdc--AdaI)
        public const int CourtShockwaveRingPoolSize = 2;
        public const int AfterimageGhostPoolSize = 3;
        public const int CourtRingSortingOrder = -95;
        public const float ShockwaveDurationSeconds = 0.48f;
        public const float OuterRingMaxScaleX = 2.8f;
        public const float InnerRingMaxScaleX = 1.9f;
        public const float RingEllipseAspectRatio = 0.36f;
        private const float OuterRingStartScaleX = 0.48f;
        private const float InnerRingStartScaleX = 0.28f;
        private const float AfterimageLifetimeSeconds = 0.18f;
        private const float AfterimageSpawnIntervalSeconds = 0.045f;
        private static readonly Color AfterimageBaseTint = new Color(0.55f, 0.90f, 1.00f, 0.38f);
        private static readonly Color ShockwaveOuterTint = new Color(0.92f, 0.98f, 1.00f, 0.88f);
        private static readonly Color ShockwaveInnerTint = new Color(0.70f, 0.93f, 1.00f, 0.76f);

        private static Sprite[] cachedFallSprites;
        private static bool fallSpritesAttempted;
        private static Sprite cachedShockwaveRingSprite;

        private static readonly int StateHash =
            Animator.StringToHash("State");

        private static readonly int OnTheGroundHash =
            Animator.StringToHash("OnTheGround");

        private static readonly int BumpShortHash =
            Animator.StringToHash("KnightBump");

        private static readonly int ServeShortHash =
            Animator.StringToHash("KnightServe");

        private static readonly int SpikeShortHash =
            Animator.StringToHash("KnightSpike");

        private static readonly int SpikeLightShortHash =
            Animator.StringToHash("SpikeLight");

        private static readonly int SpikeMediumShortHash =
            Animator.StringToHash("SpikeMedium");

        private static readonly int SpikeStrongShortHash =
            Animator.StringToHash("SpikeStrong");

        private static readonly int AirborneStateHash =
            Animator.StringToHash("AirborneState");

        private static readonly int IsFallingHash =
            Animator.StringToHash("IsFalling");

        private CharacterView characterView;
        private SpriteRenderer spriteRenderer;
        private CharacterStateTable stateTable;
        private SimulationDataTable dataTable;
        private SimulationEntityRecord previousSnapshot;
        private bool hasPreviousSnapshot;
        private float latestDeltaPosX;
        private bool pendingActionStart;
        private int lastActionStatus;
        private CharacterState activeAcceptedAction = CharacterState.None;
        private CharacterState visualHoldState = CharacterState.None;
        private int visualHoldTicks;
        private ulong lastCommittedTick;

        private CharacterState boundState =
            CharacterState.None;

        private bool boundOnTheGround;

        private Animator cachedAnimator;
        private bool hasAirborneStateParam;
        private bool hasIsFallingParam;
        private Native.SimAirborneState boundAirborneState = Native.SimAirborneState.Grounded;
        private bool boundIsFalling;

        private float fallElapsedSeconds;

        private Transform vfxPoolRoot;
        private readonly SpriteRenderer[] courtRingRenderers = new SpriteRenderer[CourtShockwaveRingPoolSize];
        private readonly SpriteRenderer[] afterimageRenderers = new SpriteRenderer[AfterimageGhostPoolSize];
        private readonly Vector3[] afterimageWorldPositions = new Vector3[AfterimageGhostPoolSize];
        private readonly Vector3[] afterimageWorldScales = new Vector3[AfterimageGhostPoolSize];
        private readonly float[] afterimageTimers = new float[AfterimageGhostPoolSize];
        private Vector3 shockwaveWorldOrigin;
        private float shockwaveTimer;
        private float afterimageSpawnCooldown;
        private int nextAfterimageIndex;
        private bool poolsInitialized;

        public bool PendingActionStart => pendingActionStart;
        public int ReplayTriggerCount { get; private set; }

        public Native.SimAirborneState CurrentAirborneState { get; private set; } = Native.SimAirborneState.Grounded;
        public bool IsFalling => CurrentAirborneState == Native.SimAirborneState.Fall;
        public bool IsJumpingUp => CurrentAirborneState == Native.SimAirborneState.JumpUp;
        public event System.Action<Native.SimAirborneState> OnAirborneStateChanged;

        public int FallSpriteFrameCount => cachedFallSprites != null ? cachedFallSprites.Length : 0;
        public int CourtRingCount => courtRingRenderers.Length;
        public int AfterimagePoolCount => afterimageRenderers.Length;
        public bool IsBoomJumpShockwaveActive => shockwaveTimer > 0f;

        public SpriteRenderer GetCourtRingRenderer(int index)
        {
            return (index >= 0 && index < courtRingRenderers.Length) ? courtRingRenderers[index] : null;
        }

        public SpriteRenderer GetAfterimageRenderer(int index)
        {
            return (index >= 0 && index < afterimageRenderers.Length) ? afterimageRenderers[index] : null;
        }

        public void Initialize(
            CharacterStateTable stateTable)
        {
            this.stateTable = stateTable;

            if (characterView == null)
            {
                characterView =
                    GetComponent<CharacterView>();
            }

            EnsureVisualPoolsInitialized();
            BindDataTable(true, true);
        }

        private void OnEnable()
        {
            if (characterView == null)
            {
                characterView = GetComponent<CharacterView>();
            }
            EnsureVisualPoolsInitialized();
            BindDataTable(false, true);
        }

        private void OnDisable()
        {
            if (dataTable != null)
            {
                dataTable.OnSnapshotCommitted -= OnSnapshotCommitted;
            }
            ResetTrackingState();
            HideAllVfxRenderers();
        }

        private void ResetTrackingState()
        {
            hasPreviousSnapshot = false;
            latestDeltaPosX = 0f;
            pendingActionStart = false;
            lastActionStatus = 0;
            activeAcceptedAction = CharacterState.None;
            visualHoldState = CharacterState.None;
            visualHoldTicks = 0;
            CurrentAirborneState = Native.SimAirborneState.Grounded;
            boundAirborneState = Native.SimAirborneState.Grounded;
            boundIsFalling = false;
            fallElapsedSeconds = 0f;
            shockwaveTimer = 0f;
            afterimageSpawnCooldown = 0f;
            for (int i = 0; i < afterimageTimers.Length; i++)
            {
                afterimageTimers[i] = 0f;
            }
        }

        private void EnsureVisualPoolsInitialized()
        {
            if (poolsInitialized && vfxPoolRoot != null)
            {
                return;
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                {
                    spriteRenderer = GetComponentInChildren<SpriteRenderer>();
                }
            }

            EnsureFallSpritesLoaded();
            EnsureShockwaveRingSpriteCreated();

            Transform existingRoot = transform.Find("BoomJumpVfxPool");
            if (existingRoot != null)
            {
                vfxPoolRoot = existingRoot;
            }
            else
            {
                GameObject rootObj = new GameObject("BoomJumpVfxPool");
                rootObj.layer = gameObject.layer;
                vfxPoolRoot = rootObj.transform;
                vfxPoolRoot.SetParent(transform, false);
                vfxPoolRoot.localPosition = Vector3.zero;
                vfxPoolRoot.localRotation = Quaternion.identity;
                vfxPoolRoot.localScale = Vector3.one;
            }

            for (int i = 0; i < CourtShockwaveRingPoolSize; i++)
            {
                string ringName = "CourtShockwaveRing_" + i;
                Transform ringTr = vfxPoolRoot.Find(ringName);
                SpriteRenderer ringSr;
                if (ringTr == null)
                {
                    GameObject ringObj = new GameObject(ringName, typeof(SpriteRenderer));
                    ringObj.layer = gameObject.layer;
                    ringTr = ringObj.transform;
                    ringTr.SetParent(vfxPoolRoot, false);
                    ringSr = ringObj.GetComponent<SpriteRenderer>();
                }
                else
                {
                    ringSr = ringTr.GetComponent<SpriteRenderer>();
                }

                ringSr.sprite = cachedShockwaveRingSprite;
                ringSr.sortingOrder = CourtRingSortingOrder;
                if (spriteRenderer != null)
                {
                    ringSr.sortingLayerID = spriteRenderer.sortingLayerID;
                }
                ringSr.color = Color.clear;
                ringSr.enabled = false;
                courtRingRenderers[i] = ringSr;
            }

            int afterimageSortOrder = spriteRenderer != null ? (spriteRenderer.sortingOrder - 1) : -1;
            for (int i = 0; i < AfterimageGhostPoolSize; i++)
            {
                string ghostName = "AfterimageGhost_" + i;
                Transform ghostTr = vfxPoolRoot.Find(ghostName);
                SpriteRenderer ghostSr;
                if (ghostTr == null)
                {
                    GameObject ghostObj = new GameObject(ghostName, typeof(SpriteRenderer));
                    ghostObj.layer = gameObject.layer;
                    ghostTr = ghostObj.transform;
                    ghostTr.SetParent(vfxPoolRoot, false);
                    ghostSr = ghostObj.GetComponent<SpriteRenderer>();
                }
                else
                {
                    ghostSr = ghostTr.GetComponent<SpriteRenderer>();
                }

                ghostSr.sortingOrder = afterimageSortOrder;
                if (spriteRenderer != null)
                {
                    ghostSr.sortingLayerID = spriteRenderer.sortingLayerID;
                }
                ghostSr.color = Color.clear;
                ghostSr.enabled = false;
                afterimageRenderers[i] = ghostSr;
                afterimageTimers[i] = 0f;
            }

            poolsInitialized = true;
        }

        private static void EnsureFallSpritesLoaded()
        {
            if (fallSpritesAttempted && cachedFallSprites != null && cachedFallSprites.Length == FallFrameCount && cachedFallSprites[0] != null)
            {
                return;
            }
            if (fallSpritesAttempted)
            {
                return;
            }
            fallSpritesAttempted = true;

            Sprite[] imported = Resources.LoadAll<Sprite>(FallResourcePath);
            if (imported != null && imported.Length >= FallFrameCount)
            {
                cachedFallSprites = imported;
                return;
            }

            Texture2D fallTex = Resources.Load<Texture2D>(FallResourcePath);
            if (fallTex == null)
            {
                return;
            }

            cachedFallSprites = new Sprite[FallFrameCount];
            int frameWidth = Mathf.Min(FallFrameSize, fallTex.width / FallFrameCount);
            int frameHeight = Mathf.Min(FallFrameSize, fallTex.height);
            for (int i = 0; i < FallFrameCount; i++)
            {
                Rect rect = new Rect(i * frameWidth, 0f, frameWidth, frameHeight);
                Sprite s = Sprite.Create(fallTex, rect, StickmanPivot, FallPixelsPerUnit);
                s.name = "FALL_" + i;
                cachedFallSprites[i] = s;
            }
        }

        private static void EnsureShockwaveRingSpriteCreated()
        {
            if (cachedShockwaveRingSprite != null)
            {
                return;
            }

            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "BoomJumpShockwaveRingTex";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32[] pixels = new Color32[size * size];
            float center = size * 0.5f;
            float maxRadius = center - 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - center) / maxRadius;
                    float dy = (y + 0.5f - center) / maxRadius;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > 1f)
                    {
                        pixels[y * size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // Crisp glowing outer ring + soft translucent inner aura disc
                    float ringDist = Mathf.Abs(r - 0.86f);
                    float outerRingAlpha = Mathf.Clamp01(1f - ringDist / 0.11f);
                    outerRingAlpha = outerRingAlpha * outerRingAlpha;

                    float secondaryRingDist = Mathf.Abs(r - 0.64f);
                    float secondaryAlpha = Mathf.Clamp01(1f - secondaryRingDist / 0.08f) * 0.45f;

                    float innerDiscAlpha = Mathf.Clamp01(1f - r / 0.85f) * 0.22f;
                    float alpha = Mathf.Clamp01(Mathf.Max(outerRingAlpha, Mathf.Max(secondaryAlpha, innerDiscAlpha)));
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            cachedShockwaveRingSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            cachedShockwaveRingSprite.name = "BoomJumpShockwaveRingSprite";
        }

        private void HideAllVfxRenderers()
        {
            for (int i = 0; i < courtRingRenderers.Length; i++)
            {
                if (courtRingRenderers[i] != null)
                {
                    courtRingRenderers[i].enabled = false;
                }
            }

            for (int i = 0; i < afterimageRenderers.Length; i++)
            {
                if (afterimageRenderers[i] != null)
                {
                    afterimageRenderers[i].enabled = false;
                }
                afterimageTimers[i] = 0f;
            }
        }

        private void TriggerBoomJumpShockwave()
        {
            EnsureVisualPoolsInitialized();

            float floorY = Simulation.CourtGeometry.ToWorldY(Simulation.CourtGeometry.FloorY);
            shockwaveWorldOrigin = new Vector3(transform.position.x, floorY, transform.position.z);
            shockwaveTimer = ShockwaveDurationSeconds;
            UpdateShockwaveRings(0f);

            // Seed first airborne afterimage immediately on takeoff
            SpawnAfterimageGhost();
            afterimageSpawnCooldown = AfterimageSpawnIntervalSeconds;
        }

        private void SpawnAfterimageGhost()
        {
            if (!poolsInitialized || spriteRenderer == null || spriteRenderer.sprite == null)
            {
                return;
            }

            int idx = nextAfterimageIndex;
            nextAfterimageIndex = (nextAfterimageIndex + 1) % AfterimageGhostPoolSize;

            SpriteRenderer ghost = afterimageRenderers[idx];
            if (ghost == null)
            {
                return;
            }

            Transform srTransform = spriteRenderer.transform;
            afterimageWorldPositions[idx] = srTransform.position;
            afterimageWorldScales[idx] = srTransform.lossyScale;
            afterimageTimers[idx] = AfterimageLifetimeSeconds;

            ghost.sprite = spriteRenderer.sprite;
            ghost.flipX = spriteRenderer.flipX;
            ghost.flipY = spriteRenderer.flipY;
            ghost.sortingOrder = spriteRenderer.sortingOrder - 1;
            ghost.color = AfterimageBaseTint;
            ghost.transform.position = afterimageWorldPositions[idx];
            ApplyWorldScale(ghost.transform, afterimageWorldScales[idx]);
            ghost.enabled = true;
        }

        private void UpdateShockwaveRings(float normalizedProgress)
        {
            float t = Mathf.Clamp01(normalizedProgress);
            float easeOut = 1f - (1f - t) * (1f - t);
            float fade = Mathf.Clamp01(1f - t * t);

            if (courtRingRenderers[0] != null)
            {
                float scaleX = Mathf.Lerp(OuterRingStartScaleX, OuterRingMaxScaleX, easeOut);
                float scaleY = scaleX * RingEllipseAspectRatio;
                SpriteRenderer r0 = courtRingRenderers[0];
                r0.transform.position = shockwaveWorldOrigin;
                ApplyWorldScale(r0.transform, new Vector3(scaleX, scaleY, 1f));
                Color c0 = ShockwaveOuterTint;
                c0.a *= fade;
                r0.color = c0;
                r0.enabled = shockwaveTimer > 0f;
            }

            if (courtRingRenderers[1] != null)
            {
                float innerT = Mathf.Clamp01(t * 1.12f);
                float innerEase = 1f - (1f - innerT) * (1f - innerT);
                float scaleX = Mathf.Lerp(InnerRingStartScaleX, InnerRingMaxScaleX, innerEase);
                float scaleY = scaleX * RingEllipseAspectRatio;
                SpriteRenderer r1 = courtRingRenderers[1];
                r1.transform.position = shockwaveWorldOrigin;
                ApplyWorldScale(r1.transform, new Vector3(scaleX, scaleY, 1f));
                Color c1 = ShockwaveInnerTint;
                c1.a *= fade;
                r1.color = c1;
                r1.enabled = shockwaveTimer > 0f;
            }
        }

        private void ApplyWorldScale(Transform target, Vector3 desiredWorldScale)
        {
            Vector3 parentScale = transform.lossyScale;
            float px = Mathf.Abs(parentScale.x) > 0.001f ? parentScale.x : 1f;
            float py = Mathf.Abs(parentScale.y) > 0.001f ? parentScale.y : 1f;
            target.localScale = new Vector3(desiredWorldScale.x / px, desiredWorldScale.y / py, 1f);
        }

        private void EnsureAnimatorParametersCached(Animator animator)
        {
            if (animator == cachedAnimator)
            {
                return;
            }

            cachedAnimator = animator;
            hasAirborneStateParam = false;
            hasIsFallingParam = false;
            boundAirborneState = Native.SimAirborneState.Grounded;
            boundIsFalling = false;

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter param = parameters[i];
                if (param.nameHash == AirborneStateHash && param.type == AnimatorControllerParameterType.Int)
                {
                    hasAirborneStateParam = true;
                }
                else if (param.nameHash == IsFallingHash && param.type == AnimatorControllerParameterType.Bool)
                {
                    hasIsFallingParam = true;
                }
            }
        }

        private void SyncAirborneAnimatorParameters()
        {
            if (characterView == null || characterView.Animator == null)
            {
                return;
            }

            Animator animator = characterView.Animator;
            EnsureAnimatorParametersCached(animator);

            if (hasAirborneStateParam && CurrentAirborneState != boundAirborneState)
            {
                animator.SetInteger(AirborneStateHash, (int)CurrentAirborneState);
                boundAirborneState = CurrentAirborneState;
            }

            bool falling = IsFalling;
            if (hasIsFallingParam && falling != boundIsFalling)
            {
                animator.SetBool(IsFallingHash, falling);
                boundIsFalling = falling;
            }
        }

        private void BindDataTable(bool forceReset, bool allowSceneLookup)
        {
            SimulationDataTable candidate = SimulationDataTable.Instance;
            if (candidate == null && allowSceneLookup)
            {
                candidate = FindAnyObjectByType<SimulationDataTable>(FindObjectsInactive.Include);
            }

            if (candidate == dataTable && !forceReset)
            {
                return;
            }

            if (dataTable != null)
            {
                dataTable.OnSnapshotCommitted -= OnSnapshotCommitted;
            }

            dataTable = candidate;
            ResetTrackingState();
            lastCommittedTick = 0;

            if (isActiveAndEnabled && dataTable != null)
            {
                dataTable.OnSnapshotCommitted -= OnSnapshotCommitted;
                dataTable.OnSnapshotCommitted += OnSnapshotCommitted;
            }
        }

        private bool TryResolveEntity(out uint entityId, out SimulationEntityRecord record)
        {
            entityId = (uint)characterView.Id;
            if (dataTable.TryGetEntity(entityId, out record))
            {
                return true;
            }

            if (characterView.Id == 1 && dataTable.TryGetEntity(SimulationDataTable.ENTITY_OPPONENT_1, out record))
            {
                entityId = SimulationDataTable.ENTITY_OPPONENT_1;
                return true;
            }

            return false;
        }

        private void OnSnapshotCommitted(ulong tick)
        {
            if (characterView == null || dataTable == null ||
                !TryResolveEntity(out uint entityId, out SimulationEntityRecord current))
            {
                return;
            }

            if (hasPreviousSnapshot && tick == lastCommittedTick)
            {
                return;
            }

            if (hasPreviousSnapshot && tick < lastCommittedTick)
            {
                ResetTrackingState();
            }

            lastActionStatus = current.hasCharacterActionState ? current.actionStatus : 0;

            Native.SimAirborneState nextAirborneState = dataTable.GetEntityAirborneState(entityId);
            if (nextAirborneState != CurrentAirborneState)
            {
                CurrentAirborneState = nextAirborneState;
                if (CurrentAirborneState == Native.SimAirborneState.Fall)
                {
                    fallElapsedSeconds = 0f;
                }
                else if (CurrentAirborneState == Native.SimAirborneState.JumpUp)
                {
                    TriggerBoomJumpShockwave();
                }

                OnAirborneStateChanged?.Invoke(CurrentAirborneState);
            }
            SyncAirborneAnimatorParameters();

            if (visualHoldTicks > 0)
            {
                visualHoldTicks--;
                if (visualHoldTicks == 0)
                {
                    visualHoldState = CharacterState.None;
                }
            }

            latestDeltaPosX = (hasPreviousSnapshot && current.hasPosition && previousSnapshot.hasPosition)
                ? (current.posX - previousSnapshot.posX)
                : 0f;

            bool isPreServeWait = dataTable.TryGetEntity(SimulationDataTable.ENTITY_BALL, out SimulationEntityRecord ball) &&
                                  ball.hasBallGameplayState &&
                                  ball.stateFrame == 0;

            CharacterState mappedAction = current.hasCharacterActionState
                ? MapActionStatusToCharacterState(current.actionStatus)
                : CharacterState.None;
            activeAcceptedAction = mappedAction;

            int prevStatus = (hasPreviousSnapshot && previousSnapshot.hasCharacterActionState)
                ? previousSnapshot.actionStatus
                : 0;
            float prevRemain = (hasPreviousSnapshot && previousSnapshot.hasCharacterActionState)
                ? previousSnapshot.actionRemainTime
                : 0f;

            if (IsOneShotAction(mappedAction))
            {
                bool isRisingOrReset = !hasPreviousSnapshot ||
                                       prevStatus != current.actionStatus ||
                                       current.actionRemainTime > prevRemain + ActionRemainResetEpsilon;
                if (isRisingOrReset)
                {
                    if (hasPreviousSnapshot)
                    {
                        pendingActionStart = true;
                    }
                    visualHoldState = mappedAction;
                    visualHoldTicks = GetOneShotHoldTicks(mappedAction);
                }
            }
            else if (isPreServeWait)
            {
                visualHoldState = CharacterState.None;
                visualHoldTicks = 0;
            }

            previousSnapshot = current;
            hasPreviousSnapshot = true;
            lastCommittedTick = tick;
        }

        private static CharacterState MapActionStatusToCharacterState(int actionStatus)
        {
            switch ((Native.SimActionState)actionStatus)
            {
                case Native.SimActionState.Spike: return CharacterState.Spike;
                case Native.SimActionState.SpikeLight: return CharacterState.SpikeLight;
                case Native.SimActionState.SpikeMedium: return CharacterState.SpikeMedium;
                case Native.SimActionState.SpikeStrong: return CharacterState.SpikeStrong;
                case Native.SimActionState.Serve: return ServeVisualState;
                case Native.SimActionState.Bump:
                case Native.SimActionState.Set:
                    return CharacterState.Bump;
                default:
                    return CharacterState.None;
            }
        }

        private static int GetOneShotHoldTicks(CharacterState state)
        {
            switch (state)
            {
                case ServeVisualState: return ServeHoldTicks;
                case CharacterState.Spike: return SpikeHoldTicks;
                case CharacterState.Bump: return BumpHoldTicks;
                case CharacterState.SpikeLight: return SpikeLightHoldTicks;
                case CharacterState.SpikeMedium: return SpikeMediumHoldTicks;
                case CharacterState.SpikeStrong: return SpikeStrongHoldTicks;
                default: return 0;
            }
        }

        private static bool IsOneShotAction(CharacterState state)
        {
            return state == CharacterState.Bump || state == ServeVisualState ||
                   state == CharacterState.Spike || state == CharacterState.SpikeLight ||
                   state == CharacterState.SpikeMedium || state == CharacterState.SpikeStrong;
        }

        private static bool IsSpikeAction(CharacterState state)
        {
            return state == CharacterState.Spike || state == CharacterState.SpikeLight ||
                   state == CharacterState.SpikeMedium || state == CharacterState.SpikeStrong;
        }

        private static int GetActionShortNameHash(CharacterState state)
        {
            switch (state)
            {
                case CharacterState.Bump: return BumpShortHash;
                case ServeVisualState: return ServeShortHash;
                case CharacterState.Spike: return SpikeShortHash;
                case CharacterState.SpikeLight: return SpikeLightShortHash;
                case CharacterState.SpikeMedium: return SpikeMediumShortHash;
                case CharacterState.SpikeStrong: return SpikeStrongShortHash;
                default: return 0;
            }
        }

        public void Update()
        {
            if (SimulationDataTable.Instance != dataTable && SimulationDataTable.Instance != null)
            {
                BindDataTable(false, false);
            }

            if (stateTable == null ||
                characterView == null ||
                characterView.Animator == null)
            {
                return;
            }

            int characterId = characterView.Id;

            bool onTheGround =
                stateTable
                    .GetGroundState(characterId)
                    .Value;

            CharacterState tableState =
                stateTable
                    .GetState(characterId)
                    .State;

            CharacterState state;
            if (IsOneShotAction(activeAcceptedAction))
            {
                state = activeAcceptedAction;
            }
            else if (visualHoldTicks > 0 && IsOneShotAction(visualHoldState))
            {
                state = visualHoldState;
            }
            else if (!onTheGround)
            {
                state = CharacterState.Jump;
            }
            else
            {
                bool movingHorizontally = tableState == CharacterState.Move ||
                    (Mathf.Abs(latestDeltaPosX) >= HorizontalMovePosDeltaMin &&
                     Mathf.Abs(latestDeltaPosX) <= HorizontalTeleportDeltaMax);
                state = movingHorizontally ? CharacterState.Move : CharacterState.Idle;
            }

            bool stateChanged = state != boundState;
            if (stateChanged)
            {
                characterView.Animator.SetInteger(
                    StateHash,
                    (int)state
                );

                boundState = state;
            }

            if (onTheGround != boundOnTheGround)
            {
                characterView.Animator.SetBool(
                    OnTheGroundHash,
                    onTheGround
                );

                boundOnTheGround = onTheGround;
            }

            SyncAirborneAnimatorParameters();

            if (IsOneShotAction(state) && (stateChanged || pendingActionStart))
            {
                Animator animator = characterView.Animator;
                int targetShortHash = GetActionShortNameHash(state);
                AnimatorStateInfo currentAnimation = animator.GetCurrentAnimatorStateInfo(0);
                if (currentAnimation.shortNameHash == targetShortHash)
                {
                    if (pendingActionStart)
                    {
                        animator.Play(currentAnimation.fullPathHash, 0, 0f);
                        ReplayTriggerCount++;
                    }
                }
                else if (targetShortHash != 0)
                {
                    animator.Play(targetShortHash, 0, 0f);
                    if (!stateChanged && pendingActionStart)
                    {
                        ReplayTriggerCount++;
                    }
                }
            }

            pendingActionStart = false;
        }

        public void LateUpdate()
        {
            EnsureVisualPoolsInitialized();

            float dt = Time.deltaTime > 0.0001f ? Time.deltaTime : (1f / 60f);

            // 1. Stickman Fall animation override when descending with no active mid-air action
            if (CurrentAirborneState == Native.SimAirborneState.Fall &&
                lastActionStatus == 0 &&
                !IsOneShotAction(activeAcceptedAction) &&
                visualHoldTicks <= 0 &&
                spriteRenderer != null &&
                cachedFallSprites != null &&
                cachedFallSprites.Length > 0)
            {
                int frameIndex = Mathf.Clamp((int)(fallElapsedSeconds * FallFrameRate), 0, cachedFallSprites.Length - 1);
                Sprite fallSprite = cachedFallSprites[frameIndex];
                if (fallSprite != null)
                {
                    spriteRenderer.sprite = fallSprite;
                }
                fallElapsedSeconds += dt;
            }

            // 2. Boom Jump court shockwave ellipses expansion & fade
            if (shockwaveTimer > 0f)
            {
                shockwaveTimer = Mathf.Max(0f, shockwaveTimer - dt);
                float progress = 1f - (shockwaveTimer / ShockwaveDurationSeconds);
                UpdateShockwaveRings(progress);
            }

            // 3. Airborne JumpUp / mid-air Spike cyan-white afterimage ghosts
            bool shouldEmitAfterimage =
                CurrentAirborneState == Native.SimAirborneState.JumpUp ||
                (CurrentAirborneState != Native.SimAirborneState.Grounded &&
                 (IsSpikeAction(activeAcceptedAction) || (visualHoldTicks > 0 && IsSpikeAction(visualHoldState))));

            if (shouldEmitAfterimage)
            {
                afterimageSpawnCooldown -= dt;
                if (afterimageSpawnCooldown <= 0f)
                {
                    SpawnAfterimageGhost();
                    afterimageSpawnCooldown = AfterimageSpawnIntervalSeconds;
                }
            }

            for (int i = 0; i < AfterimageGhostPoolSize; i++)
            {
                if (afterimageTimers[i] <= 0f)
                {
                    continue;
                }

                afterimageTimers[i] = Mathf.Max(0f, afterimageTimers[i] - dt);
                SpriteRenderer ghost = afterimageRenderers[i];
                if (ghost == null)
                {
                    continue;
                }

                if (afterimageTimers[i] <= 0f)
                {
                    ghost.enabled = false;
                }
                else
                {
                    float normalizedLife = afterimageTimers[i] / AfterimageLifetimeSeconds;
                    Color c = AfterimageBaseTint;
                    c.a *= normalizedLife;
                    ghost.color = c;
                    ghost.transform.position = afterimageWorldPositions[i];
                    ApplyWorldScale(ghost.transform, afterimageWorldScales[i]);
                }
            }
        }
    }
}
