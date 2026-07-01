#pragma once
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
class CharacterStatusSystem
{
private:
    ComponentStorage* _componentStorage;
    IntentStorage* _intentStorage;

public:
    CharacterStatusSystem (IntentStorage* _intentStorage, ComponentStorage* componentStorage);
    void update(float dt);
};
