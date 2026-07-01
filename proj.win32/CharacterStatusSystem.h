#pragma once
class CharacterStatusSystem
{
private:

public:
    CharacterStatusSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage);
    void update(float delta);
};
