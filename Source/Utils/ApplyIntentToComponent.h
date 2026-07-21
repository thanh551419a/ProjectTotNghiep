#pragma once

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/Components/JumpUpFrameComponent.h"
#include <algorithm>

//inline void ApplyCharacterStatus(IntentStorage* intentStorage, ComponentStorage* componentStorage)
//{
//    // lấy pool của CharacterStatusComponent
//    auto& characterActionStatePool = componentStorage->GetCharacterActionStatePool();
//    auto& characterIntentPool = intentStorage->GetCharacterIntentPool();
//    for (auto e = GameConfig::PLAYER; e <= GameConfig::OPPONENT_3; ++e)
//    {
//        int index                            = e;
//        CharacterIntent* intent                          = characterIntentPool.get(index);
//        auto statusComp                      = characterActionStatePool.get(index);
//        if (statusComp->status == ActionState::None && intent != nullptr)
//        {
//            if (intent->finalIntent == Slide)
//            {
//                statusComp->status      = ActionState::Slide;
//                statusComp->remainFrame = 30;  // set số frame còn lại cho trạng thái Slide
//            }
//        }
//        else
//        {
//            if (statusComp->remainFrame > 0)
//            {
//                uint8_t zero            = 0;
//                statusComp->remainFrame = std::max(zero, statusComp->remainFrame--);
//            }
//            
//            if (statusComp->remainFrame == 0)
//            {
//                statusComp->status = ActionState::None;
//            }
//        }
//    }
//}
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
    auto BallGameplayState   = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    // apply move left + right
    auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    Entity servingEntity;

    if (matchState->servingTeam == Team::LEFT)
    {
        servingEntity = matchState->leftServingEntity;
    }
    else
    {
        servingEntity = matchState->rightServingEntity;
    }
    auto& characterIntentPool = intentStorage->GetCharacterIntentPool();
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
        if (BallGameplayState->stateFrame == Alive)
        {
            if (entity < ChunkConfig::CHARACTER_PER_MATCH / 2)
                pos->position.x = clampf(SystemConfig::MIN_X_LEFT, pos->position.x, SystemConfig::MAX_X_LEFT);
            else
                pos->position.x = clampf(SystemConfig::MIN_X_RIGHT, pos->position.x, SystemConfig::MAX_X_RIGHT);
        }
        else
        {
            ///*if (entity == servingEntity)
            //{*/
            //    // Người phát
            //    if (matchState->servingTeam == Team::LEFT)
            //    {  
            //        pos->position.x = clampf(SystemConfig::MIN_X_LEFT, pos->position.x, BigRect::LEFT_9M_LINE_X);
            //    }
            //    else
            //    {
            //        pos->position.x = clampf(BigRect::RIGHT_9M_LINE_X, pos->position.x, SystemConfig::MAX_X_RIGHT);
            //    }
            ////}
            ////else
            ////{
            ////    // Các người còn lại
            ////    if (entity < ChunkConfig::CHARACTER_PER_MATCH / 2)
            ////    {
            ////        pos->position.x = clampf(BigRect::LEFT_9M_LINE_X, pos->position.x, BigRect::NET_X);
            ////    }
            ////    else
            ////    {
            ////        pos->position.x = clampf(BigRect::NET_X, pos->position.x, BigRect::RIGHT_9M_LINE_X);
            ////    }
            ////}
            if (BallGameplayState->stateFrame == Reset)
            {
                if (matchState->servingTeam == Team::LEFT)
                {
                    if (entity == GameConfig::PLAYER)
                    {
                        // Player phát bóng bên trái
                        pos->position.x = clampf(SystemConfig::MIN_X_LEFT, pos->position.x, BigRect::LEFT_9M_LINE_X);
                    }
                    else if (entity == GameConfig::OPPONENT_1)
                    {
                        // Đối thủ đứng sân phải
                        pos->position.x = clampf(BigRect::NET_X, pos->position.x, BigRect::RIGHT_9M_LINE_X);
                    }
                }
                else  // servingTeam == RIGHT
                {
                    if (entity == GameConfig::PLAYER)
                    {
                        // Player đứng sân trái
                        pos->position.x = clampf(BigRect::LEFT_9M_LINE_X, pos->position.x, BigRect::NET_X);
                    }
                    else if (entity == GameConfig::OPPONENT_1)
                    {
                        // Opponent phát bóng bên phải
                        pos->position.x = clampf(BigRect::RIGHT_9M_LINE_X, pos->position.x, SystemConfig::MAX_X_RIGHT);
                    }
                }
            }
        }
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
    auto& BallGameplayStatePool = componentStorage->GetBallGameplayStatePool();
    auto BallGameplayState      = BallGameplayStatePool.get(DEFAULT_MATCH);
    // debt Technology
    if (BallGameplayState->stateFrame > Reset)
        BallGameplayState->stateFrame--;
    auto ball = pos.get(GameConfig::BALL);
    if (ball->position.y <= SystemConfig::MIN_Y)
        return;
    Vec2 TempPos = ball->position;// tọa độ để tính ball 

    auto ballTrajectory = trajectory.get(GameConfig::BALL);
    if (ballTrajectory->a == 0)
        return;
    ball->position.x += ballTrajectory->speed;
    ball->position.y = ballTrajectory->a * sqr(ball->position.x + ballTrajectory->b) + ballTrajectory->c;
    ball->position.y = max(ball->position.y, SystemConfig::MIN_Y);
    //AXLOG("Ball Position: %f %f ", ball->position.x, ball->position.y);

    //debt Technology 
}

inline void ApplyIntentToComponent(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    //apply jump
    auto stateFrame = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH)->stateFrame;
    //AXLOG("StateFrame hiện tại : %d", stateFrame);
    ApplyJumpFrames(intentStorage, componentStorage);
    ApplyHorizontalMovement(intentStorage, componentStorage);
    ApplyVerticalVelocity(intentStorage, componentStorage);
    //ApplyCharacterStatus(intentStorage, componentStorage);
    ApplyBallTrajectory(intentStorage, componentStorage);
}
