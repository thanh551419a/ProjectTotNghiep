#include "AIInputSystem.h"

struct Curve1
{
    float a;
    float b;
    float c;
    float v0;  // vận tốc theo trục x
};


AIInputSystem::AIInputSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    _intentStorage = intentStorage;
    _componentStorage = componentStorage;
}
void AIInputSystem::update()
{
    auto& CharacterStatus      = _componentStorage->GetCharacterActionStatePool();
    bool canGenerateIntent = CharacterStatus.get(GameConfig::OPPONENT_1)->status == ActionState::None;
    if (!canGenerateIntent)  // không còn tồn tại dư thừa từ frame trước
    {
        return;
    }
    auto& BallGamePlayStatePool = _componentStorage->GetBallGameplayStatePool();
    auto ballGameplayState      = BallGamePlayStatePool.get(DEFAULT_MATCH);
    auto rallyState            = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
    auto& characterPos     = _componentStorage->GetCharacterPositionPool();
    auto AI1Pos            = characterPos.get(GameConfig::OPPONENT_1);
    auto& intentPool       = _intentStorage->GetCharacterIntentPool();
    auto& ballPosPool          = _componentStorage->GetBallPositionPool();
    auto ballPos           = ballPosPool.get(GameConfig::BALL);
    CharacterIntent intent{};
    intent.moveX       = 0.0f;
    intent.finalIntent = FinalIntent::None;
    //AXLOG("[AIInput] ballGameplayState %p", ballGameplayState);
    if (rallyState->lastTouch == GameConfig::PLAYER || rallyState->lastTouch == GameConfig::TEAMMATE_1 ||
        rallyState->lastTouch == GameConfig::TEAMMATE_2)
    {
        // Bóng cuoi do ben player thuc hien 
        // thuc hien di chuyen ve laningX cua ballGamePlay
        float dx = ballGameplayState->landingX - AI1Pos->position.x;

        if (fabs(dx) > SystemConfig::SPEED)
        {
            intent.moveX = (dx > 0.0f) ? SystemConfig::SPEED : -SystemConfig::SPEED;
        }
        else
        {
            // Đã tới vị trí
            intent.moveX       = 0.0f;
            if (ballPos->position.y < 300.0f)
                intent.finalIntent = FinalIntent::Serve;
        }
    }
    if (intent.moveX != 0.0f || intent.finalIntent != FinalIntent::None)
        intentPool.add(GameConfig::OPPONENT_1, intent);
}
