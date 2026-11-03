using UnityEngine;
using Assets.Table;

namespace Presentation.Character
{
    public class CharacterAnimationController : MonoBehaviour
    {
        private CharacterAnimationBinder binder;
        private CharacterDirectionConsumer directionConsumer;

        private void Awake()
        {
            Initialize();        
        }
        public void Initialize()
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

            CharacterStateTable stateTable =
                FindFirstObjectByType<CharacterStateTable>(
                    FindObjectsInactive.Include
                );

            if (stateTable == null)
            {
                var simMgr = FindFirstObjectByType<Simulation.SpikeSimulationManager>(FindObjectsInactive.Include);
                if (simMgr != null)
                {
                    stateTable = simMgr.StateTable;
                }
            }

            if (stateTable == null)
            {
                Debug.LogError(
                    "CharacterStateTable not found.",
                    this
                );

                return;
            }
            else
            {
                Debug.Log("CharacterStateTable found", this);
            }

            binder?.Initialize(stateTable);
            directionConsumer?.Initialize(stateTable);
        }


    }
}