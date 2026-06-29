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
    

    // reset
    intent.moveX = 0.0f;
    intent.finalIntent = FinalIntent::None;
 /*   for (auto key : input.holdingKeys)
    {
        if (key == EventKeyboard::KeyCode::KEY_A)
            AXLOG("A is holding");

        if (key == EventKeyboard::KeyCode::KEY_D)
            AXLOG("D is holding");

        if (key == EventKeyboard::KeyCode::KEY_W)
            AXLOG("W is holding");

        if (key == EventKeyboard::KeyCode::KEY_SPACE)
            AXLOG("SPACE is holding");
    }*/
    // ======================================================
    // INPUT DETECTION
    // ======================================================
    auto& posPool = _componentStorage->GetCharacterPositionPool();
    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_A) !=
        input.holdingKeys.end())
    {
        intent.moveX -= SystemConfig::SPEED;
    }

    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_D) !=
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

    if (std::find(input.pressedKeys.begin(), input.pressedKeys.end(), EventKeyboard::KeyCode::KEY_Z) !=
        input.pressedKeys.end())
    {
        intent.finalIntent = Bump;
    }

    // ======================================================
    // WRITE INTO POOL (ONLY ADD)
    // ======================================================
    //AXLOG("[Intent][Entity] moveX=%.2f jump=%s hit=%s", intent.moveX, intent.jump ? "true" : "false",
          //intent.hit ? "true" : "false");
    if (intent.moveX != 0.0f || intent.finalIntent != None)    
    {
        intentPool.add(0, intent);
    }
    //AXLOG("Đã chạy đến cuối PlayerIntent ");
    /*if(intent1 != nullptr) AXLOG("Intent moveX: %.2f, jump: %s, hit: %s", intent1->moveX, intent1->jump ? "true" : "false",
          intent1->hit ? "true" : "false");
    AXLOG("Bắt đầu sinh Intent của player dựa trên input");*/
}
