#pragma once

#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/Components/JumpUpFrameComponent.h"
#include "../Config/Match/MatchObjectConfig.h"
#include "../System/Trajectory/TrajectoryMath.h"
#include "../Utils/AABB.h"
#include <algorithm>
#include <fstream>
#include "../Utils/SimLog.h"
inline TestTrajectory& testTrajectory = TestTrajectory::getInstance();
inline float CalculateXAtY(float a, float b, float c, float targetY, float oldX, float newX)
{
    float value = (targetY - c) / a;

    if (value < 0.0f)
        return oldX;

    float root = std::sqrt(value);

    float x1 = -b + root;
    float x2 = -b - root;

    // Chọn nghiệm nằm trên đoạn oldX -> newX
    float minX = std::min(oldX, newX);
    float maxX = std::max(oldX, newX);

    if (x1 >= minX && x1 <= maxX)
        return x1;

    if (x2 >= minX && x2 <= maxX)
        return x2;

    // Nếu do sai số không nằm chính xác trong đoạn,
    // chọn nghiệm gần newX hơn.
    return std::abs(x1 - newX) < std::abs(x2 - newX) ? x1 : x2;
}
inline const char* GetMovementEvent(const CharacterIntent& intent)
{
    if (intent.moveX < 0)
        return "MoveLeft";

    if (intent.moveX > 0)
        return "MoveRight";

    return "None";
}
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
    // apply jump theo jumpUpTime (0.75s) và chỉ số jumping (JumpPower) của từng nhân vật
    auto& posPool    = componentStorage->GetCharacterPositionPool();
    auto& sizePool   = componentStorage->GetCharacterSizePool();
    auto& jumpUpPool = componentStorage->GetJumpUpFramePool();

    for (auto e = GameConfig::PLAYER; e <= GameConfig::OPPONENT_3; ++e)
    {
        JumpUpFrameComponent* jumpUpFrame = jumpUpPool.get(e);
        if (!jumpUpFrame)
            continue;

        // Tương thích ngược nếu test khởi tạo remainingFrames mà chưa gán jumpUpTime
        if (jumpUpFrame->jumpUpTime <= 0.0f && jumpUpFrame->remainingFrames > 0)
        {
            jumpUpFrame->jumpUpTime = jumpUpFrame->remainingFrames * SystemConfig::TIME_PER_FRAME;
        }

        // Công thức tính tốc độ bay lên dựa trên jumpUpTime (0.75s) và chỉ số jumping (JumpPower):
        // - Chỉ số jumping = 100 tương đương với chiều cao nhảy lên 2m50 (2.50f * PIXELS_PER_METER).
        // - Vị trí đỉnh nhảy được lấy là: tâm của nhân vật (size.y * 0.5f) + (jumping / 100.0f) * 2.50f * PIXELS_PER_METER + ground (MIN_Y).
        // - Từ đó suy ra tổng quãng đường bay lên từ mặt đất (ground) = size.y * 0.5f + (jumping / 100.0f) * 2.50f * PIXELS_PER_METER,
        //   và tốc độ bay lên của toàn bộ các nhân vật (px/s) = tổng quãng đường bay lên / JUMP_UP_TIME (0.75s).
        // - Mỗi frame, nếu jumpUpTime > 0: jumpUpTime = max(0.0f, jumpUpTime - TimePerFrame) và nhân vật bay lên theo tốc độ * dt.
        if (jumpUpFrame->jumpUpTime > 0.0f)
        {
            const float timePerFrame = SystemConfig::TIME_PER_FRAME;
            const float dt           = std::min(jumpUpFrame->jumpUpTime, timePerFrame);
            jumpUpFrame->jumpUpTime  = std::max(0.0f, jumpUpFrame->jumpUpTime - timePerFrame);
            jumpUpFrame->remainingFrames = (jumpUpFrame->jumpUpTime > 1e-5f)
                ? static_cast<int>(std::ceil(jumpUpFrame->jumpUpTime / timePerFrame - 1e-4f))
                : 0;

            PositionComponent* pos = posPool.get(e);
            SizeComponent* size    = sizePool.get(e);
            if (pos)
            {
                const float jumping             = componentStorage->GetCharacterJumpingStat(e);
                const float charHalfHeight      = (size != nullptr) ? (size->size.y * 0.5f) : (1.0f * SystemConfig::PIXELS_PER_METER);
                const float jumpHeightPx        = (jumping / 100.0f) * SystemConfig::JUMP_STAT_REF_HEIGHT_M * SystemConfig::PIXELS_PER_METER;
                const float targetPeakY         = charHalfHeight + jumpHeightPx + SystemConfig::MIN_Y;
                const float totalUpwardDistance = targetPeakY - SystemConfig::MIN_Y;
                const float jumpUpSpeedPerSec   = totalUpwardDistance / SystemConfig::JUMP_UP_TIME;

                pos->position.y += jumpUpSpeedPerSec * dt;
            }

            // Trong pha bay lên (jumpUpTime > 0), vị trí Y chỉ phụ thuộc vào jumpUpTime và thông số jumping (không chịu GRAVITY kéo xuống)
            if (intentStorage != nullptr)
            {
                if (auto* vel = intentStorage->GetVelocityIntentPool().get(e))
                {
                    vel->vy = 0.0f;
                }
            }
        }
    }
}
inline void ApplyHorizontalMovement(IntentStorage* intentStorage, ComponentStorage* componentStorage, std::ofstream* logFile) {
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
        bool hasEvent                    = true;
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
        // Clamp cho cả trạng thái bóng đang bay (Alive = -1) lẫn khi bóng đang lăn chờ Reset (stateFrame > 0)
        // để nhân vật không bao giờ đi xuyên qua lưới sang sân đối phương trong lúc chờ Reset.
        if (BallGameplayState->stateFrame != Reset)
        {
            if (entity < ChunkConfig::CHARACTER_PER_MATCH / 2)
            {
                float temp = clampf(SystemConfig::MIN_X_LEFT, pos->position.x, SystemConfig::MAX_X_LEFT);

                if (temp != pos->position.x)
                    hasEvent = false;

                pos->position.x = temp;
            }
            else
            {
                float temp = clampf(SystemConfig::MIN_X_RIGHT, pos->position.x, SystemConfig::MAX_X_RIGHT);

                if (temp != pos->position.x)
                    hasEvent = false;

                pos->position.x = temp;
            }
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
                        float temp = clampf(SystemConfig::MIN_X_LEFT, pos->position.x, BigRect::LEFT_9M_LINE_X);

                        if (temp != pos->position.x)
                            hasEvent = false;

                        pos->position.x = temp;
                    }
                    else if (entity == GameConfig::OPPONENT_1)
                    {
                        // Đối thủ đứng sân phải
                        float temp = clampf(BigRect::NET_X, pos->position.x, BigRect::RIGHT_9M_LINE_X);

                        if (temp != pos->position.x)
                            hasEvent = false;

                        pos->position.x = temp;
                    }
                }
                else  // servingTeam == RIGHT
                {
                    if (entity == GameConfig::PLAYER)
                    {
                        // Player đứng sân trái (clampf tới MAX_X_LEFT để không bao giờ vượt qua lưới)
                        float temp = clampf(BigRect::LEFT_9M_LINE_X, pos->position.x, SystemConfig::MAX_X_LEFT);

                        if (temp != pos->position.x)
                            hasEvent = false;

                        pos->position.x = temp;
                    }
                    else if (entity == GameConfig::OPPONENT_1)
                    {
                        // Opponent phát bóng bên phải
                        float temp = clampf(BigRect::RIGHT_9M_LINE_X, pos->position.x, SystemConfig::MAX_X_RIGHT);

                        if (temp != pos->position.x)
                            hasEvent = false;

                        pos->position.x = temp;
                    }
                }
            }
        }
        if (hasEvent && logFile && logFile->is_open())
        {
            auto* playerPos   = posPool.get(GameConfig::PLAYER);
            auto* opponentPos = posPool.get(GameConfig::OPPONENT_1);

            if (!playerPos || !opponentPos)
                continue;

            const char* playerEvent   = "None";
            const char* opponentEvent = "None";

            if (entity == GameConfig::PLAYER)
            {
                playerEvent = GetMovementEvent(intent);
            }
            else if (entity == GameConfig::OPPONENT_1)
            {
                opponentEvent = GetMovementEvent(intent);
            }

            (*logFile) << testTrajectory.frame << ',' << playerPos->position.x << ',' << playerPos->position.y << ','
                       << playerEvent << ',' << opponentPos->position.x << ',' << opponentPos->position.y << ','
                       << opponentEvent << '\n';
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
inline void UpdateCharacterAirborneState(ComponentStorage& storage)
{
    auto& airbornePool = storage.GetCharacterAirborneStatePool();
    auto& posPool      = storage.GetCharacterPositionPool();
    auto& jumpUpPool   = storage.GetJumpUpFramePool();

    for (Entity e : airbornePool.entities())
    {
        auto* airborne = airbornePool.get(e);
        if (!airborne)
            continue;

        auto* jumpUpFrame = jumpUpPool.get(e);
        auto* pos         = posPool.get(e);

        if (jumpUpFrame != nullptr && (jumpUpFrame->jumpUpTime > 1e-5f || jumpUpFrame->remainingFrames > 0))
        {
            airborne->status = AirborneState::JumpUp;
        }
        else if (pos != nullptr && pos->position.y > SystemConfig::MIN_Y + 0.1f)
        {
            airborne->status = AirborneState::Fall;
        }
        else
        {
            airborne->status = AirborneState::Grounded;
        }
    }
}
inline float sqr(float a) {
    return a * a;
}
inline float max(float a, float b) {
    return a > b ? a : b;
}
inline void ApplyParabolicTrajectory(ComponentStorage* componentStorage)
{

    auto& trajectoryPool = componentStorage->GetBallTrajectoryPool();
    auto& positionPool   = componentStorage->GetBallPositionPool();

    auto trajectory = trajectoryPool.get(GameConfig::BALL);
    auto ball       = positionPool.get(GameConfig::BALL);
    if (trajectory->a == 0)
        return;
    PositionComponent oldPos = *ball;
    PositionComponent newPos = oldPos;

    newPos.position.x += trajectory->speed;
    const float rawY     = trajectory->a * sqr(newPos.position.x + trajectory->b) + trajectory->c;
    const bool hitGround = (rawY <= SystemConfig::MIN_Y);

    if (hitGround && oldPos.position.y > SystemConfig::MIN_Y)
    {
        newPos.position.x = CalculateXAtY(trajectory->a, trajectory->b, trajectory->c,
                                          SystemConfig::MIN_Y, oldPos.position.x, newPos.position.x);
    }
    newPos.position.y = std::max(rawY, SystemConfig::MIN_Y);

    PositionComponent avgPos;
    avgPos.position    = (oldPos.position + newPos.position) * 0.5f;
    CollisionFace face = AABBHelper::GetCollisionFace(avgPos.position, MatchObjectConfig::BallSize,
                                                      MatchObjectConfig::NETPOSITION, MatchObjectConfig::NETSIZE);
    if (face == CollisionFace::None)
    {
        face = AABBHelper::GetCollisionFace(newPos.position, MatchObjectConfig::BallSize,
                                            MatchObjectConfig::NETPOSITION, MatchObjectConfig::NETSIZE);
    }

    switch (face)
    {
    case CollisionFace::Top:
    {
        // AXLOG("Vi tri truoc khi va cham: x = %f, y = %f", oldPos.position.x, oldPos.position.y);
        // Va vào mặt trên của lưới
        float netTop = MatchObjectConfig::NETPOSITION.y + MatchObjectConfig::NETSIZE.y;

        newPos.position.x =
            CalculateXAtY(trajectory->a, trajectory->b, trajectory->c, netTop, oldPos.position.x, newPos.position.x);

        newPos.position.y = netTop;
        // AXLOG("Sau khi va cham: x = %f, y = %f", newPos.position.x, newPos.position.y);
        // AXLOG("Vi tri cua net la : x = %f, y = %f", MatchObjectConfig::NETPOSITION.x, MatchObjectConfig::NETPOSITION.y);
        // AXLOG("Co va cham phia tren , tinh lai x = %f", newPos.position.x);
        break;
    }

    case CollisionFace::Side:
    {
        // Va vào cạnh bên của lưới
        if (trajectory->speed > 0)
        {
            newPos.position.x = MatchObjectConfig::NETPOSITION.x - MatchObjectConfig::BallSize.width;
        }
        else
        {
            newPos.position.x = MatchObjectConfig::NETPOSITION.x + MatchObjectConfig::NETSIZE.width;
        }

        // X thay đổi → tính lại Y trên trajectory
        newPos.position.y = trajectory->a * sqr(newPos.position.x + trajectory->b) + trajectory->c;
        // AXLOG("Co va cham phia ben , tinh lai y = %f", newPos.position.y);  
        break;
    }

    case CollisionFace::None:
    default:
        break;
    }

    ball->position = newPos.position;

    if (hitGround)
    {
        const float impactSpeed = std::fabs(trajectory->speed);
        const float beforeC     = trajectory->c;
        const float k           = SystemConfig::BOUNCE_SPEED_THRESHOLD_K;
        const float retainRatio = std::clamp((100.0f - SystemConfig::DecreaseSpeedAfterBounce) / 100.0f, 0.0f, 1.0f);
        const int bounceDir     = (trajectory->speed >= 0.0f) ? 1 : -1;

        // Khi bóng chạm đất (rawY <= MIN_Y):
        // - Mỗi lần bóng nảy giảm tốc đi SystemConfig::DecreaseSpeedAfterBounce % (trajectory->speed *= retainRatio)
        // - Giai đoạn 1 (Nếu vận tốc đập |speed| > k, ví dụ khi atk lớn hơn ngưỡng như atk = 230):
        //   Đỉnh parabol c TĂNG LÊN TRƯỚC so với đỉnh c đang có theo tỉ lệ (vận_tốc_đập / k) * BOUNCE_C_MULTIPLIER.
        // - Giai đoạn 2 (Nếu |speed| <= k ở các lần chạm đất tiếp theo sau khi giảm tốc, hoặc pha đánh thường):
        //   Đỉnh parabol c MỚI GIẢM DẦN SAU trên mỗi lần chạm đất theo retainRatio cho tới khi sát đất (MIN_Y).
        if (impactSpeed > k && trajectory->a < 0.0f)
        {
            const float bounceIncrease = (impactSpeed / k) * SystemConfig::BOUNCE_C_MULTIPLIER;
            trajectory->c              = std::max(trajectory->c, SystemConfig::MIN_Y) + bounceIncrease;
            trajectory->speed         *= retainRatio;
            trajectory->b              = CalculateB(trajectory->a, trajectory->c, newPos.position.x,
                                                    SystemConfig::MIN_Y, bounceDir, /*isSpike=*/-1);
            (void)beforeC;
            // SIM_LOG("[BOUNCE_V0_DIAG] hitGround Stage1(INCREASE_C): impactSpeed=%.2f (> k=%.2f), retainRatio=%.2f, "
            //         "speedAfter=%.2f, cBefore=%.2f -> cAfter=%.2f (+%.2f)",
            //         impactSpeed, k, retainRatio, trajectory->speed, beforeC, trajectory->c, bounceIncrease);
        }
        else
        {
            const float currentHeight = std::max(0.0f, trajectory->c - SystemConfig::MIN_Y);
            const float nextHeight    = currentHeight * retainRatio;
            if (nextHeight > 5.0f && std::fabs(trajectory->speed) > 0.1f && trajectory->a < 0.0f)
            {
                trajectory->c      = SystemConfig::MIN_Y + nextHeight;
                trajectory->speed *= retainRatio;
                trajectory->b      = CalculateB(trajectory->a, trajectory->c, newPos.position.x,
                                                SystemConfig::MIN_Y, bounceDir, /*isSpike=*/-1);
                // SIM_LOG("[BOUNCE_V0_DIAG] hitGround Stage2(DECREASE_C): impactSpeed=%.2f (<= k=%.2f), retainRatio=%.2f, "
                //         "speedAfter=%.2f, cBefore=%.2f -> cAfter=%.2f",
                //         impactSpeed, k, retainRatio, trajectory->speed, beforeC, trajectory->c);
            }
            else
            {
                trajectory->c      = SystemConfig::MIN_Y;
                trajectory->b      = -newPos.position.x;
                trajectory->speed *= retainRatio;
                if (std::fabs(trajectory->speed) < 0.05f)
                {
                    trajectory->speed = 0.0f;
                }
            }
        }
    }
}
inline void ApplyLinearTrajectory(ComponentStorage* componentStorage)
{
    auto& trajectoryPool = componentStorage->GetBallTrajectoryPool();
    auto& positionPool   = componentStorage->GetBallPositionPool();

    auto trajectory = trajectoryPool.get(GameConfig::BALL);
    auto ball       = positionPool.get(GameConfig::BALL);

    PositionComponent oldPos = *ball;
    PositionComponent newPos = oldPos;

    newPos.position.x += trajectory->speed;
    const float rawY     = trajectory->a * newPos.position.x + trajectory->b;
    const bool hitGround = (rawY <= SystemConfig::MIN_Y);

    newPos.position.y = std::max(rawY, SystemConfig::MIN_Y);

    PositionComponent avgPos;
    avgPos.position = (oldPos.position + newPos.position) * 0.5f;

    if (AABBHelper::CheckAABB(avgPos.position, MatchObjectConfig::BallSize, MatchObjectConfig::NETPOSITION,
                          MatchObjectConfig::NETSIZE))
    {
        if (trajectory->speed > 0)
        {
            newPos.position.x = MatchObjectConfig::NETPOSITION.x - MatchObjectConfig::BallSize.width;
        }
        else
        {
            newPos.position.x = MatchObjectConfig::NETPOSITION.x + MatchObjectConfig::NETSIZE.width;
        }

        newPos.position.y = trajectory->a * newPos.position.x + trajectory->b;
    }

    ball->position = newPos.position;

    if (hitGround)
    {
        trajectory->type = TrajectoryType::Parabolic;
        trajectory->a    = -0.005f;
        const float impactSpeed = std::fabs(trajectory->speed);
        const float k           = SystemConfig::BOUNCE_SPEED_THRESHOLD_K;
        const float retainRatio = std::clamp((100.0f - SystemConfig::DecreaseSpeedAfterBounce) / 100.0f, 0.0f, 1.0f);
        const int bounceDir     = (trajectory->speed >= 0.0f) ? 1 : -1;

        if (impactSpeed > k && trajectory->a < 0.0f)
        {
            const float bounceIncrease = (impactSpeed / k) * SystemConfig::BOUNCE_C_MULTIPLIER;
            trajectory->c              = std::max(trajectory->c, SystemConfig::MIN_Y) + bounceIncrease;
            trajectory->speed         *= retainRatio;
            trajectory->b              = CalculateB(trajectory->a, trajectory->c, newPos.position.x,
                                                    SystemConfig::MIN_Y, bounceDir, /*isSpike=*/-1);
        }
        else
        {
            const float currentHeight = std::max(0.0f, trajectory->c - SystemConfig::MIN_Y);
            const float nextHeight    = currentHeight * retainRatio;
            if (nextHeight > 5.0f && std::fabs(trajectory->speed) > 0.1f && trajectory->a < 0.0f)
            {
                trajectory->c      = SystemConfig::MIN_Y + nextHeight;
                trajectory->speed *= retainRatio;
                trajectory->b      = CalculateB(trajectory->a, trajectory->c, newPos.position.x,
                                                SystemConfig::MIN_Y, bounceDir, /*isSpike=*/-1);
            }
            else
            {
                trajectory->c      = SystemConfig::MIN_Y;
                trajectory->b      = -newPos.position.x;
                trajectory->speed *= retainRatio;
                if (std::fabs(trajectory->speed) < 0.05f)
                {
                    trajectory->speed = 0.0f;
                }
            }
        }
    }
}

inline void ApplyBallTrajectory(IntentStorage* intentStorage, ComponentStorage* componentStorage)
{
    auto& trajectoryPool = componentStorage->GetBallTrajectoryPool();
    auto& gameplayPool   = componentStorage->GetBallGameplayStatePool();

    auto ballTrajectory = trajectoryPool.get(GameConfig::BALL);
    auto gameplayState  = gameplayPool.get(DEFAULT_MATCH);

    //==============================
    // Update Ball State
    //==============================
    if (gameplayState->stateFrame > BallStateFrame::Reset)
    {
        gameplayState->stateFrame--;
        if (gameplayState->stateFrame == BallStateFrame::Reset)
        {
            // Ngay khi hết thời gian bóng nảy/lăn sau điểm số (stateFrame vừa đếm về 0 = Reset),
            // xóa sạch toàn bộ quỹ đạo và vận tốc bóng để tránh lệch pha trạng thái 1 tick.
            ballTrajectory->type    = TrajectoryType::Parabolic;
            ballTrajectory->a       = 0.0f;
            ballTrajectory->b       = 0.0f;
            ballTrajectory->c       = 0.0f;
            ballTrajectory->speed   = 0.0f;
            gameplayState->landingX = 0.0f;
            componentStorage->TriggerSlowMotion(0);
            return;
        }
    }
    else if (gameplayState->stateFrame == BallStateFrame::Reset)
    {
        ballTrajectory->type    = TrajectoryType::Parabolic;
        ballTrajectory->a       = 0.0f;
        ballTrajectory->b       = 0.0f;
        ballTrajectory->c       = 0.0f;
        ballTrajectory->speed   = 0.0f;
        gameplayState->landingX = 0.0f;
        componentStorage->TriggerSlowMotion(0);
        return;
    }

    // AXLOG("Ball State Frame: %d", gameplayState->stateFrame);
    switch (ballTrajectory->type)
    {
    case TrajectoryType::Parabolic:
        ApplyParabolicTrajectory(componentStorage);
        break;

    case TrajectoryType::Linear:
        ApplyLinearTrajectory(componentStorage);
        break;
    }
    if (gameplayState->stateFrame > BallStateFrame::Reset)
    {
        componentStorage->TickPendingSlowMotion();
    }
    auto ballPos = componentStorage->GetBallPositionPool().get(GameConfig::BALL);
    (void)ballPos;
    // AXLOG("Ball Position: %f %f", ballPos->position.x, ballPos->position.y);
}

inline void ApplyIntentToComponent(IntentStorage* intentStorage, ComponentStorage* componentStorage, std::ofstream *logFile)
{
    //apply jump
    auto stateFrame = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH)->stateFrame;
    //AXLOG("StateFrame hiện tại : %d", stateFrame);
    ApplyJumpFrames(intentStorage, componentStorage);
    ApplyHorizontalMovement(intentStorage, componentStorage, logFile);
    ApplyVerticalVelocity(intentStorage, componentStorage);
    UpdateCharacterAirborneState(*componentStorage);
    //ApplyCharacterStatus(intentStorage, componentStorage);
    ApplyBallTrajectory(intentStorage, componentStorage);
}

