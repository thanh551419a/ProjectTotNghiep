using System;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using Presentation.Feedback;
#endif
using Assets.Table;
using Native;

namespace Simulation
{
    /// <summary>
    /// Entity Data Reader / Render Adapter.
    /// The ONLY writer of its GameObject's Transform. It reads the committed record of ONE native
    /// entity id from the central SimulationDataTable and converts it with the single court
    /// coordinate convention (CourtGeometry: origin = net centre on the floor, 80 px = 1 unit).
    ///
    ///   Characters (0, 3): C++ bottom-left hitbox rectangle (posX, posY, sizeX, sizeY);
    ///                      uniform scale (scaleX = scaleY = uniformScale) calibrated from the
    ///                      measured Idle 0 body height (140 px @ 385 PPU) so Idle 0 body height
    ///                      matches rectHeight; per-frame opaque sprite centre aligned 100% to
    ///                      (rectCenterX, rectCenterY) across flips and animation transitions.
    ///   Ball (7)         : AABB centre anchor; scaled to the native ball size.
    ///   Net (6)          : AABB bottom-centre anchor; NetView builds geometry from the native size.
    ///
    /// Animator parameters and sprite flip are NOT written here: CharacterAnimationBinder and
    /// CharacterDirectionConsumer own them (via CharacterStateTable), so every property has one writer.
    /// Never queries native code and never bypasses the Data Table.
    /// </summary>
#if UNITY_5_3_OR_NEWER
    [DefaultExecutionOrder(100)]
    public class SimulationEntityReader : MonoBehaviour
#else
    public class SimulationEntityReader
#endif
    {
        public const float COURT_NET_X = 1370.0f;
        public const float COURT_FLOOR_Y = 265.0f;
        public const float PIXELS_PER_METER = 80.0f;

        public const float DefaultIdleRefTopYTopDownPx = 83.0f;
        public const float DefaultIdleRefBottomYTopDownPx = 223.0f;
        public const float DefaultIdleRefLeftXPx = 110.0f;
        public const float DefaultIdleRefRightXPx = 190.0f;
        public const float DefaultIdleRefBodyWidthPx = 80.0f;
        public const float DefaultIdleRefBodyHeightPx = 140.0f;
        public const float DefaultPixelsPerUnit = 385.0f;

#if UNITY_5_3_OR_NEWER
        private const byte OpaqueAlphaThreshold = 12; // ~0.05 alpha
        private const int SpriteMetricsCacheCapacity = 32;
        private const int TexturePixelsCacheCapacity = 8;
        private const float DefaultFallbackScale = 1.0f;
        private const float DefaultLocalZScale = 1.0f;
        private const float DefaultWorldZ = 0.0f;
        private const float HalfMultiplier = 0.5f;
        private const float MinOpaquePixelSpan = 1.0f;
        private const float PixelEdgeSpanOffset = 1.0f;

        private const string DebugHitboxObjectName = "DebugHitboxRect";
        private const string DefaultSpriteShaderName = "Sprites/Default";
        private const string DebugHitboxMaterialName = "DebugHitboxRectSharedMaterial";
        private const int DebugRectCornerCount = 4;
        private const float DebugRectLineWidth = 0.025f;
        private const int DebugRectSortingOrder = 600;

        private static readonly Color PlayerDebugRectColor = new Color(0.0f, 0.95f, 0.75f, 0.95f);
        private static readonly Color OpponentDebugRectColor = new Color(1.0f, 0.35f, 0.15f, 0.95f);

        public struct CharacterSpriteMetrics
        {
            public float widthLocal;
            public float heightLocal;
            public float centerLocalX;
            public float flippedCenterLocalX;
            public float centerLocalY;
            public float minLocalY;
            public float maxLocalY;
        }

        private static readonly Dictionary<Sprite, CharacterSpriteMetrics> SpriteMetricsCache =
            new Dictionary<Sprite, CharacterSpriteMetrics>(SpriteMetricsCacheCapacity);
        private static readonly Dictionary<Texture2D, Color32[]> TexturePixelsCache =
            new Dictionary<Texture2D, Color32[]>(TexturePixelsCacheCapacity);
        private static Material cachedDebugRectMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            SpriteMetricsCache.Clear();
            TexturePixelsCache.Clear();
            if (cachedDebugRectMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(cachedDebugRectMaterial);
                else UnityEngine.Object.DestroyImmediate(cachedDebugRectMaterial);
                cachedDebugRectMaterial = null;
            }
        }
#endif

#if UNITY_5_3_OR_NEWER
        [Header("Entity Configuration")]
        [Tooltip("Native Entity ID: 0 (PLAYER), 3 (OPPONENT_1), 7 (BALL), 6 (NET)")]
        [SerializeField] private uint entityId;

        [Header("Visual References")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private NetView netView;
        [Tooltip("Characters: Idle 0 reference body height in pixels (measured y_topDown: 83..223 = 140 px on 256x256 sheet).")]
        [SerializeField] private float idleReferenceBodyHeightPx = DefaultIdleRefBodyHeightPx;

        [Header("Diagnostics")]
        [SerializeField] private bool showDebugHitboxRect = false;
        [SerializeField] private bool verboseLogging = false;

        private SimulationDataTable boundTable;
        private ulong lastConsumedTick = ulong.MaxValue;
        private LineRenderer debugRectLineRenderer;
        private readonly Vector3[] debugRectCorners = new Vector3[DebugRectCornerCount];

        public uint EntityId { get => entityId; set => entityId = value; }
        public ulong LastConsumedTick => lastConsumedTick;
        /// <summary>World-space anchor computed from the last consumed table record.</summary>
        public Vector3 LastAnchorWorld { get; private set; }
        public Vector3 LastAppliedPosition { get; private set; }
        public float LastAppliedScale { get; private set; }
        public SimulationEntityRecord LastRecord { get; private set; }
        public SpriteRenderer SpriteRenderer => spriteRenderer;
        public bool IsBall => entityId == SimulationDataTable.ENTITY_BALL;
        public bool IsNet => entityId == SimulationDataTable.ENTITY_NET;
        public bool IsCharacter => !IsBall && !IsNet;
        public float IdleReferenceBodyHeightPx { get => idleReferenceBodyHeightPx; set => idleReferenceBodyHeightPx = value; }
        public bool ShowDebugHitboxRect { get => showDebugHitboxRect; set => showDebugHitboxRect = value; }
        public LineRenderer DebugHitboxLineRenderer => debugRectLineRenderer;
        public bool HasReferenceCharacterMetrics => spriteRenderer != null && spriteRenderer.sprite != null;
        public CharacterSpriteMetrics ReferenceCharacterMetrics =>
            spriteRenderer != null && spriteRenderer.sprite != null
                ? GetOrComputeOpaqueSpriteMetrics(spriteRenderer.sprite)
                : default;

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (netView == null) netView = GetComponent<NetView>();
            if (IsBall && GetComponent<BallFeedbackPresenter>() == null)
                gameObject.AddComponent<BallFeedbackPresenter>();

            Transform existingDebugChild = transform.Find(DebugHitboxObjectName);
            if (existingDebugChild != null)
            {
                debugRectLineRenderer = existingDebugChild.GetComponent<LineRenderer>();
                if (!showDebugHitboxRect || IsBall || IsNet)
                {
                    if (debugRectLineRenderer != null) debugRectLineRenderer.enabled = false;
                    existingDebugChild.gameObject.SetActive(false);
                }
            }
        }

        private void OnEnable() => Bind();

        private void OnDisable()
        {
            if (boundTable != null) boundTable.OnSnapshotCommitted -= OnSnapshotCommitted;
            boundTable = null;
            if (debugRectLineRenderer != null)
            {
                debugRectLineRenderer.enabled = false;
            }
        }

        private void Bind()
        {
            var table = SimulationDataTable.Instance;
            if (table == boundTable) return;
            if (boundTable != null) boundTable.OnSnapshotCommitted -= OnSnapshotCommitted;
            boundTable = table;
            if (boundTable != null) boundTable.OnSnapshotCommitted += OnSnapshotCommitted;
        }

        private void OnSnapshotCommitted(ulong tick) => ConsumeTableState(tick);

        private void LateUpdate()
        {
            Bind();
            // Re-apply after CharacterDirectionConsumer and Animator updated spriteRenderer this frame (same table record).
            var table = SimulationDataTable.Instance;
            if (table != null && table.IsInitialized) ConsumeTableState(table.CurrentTick);
        }

        /// <summary>Reads the committed table record for this entity and applies it to the visual.</summary>
        public bool ConsumeTableState(ulong tick)
        {
            var table = SimulationDataTable.Instance;
            if (table == null || !table.IsInitialized) return false;
            if (!table.TryGetEntity(entityId, out var record)) return false;
            if (!record.hasPosition || !record.hasSize) return false; // absent component: render nothing new

            bool firstForTick = tick != lastConsumedTick;
            lastConsumedTick = tick;
            LastRecord = record;
            LastAnchorWorld = AnchorWorld(record);

            if (IsBall)
            {
                if (debugRectLineRenderer != null) debugRectLineRenderer.enabled = false;
                ApplyBall(record);
            }
            else if (IsNet)
            {
                if (debugRectLineRenderer != null) debugRectLineRenderer.enabled = false;
                ApplyNet(record);
            }
            else
            {
                ApplyCharacter(in record);
            }

            LastAppliedPosition = transform.position;
            if (verboseLogging && firstForTick)
            {
                // Debug.Log($"[ENTITY_READER] id={entityId} ({record.roleName}) tick={tick} sim=({record.posX:F2},{record.posY:F2}) " +
                //           $"anchor=({LastAnchorWorld.x:F3},{LastAnchorWorld.y:F3}) pos=({transform.position.x:F3},{transform.position.y:F3})");
            }
            return true;
        }

        /// <summary>World anchor of a record: bottom-centre for characters/net, centre for the ball.</summary>
        public static Vector3 AnchorWorld(SimulationEntityRecord r)
        {
            float cx = r.posX + r.sizeX * HalfMultiplier;
            float y = r.entityId == SimulationDataTable.ENTITY_BALL ? r.posY + r.sizeY * HalfMultiplier : r.posY;
            var w = SimToWorld(cx, y);
            return new Vector3(w.x, w.y, DefaultWorldZ);
        }

        private void ApplyCharacter(in SimulationEntityRecord r)
        {
            float rectMinX = (r.posX - COURT_NET_X) / PIXELS_PER_METER;
            float rectMinY = (r.posY - COURT_FLOOR_Y) / PIXELS_PER_METER;
            float rectWidth = r.sizeX / PIXELS_PER_METER;
            float rectHeight = r.sizeY / PIXELS_PER_METER;
            float rectCenterX = rectMinX + HalfMultiplier * rectWidth;
            float rectCenterY = rectMinY + HalfMultiplier * rectHeight;
            float worldZ = LastAnchorWorld.z;

            LastAnchorWorld = new Vector3(rectCenterX, rectMinY, worldZ);

            Sprite currentSprite = spriteRenderer != null ? spriteRenderer.sprite : null;
            float ppu = (currentSprite != null && currentSprite.pixelsPerUnit > 0f)
                ? currentSprite.pixelsPerUnit
                : DefaultPixelsPerUnit;
            float idleBodyHeightLocal = Mathf.Max(MinOpaquePixelSpan, idleReferenceBodyHeightPx) / ppu;
            float uniformScale = rectHeight > 0f && idleBodyHeightLocal > 0f
                ? rectHeight / idleBodyHeightLocal
                : DefaultFallbackScale;

            transform.localScale = new Vector3(uniformScale, uniformScale, DefaultLocalZScale);
            LastAppliedScale = uniformScale;

            if (currentSprite != null)
            {
                CharacterSpriteMetrics m = GetOrComputeOpaqueSpriteMetrics(currentSprite);
                bool isFlipped = (r.facingDirection != 0) ? (r.facingDirection < 0) : spriteRenderer.flipX;
                float activeCenterLocalX = isFlipped ? m.flippedCenterLocalX : m.centerLocalX;
                transform.position = new Vector3(
                    rectCenterX - activeCenterLocalX * uniformScale,
                    rectCenterY - m.centerLocalY * uniformScale,
                    worldZ);
            }
            else
            {
                transform.position = new Vector3(rectCenterX, rectCenterY, worldZ);
            }

            UpdateDebugHitboxRect(rectMinX, rectMinY, rectWidth, rectHeight, worldZ);
        }

        private void UpdateDebugHitboxRect(float rectMinX, float rectMinY, float rectWidth, float rectHeight, float z)
        {
            if (!showDebugHitboxRect || !Application.isPlaying || IsBall || IsNet)
            {
                if (debugRectLineRenderer != null)
                {
                    debugRectLineRenderer.enabled = false;
                }
                return;
            }

            LineRenderer lr = EnsureDebugHitboxLineRenderer();
            if (lr == null) return;

            Color rectColor = entityId == SimulationDataTable.ENTITY_PLAYER
                ? PlayerDebugRectColor
                : OpponentDebugRectColor;
            lr.startColor = rectColor;
            lr.endColor = rectColor;
            lr.enabled = true;

            debugRectCorners[0] = new Vector3(rectMinX, rectMinY, z);
            debugRectCorners[1] = new Vector3(rectMinX + rectWidth, rectMinY, z);
            debugRectCorners[2] = new Vector3(rectMinX + rectWidth, rectMinY + rectHeight, z);
            debugRectCorners[3] = new Vector3(rectMinX, rectMinY + rectHeight, z);
            lr.SetPositions(debugRectCorners);
        }

        private LineRenderer EnsureDebugHitboxLineRenderer()
        {
            if (debugRectLineRenderer != null)
            {
                return debugRectLineRenderer;
            }

            Transform existingChild = transform.Find(DebugHitboxObjectName);
            GameObject childGo;
            if (existingChild != null)
            {
                childGo = existingChild.gameObject;
                debugRectLineRenderer = childGo.GetComponent<LineRenderer>();
                if (debugRectLineRenderer == null)
                {
                    debugRectLineRenderer = childGo.AddComponent<LineRenderer>();
                }
            }
            else
            {
                childGo = new GameObject(DebugHitboxObjectName);
                childGo.transform.SetParent(transform, false);
                debugRectLineRenderer = childGo.AddComponent<LineRenderer>();
            }

            debugRectLineRenderer.useWorldSpace = true;
            debugRectLineRenderer.loop = true;
            debugRectLineRenderer.positionCount = DebugRectCornerCount;
            debugRectLineRenderer.startWidth = DebugRectLineWidth;
            debugRectLineRenderer.endWidth = DebugRectLineWidth;
            debugRectLineRenderer.sortingOrder = DebugRectSortingOrder;
            if (spriteRenderer != null)
            {
                debugRectLineRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            }
            debugRectLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            debugRectLineRenderer.receiveShadows = false;

            Material sharedMat = GetOrCreateDebugRectMaterial();
            if (sharedMat != null)
            {
                debugRectLineRenderer.sharedMaterial = sharedMat;
            }

            return debugRectLineRenderer;
        }

        private static Material GetOrCreateDebugRectMaterial()
        {
            if (cachedDebugRectMaterial != null)
            {
                return cachedDebugRectMaterial;
            }

            Shader shader = Shader.Find(DefaultSpriteShaderName);
            if (shader == null)
            {
                return null;
            }

            cachedDebugRectMaterial = new Material(shader)
            {
                name = DebugHitboxMaterialName,
                hideFlags = HideFlags.HideAndDontSave
            };
            return cachedDebugRectMaterial;
        }

        public static CharacterSpriteMetrics GetOrComputeCharacterSpriteMetrics(
            Sprite sprite,
            float fallbackHeightPx = DefaultIdleRefBodyHeightPx) =>
            GetOrComputeOpaqueSpriteMetrics(sprite, fallbackHeightPx);

        public static CharacterSpriteMetrics GetOrComputeOpaqueSpriteMetrics(
            Sprite sprite,
            float fallbackHeightPx = DefaultIdleRefBodyHeightPx)
        {
            float effectiveFallbackHeightPx = fallbackHeightPx > 0f
                ? fallbackHeightPx
                : DefaultIdleRefBodyHeightPx;

            if (sprite == null)
            {
                float nullSpriteHeightLocal = effectiveFallbackHeightPx / DefaultPixelsPerUnit;
                return new CharacterSpriteMetrics
                {
                    widthLocal = nullSpriteHeightLocal,
                    heightLocal = nullSpriteHeightLocal,
                    centerLocalX = 0f,
                    flippedCenterLocalX = 0f,
                    centerLocalY = 0f,
                    minLocalY = -HalfMultiplier * nullSpriteHeightLocal,
                    maxLocalY = HalfMultiplier * nullSpriteHeightLocal
                };
            }

            if (SpriteMetricsCache.TryGetValue(sprite, out CharacterSpriteMetrics cached))
            {
                return cached;
            }

            Bounds b = sprite.bounds;
            Rect sr = sprite.rect;
            Rect tr = sprite.textureRect;
            Vector2 trOffset = sprite.textureRectOffset;
            Vector2 pivotPx = sprite.pivot;
            float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : DefaultPixelsPerUnit;
            float fallbackHeightLocal = effectiveFallbackHeightPx / ppu;
            float fallbackCenterRectX = trOffset.x + HalfMultiplier * tr.width;
            float fallbackCenterRectY = trOffset.y + HalfMultiplier * tr.height;
            float initialCenterLocalX = tr.width > 0f ? (fallbackCenterRectX - pivotPx.x) / ppu : b.center.x;
            float initialCenterLocalY = tr.height > 0f ? (fallbackCenterRectY - pivotPx.y) / ppu : b.center.y;

            CharacterSpriteMetrics metrics = new CharacterSpriteMetrics
            {
                widthLocal = b.size.x > 0f ? b.size.x : fallbackHeightLocal,
                heightLocal = b.size.y > 0f ? b.size.y : fallbackHeightLocal,
                centerLocalX = initialCenterLocalX,
                flippedCenterLocalX = -initialCenterLocalX,
                centerLocalY = initialCenterLocalY,
                minLocalY = b.size.y > 0f ? b.min.y : -HalfMultiplier * fallbackHeightLocal,
                maxLocalY = b.size.y > 0f ? b.max.y : HalfMultiplier * fallbackHeightLocal
            };

            Texture2D tex = sprite.texture;
            if (tex != null && tex.width > 0 && tex.height > 0)
            {
                Color32[] pixels = GetReadableTexturePixels(tex);
                if (pixels != null && pixels.Length == tex.width * tex.height)
                {
                    bool useSpriteRectDirectly = !sprite.packed && sr.width > 0f && sr.height > 0f;
                    Rect scanRect = useSpriteRectDirectly ? sr : tr;
                    float rectOriginX = useSpriteRectDirectly ? sr.x : (tr.x - trOffset.x);
                    float rectOriginY = useSpriteRectDirectly ? sr.y : (tr.y - trOffset.y);

                    int x0 = Mathf.Clamp(Mathf.FloorToInt(scanRect.x), 0, tex.width - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(scanRect.y), 0, tex.height - 1);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt(scanRect.xMax) - 1, x0, tex.width - 1);
                    int y1 = Mathf.Clamp(Mathf.CeilToInt(scanRect.yMax) - 1, y0, tex.height - 1);

                    int minX = int.MaxValue;
                    int maxX = int.MinValue;
                    int minY = int.MaxValue;
                    int maxY = int.MinValue;
                    int texWidth = tex.width;

                    for (int y = y0; y <= y1; y++)
                    {
                        int rowOffset = y * texWidth;
                        for (int x = x0; x <= x1; x++)
                        {
                            if (pixels[rowOffset + x].a > OpaqueAlphaThreshold)
                            {
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }

                    if (minX <= maxX && minY <= maxY)
                    {
                        float minRectX = minX - rectOriginX;
                        float maxRectX = maxX - rectOriginX;
                        float minRectY = minY - rectOriginY;
                        float maxRectY = maxY - rectOriginY;

                        float centerRectX = HalfMultiplier * (minRectX + maxRectX + PixelEdgeSpanOffset);
                        float centerRectY = HalfMultiplier * (minRectY + maxRectY + PixelEdgeSpanOffset);

                        float minLocalX = (minRectX - pivotPx.x) / ppu;
                        float maxLocalX = (maxRectX + PixelEdgeSpanOffset - pivotPx.x) / ppu;
                        float minLocalY = (minRectY - pivotPx.y) / ppu;
                        float maxLocalY = (maxRectY + PixelEdgeSpanOffset - pivotPx.y) / ppu;
                        float centerLocalX = (centerRectX - pivotPx.x) / ppu;

                        metrics.widthLocal = Mathf.Max(MinOpaquePixelSpan / ppu, maxLocalX - minLocalX);
                        metrics.heightLocal = Mathf.Max(MinOpaquePixelSpan / ppu, maxLocalY - minLocalY);
                        metrics.centerLocalX = centerLocalX;
                        metrics.flippedCenterLocalX = -centerLocalX;
                        metrics.centerLocalY = (centerRectY - pivotPx.y) / ppu;
                        metrics.minLocalY = minLocalY;
                        metrics.maxLocalY = maxLocalY;
                    }
                }
            }

            SpriteMetricsCache[sprite] = metrics;
            return metrics;
        }

        private static Color32[] GetReadableTexturePixels(Texture2D tex)
        {
            if (tex == null) return null;
            if (TexturePixelsCache.TryGetValue(tex, out Color32[] cachedPixels))
            {
                return cachedPixels;
            }

            Color32[] pixels = null;
            if (tex.isReadable)
            {
                try
                {
                    pixels = tex.GetPixels32();
                }
                catch
                {
                    pixels = null;
                }
            }

            if (pixels == null)
            {
                RenderTexture prevActive = RenderTexture.active;
                RenderTexture tmpRt = RenderTexture.GetTemporary(
                    tex.width,
                    tex.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);
                Texture2D readableCopy = null;
                try
                {
                    Graphics.Blit(tex, tmpRt);
                    RenderTexture.active = tmpRt;
                    readableCopy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                    readableCopy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
                    pixels = readableCopy.GetPixels32();
                }
                catch
                {
                    pixels = null;
                }
                finally
                {
                    RenderTexture.active = prevActive;
                    RenderTexture.ReleaseTemporary(tmpRt);
                    if (readableCopy != null)
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(readableCopy);
                        else UnityEngine.Object.DestroyImmediate(readableCopy);
                    }
                }
            }

            if (pixels != null)
            {
                TexturePixelsCache[tex] = pixels;
            }
            return pixels;
        }

        private void ApplyBall(SimulationEntityRecord r)
        {
            if (spriteRenderer == null) { transform.position = LastAnchorWorld; return; }
            if (spriteRenderer.sprite == null || spriteRenderer.sprite.name != BallFeedbackPresenter.IronSphereSpriteName)
            {
                BallFeedbackPresenter.ApplyMedievalIronSphere(spriteRenderer);
            }
            if (spriteRenderer.sprite == null) { transform.position = LastAnchorWorld; return; }
            Bounds b = spriteRenderer.sprite.bounds;
            float s = b.size.y > 0f ? ToWorldLength(r.sizeY) / b.size.y : 1f;
            transform.localScale = new Vector3(s, s, 1f);
            LastAppliedScale = s;
            transform.position = LastAnchorWorld - new Vector3(b.center.x * s, b.center.y * s, 0f);

            if (r.hasBallGameplayState)
            {
                bool dead = r.stateFrame >= 1;           // post-point countdown
                bool belowFloor = r.posY < COURT_FLOOR_Y;
                spriteRenderer.enabled = !(dead && belowFloor);
                Color c = Color.white;
                c.a = dead ? 0.45f : 1f;
                spriteRenderer.color = c;
            }
        }

        private void ApplyNet(SimulationEntityRecord r)
        {
            transform.localScale = Vector3.one;
            transform.position = LastAnchorWorld;
            LastAppliedScale = 1f;
            if (netView != null) netView.SetSize(ToWorldLength(r.sizeX), ToWorldLength(r.sizeY));
        }
#endif

        public static float ToWorldLength(float px) => px / PIXELS_PER_METER;

        /// <summary>
        /// Pure conversion native pixels -> Unity world (PPM = 80, origin = net centre on the floor).
        /// Identical to CourtGeometry.ToWorld; kept dependency-free for the .NET decoder harness.
        /// </summary>
        public static (float x, float y) SimToWorld(float simX, float simY, float simScale = 1f / PIXELS_PER_METER, float offsetX = 0f, float offsetY = 0f)
        {
            float worldX = (simX - COURT_NET_X) * simScale + offsetX;
            float worldY = (simY - COURT_FLOOR_Y) * simScale + offsetY;
            return (worldX, worldY);
        }
    }
}
