using UnityEngine;
using Presentation.State;

namespace Presentation.Character
{
    public class CharacterAnimationBinder : MonoBehaviour
    {
        private static readonly int StateHash =
            Animator.StringToHash("State");

        private static readonly int OnTheGroundHash =
            Animator.StringToHash("OnTheGround");

        private CharacterView characterView;
        private CharacterStateTable stateTable;

        private CharacterState boundState =
            CharacterState.None;

        private bool boundOnTheGround;

        public void Initialize(
            CharacterStateTable stateTable)
        {
            this.stateTable = stateTable;

            if (characterView == null)
            {
                characterView =
                    GetComponent<CharacterView>();
            }
        }

        public void Update()
        {
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

            if (state != boundState)
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
        }
    }
}