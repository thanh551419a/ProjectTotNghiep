#pragma once
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/Components/JumpUpFrameComponent.h"
class JumpStartSystem
{
    private:
        IntentStorage* _intentStorage = nullptr;
        ComponentStorage* _componentStorage = nullptr;
    public:
        JumpStartSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);
        void update(float delta);
};
