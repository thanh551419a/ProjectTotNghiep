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
        return newX;

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

    // Nếu do sai số không nằm chính xác trong đoạn, chọn nghiệm gần newX hơn và khóa chặt trong [minX, maxX]
    // để không bao giờ giật X ngược ra phía sau oldX gây kẹt bóng.
    const float chosenX = std::abs(x1 - newX) < std::abs(x2 - newX) ? x1 : x2;
    return std::clamp(chosenX, minX, maxX);
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
        const float timePerFrame = SystemConfig::TIME_PER_FRAME;
        if (jumpUpFrame->jumpUpTime > 0.0f)
        {
            const float jumping             = componentStorage->GetCharacterJumpingStat(e);
            SizeComponent* size             = sizePool.get(e);
            const float charHeight          = (size != nullptr) ? size->size.y : (2.0f * SystemConfig::PIXELS_PER_METER);
            const float totalUpwardDistance = SystemConfig::ComputeMaxJumpDisplacementAboveMinYPx(jumping, charHeight);
            const float fullJumpDuration    = SystemConfig::ComputeJumpUpDurationSeconds(jumping);

            const float prevRemTime = std::min(jumpUpFrame->jumpUpTime, fullJumpDuration);
            float nextRemTime       = jumpUpFrame->jumpUpTime - timePerFrame;
            if (nextRemTime <= 1e-5f)
            {
                nextRemTime = 0.0f;
            }
            jumpUpFrame->jumpUpTime      = nextRemTime;
            jumpUpFrame->remainingFrames = (nextRemTime > 1e-5f)
                ? static_cast<int>(std::ceil(nextRemTime / timePerFrame - 1e-4f))
                : 0;

            PositionComponent* pos = posPool.get(e);
            if (pos)
            {
                const float elapsedBefore  = std::max(0.0f, fullJumpDuration - prevRemTime);
                const float elapsedAfter   = std::max(elapsedBefore, fullJumpDuration - std::min(nextRemTime, fullJumpDuration));
                const float stepDy         = SystemConfig::ComputeJumpAscentStepDy(
                    elapsedBefore, elapsedAfter, fullJumpDuration, totalUpwardDistance);
                const float maxBottomLeftY = SystemConfig::MIN_Y + totalUpwardDistance;
                float newY                 = std::min(pos->position.y + stepDy, maxBottomLeftY);
                if (nextRemTime == 0.0f && (maxBottomLeftY - newY) <= 0.05f)
                {
                    newY = maxBottomLeftY;
                }
                pos->position.y = newY;
            }

            if (nextRemTime == 0.0f)
            {
                jumpUpFrame->apexHangTime = SystemConfig::JUMP_APEX_HANG_TIME;
            }

            // Trong pha bay lên (jumpUpTime > 0), vị trí Y chỉ phụ thuộc vào quỹ đạo nhảy lên (vy = 0)
            if (intentStorage != nullptr)
            {
                if (auto* vel = intentStorage->GetVelocityIntentPool().get(e))
                {
                    vel->vy = 0.0f;
                }
            }
        }
        else if (jumpUpFrame->apexHangTime > 0.0f)
        {
            float nextHang = jumpUpFrame->apexHangTime - timePerFrame;
            if (nextHang <= 1e-5f)
            {
                nextHang = 0.0f;
            }
            jumpUpFrame->apexHangTime = nextHang;

            // Đứng yên hoàn toàn trên không tại đỉnh nhảy trong JUMP_APEX_HANG_TIME (0.10s = 6 frames)
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
    auto rallyState = componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);

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
        // Đặc biệt ở Phase 2 của Attack Serve (stateFrame == Alive && rallyState->touchCount == 0):
        // - Nếu cầu thủ phát bóng đang nhảy trên không (pos->position.y > MIN_Y): cho phép bay vượt qua vạch 9m vào trong sân [MIN_X_LEFT, MAX_X_LEFT].
        // - Nếu cầu thủ phát bóng đang chạm đất (pos->position.y <= MIN_Y) khi chưa chạm bóng (touchCount == 0): giới hạn ở ngoài vạch 9m [MIN_X_LEFT, LEFT_9M_LINE_X]
        //   (và nếu nhảy vào sân đánh trượt rồi rơi xuống đất khi touchCount == 0 thì tự động giật về mép ngoài vạch 9m = LEFT_9M_LINE_X).
        if (BallGameplayState->stateFrame != Reset)
        {
            if (entity < ChunkConfig::CHARACTER_PER_MATCH / 2)
            {
                float maxXLeft = SystemConfig::MAX_X_LEFT;
                if (BallGameplayState->stateFrame == BallStateFrame::Alive &&
                    rallyState != nullptr && rallyState->touchCount == 0 &&
                    matchState != nullptr && matchState->servingTeam == Team::LEFT &&
                    entity == GameConfig::PLAYER && pos->position.y <= SystemConfig::MIN_Y)
                {
                    maxXLeft = BigRect::LEFT_9M_LINE_X;
                }
                float temp = clampf(SystemConfig::MIN_X_LEFT, pos->position.x, maxXLeft);

                if (temp != pos->position.x)
                    hasEvent = false;

                pos->position.x = temp;
            }
            else
            {
                float minXRight = SystemConfig::MIN_X_RIGHT;
                if (BallGameplayState->stateFrame == BallStateFrame::Alive &&
                    rallyState != nullptr && rallyState->touchCount == 0 &&
                    matchState != nullptr && matchState->servingTeam == Team::RIGHT &&
                    entity == GameConfig::OPPONENT_1 && pos->position.y <= SystemConfig::MIN_Y)
                {
                    minXRight = BigRect::RIGHT_9M_LINE_X;
                }
                float temp = clampf(minXRight, pos->position.x, SystemConfig::MAX_X_RIGHT);

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
    auto* ballState         = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    auto* matchState        = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    auto* rallyState        = componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
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

    // Nếu cầu thủ phát bóng nhảy từ ngoài vạch 9m vào sân trong lúc bóng tung lên (stateFrame == Alive && touchCount == 0)
    // nhưng đánh trượt và rơi chạm đất (pos.y <= MIN_Y khi touchCount == 0), lập tức đẩy nhân vật về mép ngoài vạch 9m (650.0f)
    // ngay cả khi frame chạm đất không có input di chuyển ngang (intent == None).
    if (ballState != nullptr && ballState->stateFrame == BallStateFrame::Alive &&
        rallyState != nullptr && rallyState->touchCount == 0 && matchState != nullptr)
    {
        if (matchState->servingTeam == Team::LEFT)
        {
            if (auto* pPos = posPool.get(GameConfig::PLAYER))
            {
                if (pPos->position.y <= SystemConfig::MIN_Y && pPos->position.x > BigRect::LEFT_9M_LINE_X)
                {
                    pPos->position.x = BigRect::LEFT_9M_LINE_X;
                }
            }
        }
        else if (matchState->servingTeam == Team::RIGHT)
        {
            if (auto* oPos = posPool.get(GameConfig::OPPONENT_1))
            {
                if (oPos->position.y <= SystemConfig::MIN_Y && oPos->position.x < BigRect::RIGHT_9M_LINE_X)
                {
                    oPos->position.x = BigRect::RIGHT_9M_LINE_X;
                }
            }
        }
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

    // Unified swept parabola-vs-net collision:
    // Expanded net X-bounds in bottom-left ball coordinates: [minNetBallX, maxNetBallX] = [netLeft - ballW, netRight].
    const float minNetBallX = MatchObjectConfig::NETPOSITION.x - MatchObjectConfig::BallSize.width;
    const float maxNetBallX = MatchObjectConfig::NETPOSITION.x + MatchObjectConfig::NETSIZE.width;
    const float netTop      = MatchObjectConfig::NETPOSITION.y + MatchObjectConfig::NETSIZE.y;
    constexpr float kNetOverlapEps = 0.05f;
    constexpr float kMaxSideTopGap = 0.20f;

    const float segMinX = std::min(oldPos.position.x, newPos.position.x);
    const float segMaxX = std::max(oldPos.position.x, newPos.position.x);

    CollisionFace face = CollisionFace::None;
    float enterX = oldPos.position.x;
    float exitX  = newPos.position.x;

    if (segMaxX >= minNetBallX && segMinX <= maxNetBallX)
    {
        if (newPos.position.x >= oldPos.position.x)
        {
            enterX = std::max(oldPos.position.x, minNetBallX);
            exitX  = std::min(newPos.position.x, maxNetBallX);
        }
        else
        {
            enterX = std::min(oldPos.position.x, maxNetBallX);
            exitX  = std::max(newPos.position.x, minNetBallX);
        }

        const auto evalClampedY = [&](float x) -> float {
            return std::max(SystemConfig::MIN_Y, trajectory->a * sqr(x + trajectory->b) + trajectory->c);
        };

        const float yAtEnter  = evalClampedY(enterX);
        const float yAtExit   = evalClampedY(exitX);
        const float minYInNet = std::min(yAtEnter, yAtExit);

        if (minYInNet <= netTop)
        {
            // Top collision ONLY if ball approached from at/above netTop AND remained >= netTop
            // at the horizontal entry X into [minNetBallX, maxNetBallX], guaranteeing x_top lies inside the net AABB.
            if (oldPos.position.y >= netTop && yAtEnter >= netTop)
            {
                face = CollisionFace::Top;
            }
            else
            {
                face = CollisionFace::Side;
            }
        }
    }

    switch (face)
    {
    case CollisionFace::Top:
    {
        // If ball already bounced from netTop and is ascending away, do not pull back to netTop
        if (newPos.position.y >= netTop && newPos.position.y >= oldPos.position.y)
        {
            break;
        }
        const float rawTopX =
            CalculateXAtY(trajectory->a, trajectory->b, trajectory->c, netTop, enterX, exitX);
        newPos.position.x = std::clamp(rawTopX, minNetBallX + kNetOverlapEps, maxNetBallX - kNetOverlapEps);
        newPos.position.y = netTop;
        break;
    }

    case CollisionFace::Side:
    {
        const float oldCenterX = oldPos.position.x + MatchObjectConfig::BallSize.width * 0.5f;
        const float netCenterX = MatchObjectConfig::NETPOSITION.x + MatchObjectConfig::NETSIZE.width * 0.5f;
        const bool fromLeft    = (trajectory->speed > 0.0f) ||
                                 (trajectory->speed == 0.0f && oldCenterX <= netCenterX);
        if (fromLeft)
        {
            newPos.position.x = minNetBallX + kNetOverlapEps;
        }
        else
        {
            newPos.position.x = maxNetBallX - kNetOverlapEps;
        }

        // Evaluate trajectory Y at side contact X and keep overlapTop >= kMaxSideTopGap > kNetOverlapEps
        // so Step 6 GetCollisionFace (overlapTop <= overlapSide) deterministically classifies Side.
        const float clampedY = trajectory->a * sqr(newPos.position.x + trajectory->b) + trajectory->c;
        const float maxSideY = netTop - kMaxSideTopGap;
        newPos.position.y    = std::clamp(clampedY, SystemConfig::MIN_Y, maxSideY);
        break;
    }

    case CollisionFace::None:
    default:
        break;
    }

    ball->position = newPos.position;

    if (hitGround)
    {
        componentStorage->TriggerSlowMotion(0);
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
            const float bounceIncrease = (impactSpeed / k) * SystemConfig::GetBounceCMultiplier();
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

    const float minNetBallX = MatchObjectConfig::NETPOSITION.x - MatchObjectConfig::BallSize.width;
    const float maxNetBallX = MatchObjectConfig::NETPOSITION.x + MatchObjectConfig::NETSIZE.width;
    const float netTop      = MatchObjectConfig::NETPOSITION.y + MatchObjectConfig::NETSIZE.y;
    constexpr float kNetOverlapEps = 0.05f;
    constexpr float kMaxSideTopGap = 0.20f;

    const float segMinX = std::min(oldPos.position.x, newPos.position.x);
    const float segMaxX = std::max(oldPos.position.x, newPos.position.x);
    if (segMaxX >= minNetBallX && segMinX <= maxNetBallX)
    {
        const float enterX = (newPos.position.x >= oldPos.position.x)
                                 ? std::max(oldPos.position.x, minNetBallX)
                                 : std::min(oldPos.position.x, maxNetBallX);
        const float exitX  = (newPos.position.x >= oldPos.position.x)
                                 ? std::min(newPos.position.x, maxNetBallX)
                                 : std::max(newPos.position.x, minNetBallX);
        const float yEnter = std::max(SystemConfig::MIN_Y, trajectory->a * enterX + trajectory->b);
        const float yExit  = std::max(SystemConfig::MIN_Y, trajectory->a * exitX + trajectory->b);
        if (std::min(yEnter, yExit) <= netTop)
        {
            const float oldCenterX = oldPos.position.x + MatchObjectConfig::BallSize.width * 0.5f;
            const float netCenterX = MatchObjectConfig::NETPOSITION.x + MatchObjectConfig::NETSIZE.width * 0.5f;
            const bool fromLeft    = (trajectory->speed > 0.0f) ||
                                     (trajectory->speed == 0.0f && oldCenterX <= netCenterX);
            newPos.position.x      = fromLeft ? (minNetBallX + kNetOverlapEps) : (maxNetBallX - kNetOverlapEps);
            newPos.position.y      = std::clamp(trajectory->a * newPos.position.x + trajectory->b,
                                                SystemConfig::MIN_Y, netTop - kMaxSideTopGap);
        }
    }

    ball->position = newPos.position;

    if (hitGround)
    {
        componentStorage->TriggerSlowMotion(0);
        trajectory->type = TrajectoryType::Parabolic;
        trajectory->a    = TrajectoryConfig::GetLinearFallbackParabolicA();
        const float impactSpeed = std::fabs(trajectory->speed);
        const float k           = SystemConfig::BOUNCE_SPEED_THRESHOLD_K;
        const float retainRatio = std::clamp((100.0f - SystemConfig::DecreaseSpeedAfterBounce) / 100.0f, 0.0f, 1.0f);
        const int bounceDir     = (trajectory->speed >= 0.0f) ? 1 : -1;

        if (impactSpeed > k && trajectory->a < 0.0f)
        {
            const float bounceIncrease = (impactSpeed / k) * SystemConfig::GetBounceCMultiplier();
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
            // xóa sạch toàn bộ quỹ đạo, vận tốc bóng và reset lastAct = None để ResetBall khởi tạo mới quỹ đạo tung bóng cơ bản.
            componentStorage->SetLastBallAction(ActionState::None, BallStateFrame::Reset);
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
        // Khi bóng đang ở trạng thái chờ phát bóng (stateFrame == Reset):
        // Giữ nguyên các hệ số parabol (a, b, c) đã được tích lũy bởi PrepareAttackServe (không xóa về 0),
        // chỉ giữ speed = 0 để bóng nằm trên tay cầu thủ phát bóng.
        componentStorage->SetLastBallAction(ActionState::None, BallStateFrame::Reset);
        ballTrajectory->type  = TrajectoryType::Parabolic;
        ballTrajectory->speed = 0.0f;
        auto* ballPos         = componentStorage->GetBallPositionPool().get(GameConfig::BALL);
        auto* matchState      = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
        if (!componentStorage->IsPreparingAttackServe() || ballTrajectory->a == 0.0f || ballTrajectory->c <= SystemConfig::MIN_Y)
        {
            const auto serveCfg = SystemConfig::GetScaledAttackServeConfig();
            ballTrajectory->a   = serveCfg.baseA;
            ballTrajectory->c   = serveCfg.baseC;
        }
        if (ballPos != nullptr)
        {
            const int serveDir = (matchState && matchState->servingTeam == Team::RIGHT) ? -1 : 1;
            if (ballTrajectory->c < ballPos->position.y)
            {
                ballTrajectory->c = ballPos->position.y;
            }
            ballTrajectory->b       = CalculateB(ballTrajectory->a, ballTrajectory->c, ballPos->position.x,
                                                 ballPos->position.y, serveDir, /*isSpike=*/-1);
            if (ballTrajectory->a < 0.0f && ballTrajectory->c >= SystemConfig::MIN_Y)
            {
                const float root = std::sqrt(std::max(0.0f, (SystemConfig::MIN_Y - ballTrajectory->c) / ballTrajectory->a));
                gameplayState->landingX = -ballTrajectory->b + static_cast<float>(serveDir) * root;
            }
            else
            {
                gameplayState->landingX = ballPos->position.x;
            }
        }
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

