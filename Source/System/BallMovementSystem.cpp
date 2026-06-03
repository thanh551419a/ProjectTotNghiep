#include "BallMovementSystem.h"

BallMovementSystem::BallMovementSystem(IntentStorage* intentStorage)
{
    _intentStorage = intentStorage;
    AXLOG("[BallMovementSystem] intentStorage=%p", (void*)_intentStorage);
}

void BallMovementSystem::update(float delta, InputListener::InputFrame input) {
    AXLOG("đã chạy vào ball movement system");
    // Lấy position component của ball)
}
