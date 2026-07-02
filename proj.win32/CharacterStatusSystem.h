#pragma once
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
class CharacterStatusSystem
{
private:

public:
    CharacterStatusSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);
    void update(float delta);
private:
    IntentStorage* _intentStorage;
    ComponentStorage* _componentStorage;
};
