#pragma once
#pragma once

#include <random>

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Config/Match/MatchEntityConfig.h"

class AIInputSystem
{
public:
    AIInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);

    void update();

private:
    IntentStorage* _intentStorage = nullptr;
    ComponentStorage* _componentStorage = nullptr;
    std::mt19937 _rng;
};
