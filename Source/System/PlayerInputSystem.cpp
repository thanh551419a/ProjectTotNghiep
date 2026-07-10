#include "PlayerInputSystem.h"
#include <algorithm>
PlayerInputSystem::PlayerInputSystem(IntentStorage* intentStorage , ComponentStorage* componentStorage)
{
    _componentStorage = componentStorage;
    _intentStorage = intentStorage;
    //AXLOG("[PlayerInputSystem] storage=%p", (void*)_storage);
}

void PlayerInputSystem::update(float delta, InputListener::InputFrame input)
{
    ComponentPool<CharacterIntent>& intentPool = _intentStorage->GetCharacterIntentPool();
    //ComponentPool < CharacterIntent > & intentPool = _intentStorage->GetCharacterIntentPool();
    // tạo intent mới mỗi frame
    CharacterIntent intent;

    //AXLOG("intentPool addr = %p", &intentPool);
    auto& CharacterStatus = _componentStorage->GetCharacterActionStatePool();
    bool canGenerateIntent = CharacterStatus.get(GameConfig::PLAYER)->status == ActionState::None;
    if (!canGenerateIntent)  // còn tồn tại dư thừa từ frame trước
    {
        return;
    }
    
    // reset
    intent.moveX = 0.0f;
    intent.finalIntent = FinalIntent::None;
    // ======================================================
    // INPUT DETECTION
    // ======================================================
    auto& posPool = _componentStorage->GetCharacterPositionPool();
    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_LEFT_ARROW) !=
        input.holdingKeys.end())
    {
        intent.moveX -= SystemConfig::SPEED;
    }

    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_RIGHT_ARROW) !=
        input.holdingKeys.end())
    {
        intent.moveX += SystemConfig::SPEED;
    }

    if (std::find(input.pressedKeys.begin(), input.pressedKeys.end(), EventKeyboard::KeyCode::KEY_SPACE) !=
        input.pressedKeys.end())
        if (posPool.get(0)->position.y == SystemConfig::MIN_Y)
        {
            //AXLOG("co tin hieu jump");
            // Ấn phím Space và nhân vật đang ở trên mặt đất
            intent.finalIntent = Jump;
        }
        else
        {
            intent.finalIntent = Spike;
        }

    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_E) !=
        input.holdingKeys.end())
    {
        intent.finalIntent = Bump;
    }
    //if (std::find(input.pressedKeys.begin(), input.pressedKeys.end(), EventKeyboard::KeyCode::KEY_C) !=
    //    input.pressedKeys.end())
    //{
    //    intent.finalIntent = Slide;
    //}
    if (std::find(input.pressedKeys.begin(), input.pressedKeys.end(), EventKeyboard::KeyCode::KEY_R) !=
        input.pressedKeys.end())
    {
        intent.finalIntent = Serve;
    }
    // ======================================================
    // WRITE INTO POOL (ONLY ADD)
    // ======================================================
    //AXLOG("[Intent][Entity] moveX=%.2f jump=%s hit=%s", intent.moveX, intent.jump ? "true" : "false",
          //intent.hit ? "true" : "false");
    if (intent.moveX != 0.0f || intent.finalIntent != None)    
    {
      /*  AXLOG("[PlayerInputSystem] Adding intent: moveX=%.2f, finalIntent=%d", intent.moveX,
              static_cast<int>(intent.finalIntent));*/
        intentPool.add(0, intent);
       // AXLOG("intentPool size = %d", intentPool.size());
    }
    //AXLOG("Đã chạy đến cuối PlayerIntent ");
    /*if(intent1 != nullptr) AXLOG("Intent moveX: %.2f, jump: %s, hit: %s", intent1->moveX, intent1->jump ? "true" : "false",
          intent1->hit ? "true" : "false");
    AXLOG("Bắt đầu sinh Intent của player dựa trên input");*/
}
