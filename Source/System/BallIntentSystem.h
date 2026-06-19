#pragma once
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Utils/ObjectData.h"
#include "../Utils/GetData.h"
#include "../input/InputListener.h"
#include "../Config/System/SystemConf.h"
#include "../Config/Match/BigRect.h"
#include "../System/PlayerInputSystem.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/IntentComponents/BallIntent.h"
#include "../ECSCore/IntentComponents/CharacterIntent.h"
class BallIntentSystem
{
    private:
        IntentStorage* _intentStorage = nullptr;

    public:
        BallIntentSystem(IntentStorage* intentStorage);
        void update(float delta, InputListener::InputFrame input, ObjectData* data);
};
