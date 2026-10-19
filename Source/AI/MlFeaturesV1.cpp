#include "MlFeaturesV1.h"
#include "../Config/ECS/ChunkConfig.h"

namespace
{
// x' = 2*NET_X - x cho sân phải, y hệt to_view_x() trong features.py
inline float ToViewX(float x, bool onLeft)
{
    return onLeft ? x : 2.0f * MlFeaturesV1::NET_X - x;
}

// Đại lượng có hướng (vận tốc) đổi dấu theo phía sân, y hệt to_view_sign()
inline float ToViewSign(float v, bool onLeft)
{
    return onLeft ? v : -v;
}
}  // namespace

bool MlFeaturesV1::Build(ComponentStorage* storage, GameConfig::Entity self, float* out)
{
    const bool onLeft              = self < ChunkConfig::CHARACTER_PER_MATCH / 2;
    const GameConfig::Entity other = onLeft ? GameConfig::OPPONENT_1 : GameConfig::PLAYER;

    auto* selfPos   = storage->GetCharacterPositionPool().get(self);
    auto* oppPos    = storage->GetCharacterPositionPool().get(other);
    auto* selfState = storage->GetCharacterActionStatePool().get(self);
    auto* ballPos   = storage->GetBallPositionPool().get(GameConfig::BALL);
    auto* ballTraj  = storage->GetBallTrajectoryPool().get(GameConfig::BALL);
    auto* ballState = storage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
    auto* rally     = storage->GetRallyStatePool().get(DEFAULT_MATCH);
    auto* match     = storage->GetMatchGamePlayStatePool().get(DEFAULT_MATCH);

    if (!selfPos || !oppPos || !selfState || !ballPos || !ballTraj || !ballState || !rally || !match)
        return false;

    const float selfX = ToViewX(selfPos->position.x, onLeft);
    const float ballX = ToViewX(ballPos->position.x, onLeft);

    // Parabol y = a*(x+b)^2 + c có đỉnh tại x = -b. a == 0 nghĩa là chưa có quỹ đạo xác định,
    // lúc đó features.py đặt vertex_dx = 0 chứ không tính từ b (b khi đó vô nghĩa).
    const float a        = ballTraj->a;
    const float vertexX  = ToViewX(-ballTraj->b, onLeft);
    const float vertexDx = (a != 0.0f) ? (vertexX - selfX) : 0.0f;

    // landingX mặc định là 9999 khi chưa tính được điểm rơi -> coi là không hợp lệ.
    const float landingRaw   = ballState->landingX;
    const bool landingValid  = landingRaw < LANDING_X_VALID_BELOW;
    const float landingX     = ToViewX(landingRaw, onLeft);

    const int myScore  = onLeft ? match->leftScore : match->rightScore;
    const int oppScore = onLeft ? match->rightScore : match->leftScore;
    const Team myTeam  = onLeft ? Team::LEFT : Team::RIGHT;

    out[SELF_X]             = selfX;
    out[SELF_Y]             = selfPos->position.y;
    out[OPP_X]              = ToViewX(oppPos->position.x, onLeft);
    out[SELF_DIST_TO_NET]   = NET_X - selfX;
    out[BALL_DX]            = ballX - selfX;
    out[BALL_Y]             = ballPos->position.y;
    out[BALL_VX]            = ToViewSign(ballTraj->speed, onLeft);
    out[BALL_A]             = a;
    out[BALL_C]             = ballTraj->c;
    out[BALL_VERTEX_DX]     = vertexDx;
    out[LANDING_VALID]      = landingValid ? 1.0f : 0.0f;
    out[LANDING_DX]         = landingValid ? (landingX - selfX) : 0.0f;
    out[LANDING_ON_MY_SIDE] = (landingValid && landingX < NET_X) ? 1.0f : 0.0f;
    out[BALL_ALIVE]         = (ballState->stateFrame == -1) ? 1.0f : 0.0f;
    out[TOUCH_COUNT]        = static_cast<float>(rally->touchCount);
    out[LAST_TOUCH_ME]      = (rally->lastTouch == self) ? 1.0f : 0.0f;
    out[LAST_TOUCH_OPP]     = (rally->lastTouch == other) ? 1.0f : 0.0f;
    out[CAN_ACT]            = (static_cast<int>(selfState->status) == 0) ? 1.0f : 0.0f;
    out[ACTION_REMAIN_MS]   = selfState->remainTime;
    out[MY_SERVE]           = (match->servingTeam == myTeam) ? 1.0f : 0.0f;
    out[SCORE_DIFF]         = static_cast<float>(myScore - oppScore);

    return true;
}
