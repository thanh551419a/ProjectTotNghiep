using UnityEngine;
using Presentation.State;

namespace Presentation.Character
{
    public class CharacterAnimationEndHandler : MonoBehaviour
    {
        private CharacterView characterView;

        private CharacterStateTable stateTable;

        private int lastAnimationHash;
        private bool animationEndHandled;

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

            Animator animator =
                characterView.Animator;

            if (animator.IsInTransition(0))
            {
                return;
            }

            AnimatorStateInfo stateInfo =
                animator.GetCurrentAnimatorStateInfo(0);

            if (stateInfo.fullPathHash != lastAnimationHash)
            {
                lastAnimationHash =
                    stateInfo.fullPathHash;

                animationEndHandled = false;
            }

            if (stateInfo.normalizedTime < 1f)
            {
                return;
            }

            if (animationEndHandled)
            {
                return;
            }

            animationEndHandled = true;

            stateTable.Set(
                characterView.Id,
                CharacterState.Idle
            );
        }
    }
}