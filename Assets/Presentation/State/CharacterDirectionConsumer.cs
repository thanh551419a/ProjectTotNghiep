using System;
using Assets.Table;
using Presentation.State;
using UnityEngine;

namespace Presentation.Character
{
    /// <summary>
    /// Synchronizes SpriteRenderer.flipX in the exact same snapshot-commit phase as SimulationDataTable
    /// (zero 1-frame / intra-frame lag relative to posX and SimAnimId), while preserving the legacy
    /// CharacterStateTable fallback in Update() when SimulationDataTable is absent or lacks the entity.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class CharacterDirectionConsumer : MonoBehaviour
    {
        private const int PlayerViewId = 0;
        private const int LegacyOpponentViewId = 1;
        private const int Opponent1ViewId = 3;
        private const uint PlayerEntityId = SimulationDataTable.ENTITY_PLAYER;
        private const uint Teammate1EntityId = 1u;
        private const uint Opponent1EntityId = 3u;
        private const int NeutralFacingDirection = 0;

        private CharacterView characterView;
        private SpriteRenderer spriteRenderer;
        private CharacterStateTable stateTable;
        private SimulationDataTable boundDataTable;
        private Action<ulong> snapshotCommittedHandler;

        private void Awake()
        {
            snapshotCommittedHandler = HandleSnapshotCommitted;
            CacheComponents();
        }

        private void OnEnable()
        {
            if (snapshotCommittedHandler == null)
            {
                snapshotCommittedHandler = HandleSnapshotCommitted;
            }
            CacheComponents();
            BindDataTable(GetActiveDataTable());
        }

        private void Start()
        {
            CacheComponents();
            BindDataTable(GetActiveDataTable());
        }

        private void OnDisable()
        {
            UnbindDataTable();
        }

        public void Initialize(CharacterStateTable stateTable)
        {
            this.stateTable = stateTable;
            if (snapshotCommittedHandler == null)
            {
                snapshotCommittedHandler = HandleSnapshotCommitted;
            }
            CacheComponents();
            BindDataTable(GetActiveDataTable());
        }

        public void Update()
        {
            SimulationDataTable currentTable = GetActiveDataTable();
            if (!ReferenceEquals(boundDataTable, currentTable))
            {
                BindDataTable(currentTable);
            }

            if (characterView == null || spriteRenderer == null)
            {
                return;
            }

            if (HasEntityInDataTable(currentTable, characterView.Id))
            {
                return;
            }

            if (stateTable == null)
            {
                return;
            }

            int id = characterView.Id;
            Direction direction = stateTable.GetDirection(id).Direction;
            spriteRenderer.flipX = (direction == Direction.Left);
        }

        private void CacheComponents()
        {
            if (characterView == null)
            {
                characterView = GetComponent<CharacterView>();
            }
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
        }

        private static SimulationDataTable GetActiveDataTable()
        {
            SimulationDataTable table = SimulationDataTable.Instance;
            return table != null ? table : null;
        }

        private void BindDataTable(SimulationDataTable table)
        {
            if (ReferenceEquals(boundDataTable, table))
            {
                if (table != null && table.IsInitialized)
                {
                    ApplyDirectionFromDataTable(table);
                }
                return;
            }

            UnbindDataTable();
            if (table == null)
            {
                return;
            }

            boundDataTable = table;
            boundDataTable.OnSnapshotCommitted -= snapshotCommittedHandler;
            boundDataTable.OnSnapshotCommitted += snapshotCommittedHandler;

            if (boundDataTable.IsInitialized)
            {
                ApplyDirectionFromDataTable(boundDataTable);
            }
        }

        private void UnbindDataTable()
        {
            if (!ReferenceEquals(boundDataTable, null) && snapshotCommittedHandler != null)
            {
                boundDataTable.OnSnapshotCommitted -= snapshotCommittedHandler;
            }
            boundDataTable = null;
        }

        private void HandleSnapshotCommitted(ulong tick)
        {
            SimulationDataTable table = boundDataTable != null ? boundDataTable : GetActiveDataTable();
            ApplyDirectionFromDataTable(table);
        }

        private void ApplyDirectionFromDataTable(SimulationDataTable table)
        {
            if (characterView == null || spriteRenderer == null)
            {
                return;
            }

            if (TryResolveEntityRecord(table, characterView.Id, out SimulationEntityRecord rec) &&
                rec.facingDirection != NeutralFacingDirection)
            {
                spriteRenderer.flipX = (rec.facingDirection < NeutralFacingDirection);
            }
        }

        private static bool HasEntityInDataTable(SimulationDataTable table, int viewId)
        {
            if (table == null || !table.IsInitialized || viewId < 0)
            {
                return false;
            }

            uint entityId = ResolveEntityId(table, viewId);
            return table.HasEntity(entityId);
        }

        private static bool TryResolveEntityRecord(SimulationDataTable table, int viewId, out SimulationEntityRecord record)
        {
            if (table == null || !table.IsInitialized || viewId < 0)
            {
                record = default;
                return false;
            }

            uint entityId = ResolveEntityId(table, viewId);
            return table.TryGetEntity(entityId, out record);
        }

        private static uint ResolveEntityId(SimulationDataTable table, int viewId)
        {
            if (viewId == PlayerViewId)
            {
                return PlayerEntityId;
            }
            if (viewId == Opponent1ViewId)
            {
                return Opponent1EntityId;
            }
            if (viewId == LegacyOpponentViewId && !table.HasEntity(Teammate1EntityId))
            {
                return Opponent1EntityId;
            }
            return (uint)viewId;
        }
    }
}
