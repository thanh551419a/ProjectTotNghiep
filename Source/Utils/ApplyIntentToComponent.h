#pragma once

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/Components/JumpUpFrameComponent.h"

inline void ApplyJumpFrames(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    // apply jump
    auto& posPool    = componentStorage->GetCharacterPositionPool();
    auto& jumpUpPool = componentStorage->GetJumpUpFramePool();

    // chỉ cần duyệt entity đang có intent
    const auto& jumpUpFrames = jumpUpPool.components();
    // duyệt toàn bộ entity có trong jumpUpFramePool
    for (auto e = GameConfig::PLAYER; e <= GameConfig::OPPONENT_3; ++e)
    {
        JumpUpFrameComponent* jumpUpFrame = jumpUpPool.get(e);
        if (!jumpUpFrame)
            continue;
        if (jumpUpFrame->remainingFrames > 0)
        {
            // nếu còn frame nhảy thì giảm y xuống
            PositionComponent* pos = posPool.get(e);
            if (pos)
            {
                pos->position.y += 2 * (-1) * SystemConfig::GRAVITY;
            }
            jumpUpFrame->remainingFrames--;
        }
    }
}
inline void ApplyHorizontalMovement(IntentStorage* intentStorage, ComponentStorage* componentStorage) {
    auto& posPool    = componentStorage->GetCharacterPositionPool();
    auto& jumpUpPool = componentStorage->GetJumpUpFramePool();
    // apply move left + right
    auto characterIntentPool = intentStorage->GetCharacterIntentPool();
    // lay toan bo entity co trong CharacterIntentPool
    auto entitiesInCIP = characterIntentPool.entities();
    for (size_t i = 0; i < entitiesInCIP.size(); ++i)
    {
        Entity entity                 = entitiesInCIP[i];
        const CharacterIntent& intent = characterIntentPool.components()[i];
        PositionComponent* pos        = posPool.get(entity);
        if (!pos)
            continue;
        // ===== APPLY LOGIC TỐI THIỂU =====
        if (pos->position.y > SystemConfig::MIN_Y)
            pos->position.x += intent.moveX / 2;
        else
        {
            pos->position.x += intent.moveX;
        }
        // if (pos->position.y > SystemConfig::MIN_Y) // dang o tren khong trung
        //{

        //    pos->position.x += -1*(intent.moveX / abs(intent.moveX)) * SystemConfig::SPEED/2;  // giam toc do di
        //    chuyen tren khong trung
        //}
        if (entity < 3)
            pos->position.x = clampf(SystemConfig::MIN_X_LEFT, pos->position.x, SystemConfig::MAX_X_LEFT);
        else if (entity < 6)
            pos->position.x = clampf(SystemConfig::MIN_X_RIGHT, pos->position.x, SystemConfig::MAX_X_RIGHT);
    }
}
inline void ApplyVerticalVelocity(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    auto& posPool           = componentStorage->GetCharacterPositionPool();
    auto velocityIntentPool = intentStorage->GetVelocityIntentPool();
    const auto& entities1   = velocityIntentPool.entities();
    const auto& intents1    = velocityIntentPool.components();
    // if(intents1.size()) AXLOG("có chạy vào apply intent  , số lượng intent=%zu", intents1.size());
    for (size_t i = 0; i < intents1.size(); ++i)
    {
        Entity entity                = entities1[i];
        const VelocityIntent& intent = intents1[i];
        if (i == 0)
        {
            //AXLOG("Intent cua entity=%d => vx=%.2f vy=%.2f", entity, intent.vx, intent.vy);
        }
        // AXLOG("VelocityIntent => vx=%.2f vy=%.2f", intent.vx, intent.vy);
        PositionComponent* pos = posPool.get(entity);  // lấy tọa độ
        if (!pos)
            continue;

        // ===== APPLY LOGIC TỐI THIỂU =====
        pos->position.y = std::max(SystemConfig::MIN_Y, pos->position.y + intent.vy);
    }
    if (velocityIntentPool.has(0))  // neu player co intent
    {
        auto Player = posPool.get(0);
        //AXLOG("Toa do Player : %f %f", Player->position.x, Player->position.y);
    }
}
inline float sqr(float a) {
    return a * a;
}
inline float max(float a, float b) {
    return a > b ? a : b;
}
inline void ApplyBallTrajectory(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    auto& trajectory = componentStorage->GetBallTrajectoryPool();
    auto& pos    = componentStorage->GetBallPositionPool();
    // debt Technology
    auto ball = pos.get(GameConfig::BALL);
    if (ball->position.y <= SystemConfig::MIN_Y)
        return;
    auto ballTrajectory = trajectory.get(GameConfig::BALL);
    if (ballTrajectory->a == 0)
        return;
    ball->position.x += ballTrajectory->speed;
    ball->position.y = ballTrajectory->a * sqr(ball->position.x + ballTrajectory->b) + ballTrajectory->c;
    ball->position.y = max(ball->position.y, SystemConfig::MIN_Y);
    AXLOG("Ball Position: %f %f ", ball->position.x, ball->position.y);
    //debt Technology 
}
inline void ResetBall(IntentStorage* intentStorage , ComponentStorage* componentStorage) {

    auto& pos = componentStorage->GetBallPositionPool();
    auto ball = pos.get(GameConfig::BALL);
    if (ball->position.y <= SystemConfig::MIN_Y)
    {
        AXLOG("Chạy vào reset Ball rồi ");
        // Reset position
        float left  = SystemConfig::offsetX;
        float right = SystemConfig::offsetX + BigRect::RECT_WIDTH;

        float bottom = SystemConfig::offsetY;
        float top    = SystemConfig::offsetY + BigRect::RECT_HEIGHT;

        float netX     = left + BigRect::RECT_WIDTH * 0.5f;
        float floorY   = bottom;
        ball->position = Vec2(netX - GameConfig::BallSize * 0.5f , floorY + 350.0f);
        // Reset Trajectory
        auto& trajectory      = componentStorage->GetBallTrajectoryPool();
        auto ballTrajectory   = trajectory.get(GameConfig::BALL);
        auto& BallGamePlayPool = componentStorage->GetBallGameplayPool();
        auto ballGameplay      = BallGamePlayPool.get(GameConfig::BALL);
     //   AXLOG("lấy được con trỏ của ball Trajectory");
        ballTrajectory->type  = TrajectoryType::Parabolic;
        ballTrajectory->a     = 0.0f;
        ballTrajectory->b     = 0.0f;
        ballTrajectory->c     = 0.0f;
        ballTrajectory->speed = 0.0f;
        float x               = netX - GameConfig::BallSize * 0.5f - 100.0f;
        float y               = floorY + 350.0f - 200.0f;
        ballGameplay->lastTouch = GameConfig::NONE;
        ballGameplay->landingX  = 0;
        AXLOG("Position after Reset: %f %f", (netX - GameConfig::BallSize * 0.5f - 100.0f),
            (floorY + 350.0f - 200.0f));
    }
}
inline void ApplyIntentToComponent(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    //apply jump
    ResetBall(intentStorage, componentStorage);
    ApplyJumpFrames(intentStorage, componentStorage);
    ApplyHorizontalMovement(intentStorage, componentStorage);
    ApplyVerticalVelocity(intentStorage, componentStorage);
    ApplyBallTrajectory(intentStorage, componentStorage);
}
