#include "GenerateIntent.h"

#include <algorithm>
GenerateIntent::GenerateIntent(IntentStorage* intentStorage)
{
    _intentStorage = intentStorage; 
    playerInputSystem  = new PlayerInputSystem(_intentStorage);
    ballMovementSystem   = new BallMovementSystem(_intentStorage);
    aiInputSystem      = new AIInputSystem(_intentStorage);
    AXLOG("[ProcessInput] storage=%p", (void*)_intentStorage);
}
void GenerateIntent::update(float delta, InputListener::InputFrame input)
{
    // DEBUG: kiểm tra input có xuống đúng không
    playerInputSystem->update(delta, input);// sinh Intent muốn di chuyển đi đâu , cần nhận input và ghi vào intent Storage , vậy thì cần phải có con trỏ đến object IntentStorage đưa xuống
    aiInputSystem->update();
    ballMovementSystem->update(delta, input);
}
