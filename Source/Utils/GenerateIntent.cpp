#include "GenerateIntent.h"

#include <algorithm>
GenerateIntent::GenerateIntent(IntentStorage* intentStorage , ComponentStorage* componentStorage)
{
    _intentStorage = intentStorage; 
    _componentStorage = componentStorage;
    playerInputSystem  = new PlayerInputSystem(_intentStorage);
    ballIntentSystem   = new BallIntentSystem(_intentStorage);
    aiInputSystem      = new AIInputSystem(_intentStorage);
    velocityIntentSystem = new VelocityIntentSystem(_intentStorage);
    jumpStartSystem      = new JumpStartSystem(_intentStorage, _componentStorage);
    AXLOG("[ProcessInput] storage=%p", (void*)_intentStorage);
}
void GenerateIntent::update(float delta, InputListener::InputFrame input)
{
    // DEBUG: kiểm tra input có xuống đúng không
    
    playerInputSystem->update(delta, input);// sinh Intent muốn di chuyển đi đâu , cần nhận input và ghi vào intent Storage , vậy thì cần phải có con trỏ đến object IntentStorage đưa xuống
    aiInputSystem->update();// sinh Intent cua AI , dựa trên Intent của player có simulation nhẹ
    jumpStartSystem->update(delta);
    auto data = GetData(_componentStorage);// lấy data
    velocityIntentSystem->update(delta, data);     // sinh Intent của velocity dựa trên Intent của player và AI
    ballIntentSystem->update(delta, input , data); // sinh Intent của bóng chỉ dựa trên trajectory và delta time
}
