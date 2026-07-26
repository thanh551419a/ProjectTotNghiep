#pragma once
#include <axmol.h>
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "ObjectData.h"
#include "GetData.h"
#include "../input/InputListener.h"
#include "../Config/System/SystemConf.h"
#include "../Config/Match/BigRect.h"
#include "../System/PlayerInputSystem.h"
//#include "../System/BallIntentSystem.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../System/AIInputSystem.h"
#include "../System/VelocityIntentSystem.h"
#include "../System/JumpStartSystem.h"
#include "../System/UpdateTrajectoryFromIntent.h"
#include "../System/CharacterStatusSystem.h"
#include "../System/UpdateCharacterActionStateSystem.h"
#include "../System/Player2InputSystem.h"
#include "../Config/Match/MatchRuleConfig.h"
USING_NS_AX;
class GenerateIntent
{
private:
    IntentStorage* _intentStorage = nullptr;
    Player2InputSystem* player2InputSystem = nullptr;
    ComponentStorage* _componentStorage    = nullptr;
    PlayerInputSystem* playerInputSystem   = nullptr;
    //BallIntentSystem* ballIntentSystem     = nullptr;
    AIInputSystem* aiInputSystem           = nullptr;
    CharacterStatusSystem* characterStatusSystem           = nullptr;
    VelocityIntentSystem* velocityIntentSystem = nullptr;
    JumpStartSystem* jumpStartSystem           = nullptr;
    UpdateTrajectoryFromIntent* updateTrajectoryFromIntent = nullptr;
    UpdateCharacterActionStateSystem* updateCharacterActionStateSystem = nullptr;
    public:
        GenerateIntent(IntentStorage* intentStorage, ComponentStorage* componentStorage);
        void update(float delta , InputListener::InputFrame input, std::ofstream* logFile);
};
