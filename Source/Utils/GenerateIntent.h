#pragma once
#include <axmol.h>
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "ObjectData.h"
#include "GetData.h"
#include "../input/InputListener.h"
#include "../Config/System/SystemConf.h"
#include "../Config/Match/BigRect.h"
#include "../System/PlayerInputSystem.h"
#include "../System/BallMovementSystem.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../System/AIInputSystem.h"
USING_NS_AX;
class GenerateIntent
{
private:
    IntentStorage* _intentStorage = nullptr;
    PlayerInputSystem* playerInputSystem   = nullptr;
    BallMovementSystem* ballMovementSystem     = nullptr;
    AIInputSystem* aiInputSystem           = nullptr;
    public:
        GenerateIntent(IntentStorage* intentStorage);
        void update(float delta , InputListener::InputFrame input);
};
