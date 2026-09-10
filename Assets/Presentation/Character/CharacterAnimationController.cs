using UnityEngine;
using Presentation.State;

namespace Presentation.Character
{
    public class CharacterAnimationController : MonoBehaviour
    {
        private CharacterAnimationBinder binder;
        private CharacterDirectionConsumer directionConsumer;

        public void Initialize(
            CharacterStateTable stateTable)
        {
            if (binder == null)
            {
                binder =
                    GetComponent<CharacterAnimationBinder>();
            }

            if (directionConsumer == null)
            {
                directionConsumer =
                    GetComponent<CharacterDirectionConsumer>();
            }

            binder?.Initialize(stateTable);
            directionConsumer?.Initialize(stateTable);
        }

        private void Update()
        {
            BinderUpdate();
        }

        private void BinderUpdate()
        {
            binder?.Update();
        }
    }
}