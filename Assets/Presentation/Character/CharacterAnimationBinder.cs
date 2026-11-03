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

        private CharacterView characterView;
        private CharacterStateTable stateTable;

        private CharacterState boundState =
            CharacterState.None;

        private bool boundOnTheGround;

        public void Initialize(
            CharacterStateTable stateTable)
        {
            //Debug.Log(
            //    $"[CharacterAnimationBinder] Initialize() " +
            //    $"GameObject={gameObject.name}",
            //    this
            //);

            this.stateTable = stateTable;

            if (characterView == null)
            {
                characterView =
                    GetComponent<CharacterView>();
            }

            //Debug.Log(
            //    $"[CharacterAnimationBinder] " +
            //    $"stateTable={this.stateTable != null} | " +
            //    $"characterView={characterView != null} | " +
            //    $"animator={characterView?.Animator != null}",
            //    this
            //);
        }

        public void Update()
        {
            //Debug.Log(
            //    $"[CharacterAnimationBinder] Update() " +
            //    $"GameObject={gameObject.name}",
            //    this
            //);

            if (stateTable == null ||
                characterView == null ||
                characterView.Animator == null)
            {
                //Debug.Log(
                //    $"[CharacterAnimationBinder] Missing reference | " +
                //    $"stateTable={stateTable != null} | " +
                //    $"characterView={characterView != null} | " +
                //    $"animator={characterView?.Animator != null}",
                //    this
                //);

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

            //Debug.Log(
            //    $"[CharacterAnimationBinder] " +
            //    $"ID={characterId} | " +
            //    $"State={state} ({(int)state}) | " +
            //    $"OnTheGround={onTheGround}",
            //    this
            //);

            if (state != boundState)
            {
                //Debug.Log(
                //    $"[CharacterAnimationBinder] " +
                //    $"Set Animator State = {state} ({(int)state})",
                //    this
                //);

                characterView.Animator.SetInteger(
                    StateHash,
                    (int)state
                );

                boundState = state;
            }

            if (onTheGround != boundOnTheGround)
            {
                //Debug.Log(
                //    $"[CharacterAnimationBinder] " +
                //    $"Set Animator OnTheGround = {onTheGround}",
                //    this
                //);

                characterView.Animator.SetBool(
                    OnTheGroundHash,
                    onTheGround
                );

                boundOnTheGround = onTheGround;
            }
        }
    }
}