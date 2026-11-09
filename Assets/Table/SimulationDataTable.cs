using System;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif
using Native;

namespace Assets.Table
{
    /// <summary>
    /// Consumer-facing entity state record stored in SimulationDataTable.
    /// Preserves exact native simulation fields and component presence flags.
    /// Keyed by native entity ID (0: PLAYER, 3: OPPONENT_1, 7: BALL, 6: NET).
    /// </summary>
    [Serializable]
    public struct SimulationEntityRecord
    {
        public uint entityId;
        public string roleName;
        public int team;
        public bool isGrounded;
        public int facingDirection; // -1: Left, +1: Right

        // Component Presence Flags
        public bool hasPosition;
        public bool hasSize;
        public bool hasJumpUpFrame;
        public bool hasCharacterActionState;
        public bool hasCharacterIntent;
        public bool hasVelocityIntent;
        public bool hasBallTrajectory;
        public bool hasBallGameplayState;

        // Position & Size
        public float posX;
        public float posY;
        public float sizeX;
        public float sizeY;

        // Jump & Action
        public int jumpRemainingFrames;
        public int actionStatus;
        public float actionRemainTime;

        // Intent & Velocity
        public float moveX;
        public int finalIntent;
        public float vx;
        public float vy;

        // Ball Trajectory & Gameplay State
        public int trajectoryType;
        public float ballSpeed;
        public float trajA;
        public float trajB;
        public float trajC;
        public float landingX;
        public int stateFrame;
        public int collisionState;

        public static SimulationEntityRecord FromNativeEntity(NativeSimEntity ent)
        {
            var rec = new SimulationEntityRecord
            {
                entityId = ent.entityId,
                roleName = ent.RoleName,
                team = ent.team,
                isGrounded = ent.isGrounded != 0,
                facingDirection = ent.facingDirection,

                hasPosition = ent.flags.HasPosition,
                hasSize = ent.flags.HasSize,
                hasJumpUpFrame = ent.flags.HasJumpUpFrame,
                hasCharacterActionState = ent.flags.HasCharacterActionState,
                hasCharacterIntent = ent.flags.HasCharacterIntent,
                hasVelocityIntent = ent.flags.HasVelocityIntent,
                hasBallTrajectory = ent.flags.HasBallTrajectory,
                hasBallGameplayState = ent.flags.HasBallGameplayState
            };

            if (rec.hasPosition)
            {
                rec.posX = ent.position.position.x;
                rec.posY = ent.position.position.y;
            }

            if (rec.hasSize)
            {
                rec.sizeX = ent.size.size.x;
                rec.sizeY = ent.size.size.y;
            }

            if (rec.hasJumpUpFrame)
            {
                rec.jumpRemainingFrames = ent.jumpFrame.remainingFrames;
            }

            if (rec.hasCharacterActionState)
            {
                rec.actionStatus = ent.actionState.status;
                rec.actionRemainTime = ent.actionState.remainTime;
            }

            if (rec.hasCharacterIntent)
            {
                rec.moveX = ent.characterIntent.moveX;
                rec.finalIntent = ent.characterIntent.finalIntent;
            }

            if (rec.hasVelocityIntent)
            {
                rec.vx = ent.velocityIntent.vx;
                rec.vy = ent.velocityIntent.vy;
            }

            if (rec.hasBallTrajectory)
            {
                rec.trajectoryType = ent.ballTrajectory.type;
                rec.ballSpeed = ent.ballTrajectory.speed;
                rec.trajA = ent.ballTrajectory.a;
                rec.trajB = ent.ballTrajectory.b;
                rec.trajC = ent.ballTrajectory.c;
            }

            if (rec.hasBallGameplayState)
            {
                rec.landingX = ent.ballGameplayState.landingX;
                rec.stateFrame = ent.ballGameplayState.stateFrame;
                rec.collisionState = ent.ballGameplayState.collisionState;
            }

            return rec;
        }
    }

    /// <summary>
    /// Global match and simulation state metadata stored in SimulationDataTable.
    /// </summary>
    [Serializable]
    public struct SimulationMatchRecord
    {
        public int leftScore;
        public int rightScore;
        public int servingTeam;
        public uint leftServingEntity;
        public uint rightServingEntity;
        public bool finished;
        public int matchWinner;
        public int lastTouchEntity;
        public uint touchCount;

        public static SimulationMatchRecord FromNative(NativeSimMatchState match, NativeSimRallyState rally)
        {
            return new SimulationMatchRecord
            {
                leftScore = match.leftScore,
                rightScore = match.rightScore,
                servingTeam = match.servingTeam,
                leftServingEntity = match.leftServingEntity,
                rightServingEntity = match.rightServingEntity,
                finished = match.finished != 0,
                matchWinner = match.matchWinner,
                lastTouchEntity = (int)rally.lastTouch,
                touchCount = rally.touchCount
            };
        }
    }

    /// <summary>
    /// Central runtime Data Table serving as the authoritative consumer-facing cache
    /// of the latest native simulation snapshot returned by Get_data(Input).
    /// Guarantees atomic, double-buffered snapshot consistency and exposes entity records
    /// keyed strictly by native entity ID (0: PLAYER, 3: OPPONENT_1, 7: BALL, 6: NET).
    /// </summary>
#if UNITY_5_3_OR_NEWER
    public class SimulationDataTable : MonoBehaviour
#else
    public class SimulationDataTable
#endif
    {
        public static SimulationDataTable Instance { get; private set; }

        public const uint ENTITY_PLAYER     = 0;
        public const uint ENTITY_OPPONENT_1 = 3;
        public const uint ENTITY_NET        = 6;
        public const uint ENTITY_BALL       = 7;

        private readonly Dictionary<uint, SimulationEntityRecord> frontEntities = new();
        private readonly Dictionary<uint, SimulationEntityRecord> backEntities = new();
        private readonly object lockObj = new object();

        public ulong CurrentTick { get; private set; }
        public int LastStatus { get; private set; }
        public SimulationMatchRecord MatchRecord { get; private set; }
        public bool IsInitialized { get; private set; }

        public event Action<ulong> OnSnapshotCommitted;

#if UNITY_5_3_OR_NEWER
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Debug.LogError("[SimulationDataTable] Duplicate SimulationDataTable detected; destroying the extra one.", this);
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }
#endif

        public static SimulationDataTable GetOrCreateInstance()
        {
            if (Instance != null) return Instance;

#if UNITY_5_3_OR_NEWER
            var found = UnityEngine.Object.FindAnyObjectByType<SimulationDataTable>();
            if (found != null)
            {
                Instance = found;
                return Instance;
            }

            var go = new GameObject("[SimulationDataTable]");
            Instance = go.AddComponent<SimulationDataTable>();
            return Instance;
#else
            Instance = new SimulationDataTable();
            return Instance;
#endif
        }

        public static void SetInstance(SimulationDataTable customInstance)
        {
            Instance = customInstance;
        }

        public const int EXPECTED_ENTITY_COUNT = 4;

        /// <summary>Number of snapshots rejected by validation since the last reset.</summary>
        public int RejectedCount { get; private set; }
        /// <summary>Reason of the most recent rejection (null if none).</summary>
        public string LastRejectReason { get; private set; }
        /// <summary>Number of committed snapshots since the last reset.</summary>
        public long CommitCount { get; private set; }

        private bool allowTickRestart = true;

        /// <summary>
        /// Validates a raw native snapshot without touching the table. Returns null if valid,
        /// otherwise a human-readable rejection reason.
        /// </summary>
        public static string ValidateSnapshot(NativeSimData snapshot)
        {
            if (snapshot.status != 0)
                return $"native status {snapshot.status} != SIM_OK";
            if (snapshot.entityCount != EXPECTED_ENTITY_COUNT)
                return $"entityCount {snapshot.entityCount} != {EXPECTED_ENTITY_COUNT}";

            bool seen0 = false, seen3 = false, seen6 = false, seen7 = false;
            for (int i = 0; i < EXPECTED_ENTITY_COUNT; i++)
            {
                var e = snapshot.GetEntity(i);
                string role = e.RoleName;
                string expectedRole;
                switch (e.entityId)
                {
                    case ENTITY_PLAYER:
                        if (seen0) return "duplicate entity id 0";
                        seen0 = true; expectedRole = "PLAYER"; break;
                    case ENTITY_OPPONENT_1:
                        if (seen3) return "duplicate entity id 3";
                        seen3 = true; expectedRole = "OPPONENT_1"; break;
                    case ENTITY_NET:
                        if (seen6) return "duplicate entity id 6";
                        seen6 = true; expectedRole = "NET"; break;
                    case ENTITY_BALL:
                        if (seen7) return "duplicate entity id 7";
                        seen7 = true; expectedRole = "BALL"; break;
                    default:
                        return $"unexpected entity id {e.entityId} at index {i}";
                }
                if (role != expectedRole)
                    return $"entity {e.entityId} role '{role}' != '{expectedRole}'";
                // Required components for rendering: every rendered entity needs a position and a size.
                if (!e.flags.HasPosition) return $"entity {e.entityId} ({role}) has no Position component";
                if (!e.flags.HasSize) return $"entity {e.entityId} ({role}) has no Size component";
                if (e.entityId == ENTITY_BALL && !e.flags.HasBallGameplayState)
                    return "BALL has no BallGameplayState component";
                if ((e.entityId == ENTITY_PLAYER || e.entityId == ENTITY_OPPONENT_1) && !e.flags.HasCharacterActionState)
                    return $"entity {e.entityId} has no CharacterActionState component";
                if (float.IsNaN(e.position.position.x) || float.IsNaN(e.position.position.y) ||
                    float.IsInfinity(e.position.position.x) || float.IsInfinity(e.position.position.y))
                    return $"entity {e.entityId} has a non-finite position";
            }
            if (!(seen0 && seen3 && seen6 && seen7)) return "entity id set != {0,3,7,6}";
            return null;
        }

        /// <summary>
        /// Ingests a raw native snapshot into the shadow buffer, validates it,
        /// and atomically swaps/commits it into the consumer-facing front buffer.
        /// Invalid or stale snapshots are rejected as a whole and never modify the table.
        /// </summary>
        public bool UpdateFromNativeSnapshot(NativeSimData snapshot)
        {
            string reason = ValidateSnapshot(snapshot);
            if (reason == null && IsInitialized && !allowTickRestart && snapshot.tick <= CurrentTick)
            {
                reason = $"stale tick {snapshot.tick} <= committed tick {CurrentTick}";
            }
            if (reason != null)
            {
                RejectedCount++;
                LastRejectReason = reason;
#if UNITY_5_3_OR_NEWER
                if (RejectedCount <= 20)
                    Debug.LogError($"[SimulationDataTable] Snapshot rejected (tick {snapshot.tick}): {reason}");
#endif
                return false;
            }

            // Populate back buffer (keyed by entity id, never by array index)
            backEntities.Clear();
            for (int i = 0; i < EXPECTED_ENTITY_COUNT; i++)
            {
                var ent = snapshot.GetEntity(i);
                backEntities[ent.entityId] = SimulationEntityRecord.FromNativeEntity(ent);
            }

            var nextMatch = SimulationMatchRecord.FromNative(snapshot.matchState, snapshot.rallyState);

            // Atomic commit to front buffer
            lock (lockObj)
            {
                frontEntities.Clear();
                foreach (var kvp in backEntities)
                {
                    frontEntities[kvp.Key] = kvp.Value;
                }
                CurrentTick = snapshot.tick;
                LastStatus = snapshot.status;
                MatchRecord = nextMatch;
                IsInitialized = true;
                allowTickRestart = false;
                CommitCount++;
            }

#if UNITY_5_3_OR_NEWER
            // Diagnostics checkpoint (gated)
            if (CurrentTick % 300 == 0 || CurrentTick <= 2)
            {
                Debug.Log($"[DATA_TABLE][CHECKPOINT_COMMIT] Committed Tick={CurrentTick}, Status={LastStatus}, " +
                          $"Entities={frontEntities.Count}, Score={MatchRecord.leftScore}-{MatchRecord.rightScore}");
            }
#endif

            OnSnapshotCommitted?.Invoke(CurrentTick);
            return true;
        }

        public bool TryGetEntity(uint entityId, out SimulationEntityRecord record)
        {
            lock (lockObj)
            {
                return frontEntities.TryGetValue(entityId, out record);
            }
        }

        public SimulationEntityRecord GetEntity(uint entityId)
        {
            lock (lockObj)
            {
                if (frontEntities.TryGetValue(entityId, out var rec))
                {
                    return rec;
                }
                return default;
            }
        }

        public bool HasEntity(uint entityId)
        {
            lock (lockObj)
            {
                return frontEntities.ContainsKey(entityId);
            }
        }

        public int EntityCount
        {
            get
            {
                lock (lockObj)
                {
                    return frontEntities.Count;
                }
            }
        }

        public void ResetTable()
        {
            lock (lockObj)
            {
                frontEntities.Clear();
                backEntities.Clear();
                CurrentTick = 0;
                LastStatus = 0;
                MatchRecord = default;
                IsInitialized = false;
                allowTickRestart = true; // native tick restarts after Sim_Reset / re-initialization
                RejectedCount = 0;
                LastRejectReason = null;
                CommitCount = 0;
            }
        }

#if UNITY_5_3_OR_NEWER
        /// <summary>
        /// Backward-compatibility helper to synchronize character animation states
        /// to existing CharacterStateTable if present.
        /// </summary>
        public void SyncToCharacterStateTable(CharacterStateTable charTable)
        {
            if (charTable == null || !IsInitialized) return;

            // Player 0
            if (TryGetEntity(ENTITY_PLAYER, out var p0))
            {
                var state = MapActionToCharacterState(p0.actionStatus, p0.isGrounded, p0.moveX);
                var dir = (p0.facingDirection < 0) ? Presentation.State.Direction.Left : Presentation.State.Direction.Right;
                charTable.SetState(0, state);
                charTable.SetDirection(0, dir);
                charTable.SetGroundState(0, p0.isGrounded);
            }

            // Opponent 3 (slot 3 in CharacterStateTable)
            if (TryGetEntity(ENTITY_OPPONENT_1, out var opp3) && charTable.Count > 3)
            {
                var state = MapActionToCharacterState(opp3.actionStatus, opp3.isGrounded, opp3.moveX);
                var dir = (opp3.facingDirection < 0) ? Presentation.State.Direction.Left : Presentation.State.Direction.Right;
                charTable.SetState(3, state);
                charTable.SetDirection(3, dir);
                charTable.SetGroundState(3, opp3.isGrounded);
            }
        }
#endif

#if UNITY_5_3_OR_NEWER
        private static Presentation.State.CharacterState MapActionToCharacterState(int actionStatus, bool isGrounded, float moveX)
        {
            switch ((SimActionState)actionStatus)
            {
                case SimActionState.Jump:
                    return Presentation.State.CharacterState.Jump;

                case SimActionState.Spike:
                    return Presentation.State.CharacterState.Spike;

                case SimActionState.SpikeLight:
                    return Presentation.State.CharacterState.SpikeLight;

                case SimActionState.SpikeMedium:
                    return Presentation.State.CharacterState.SpikeMedium;

                case SimActionState.SpikeStrong:
                    return Presentation.State.CharacterState.SpikeStrong;

                case SimActionState.Bump:
                case SimActionState.Set:
                case SimActionState.Serve:
                case SimActionState.Slide:
                    return Presentation.State.CharacterState.Bump;

                case SimActionState.None:
                default:
                    if (isGrounded)
                    {
                        return (Mathf.Abs(moveX) > 0.05f) ? Presentation.State.CharacterState.Move : Presentation.State.CharacterState.Idle;
                    }
                    else
                    {
                        return Presentation.State.CharacterState.Jump;
                    }
            }
        }
#endif
    }
}
