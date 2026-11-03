using UnityEngine;

namespace Presentation.Character
{
    public class CharacterView : MonoBehaviour
    {
        [SerializeField]
        private int id;

        private SpriteRenderer spriteRenderer;
        private Animator animator;

        public int Id { get => id; set => id = value; }

        public Animator Animator => animator;
        public SpriteRenderer SpriteRenderer => spriteRenderer;

        public void InitializeComponents()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);

            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        private void Awake()
        {
            InitializeComponents();

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
