#pragma once

#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../Input/InputMapping.h"
#include "../Utils/ObjectData.h"
#include "../Utils/GetData.h"
#include "../Config/System/SystemConf.h"
#include "../Config/Match/BigRect.h"
#include "../Utils/SimLog.h"

#if defined(AXMOL_ENABLED)
#include "../Input/InputListener.h"
#endif

class Player2InputSystem
{
private:
    IntentStorage* _intentStorage       = nullptr;
    ComponentStorage* _componentStorage = nullptr;

public:
    Player2InputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);
    void update(float delta, const SimInputFrame& input);

#if defined(AXMOL_ENABLED)
    void update(float delta, const InputListener::InputFrame& input);
#endif
};
