#include "BallMovementSystem.h"

BallMovementSystem::BallMovementSystem(ComponentStorage* storage)
{
    _storage = storage;
    AXLOG("[BallMovementSystem] storage=%p", (void*)_storage);
}

void BallMovementSystem::update(float delta, InputListener::InputFrame input) {

}
