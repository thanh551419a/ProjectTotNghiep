#include "PlayerInputSystem.h"
#include <algorithm>
PlayerInputSystem::PlayerInputSystem(IntentStorage* intentStorage)
{
    _intentStorage = intentStorage;
    //AXLOG("[PlayerInputSystem] storage=%p", (void*)_storage);
}
void PlayerInputSystem::update(float delta, InputListener::InputFrame input)
{
    // DEBUG: kiểm tra input có xuống đúng không
    //ComponentPool<PositionComponent>& posPool = _storage->GetPositionPool();

    //PositionComponent* pos                 = posPool.get(1);
    //ComponentPool<SizeComponent>& sizePool = _storage->GetSizePool();
    //SizeComponent* size                    = sizePool.get(1);

    //float left  = SystemConfig::offsetX;
    //float right = SystemConfig::offsetX + BigRect::RECT_WIDTH;

    //float netX = left + BigRect::RECT_WIDTH * 0.5f;

    //if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_A) !=
    //    input.holdingKeys.end())
    //{
    //    pos->position.x -= 10.0f;
    //    //        PositionComponent* pos = posPool.get(1);

    //    pos->position.x = std::clamp(pos->position.x, left, netX - size->size.x);
    //}

    //if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_D) !=
    //    input.holdingKeys.end())
    //{
    //    // code bạn viết ở đây (MOVE RIGHT)
    //    pos->position.x += 10.0f;
    //    pos->position.x = std::clamp(pos->position.x, left, netX - size->size.x);
    //}
    ComponentPool<CharacterIntent>& intentPool = _intentStorage->GetCharacterIntentPool();
    //ComponentPool < CharacterIntent > & intentPool = _intentStorage->GetCharacterIntentPool();

    // tạo intent mới mỗi frame
    CharacterIntent intent;

    

    // reset
    intent.moveX = 0.0f;
    intent.jump  = false;
    intent.hit   = false;

    // ======================================================
    // INPUT DETECTION
    // ======================================================

    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_A) !=
        input.holdingKeys.end())
    {
        intent.moveX -= 1.0f;
    }

    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_D) !=
        input.holdingKeys.end())
    {
        intent.moveX += 1.0f;
    }

    if (std::find(input.pressedKeys.begin(), input.pressedKeys.end(), EventKeyboard::KeyCode::KEY_W) !=
        input.pressedKeys.end())
    {
        intent.jump = true;
    }

    if (std::find(input.pressedKeys.begin(), input.pressedKeys.end(), EventKeyboard::KeyCode::KEY_SPACE) !=
        input.pressedKeys.end())
    {
        intent.hit = true;
    }

    // ======================================================
    // WRITE INTO POOL (ONLY ADD)
    // ======================================================

    if (intent.moveX != 0.0f || intent.jump || intent.hit)
    {
        intentPool.add(0, intent);
    }

    AXLOG("Bắt đầu sinh Intent của player dựa trên input");
}
