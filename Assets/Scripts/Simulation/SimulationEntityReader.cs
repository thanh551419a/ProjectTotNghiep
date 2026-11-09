using System;
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
    ///   Characters (0, 3): AABB bottom-centre anchor; uniform scale so the reference sprite height
    ///                      equals the native AABB height (animation frames never change the scale).
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
        [Header("Entity Configuration")]
        [Tooltip("Native Entity ID: 0 (PLAYER), 3 (OPPONENT_1), 7 (BALL), 6 (NET)")]
        [SerializeField] private uint entityId;

        [Header("Visual References")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private NetView netView;
        [Tooltip("Characters: sprite height (px) that is mapped onto the native AABB height. Knight idle frame = 39 px.")]
        [SerializeField] private float referenceSpriteHeightPx = 39f;

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
                Debug.Log($"[ENTITY_READER] id={entityId} ({record.roleName}) tick={tick} sim=({record.posX:F2},{record.posY:F2}) " +
                          $"anchor=({LastAnchorWorld.x:F3},{LastAnchorWorld.y:F3}) pos=({transform.position.x:F3},{transform.position.y:F3})");
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
            if (spriteRenderer == null || spriteRenderer.sprite == null) { transform.position = LastAnchorWorld; return; }
            Sprite sprite = spriteRenderer.sprite;
            float refHeightUnits = referenceSpriteHeightPx / sprite.pixelsPerUnit;
            float s = ToWorldLength(r.sizeY) / refHeightUnits;
            transform.localScale = new Vector3(s, s, 1f);
            LastAppliedScale = s;

            Bounds b = sprite.bounds; // local, unflipped
            float localX = spriteRenderer.flipX ? -b.center.x : b.center.x;
            transform.position = LastAnchorWorld - new Vector3(localX * s, b.min.y * s, 0f);
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
