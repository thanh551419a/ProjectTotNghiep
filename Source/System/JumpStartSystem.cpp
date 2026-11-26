#include "JumpStartSystem.h"
#include "../Utils/SimLog.h"

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
            // Khởi tạo thời gian nhảy lên theo công thức: jumpTime = BASE_JUMP_TIME_UP * (jump / BASE_JUMP_STAT)
            const float jumping          = _componentStorage->GetCharacterJumpingStat(entity);
            const float jumpTimeSeconds  = SystemConfig::ComputeJumpUpDurationSeconds(jumping);
            jumpUpFrame->jumpUpTime      = jumpTimeSeconds;
            jumpUpFrame->remainingFrames = SystemConfig::ComputeJumpUpFrames(jumping);
            jumpUpFrame->apexHangTime    = 0.0f;
        }
    }
    //AXLOG("đã chạy đến cuối jumpStartSystems");
}
