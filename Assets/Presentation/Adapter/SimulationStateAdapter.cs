using Assets.Table;
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
            stateTable
                .GetState(characterId)
                .Set(state);
        }

        public void SetDirection(
            int characterId,
            Direction direction)
        {
            stateTable.SetDirection(characterId, direction);
        }

        public void SetGroundState(
            int characterId,
            bool isGrounded)
        {
            stateTable.SetGroundState(characterId, isGrounded);
        }
    }
}