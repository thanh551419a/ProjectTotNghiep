using UnityEngine;
using Presentation.Adapter;
using Assets.Table;

namespace Presentation.Bootstrap
{
    public class PresentationBootstrap : MonoBehaviour
    {
        private CharacterStateTable stateTable;
        private SimulationStateAdapter stateAdapter;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            stateTable =
                FindFirstObjectByType<CharacterStateTable>(
                    FindObjectsInactive.Include
                );

            if (stateTable == null)
            {
                Debug.LogError(
                    "CharacterStateTable not found in hierarchy.",
                    this
                );

                return;
            }

            stateAdapter =
                new SimulationStateAdapter(stateTable);
        }

        public SimulationStateAdapter StateAdapter =>
            stateAdapter;
    }
}