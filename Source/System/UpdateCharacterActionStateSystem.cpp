#include "UpdateCharacterActionStateSystem.h"
UpdateCharacterActionStateSystem::UpdateCharacterActionStateSystem(ComponentStorage* componentStorage) {
    c = componentStorage;
}
void UpdateCharacterActionStateSystem::update(float dt) {
    //AXLOG("delta time la : %f", dt);
    auto& characterActionStatePool = c->GetCharacterActionStatePool();
    for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; ++i)
    {
        auto* actionState = characterActionStatePool.get(i);

        if (actionState->remainTime > 0.0f)
        {
            actionState->remainTime = std::max(actionState->remainTime - dt, 0.0f);

            if (actionState->remainTime == 0.0f)
            {
                actionState->status = ActionState::None;
            }
        }
    }
}
