#include "FramePreparationSystem.h"
#include "Trajectory/TrajectoryMath.h"
#include "../Input/InputMapping.h"
#include "../Utils/SimLog.h"
#include <cmath>

FramePreparationSystem::FramePreparationSystem(ComponentStorage* c, IntentStorage* i)
{
    _componentStorage = c;
    _intentStorage    = i;
    Reset();
}

namespace
{
    inline void AttachBallToCharacter(ComponentStorage* componentStorage, Entity entity)
    {
        auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

        if (ballState->stateFrame != Reset)
            return;

        auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

        auto ballPos  = componentStorage->GetBallPositionPool().get(GameConfig::BALL);
        auto charPos  = componentStorage->GetCharacterPositionPool().get(entity);
        auto charSize = componentStorage->GetCharacterSizePool().get(entity);

        constexpr float GAP = 5.0f;

        float charCenterY = charPos->position.y + charSize->size.y * 0.5f;

        if (matchState->servingTeam == Team::RIGHT)
        {
            ballPos->position = {charPos->position.x - BallSize.x - GAP, charCenterY};
        }
        else
        {
            ballPos->position = {charPos->position.x + charSize->size.x + GAP, charCenterY};
        }
    }
    inline void ResetBall(ComponentStorage* componentStorage)
    {
        auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

        if (ballState->stateFrame != Reset)
            return;

        auto ballTrajectory = componentStorage->GetBallTrajectoryPool().get(GameConfig::BALL);
        auto ballPos        = componentStorage->GetBallPositionPool().get(GameConfig::BALL);
        auto matchState     = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

        ballTrajectory->type  = TrajectoryType::Parabolic;
        ballTrajectory->speed = 0.0f;
        componentStorage->SetLastBallAction(ActionState::None, BallStateFrame::Reset);

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
            ballTrajectory->b  = CalculateB(ballTrajectory->a, ballTrajectory->c, ballPos->position.x,
                                            ballPos->position.y, serveDir, /*isSpike=*/-1);
            if (ballTrajectory->a < 0.0f && ballTrajectory->c >= SystemConfig::MIN_Y)
            {
                const float root = std::sqrt(std::max(0.0f, (SystemConfig::MIN_Y - ballTrajectory->c) / ballTrajectory->a));
                ballState->landingX = -ballTrajectory->b + static_cast<float>(serveDir) * root;
            }
            else
            {
                ballState->landingX = ballPos->position.x;
            }
        }
        else
        {
            ballState->landingX = 0.0f;
        }
    }

    inline void PrepareBall(ComponentStorage* componentStorage)
    {
        auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

        if (ballState->stateFrame != Reset)
            return;

        auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

        if (matchState -> servingTeam ==  Team::LEFT) AttachBallToCharacter(componentStorage, GameConfig::PLAYER);
        else if (matchState->servingTeam == Team::RIGHT) AttachBallToCharacter(componentStorage, GameConfig::OPPONENT_1);

        ResetBall(componentStorage);
    }
    inline void ResetCharacters(ComponentStorage* componentStorage)
    {
        auto ballState = componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);

        if (ballState->stateFrame != FrameFlyUntilReset)
            return;

        auto& positionPool = componentStorage->GetCharacterPositionPool();
        auto matchState    = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

        float left   = SystemConfig::offsetX;
        float bottom = SystemConfig::offsetY;

        float netX   = left + BigRect::RECT_WIDTH * 0.5f;
        float floorY = bottom;

        constexpr float spacing = 180.0f;

        // ================= LEFT =================
        {
            int serve = matchState->leftServingEntity - GameConfig::PLAYER;

            Entity order[3] = {
                static_cast<Entity>(GameConfig::PLAYER + serve),
                static_cast<Entity>(GameConfig::PLAYER + (serve + 2) % 3),  // serve - 1
                static_cast<Entity>(GameConfig::PLAYER + (serve + 1) % 3)   // serve + 1
            };

            for (int slot = 0; slot < 3; ++slot)
            {
                positionPool.get(order[slot])->position = {netX - 400.0f + slot * spacing, floorY};
            }
        }

        // ================= RIGHT =================
        {
            int serve = matchState->rightServingEntity - GameConfig::OPPONENT_1;

            Entity order[3] = {static_cast<Entity>(GameConfig::OPPONENT_1 + (serve + 1) % 3),
                               static_cast<Entity>(GameConfig::OPPONENT_1 + (serve + 2) % 3),
                               static_cast<Entity>(GameConfig::OPPONENT_1 + serve)};

            for (int slot = 0; slot < 3; ++slot)
            {
                positionPool.get(order[slot])->position = {netX + 120.0f + slot * spacing, floorY};
            }
        }

        // Reset character action states & jump timers
        auto& actionPool   = componentStorage->GetCharacterActionStatePool();
        auto& airbornePool = componentStorage->GetCharacterAirborneStatePool();
        auto& jumpUpPool   = componentStorage->GetJumpUpFramePool();
        for (int e = 0; e < ChunkConfig::CHARACTER_PER_MATCH; ++e)
        {
            auto* action = actionPool.get(e);
            if (action)
            {
                action->status = ActionState::None;
                action->remainTime = 0.0f;
            }
            auto* airborne = airbornePool.get(e);
            if (airborne)
            {
                airborne->status = AirborneState::Grounded;
            }
            auto* jumpUp = jumpUpPool.get(e);
            if (jumpUp)
            {
                jumpUp->remainingFrames = 0;
                jumpUp->jumpUpTime      = 0.0f;
                jumpUp->apexHangTime    = 0.0f;
            }
        }
    }
    inline void SetServingEntityPosition(ComponentStorage* componentStorage)
    {
        auto matchState    = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
        auto& positionPool = componentStorage->GetCharacterPositionPool();

        Entity servingEntity;
        float servingX;

        if (matchState->servingTeam == Team::LEFT)
        {
            servingEntity = matchState->leftServingEntity;
            servingX      = BigRect::LEFT_POSITION_RESET_X;
        }
        else
        {
            servingEntity = matchState->rightServingEntity;
            servingX      = BigRect::RIGHT_POSITION_RESET_X;
        }

        auto pos = positionPool.get(servingEntity);
        if (pos == nullptr)
            return;

        pos->position.x = servingX;
    }
    inline void SetServingEntityPositionVer2(ComponentStorage* componentStorage) {
        auto& positionPool = componentStorage->GetCharacterPositionPool();
        auto matchState = componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
        Entity servingEntity;
        float servingX;
        if (matchState->servingTeam == Team::LEFT)
        {
            servingEntity = GameConfig::PLAYER;
            servingX      = BigRect::LEFT_POSITION_RESET_X;
        }
        else
        {
            servingEntity = GameConfig::OPPONENT_1;
            servingX      = BigRect::RIGHT_POSITION_RESET_X;
        }
        auto pos = positionPool.get(servingEntity);
        if (pos == nullptr)
            return;

        pos->position.x = servingX;
    }
    }  // namespace

void FramePreparationSystem::update()
{
    auto ballState  = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    auto matchState = _componentStorage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    auto& positionPool = _componentStorage->GetCharacterPositionPool();
    auto& actionPool   = _componentStorage->GetCharacterActionStatePool();
    auto& airbornePool = _componentStorage->GetCharacterAirborneStatePool();
    auto& jumpUpPool   = _componentStorage->GetJumpUpFramePool();

    const int stateFrame = ballState->stateFrame;

    // Khi vừa chuyển vào trạng thái Reset (0) sau khi kết thúc pha bóng:
    // 1. Teleport Y của toàn bộ nhân vật về mặt đất (ground = MIN_Y) và xóa trạng thái nhảy/hành động.
    // 2. Nhân vật phát bóng (servingEntity) giữ nguyên cơ chế teleport về vạch phát bóng.
    // 3. Các cầu thủ còn lại trong sân: tính toán trước xem trong 400ms (24 ticks tại 60Hz) có di chuyển kịp
    //    tới vị trí đứng chuẩn không:
    //    - Nếu khoảng cách > quãng đường đi được trong 400ms: teleport về vị trí đứng.
    //    - Nếu khoảng cách <= quãng đường đi được trong 400ms: phát sự kiện MoveLeft / MoveRight mỗi tick để đi bộ về.
    if (_prevStateFrame != BallStateFrame::Reset && stateFrame == BallStateFrame::Reset)
    {
        const float left   = SystemConfig::offsetX;
        const float bottom = SystemConfig::offsetY;
        const float netX   = left + BigRect::RECT_WIDTH * 0.5f;
        const float floorY = bottom;
        constexpr float spacing = 180.0f;

        float targetX[ChunkConfig::CHARACTER_PER_MATCH]{};

        // Vị trí đứng chuẩn đội LEFT (0, 1, 2)
        {
            int serve = matchState->leftServingEntity - GameConfig::PLAYER;
            Entity order[3] = {
                static_cast<Entity>(GameConfig::PLAYER + serve),
                static_cast<Entity>(GameConfig::PLAYER + (serve + 2) % 3),
                static_cast<Entity>(GameConfig::PLAYER + (serve + 1) % 3)
            };
            for (int slot = 0; slot < 3; ++slot)
            {
                targetX[order[slot]] = clampf(BigRect::LEFT_9M_LINE_X, netX - 400.0f + slot * spacing, SystemConfig::MAX_X_LEFT);
            }
        }

        // Vị trí đứng chuẩn đội RIGHT (3, 4, 5)
        {
            int serve = matchState->rightServingEntity - GameConfig::OPPONENT_1;
            Entity order[3] = {
                static_cast<Entity>(GameConfig::OPPONENT_1 + (serve + 1) % 3),
                static_cast<Entity>(GameConfig::OPPONENT_1 + (serve + 2) % 3),
                static_cast<Entity>(GameConfig::OPPONENT_1 + serve)
            };
            for (int slot = 0; slot < 3; ++slot)
            {
                targetX[order[slot]] = clampf(SystemConfig::MIN_X_RIGHT, netX + 120.0f + slot * spacing, BigRect::RIGHT_9M_LINE_X);
            }
        }

        // Xác định cầu thủ phát bóng (Ver2 contract: PLAYER phát cho LEFT, OPPONENT_1 phát cho RIGHT)
        const Entity servingEntity = (matchState->servingTeam == Team::LEFT) ? GameConfig::PLAYER : GameConfig::OPPONENT_1;
        const float servingX       = (matchState->servingTeam == Team::LEFT) ? BigRect::LEFT_POSITION_RESET_X : BigRect::RIGHT_POSITION_RESET_X;
        targetX[servingEntity]     = servingX;

        constexpr int kMaxWalkBackTicks = (400 * SystemConfig::TICK_RATE) / 1000; // 400ms = 24 ticks (at 60Hz)
        const auto& mapping = InputMapping::GetInstance();

        for (int e = 0; e < ChunkConfig::CHARACTER_PER_MATCH; ++e)
        {
            auto* pos = positionPool.get(e);
            if (pos)
            {
                // Bước 1: Đầu tiên teleport Y cho bằng với ground
                pos->position.y = floorY;
            }
            if (auto* action = actionPool.get(e))
            {
                action->status     = ActionState::None;
                action->remainTime = 0.0f;
            }
            if (auto* airborne = airbornePool.get(e))
            {
                airborne->status = AirborneState::Grounded;
            }
            if (auto* jumpUp = jumpUpPool.get(e))
            {
                jumpUp->remainingFrames = 0;
                jumpUp->jumpUpTime      = 0.0f;
                jumpUp->apexHangTime    = 0.0f;
            }

            if (!pos)
            {
                _walkBackTicksRemaining[e] = 0;
                continue;
            }

            // Bước 2: Với vị trí phát bóng vẫn giữ nguyên teleport
            if (static_cast<Entity>(e) == servingEntity)
            {
                pos->position.x            = targetX[e];
                _walkBackTargetX[e]        = targetX[e];
                _walkBackTicksRemaining[e] = 0;
                continue;
            }

            // Bước 3: Các cầu thủ trong sân (không phải người phát bóng) — tính toán trước khả năng di chuyển trong 400ms
            const float baseCfgSpeed = (e < 3) ? mapping.GetPlayerSpeed() : mapping.GetAISpeed();
            const float baseStatSpd  = DefaultCharacterStats::kTable[e].Speed;
            const float curStatSpd   = _componentStorage->GetCharacterSpeedStat(e);
            const float speedPerTick = baseCfgSpeed * (baseStatSpd > 0.0f ? (curStatSpd / baseStatSpd) : 1.0f);

            const float dist             = std::fabs(targetX[e] - pos->position.x);
            const float maxReachableDist = speedPerTick * static_cast<float>(kMaxWalkBackTicks);

            if (dist > maxReachableDist || dist <= 0.01f || speedPerTick <= 0.0f)
            {
                // Không kịp về trong 400ms (hoặc đã đứng sẵn ở đó) -> teleport về vị trí cần đứng
                pos->position.x            = targetX[e];
                _walkBackTargetX[e]        = targetX[e];
                _walkBackTicksRemaining[e] = 0;
            }
            else
            {
                // Kịp về trong <= 400ms -> truyền event MoveLeft / MoveRight mỗi tick
                _walkBackTargetX[e]        = targetX[e];
                _walkBackTicksRemaining[e] = kMaxWalkBackTicks;
            }
        }
    }

    // Thực thi di chuyển dần về vị trí đứng trong <= 400ms bằng cách truyền CharacterIntent (MoveLeft / MoveRight)
    if (stateFrame == BallStateFrame::Reset)
    {
        const auto& mapping = InputMapping::GetInstance();
        auto& intentPool    = _intentStorage->GetCharacterIntentPool();

        for (int e = 0; e < ChunkConfig::CHARACTER_PER_MATCH; ++e)
        {
            if (_walkBackTicksRemaining[e] <= 0)
                continue;

            auto* pos = positionPool.get(e);
            if (!pos)
            {
                _walkBackTicksRemaining[e] = 0;
                continue;
            }

            const float baseCfgSpeed = (e < 3) ? mapping.GetPlayerSpeed() : mapping.GetAISpeed();
            const float baseStatSpd  = DefaultCharacterStats::kTable[e].Speed;
            const float curStatSpd   = _componentStorage->GetCharacterSpeedStat(e);
            const float speedPerTick = baseCfgSpeed * (baseStatSpd > 0.0f ? (curStatSpd / baseStatSpd) : 1.0f);

            const float dx   = _walkBackTargetX[e] - pos->position.x;
            const float dist = std::fabs(dx);

            if (dist <= 0.01f)
            {
                pos->position.x            = _walkBackTargetX[e];
                _walkBackTicksRemaining[e] = 0;
                continue;
            }

            CharacterIntent walkIntent{};
            walkIntent.finalIntent = FinalIntent::None;
            if (dist <= speedPerTick || _walkBackTicksRemaining[e] == 1)
            {
                walkIntent.moveX           = dx;
                _walkBackTicksRemaining[e] = 0;
            }
            else
            {
                walkIntent.moveX = (dx > 0.0f) ? speedPerTick : -speedPerTick;
                _walkBackTicksRemaining[e]--;
            }
            intentPool.add(static_cast<Entity>(e), walkIntent);
        }
    }
    else
    {
        for (int e = 0; e < ChunkConfig::CHARACTER_PER_MATCH; ++e)
        {
            _walkBackTicksRemaining[e] = 0;
        }
    }

    _prevStateFrame = stateFrame;
    PrepareBall(_componentStorage);
}
