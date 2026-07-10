#include "CharacterStatusSystem.h"

CharacterStatusSystem::CharacterStatusSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _intentStorage = intentStorage;
    _componentStorage = componentStorage;
}

void CharacterStatusSystem::update(float delta)
{
    auto& CharacterStatusPool = _componentStorage->GetCharacterActionStatePool();
    auto& component           = CharacterStatusPool.components();

    for (int i = 0; i < component.size(); ++i)
    {

    auto& statusComponent = component[i];
    if (statusComponent.status != ActionState::None){
        if (statusComponent.remainFrame > 0){
            switch (statusComponent.status)
            {
            case ActionState::Slide:
                // Update slide logic here
                break;
            case ActionState::Stun:
                // Update stun logic here
                break;
            }
            statusComponent.remainFrame--;
        }
        else
        {
            statusComponent.status      = ActionState::None;
            statusComponent.remainFrame = 0;
        }
    }
}
    // Update character status logic here
}
