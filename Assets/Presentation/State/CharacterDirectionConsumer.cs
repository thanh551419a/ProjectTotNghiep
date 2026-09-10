using UnityEngine;
using Presentation.State;

namespace Presentation.Character
{
    public class CharacterDirectionConsumer : MonoBehaviour
    {
        private CharacterView characterView;
        private SpriteRenderer spriteRenderer;
        private CharacterStateTable stateTable;

        public void Initialize(CharacterStateTable stateTable)
        {
            this.stateTable = stateTable;

            characterView = GetComponent<CharacterView>();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public void Update()
        {
            Debug.Log("[DirectionConsumer] Update");

            if (stateTable == null ||
                characterView == null ||
                spriteRenderer == null)
            {
                Debug.Log(
                    $"[DirectionConsumer] Missing reference: " +
                    $"table={stateTable != null}, " +
                    $"view={characterView != null}, " +
                    $"sprite={spriteRenderer != null}"
                );

                return;
            }

            int id = characterView.Id;

            Direction direction =
                stateTable
                    .GetDirection(id)
                    .Direction;

            Debug.Log(
                $"[DirectionConsumer] ID={id} Direction={direction} Value={(int)direction}"
            );

            spriteRenderer.flipX =
                direction == Direction.Left;
        }
    }
}