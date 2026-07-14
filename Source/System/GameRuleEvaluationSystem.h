#pragma once
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../Config/Match/MatchRuleConfig.h"
#include "../Config/ECS/ChunkConfig.h"
#include "../Config/Match/MatchEntityConfig.h"
using namespace MatchRuleConfig;
class GameRuleEvaluationSystem
{
private:
    ComponentStorage* _componentStorage;
    IntentStorage* _intentStorage;

public:
    GameRuleEvaluationSystem(ComponentStorage* componentStorage, IntentStorage* intentStorage);
    void update();
};
