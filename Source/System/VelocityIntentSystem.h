#pragma once
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/IntentComponents/VelocityIntent.h"
#include "../Utils/ObjectData.h"
#include "../Utils/GetData.h"
#include "../Config/Match/MatchEntityConfig.h"
#include "../Config/System/SystemConf.h"
class VelocityIntentSystem
{
    private:
        IntentStorage* _intentStorage = nullptr;

    public:
        VelocityIntentSystem(IntentStorage* intentStorage);
        void update(float delta, ObjectData* data);
};
