#pragma once
#include <axmol.h>
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "ObjectData.h"
#include "GetData.h"
#include "../input/InputListener.h"
#include "../Config/System/SystemConf.h"
#include "../Config/Match/BigRect.h"
#include "../System/PlayerInputSystem.h"
//#include "../System/BallIntentSystem.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../System/AIInputSystem.h"
#include "../System/VelocityIntentSystem.h"
#include "../System/JumpStartSystem.h"
#include "../System/Trajectory/UpdateTrajectoryFromIntent.h"
#include "../System/CharacterStatusSystem.h"
#include "../System/UpdateCharacterActionStateSystem.h"
#include "../System/Player2InputSystem.h"
#include "../Config/Match/MatchRuleConfig.h"
#include "../System/UpdateTrajectory.h"
USING_NS_AX;
class GenerateIntent
{
private:
    IntentStorage* _intentStorage = nullptr;
    Player2InputSystem* player2InputSystem = nullptr;
    ComponentStorage* _componentStorage    = nullptr;
    PlayerInputSystem* playerInputSystem   = nullptr;
    //BallIntentSystem* ballIntentSystem     = nullptr;
    AIInputSystem* aiInputSystem           = nullptr;  // điều khiển OPPONENT_1 (sân phải)
    AIInputSystem* aiLeftInputSystem       = nullptr;  // điều khiển PLAYER (sân trái), chỉ dùng khi bot-vs-bot
    CharacterStatusSystem* characterStatusSystem           = nullptr;
    VelocityIntentSystem* velocityIntentSystem = nullptr;
    JumpStartSystem* jumpStartSystem           = nullptr;
    UpdateTrajectory* updateTrajectory                                 = nullptr;
    UpdateCharacterActionStateSystem* updateCharacterActionStateSystem = nullptr;
    // DAgger: người có đang giữ quyền điều khiển sân phải không, và còn bao nhiêu frame
    // đệm trước khi trả quyền (chỉ đếm khi lượt bóng của người đã xong).
    bool _takeoverActive    = false;
    int _takeoverFramesLeft = 0;
    // Frame vừa rồi sân phải do NGƯỜI điều khiển hay không - logger đọc để ghi o_controller.
    bool _opponentHuman = false;

    // DAgger: cập nhật _takeoverActive mỗi frame (xem GenerateIntent.cpp).
    void UpdateTakeover(const InputListener::InputFrame& input);

    public:
        GenerateIntent(IntentStorage* intentStorage, ComponentStorage* componentStorage);
        bool OpponentHumanControlled() const { return _opponentHuman; }
        void update(float delta , InputListener::InputFrame input, std::ofstream* logFile);
};
