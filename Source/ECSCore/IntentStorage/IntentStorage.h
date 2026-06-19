#pragma once
#pragma once

#include "../ComponentPools/ComponentPool.h"
#include "../IntentComponents/CharacterIntent.h"
#include "../IntentComponents/BallIntent.h"
class IntentStorage
{
public:
    // =========================================
    // PLAYER INTENT
    // =========================================

    ComponentPool<CharacterIntent> characterIntentPool;
    ComponentPool<BallIntent> ballIntentPool;

public:
    IntentStorage() = default;

    // =========================================
    // CLEAR ALL INTENTS
    // gọi đầu mỗi frame
    // =========================================

    void clear() { characterIntentPool.clear(); }

    // =========================================
    // GETTERS
    // =========================================

    ComponentPool<CharacterIntent>& GetCharacterIntentPool() { return characterIntentPool; }

    const ComponentPool<CharacterIntent>& GetCharacterIntentPool() const { return characterIntentPool; }

    ComponentPool<BallIntent>& GetBallIntentPool() { return ballIntentPool; }

    const ComponentPool<BallIntent>& GetBallIntentPool() const { return ballIntentPool; }
};
