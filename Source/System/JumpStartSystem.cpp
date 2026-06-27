#include "JumpStartSystem.h"

JumpStartSystem::JumpStartSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _intentStorage = intentStorage;
    _componentStorage = componentStorage;
}

void JumpStartSystem::update(float delta)
{
    //AXLOG("đã chạy vào đầu jumpStartSystems");
    auto& intentPool      = _intentStorage->GetCharacterIntentPool();
    auto& jumpUpFramePool = _componentStorage->GetJumpUpFramePool();
    auto& posPool         = _componentStorage->GetCharacterPositionPool();  
    const auto& entities = intentPool.entities();
    const auto& intents  = intentPool.components();
    for (size_t i = 0; i < intents.size(); ++i)
    {
        Entity entity = entities[i];// lấy entityID ở vị trí i trong mảng entities
        const CharacterIntent& intent = intents[i]; // lấy component Intent ở vị trí intent 

      //  if(intent.jump == true) AXLOG("data entity %d %f %s %s", entity , intent.moveX , intent.jump, intent.hit);
        
        JumpUpFrameComponent* jumpUpFrame = jumpUpFramePool.get(entity);
        PositionComponent* pos            = posPool.get(entity);
        //if (intent.finalIntent == Jump)
            //AXLOG("data entity %d %f %s %s %d", entity, intent.moveX, intent.jump ? "true" : "false",
              //    intent.hit ? "true" : "false", jumpUpFrame->remainingFrames);
        if (!jumpUpFrame)
            continue;
        
        if (intent.finalIntent == Jump && pos->position.y == SystemConfig::MIN_Y)
        {
            //AXLOG("co tin hieu player nhay");
            jumpUpFrame->remainingFrames = SystemConfig::JUMP_FRAME; // ví dụ nhảy lên trong 10 frame
        }
    }
    //AXLOG("đã chạy đến cuối jumpStartSystems");
}
