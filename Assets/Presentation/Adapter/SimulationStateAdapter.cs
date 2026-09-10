using Presentation.State;

namespace Presentation.Adapter
{
    public class SimulationStateAdapter
    {
        private readonly CharacterStateTable stateTable;

        public SimulationStateAdapter(
            CharacterStateTable stateTable)
        {
            this.stateTable = stateTable;
        }

        public void SetCharacterState(
            int characterId,
            CharacterState state)
        {
            stateTable.Set(
                characterId,
                state
            );
        }
    }
}