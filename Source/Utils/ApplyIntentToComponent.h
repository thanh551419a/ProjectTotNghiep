#pragma once

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"

inline void ApplyIntentToComponent(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    auto& intentPool = intentStorage->GetCharacterIntentPool();
    auto& posPool    = componentStorage->GetCharacterPositionPool();

    // chỉ cần duyệt entity đang có intent
    const auto& entities = intentPool.entities();
    const auto& intents  = intentPool.components();

    for (size_t i = 0; i < intents.size(); ++i)
    {
        Entity entity = entities[i];

        const CharacterIntent& intent = intents[i];

        PositionComponent* pos = posPool.get(entity);
        if (!pos)
            continue;

        // ===== APPLY LOGIC TỐI THIỂU =====
        pos->position.x += intent.moveX * 10.0f;
        if (intent.jump == true && pos->position.y <= 265.0f)
        {
            pos->position.y += 150.0f;
        }
    }
}
