#pragma once
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
class UpdateCharacterActionStateSystem
{
    private:
        ComponentStorage* c;

    public:
        UpdateCharacterActionStateSystem(ComponentStorage* componentStorage);
        void update(float dt);
};
