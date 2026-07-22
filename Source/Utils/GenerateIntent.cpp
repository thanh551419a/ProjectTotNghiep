#include "GenerateIntent.h"

#include <algorithm>
GenerateIntent::GenerateIntent(IntentStorage* intentStorage , ComponentStorage* componentStorage)
{
    _intentStorage = intentStorage; 
    _componentStorage = componentStorage;
    playerInputSystem  = new PlayerInputSystem(_intentStorage , componentStorage);
    player2InputSystem = new Player2InputSystem(_intentStorage, _componentStorage);
    //ballIntentSystem   = new BallIntentSystem(_intentStorage);
    aiInputSystem      = new AIInputSystem(_intentStorage, _componentStorage);
    velocityIntentSystem = new VelocityIntentSystem(_intentStorage);
    jumpStartSystem      = new JumpStartSystem(_intentStorage, _componentStorage);
    updateTrajectoryFromIntent = new UpdateTrajectoryFromIntent(_intentStorage, _componentStorage);
    updateCharacterActionStateSystem = new UpdateCharacterActionStateSystem(_componentStorage);
    AXLOG("[ProcessInput] storage=%p", (void*)_intentStorage);
}
void GenerateIntent::update(float delta, InputListener::InputFrame input)
{
    // DEBUG: kiểm tra input có xuống đúng không
    playerInputSystem->update(delta, input);// sinh Intent muốn di chuyển đi đâu , cần nhận input và ghi vào intent Storage , vậy thì cần phải có con trỏ đến object IntentStorage đưa xuống
    if(MatchRuleConfig::ENABLE_BOT) aiInputSystem->update();// sinh Intent cua AI , dựa trên Intent của player có simulation nhẹ
    else player2InputSystem->update(delta, input);
    updateCharacterActionStateSystem->update(delta);
    //characterStatusSystem->update(delta);
    jumpStartSystem->update(delta);
    auto data = GetPositionData(_componentStorage);// lấy data
    updateTrajectoryFromIntent->update(data, delta);
    velocityIntentSystem->update(delta, data);     // sinh Intent của velocity dựa trên Intent của player và AI
    //ballIntentSystem->update(delta, input , data); // sinh Intent của bóng chỉ dựa trên trajectory và delta time
}





