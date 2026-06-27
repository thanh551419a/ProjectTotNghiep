#include "AIInputSystem.h"
#include <axmol.h>
AIInputSystem::AIInputSystem(IntentStorage* intentStorage) {
    _intentStorage = intentStorage;
    _rng.seed(std::random_device{}());
}

void AIInputSystem::update()
{
    // AXLOG("đã chạy vào AI input system");
    auto& intentPool = _intentStorage->GetCharacterIntentPool();

    std::uniform_int_distribution<int> directionDist(-1, 1);

    for (Entity e = GameConfig::TEAMMATE_1; e <= GameConfig::OPPONENT_3; ++e)
    {
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
