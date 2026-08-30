#pragma once
#include "../../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../../ECSCore/IntentStorage/IntentStorage.h"
#include "../../input/InputListener.h"
class TestTrajectorySystem
{
    public:
        TestTrajectorySystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);
        void update(float delta, InputListener::InputFrame input);

    private:
        IntentStorage* _intentStorage;
        ComponentStorage* _componentStorage;

};
