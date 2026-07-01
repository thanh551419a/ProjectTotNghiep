#include "AIInputSystem.h"
#include <axmol.h>
AIInputSystem::AIInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage) {
    _intentStorage = intentStorage;
    _componentStorage = componentStorage;
    _rng.seed(std::random_device{}());
}

void AIInputSystem::update()
{
    // AXLOG("đã chạy vào AI input system");
    auto& intentPool = _intentStorage->GetCharacterIntentPool();
    std::uniform_int_distribution<int> directionDist(-1, 1);
    auto& CharacterStatus = _componentStorage->GetCharacterStatusPool();
    for (Entity e = GameConfig::TEAMMATE_1; e <= GameConfig::OPPONENT_3; ++e)
    {
        int index = e - GameConfig::PLAYER;
        if (CharacterStatus.get(index) ->status != CharacterStatus::None)
        {
            // Nếu nhân vật đang trong trạng thái đặc biệt, bỏ qua việc tạo intent
            continue;
        }

        CharacterIntent intent;

        int dir = directionDist(_rng);
        if (dir < 0)
        {
            intent.moveX = -SystemConfig::SPEED;
        }
        else if (dir > 0)
        {
            intent.moveX = SystemConfig::SPEED;
        }
        else
        {
            intent.moveX = 0.0f;
            intent.finalIntent  = None;
        }

        intentPool.add(e, intent);
    }
    //AXLOG("đã chạy đến cuối AI ");


}
