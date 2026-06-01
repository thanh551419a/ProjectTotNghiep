#include "ProcessInput.h"

#include <algorithm>
ProcessInput::ProcessInput(ComponentStorage* storage)
{
    _storage = storage;
    AXLOG("[ProcessInput] storage=%p", (void*)_storage);
}
void ProcessInput::update(float delta, InputListener::InputFrame input)
{
    // DEBUG: kiểm tra input có xuống đúng không
    ComponentPool<PositionComponent>& posPool = _storage->GetPositionPool();
    
    PositionComponent* pos                   = posPool.get(1);
    ComponentPool<SizeComponent>& sizePool = _storage->GetSizePool();
    SizeComponent* size                      = sizePool.get(1);

    float left  = SystemConfig::offsetX;
    float right = SystemConfig::offsetX + BigRect::RECT_WIDTH;

    float netX = left + BigRect::RECT_WIDTH * 0.5f;

    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_A) != input.holdingKeys.end())
    {
        pos->position.x -= 10.0f;
//        PositionComponent* pos = posPool.get(1);

        pos->position.x = std::clamp(pos->position.x, left, netX - size->size.x);
    }   

    if (std::find(input.holdingKeys.begin(), input.holdingKeys.end(), EventKeyboard::KeyCode::KEY_D) != input.holdingKeys.end())
    {
        // code bạn viết ở đây (MOVE RIGHT)
        pos->position.x += 10.0f;
        pos->position.x = std::clamp(pos->position.x, left, netX - size->size.x);

    }

}
