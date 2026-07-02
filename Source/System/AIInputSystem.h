#pragma once
#pragma once

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Config/Match/MatchEntityConfig.h"
#include "../System/TrajectoryTestConfig.h"
class AIInputSystem
{
public:
    AIInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);

    void update();

private:
    IntentStorage* _intentStorage       = nullptr;
    ComponentStorage* _componentStorage = nullptr;

    
};
