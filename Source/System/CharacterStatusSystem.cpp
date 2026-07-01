#include "CharacterStatusSystem.h"

CharacterStatusSystem::CharacterStatusSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _intentStorage = intentStorage;
    _componentStorage = componentStorage;
}

void CharacterStatusSystem::update(float delta)
{
    auto& CharacterStatusPool = _componentStorage->GetCharacterStatusPool();
    auto& component           = CharacterStatusPool.components();

    for (int i = 0; i < component.size(); ++i)
    {

    auto& statusComponent = component[i];
    if (statusComponent.status != CharacterStatus::None){
        if (statusComponent.remainFrame > 0){
            switch (statusComponent.status)
            {
            case CharacterStatus::Slide:
                // Update slide logic here
                break;
            case CharacterStatus::Stun:
                // Update stun logic here
                break;
            }
            statusComponent.remainFrame--;
        }
        else
        {
                statusComponent.status = CharacterStatus::None;
                statusComponent.remainFrame = 0;
        }
    }
}
    // Update character status logic here
}
