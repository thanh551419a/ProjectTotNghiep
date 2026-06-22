#pragma once

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/Components/JumpUpFrameComponent.h"

inline void ApplyIntentToComponent(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{

    //apply jump
    auto& posPool    = componentStorage->GetCharacterPositionPool();
    auto& jumpUpPool = componentStorage->GetJumpUpFramePool();
    // chỉ cần duyệt entity đang có intent
    const auto& jumpUpFrames = jumpUpPool.components();
    // duyệt toàn bộ entity có trong jumpUpFramePool
    for (auto e = GameConfig::PLAYER; e <= GameConfig::OPPONENT_3; ++e)
    {
        JumpUpFrameComponent* jumpUpFrame = jumpUpPool.get(e);
        if (!jumpUpFrame)
            continue;
        if (jumpUpFrame->remainingFrames > 0)
        {
            // nếu còn frame nhảy thì giảm y xuống
            PositionComponent* pos = posPool.get(e);
            if (pos)
            {
                pos->position.y += 2*(-1)*SystemConfig::GRAVITY;
            }
            jumpUpFrame->remainingFrames--;
        }
    }

    //apply move left + right
    auto characterIntentPool = intentStorage->GetCharacterIntentPool();
    // lay toan bo entity co trong CharacterIntentPool
    auto entitiesInCIP = characterIntentPool.entities();
    for (size_t i = 0; i < entitiesInCIP.size(); ++i)
    {
        Entity entity = entitiesInCIP[i];
        const CharacterIntent& intent = characterIntentPool.components()[i];
        PositionComponent* pos = posPool.get(entity);
        if (!pos)
            continue;
        // ===== APPLY LOGIC TỐI THIỂU =====
        pos->position.x += intent.moveX;
        if (pos->position.y > SystemConfig::MIN_Y) // dang o tren khong trung
        {

            pos->position.x += -1*(intent.moveX / abs(intent.moveX)) * SystemConfig::SPEED/2;  // giam toc do di chuyen tren khong trung
        }
        if (entity < 3)
            pos->position.x = clampf(SystemConfig::MIN_X_LEFT, pos->position.x, SystemConfig::MAX_X_LEFT);
        else if (entity < 6)
            pos->position.x = clampf(SystemConfig::MIN_X_RIGHT, pos->position.x, SystemConfig::MAX_X_RIGHT);
    }

    //apply velocity
    auto velocityIntentPool = intentStorage->GetVelocityIntentPool();
    const auto& entities1 = velocityIntentPool.entities();
    const auto& intents1  = velocityIntentPool.components();
   // if(intents1.size()) AXLOG("có chạy vào apply intent  , số lượng intent=%zu", intents1.size());
    for (size_t i = 0; i < intents1.size(); ++i)
    {
        Entity entity = entities1[i];

        const VelocityIntent& intent = intents1[i];
        AXLOG("VelocityIntent => vx=%.2f vy=%.2f", intent.vx, intent.vy);
        PositionComponent* pos = posPool.get(entity);
        if (!pos)
            continue;

        // ===== APPLY LOGIC TỐI THIỂU =====
        pos->position.y = std::max(SystemConfig::MIN_Y, pos->position.y + intent.vy);

    }
}
