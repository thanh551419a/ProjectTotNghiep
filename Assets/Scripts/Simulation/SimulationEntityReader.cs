using System;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif
using Assets.Table;
using Native;
using Presentation.Feedback;

namespace Simulation
{
    /// <summary>
    /// Entity Data Reader / Render Adapter.
    /// The ONLY writer of its GameObject's Transform. It reads the committed record of ONE native
    /// entity id from the central SimulationDataTable and converts it with the single court
    /// coordinate convention (CourtGeometry: origin = net centre on the floor, 80 px = 1 unit).
    ///
    ///   Characters (0, 3): AABB bottom-centre anchor; non-uniform opaque-bounds scale so the visible
    ///                      character pixels tightly fill the native AABB rectangle
    ///                      (ToWorldLength(r.sizeX), ToWorldLength(r.sizeY)) regardless of PPU/padding.
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

#if UNITY_5_3_OR_NEWER
        private const byte OpaqueAlphaThreshold = 12; // ~0.05 alpha

        public struct OpaqueSpriteMetrics
        {
            public float widthLocal;
            public float heightLocal;
            public float centerLocalX;
            public float centerLocalY;
            public float minLocalY;
            public float maxLocalY;
        }

        private static readonly Dictionary<Sprite, OpaqueSpriteMetrics> CachedCharacterSpriteMetrics =
            new Dictionary<Sprite, OpaqueSpriteMetrics>(64);
        private static readonly Dictionary<Texture2D, Color32[]> CachedTexturePixels =
            new Dictionary<Texture2D, Color32[]>(8);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            CachedCharacterSpriteMetrics.Clear();
            CachedTexturePixels.Clear();
        }
#endif

#if UNITY_5_3_OR_NEWER
        [Header("Entity Configuration")]
        [Tooltip("Native Entity ID: 0 (PLAYER), 3 (OPPONENT_1), 7 (BALL), 6 (NET)")]
        [SerializeField] private uint entityId;

        [Header("Visual References")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private NetView netView;
        [Tooltip("Characters: fallback sprite height (px) if sprite.bounds.size.y is unavailable. Stickman frame = 256 px.")]
        [SerializeField] private float referenceSpriteHeightPx = 256f;

        [Header("Diagnostics")]
        [SerializeField] private bool verboseLogging = false;

        private SimulationDataTable boundTable;
        private ulong lastConsumedTick = ulong.MaxValue;

        public uint EntityId { get => entityId; set => entityId = value; }
        public ulong LastConsumedTick => lastConsumedTick;
        /// <summary>World-space anchor computed from the last consumed table record.</summary>
        public Vector3 LastAnchorWorld { get; private set; }
        public Vector3 LastAppliedPosition { get; private set; }
        public float LastAppliedScale { get; private set; }
        public SimulationEntityRecord LastRecord { get; private set; }
        public SpriteRenderer SpriteRenderer => spriteRenderer;
        public bool IsCharacter => entityId == SimulationDataTable.ENTITY_PLAYER || entityId == SimulationDataTable.ENTITY_OPPONENT_1;

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (netView == null) netView = GetComponent<NetView>();
            if (entityId == SimulationDataTable.ENTITY_BALL && GetComponent<BallFeedbackPresenter>() == null)
                gameObject.AddComponent<BallFeedbackPresenter>();
        }

        private void OnEnable() => Bind();

        private void OnDisable()
        {
            if (boundTable != null) boundTable.OnSnapshotCommitted -= OnSnapshotCommitted;
            boundTable = null;
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
            // Re-apply after the Animator changed the sprite frame this frame (same table record).
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

            if (IsCharacter) ApplyCharacter(record);
            else if (entityId == SimulationDataTable.ENTITY_BALL) ApplyBall(record);
            else if (entityId == SimulationDataTable.ENTITY_NET) ApplyNet(record);

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
            float cx = r.posX + r.sizeX * 0.5f;
            float y = r.entityId == SimulationDataTable.ENTITY_BALL ? r.posY + r.sizeY * 0.5f : r.posY;
            var w = SimToWorld(cx, y);
            return new Vector3(w.x, w.y, 0f);
        }

        private void ApplyCharacter(SimulationEntityRecord r)
        {
            float targetWidthWorld = ToWorldLength(r.sizeX);
            float targetHeightWorld = ToWorldLength(r.sizeY);
            if (spriteRenderer == null || spriteRenderer.sprite == null)
            {
                float fallbackW = targetWidthWorld > 0f ? targetWidthWorld : 1f;
                float fallbackH = targetHeightWorld > 0f ? targetHeightWorld : 1f;
                transform.localScale = new Vector3(fallbackW, fallbackH, 1f);
                LastAppliedScale = fallbackH;
                transform.position = LastAnchorWorld;
                return;
            }

            Sprite sprite = spriteRenderer.sprite;
            OpaqueSpriteMetrics m = GetOrComputeOpaqueSpriteMetrics(sprite, referenceSpriteHeightPx);
            float scaleX = m.widthLocal > 0f ? targetWidthWorld / m.widthLocal : 1f;
            float scaleY = m.heightLocal > 0f ? targetHeightWorld / m.heightLocal : 1f;
            transform.localScale = new Vector3(scaleX, scaleY, 1f);
            LastAppliedScale = scaleY;

            float localX = spriteRenderer.flipX ? -m.centerLocalX : m.centerLocalX;
            float centerOffsetX = localX * scaleX;
            float bottomOffsetY = m.minLocalY * scaleY;
            transform.position = new Vector3(
                LastAnchorWorld.x - centerOffsetX,
                LastAnchorWorld.y - bottomOffsetY,
                LastAnchorWorld.z);
        }

        public static OpaqueSpriteMetrics GetOrComputeOpaqueSpriteMetrics(Sprite sprite, float fallbackHeightPx = 256f)
        {
            if (sprite == null)
            {
                return new OpaqueSpriteMetrics
                {
                    widthLocal = 1f,
                    heightLocal = 1f,
                    centerLocalX = 0f,
                    centerLocalY = 0.5f,
                    minLocalY = 0f,
                    maxLocalY = 1f
                };
            }

            if (CachedCharacterSpriteMetrics.TryGetValue(sprite, out OpaqueSpriteMetrics cached))
            {
                return cached;
            }

            Bounds b = sprite.bounds;
            float ppu = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
            float fallbackLocal = fallbackHeightPx > 0f ? fallbackHeightPx / ppu : 1f;
            OpaqueSpriteMetrics metrics = new OpaqueSpriteMetrics
            {
                widthLocal = b.size.x > 0f ? b.size.x : fallbackLocal,
                heightLocal = b.size.y > 0f ? b.size.y : fallbackLocal,
                centerLocalX = b.center.x,
                centerLocalY = b.center.y,
                minLocalY = b.min.y,
                maxLocalY = b.max.y
            };

            Texture2D tex = sprite.texture;
            if (tex != null && tex.width > 0 && tex.height > 0)
            {
                Color32[] pixels = GetReadableTexturePixels(tex);
                if (pixels != null && pixels.Length == tex.width * tex.height)
                {
                    Rect tr = sprite.textureRect;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(tr.x), 0, tex.width - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(tr.y), 0, tex.height - 1);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt(tr.xMax) - 1, x0, tex.width - 1);
                    int y1 = Mathf.Clamp(Mathf.CeilToInt(tr.yMax) - 1, y0, tex.height - 1);

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
                        Vector2 pivotPx = sprite.pivot;
                        float minLocalX = ((minX - tr.x) - pivotPx.x) / ppu;
                        float maxLocalX = ((maxX + 1 - tr.x) - pivotPx.x) / ppu;
                        float minLocalY = ((minY - tr.y) - pivotPx.y) / ppu;
                        float maxLocalY = ((maxY + 1 - tr.y) - pivotPx.y) / ppu;

                        metrics.widthLocal = Mathf.Max(1f / ppu, maxLocalX - minLocalX);
                        metrics.heightLocal = Mathf.Max(1f / ppu, maxLocalY - minLocalY);
                        metrics.centerLocalX = 0.5f * (minLocalX + maxLocalX);
                        metrics.centerLocalY = 0.5f * (minLocalY + maxLocalY);
                        metrics.minLocalY = minLocalY;
                        metrics.maxLocalY = maxLocalY;
                    }
                }
            }

            CachedCharacterSpriteMetrics[sprite] = metrics;
            return metrics;
        }

        private static Color32[] GetReadableTexturePixels(Texture2D tex)
        {
            if (tex == null) return null;
            if (CachedTexturePixels.TryGetValue(tex, out Color32[] cachedPixels))
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
                CachedTexturePixels[tex] = pixels;
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
