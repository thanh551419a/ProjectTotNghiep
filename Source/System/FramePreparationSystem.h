#pragma once
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
class FramePreparationSystem
{
private:
    ComponentStorage* _componentStorage;
    IntentStorage* _intentStorage;

public:
    FramePreparationSystem(ComponentStorage* c, IntentStorage* i);
    void update();
};
