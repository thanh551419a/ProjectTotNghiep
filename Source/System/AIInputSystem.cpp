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
    auto& BallGamePlayPool = _componentStorage->GetBallGameplayPool();
    auto ballGameplay      = BallGamePlayPool.get(GameConfig::BALL);
    auto& characterPos     = _componentStorage->GetCharacterPositionPool();
    auto AI1Pos            = characterPos.get(GameConfig::OPPONENT_1);
    auto& intentPool       = _intentStorage->GetCharacterIntentPool();
    auto& ballPosPool          = _componentStorage->GetBallPositionPool();
    auto ballPos           = ballPosPool.get(GameConfig::BALL);
    CharacterIntent intent{};
    intent.moveX       = 0.0f;
    intent.finalIntent = FinalIntent::None;

    if (ballGameplay->lastTouch == GameConfig::PLAYER || ballGameplay->lastTouch == GameConfig::TEAMMATE_1 ||
        ballGameplay->lastTouch == GameConfig::TEAMMATE_2){
        // Bóng cuoi do ben player thuc hien 
        // thuc hien di chuyen ve laningX cua ballGamePlay
        float dx = ballGameplay->landingX - AI1Pos->position.x;

        if (fabs(dx) > SystemConfig::SPEED)
        {
            intent.moveX = (dx > 0.0f) ? SystemConfig::SPEED : -SystemConfig::SPEED;
        }
        else
        {
            // Đã tới vị trí
            intent.moveX       = 0.0f;
            if (ballPos->position.y < 300.0f)
                intent.finalIntent = FinalIntent::Spike;
        }
    }
    if (intent.moveX != 0.0f || intent.finalIntent != FinalIntent::None)
        intentPool.add(GameConfig::OPPONENT_1, intent);

}
