using UnityEngine;
using Presentation.State;
using Presentation.Character;
using Presentation.Adapter;

namespace Presentation.Bootstrap
{
    public class PresentationBootstrap : MonoBehaviour
    {
        [SerializeField]
        private CharacterStateTable stateTable;

        [SerializeField]
        private CharacterView[] characters;

        private CharacterAnimationBinder[] binders;

        private CharacterAnimationEndHandler[] endHandlers;

        private SimulationStateAdapter stateAdapter;

        private void Awake()
        {
            Debug.Log(
                "[PresentationBootstrap] Awake() CALLED",
                this
            );
            Initialize();
        }

        private void Initialize()
        {
            if (stateTable == null)
            {
                Debug.LogError(
                    "CharacterStateTable is missing.",
                    this
                );

                return;
            }

            stateAdapter =
                new SimulationStateAdapter(stateTable);

            for (int i = 0; i < characters.Length; i++)
            {
                CharacterView character =
                    characters[i];

                if (character == null)
                    continue;

                CharacterAnimationBinder binder =
                    character.GetComponent<
                        CharacterAnimationBinder>();

                CharacterAnimationEndHandler endHandler =
                    character.GetComponent<
                        CharacterAnimationEndHandler>();

                binder?.Initialize(stateTable);
                endHandler?.Initialize(stateTable);
            }
        }
        public SimulationStateAdapter StateAdapter =>
            stateAdapter;
    }
}