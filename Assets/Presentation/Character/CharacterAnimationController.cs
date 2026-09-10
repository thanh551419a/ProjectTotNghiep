using UnityEngine;
using Presentation.State;

namespace Presentation.Character
{
    public class CharacterAnimationController : MonoBehaviour
    {
        private CharacterAnimationBinder binder;
        private CharacterAnimationEndHandler endHandler;

        public void Initialize(
            CharacterStateTable stateTable)
        {
            if (binder == null)
            {
                binder =
                    GetComponent<CharacterAnimationBinder>();
            }

            if (endHandler == null)
            {
                endHandler =
                    GetComponent<CharacterAnimationEndHandler>();
            }

            binder?.Initialize(stateTable);
            endHandler?.Initialize(stateTable);
        }

        private void Update()
        {
            BinderUpdate();
            EndHandlerUpdate();
        }

        private void BinderUpdate()
        {
            binder?.Update();
        }

        private void EndHandlerUpdate()
        {
            endHandler?.Update();
        }
    }
}