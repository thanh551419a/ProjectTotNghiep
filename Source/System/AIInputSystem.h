#pragma once
#pragma once

#include <random>

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../Config/Match/MatchEntityConfig.h"

class AIInputSystem
{
public:
    AIInputSystem(IntentStorage* intentStorage);

    void update();

private:
    IntentStorage* _intentStorage = nullptr;

    std::mt19937 _rng;
};
