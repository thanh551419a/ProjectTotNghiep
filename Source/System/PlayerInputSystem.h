#pragma once

#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../Input/InputMapping.h"
#include "../Utils/ObjectData.h"
#include "../Utils/GetData.h"
#include "../Config/System/SystemConf.h"
#include "../Config/Match/BigRect.h"

#if defined(AXMOL_ENABLED)
#include "../Input/InputListener.h"
#endif

class PlayerInputSystem
{
private:
    IntentStorage* _intentStorage       = nullptr;
    ComponentStorage* _componentStorage = nullptr;

public:
    PlayerInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);

    // Primary native / data-driven update using SimInputFrame
    void update(float delta, const SimInputFrame& input);

#if defined(AXMOL_ENABLED)
    // Legacy Axmol keyboard listener adapter
    void update(float delta, const InputListener::InputFrame& input);
#endif
};
