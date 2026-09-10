using UnityEngine;

namespace Presentation.Character
{
    public class CharacterView : MonoBehaviour
    {
        [SerializeField]
        private int id;

        private SpriteRenderer spriteRenderer;
        private Animator animator;

        public int Id => id;

        public Animator Animator => animator;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            spriteRenderer = GetComponent<SpriteRenderer>();

            if (animator == null)
            {
                Debug.LogError(
                    $"CharacterView [{id}] has no Animator.",
                    this
                );
            }

            if (spriteRenderer == null)
            {
                Debug.LogError(
                    $"CharacterView [{id}] has no SpriteRenderer.",
                    this
                );
            }
        }
    } 
}
