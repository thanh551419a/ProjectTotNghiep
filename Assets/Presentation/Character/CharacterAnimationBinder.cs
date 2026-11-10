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
        private bool pendingIntentStart;
        private CharacterState visualHoldState = CharacterState.None;
        private int visualHoldTicks;
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
            pendingIntentStart = false;
            visualHoldState = CharacterState.None;
            visualHoldTicks = 0;
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
            pendingIntentStart = false;
            visualHoldState = CharacterState.None;
            visualHoldTicks = 0;
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
                pendingIntentStart = false;
                visualHoldState = CharacterState.None;
                visualHoldTicks = 0;
            }

            if (visualHoldTicks > 0)
            {
                visualHoldTicks--;
                if (visualHoldTicks == 0)
                {
                    visualHoldState = CharacterState.None;
                }
            }

            if (current.hasCharacterActionState && current.actionStatus != 0 &&
                (!hasPreviousAction || !previousAction.hasCharacterActionState ||
                 previousAction.actionStatus == 0 || previousAction.actionStatus != current.actionStatus ||
                 current.actionRemainTime > previousAction.actionRemainTime + 0.001f))
            {
                if (hasPreviousAction)
                {
                    pendingActionStart = true;
                }
                CharacterState actionState = MapActionStatusToCharacterState(current.actionStatus);
                if (IsOneShotAction(actionState))
                {
                    visualHoldState = actionState;
                    visualHoldTicks = 15;
                }
            }
            else if (characterView.Id == 0 && current.hasCharacterIntent && current.finalIntent != 0 &&
                     (!hasPreviousAction || !previousAction.hasCharacterIntent || previousAction.finalIntent != current.finalIntent))
            {
                CharacterState intentState = MapFinalIntentToCharacterState(current.finalIntent);
                if (IsOneShotAction(intentState))
                {
                    visualHoldState = intentState;
                    visualHoldTicks = 15;
                    pendingIntentStart = true;
                }
            }

            previousAction = current;
            hasPreviousAction = true;
            lastActionTick = tick;
        }

        private static CharacterState MapActionStatusToCharacterState(int actionStatus)
        {
            switch ((Native.SimActionState)actionStatus)
            {
                case Native.SimActionState.Spike: return CharacterState.Spike;
                case Native.SimActionState.SpikeLight: return CharacterState.SpikeLight;
                case Native.SimActionState.SpikeMedium: return CharacterState.SpikeMedium;
                case Native.SimActionState.SpikeStrong: return CharacterState.SpikeStrong;
                case Native.SimActionState.Bump:
                case Native.SimActionState.Set:
                case Native.SimActionState.Serve:
                case Native.SimActionState.Slide:
                    return CharacterState.Bump;
                default:
                    return CharacterState.None;
            }
        }

        private static CharacterState MapFinalIntentToCharacterState(int finalIntent)
        {
            switch (finalIntent)
            {
                case 2: return CharacterState.Spike;
                case 5: return CharacterState.Bump;
                case 9: return CharacterState.SpikeLight;
                case 10: return CharacterState.SpikeMedium;
                case 11: return CharacterState.SpikeStrong;
                default: return CharacterState.None;
            }
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

            if (!IsOneShotAction(state) && visualHoldTicks > 0 && IsOneShotAction(visualHoldState))
            {
                state = visualHoldState;
            }

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

            if (onTheGround != boundOnTheGround)
            {
                characterView.Animator.SetBool(
                    OnTheGroundHash,
                    onTheGround
                );

                boundOnTheGround = onTheGround;
            }

            if (IsOneShotAction(state) && (stateChanged || pendingActionStart || pendingIntentStart))
            {
                Animator animator = characterView.Animator;
                int targetShortHash = GetActionShortNameHash(state);
                AnimatorStateInfo currentAnimation = animator.GetCurrentAnimatorStateInfo(0);
                if (currentAnimation.shortNameHash == targetShortHash)
                {
                    if (pendingActionStart || pendingIntentStart)
                    {
                        animator.Play(currentAnimation.fullPathHash, 0, 0f);
                        if (pendingActionStart)
                        {
                            ReplayTriggerCount++;
                        }
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
            pendingIntentStart = false;
        }
    }
}
