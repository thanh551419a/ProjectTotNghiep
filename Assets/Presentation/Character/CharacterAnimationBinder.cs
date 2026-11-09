using UnityEngine;
using Presentation.State;
using Assets.Table;

namespace Presentation.Character
{
    public class CharacterAnimationBinder : MonoBehaviour
    {
        private static readonly int StateHash =
            Animator.StringToHash("State");

        private static readonly int OnTheGroundHash =
            Animator.StringToHash("OnTheGround");

        private static readonly int BumpShortHash =
            Animator.StringToHash("KnightBump");

        private static readonly int SpikeShortHash =
            Animator.StringToHash("KnightSpike");

        private static readonly int SpikeLightShortHash =
            Animator.StringToHash("SpikeLight");

        private static readonly int SpikeMediumShortHash =
            Animator.StringToHash("SpikeMedium");

        private static readonly int SpikeStrongShortHash =
            Animator.StringToHash("SpikeStrong");

        private CharacterView characterView;
        private CharacterStateTable stateTable;
        private SimulationDataTable dataTable;
        private SimulationEntityRecord previousAction;
        private bool hasPreviousAction;
        private bool pendingActionStart;
        private ulong lastActionTick;

        private CharacterState boundState =
            CharacterState.None;

        private bool boundOnTheGround;

        public bool PendingActionStart => pendingActionStart;
        public int ReplayTriggerCount { get; private set; }

        public void Initialize(
            CharacterStateTable stateTable)
        {
            this.stateTable = stateTable;

            if (characterView == null)
            {
                characterView =
                    GetComponent<CharacterView>();
            }

            BindDataTable(true);
        }

        private void OnEnable()
        {
            if (characterView == null)
            {
                characterView = GetComponent<CharacterView>();
            }
            BindDataTable(false);
        }

        private void OnDisable()
        {
            if (dataTable != null)
            {
                dataTable.OnSnapshotCommitted -= OnSnapshotCommitted;
            }
            hasPreviousAction = false;
            pendingActionStart = false;
        }

        private void BindDataTable(bool forceReset)
        {
            SimulationDataTable candidate = SimulationDataTable.Instance;
            if (candidate == null)
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
            hasPreviousAction = false;
            pendingActionStart = false;
            lastActionTick = 0;

            if (isActiveAndEnabled && dataTable != null)
            {
                dataTable.OnSnapshotCommitted -= OnSnapshotCommitted;
                dataTable.OnSnapshotCommitted += OnSnapshotCommitted;
            }
        }

        private void OnSnapshotCommitted(ulong tick)
        {
            if (characterView == null)
            {
                characterView = GetComponent<CharacterView>();
            }

            if (characterView == null || dataTable == null ||
                !dataTable.TryGetEntity((uint)characterView.Id, out SimulationEntityRecord current))
            {
                return;
            }

            if (hasPreviousAction && tick == lastActionTick)
            {
                return;
            }

            if (hasPreviousAction && tick < lastActionTick)
            {
                hasPreviousAction = false;
                pendingActionStart = false;
            }

            if (hasPreviousAction && current.hasCharacterActionState &&
                current.actionStatus != 0 && previousAction.hasCharacterActionState &&
                (previousAction.actionStatus == 0 || previousAction.actionStatus != current.actionStatus ||
                 current.actionRemainTime > previousAction.actionRemainTime + 0.001f))
            {
                pendingActionStart = true;
            }

            previousAction = current;
            hasPreviousAction = true;
            lastActionTick = tick;
        }

        private static bool IsOneShotAction(CharacterState state)
        {
            return state == CharacterState.Bump || state == CharacterState.Spike ||
                   state == CharacterState.SpikeLight || state == CharacterState.SpikeMedium ||
                   state == CharacterState.SpikeStrong;
        }

        private static int GetActionShortNameHash(CharacterState state)
        {
            switch (state)
            {
                case CharacterState.Bump: return BumpShortHash;
                case CharacterState.Spike: return SpikeShortHash;
                case CharacterState.SpikeLight: return SpikeLightShortHash;
                case CharacterState.SpikeMedium: return SpikeMediumShortHash;
                case CharacterState.SpikeStrong: return SpikeStrongShortHash;
                default: return 0;
            }
        }

        public void Update()
        {
            BindDataTable(false);

            if (stateTable == null ||
                characterView == null ||
                characterView.Animator == null)
            {
                return;
            }

            int characterId = characterView.Id;

            CharacterState state =
                stateTable
                    .GetState(characterId)
                    .State;

            bool onTheGround =
                stateTable
                    .GetGroundState(characterId)
                    .Value;

            bool stateChanged = state != boundState;
            if (stateChanged)
            {
                characterView.Animator.SetInteger(
                    StateHash,
                    (int)state
                );

                boundState = state;
            }

            if (pendingActionStart && IsOneShotAction(state))
            {
                Animator animator = characterView.Animator;
                int targetShortHash = GetActionShortNameHash(state);
                AnimatorStateInfo currentAnimation = animator.GetCurrentAnimatorStateInfo(0);
                if (currentAnimation.shortNameHash == targetShortHash)
                {
                    animator.Play(currentAnimation.fullPathHash, 0, 0f);
                    ReplayTriggerCount++;
                }
                else if (!stateChanged && targetShortHash != 0)
                {
                    animator.Play(targetShortHash, 0, 0f);
                    ReplayTriggerCount++;
                }
            }

            pendingActionStart = false;

            if (onTheGround != boundOnTheGround)
            {
                characterView.Animator.SetBool(
                    OnTheGroundHash,
                    onTheGround
                );

                boundOnTheGround = onTheGround;
            }
        }
    }
}
