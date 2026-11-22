#include "UpdateCharacterActionStateSystem.h"
#include "../Utils/SimLog.h"
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
        else if (actionState->status != ActionState::None)
        {
            // Các hành động không có cooldown (remainTime == 0.0f như Serve) giữ status trong 1 tick để gửi lên Unity rồi về None ở tick kế tiếp.
            actionState->status = ActionState::None;
        }
    }
}
