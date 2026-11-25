#include "SimulationApi.h"
#include "SimulationRuntime.h"
#include "SpikeNativeApi.h"
#include "../Input/InputMapping.h"
#include "../Config/Match/MatchEntityConfig.h"
#include "../Config/Match/MatchObjectConfig.h"
#include "../Config/System/SystemConf.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <cstdio>
#include <sstream>

namespace
{
void PopulateCharacterEntitySnapshot(
    SimEntityData& e,
    uint32_t entityId,
    const char* roleName,
    int32_t team,
    ComponentStorage& storage,
    IntentStorage& intentStorage)
{
    e.entityId = entityId;
    std::strncpy(e.roleName, roleName, sizeof(e.roleName) - 1);
    e.team = team;

    auto* pos = storage.GetCharacterPositionPool().get(entityId);
    if (pos)
    {
        e.flags.hasPosition = 1;
        e.position.position.x = pos->position.x;
        e.position.position.y = pos->position.y;
        e.isGrounded = (pos->position.y <= SystemConfig::MIN_Y + 0.1f) ? 1 : 0;
    }

    auto* size = storage.GetCharacterSizePool().get(entityId);
    if (size)
    {
        e.flags.hasSize = 1;
        e.size.size.x = size->size.x;
        e.size.size.y = size->size.y;
    }

    auto* jump = storage.GetJumpUpFramePool().get(entityId);
    if (jump)
    {
        e.flags.hasJumpUpFrame = 1;
        e.jumpFrame.remainingFrames = jump->remainingFrames;
    }

    auto* airborne = storage.GetCharacterAirborneStatePool().get(entityId);
    if (airborne)
    {
        e.airborneState = static_cast<int16_t>(airborne->status);
    }
    else
    {
        e.airborneState = (jump && jump->remainingFrames > 0) ? 1 : (e.isGrounded ? 0 : 2);
    }

    auto* act = storage.GetCharacterActionStatePool().get(entityId);
    if (act)
    {
        e.flags.hasCharacterActionState = 1;
        e.actionState.status = static_cast<int32_t>(act->status);
        e.actionState.remainTime = act->remainTime;
    }

    auto* intent = intentStorage.GetCharacterIntentPool().get(entityId);
    if (intent)
    {
        e.flags.hasCharacterIntent = 1;
        e.characterIntent.moveX = intent->moveX;
        e.characterIntent.finalIntent = static_cast<int32_t>(intent->finalIntent);
        if (team == 0)
        {
            e.facingDirection = (intent->moveX < -0.1f) ? -1 : 1;
        }
        else
        {
            e.facingDirection = (intent->moveX > 0.1f) ? 1 : -1;
        }
    }
    else
    {
        e.facingDirection = (team == 0) ? 1 : -1;
    }

    auto* vel = intentStorage.GetVelocityIntentPool().get(entityId);
    if (vel)
    {
        e.flags.hasVelocityIntent = 1;
        e.velocityIntent.vx = vel->vx;
        e.velocityIntent.vy = vel->vy;
    }

    // Authoritative Character AnimId + Skill Cooldown Metadata (offsets 124..143)
    const ActionState actStatus = act ? act->status : ActionState::None;
    const float actRemainMs     = act ? act->remainTime : 0.0f;
    const bool isMovingHoriz    = (std::fabs(e.characterIntent.moveX) > 0.05f) ||
                                  (std::fabs(e.velocityIntent.vx) > 0.05f);

    int32_t animId = SIM_ANIM_IDLE;
    if (actStatus == ActionState::Serve)
    {
        animId = SIM_ANIM_SERVE;
    }
    else if (actStatus != ActionState::None && actRemainMs > 0.0f)
    {
        switch (actStatus)
        {
        case ActionState::Spike:
            animId = SIM_ANIM_SPIKE;
            break;
        case ActionState::Bump:
        case ActionState::Set:
            animId = SIM_ANIM_BUMP; // NEVER Slide
            break;
        case ActionState::SpikeLight:
            animId = SIM_ANIM_SPIKE_LIGHT;
            break;
        case ActionState::SpikeMedium:
            animId = SIM_ANIM_SPIKE_MEDIUM;
            break;
        case ActionState::SpikeStrong:
            animId = SIM_ANIM_SPIKE_STRONG;
            break;
        case ActionState::Jump:
            animId = SIM_ANIM_JUMP_UP;
            break;
        default:
            if (!e.isGrounded)
                animId = (e.airborneState == SIM_AIRBORNE_FALL) ? SIM_ANIM_FALL : SIM_ANIM_JUMP_UP;
            else
                animId = isMovingHoriz ? SIM_ANIM_MOVE : SIM_ANIM_IDLE;
            break;
        }
    }
    else if (!e.isGrounded)
    {
        animId = (e.airborneState == SIM_AIRBORNE_FALL) ? SIM_ANIM_FALL : SIM_ANIM_JUMP_UP;
    }
    else
    {
        animId = isMovingHoriz ? SIM_ANIM_MOVE : SIM_ANIM_IDLE;
    }

    int32_t cooldownAction   = SIM_ACTION_NONE;
    float cooldownRemainMs   = 0.0f;
    float cooldownDurationMs = 0.0f;

    if (actRemainMs > 0.0f &&
        (actStatus == ActionState::Bump ||
         actStatus == ActionState::Spike ||
         actStatus == ActionState::SpikeLight ||
         actStatus == ActionState::SpikeMedium ||
         actStatus == ActionState::SpikeStrong))
    {
        cooldownAction     = static_cast<int32_t>(actStatus);
        cooldownRemainMs   = actRemainMs;
        const float jumpSt = storage.GetCharacterJumpingStat(entityId);
        cooldownDurationMs = std::max(
            actRemainMs,
            ActionStateInfo::ComputeCooldownMsForJumpStat(actStatus, jumpSt));
    }

    e.characterPresentation.animId             = animId;
    e.characterPresentation.cooldownAction     = cooldownAction;
    e.characterPresentation.cooldownRemainMs   = cooldownRemainMs;
    e.characterPresentation.cooldownDurationMs = cooldownDurationMs;
    e.characterPresentation.reserved           = 0;
}
} // namespace

extern "C" {

SIM_API data Get_data(Input inputSend)
{
    auto& rt = SimulationRuntime::GetInstance();
    if (!rt.IsInitialized())
    {
        rt.Initialize(nullptr);
    }

    // 1. Process Input via button-ID array and InputMapping
    if (inputSend.IsEmpty())
    {
        // Empty input: skip player-input submission.
        // Clear buttons and axis so PlayerInputSystem does not generate false intent.
        rt.SetInput(GameConfig::PLAYER, 0, 0.0f);
    }
    else
    {
        // Non-empty input: map button IDs to SimInputFrame flags using InputMapping
        uint32_t buttonMask = 0;
        float moveAxisX = 0.0f;
        const auto& mapping = InputMapping::GetInstance();

        for (uint32_t i = 0; i < inputSend.buttonCount && i < MAX_INPUT_BUTTONS; ++i)
        {
            uint32_t btnId = inputSend.buttonIds[i];
            GameAction act = GameAction::None;
            if (mapping.MapButtonToAction(static_cast<int32_t>(btnId), act))
            {
                uint32_t mask = mapping.ActionToButtonMask(act);
                buttonMask |= mask;
                if (act == GameAction::MoveLeft)
                {
                    moveAxisX -= 1.0f;
                }
                else if (act == GameAction::MoveRight)
                {
                    moveAxisX += 1.0f;
                }
            }
        }

        rt.SetInput(GameConfig::PLAYER, buttonMask, moveAxisX);
    }

    // 2. Advance exactly one simulation tick (fixed 60 Hz = 1/60 s)
    rt.Update(1.0f / 60.0f);

    // 3. Build snapshot for exactly four entities: PLAYER, OPPONENT_1, BALL, NET
    data result{};
    result.status = SIM_OK;
    result.tick = rt.GetTick();
    result.entityCount = 4;

    auto& storage = const_cast<ComponentStorage&>(rt.GetComponentStorage());
    auto& intentStorage = const_cast<IntentStorage&>(rt.GetIntentStorage());

    // --- Entity 0: PLAYER (GameConfig::PLAYER = 0) ---
    PopulateCharacterEntitySnapshot(result.entities[0], GameConfig::PLAYER, "PLAYER", 0, storage, intentStorage);

    // --- Entity 1: OPPONENT_1 (GameConfig::OPPONENT_1 = 3) ---
    PopulateCharacterEntitySnapshot(result.entities[1], GameConfig::OPPONENT_1, "OPPONENT_1", 1, storage, intentStorage);

    // --- Entity 2: BALL (GameConfig::BALL = 7) ---
    {
        auto& e = result.entities[2];
        e.entityId = GameConfig::BALL;
        std::strncpy(e.roleName, "BALL", sizeof(e.roleName) - 1);
        e.team = -1;

        auto* pos = storage.GetBallPositionPool().get(GameConfig::BALL);
        if (pos)
        {
            e.flags.hasPosition = 1;
            e.position.position.x = pos->position.x;
            e.position.position.y = pos->position.y;
            e.isGrounded = (pos->position.y <= SystemConfig::MIN_Y + 0.1f) ? 1 : 0;
        }

        e.flags.hasSize = 1;
        e.size.size.x = MatchObjectConfig::BallSize.x;
        e.size.size.y = MatchObjectConfig::BallSize.y;

        auto* traj = storage.GetBallTrajectoryPool().get(GameConfig::BALL);
        if (traj)
        {
            e.flags.hasBallTrajectory = 1;
            e.ballTrajectory.type = static_cast<int32_t>(traj->type);
            e.ballTrajectory.speed = traj->speed;
            e.ballTrajectory.a = traj->a;
            e.ballTrajectory.b = traj->b;
            e.ballTrajectory.c = traj->c;
            e.facingDirection = (traj->speed >= 0.0f) ? 1 : -1;
        }
        else
        {
            e.facingDirection = 1;
        }

        auto* bState = storage.GetBallGameplayStatePool().get(DEFAULT_MATCH);
        if (bState)
        {
            e.flags.hasBallGameplayState = 1;
            e.ballGameplayState.landingX = bState->landingX;
            e.ballGameplayState.stateFrame = bState->stateFrame;
            e.ballGameplayState.collisionState = static_cast<int32_t>(bState->collisionState);
        }

        // Export Ball.lastAct in e.actionState.status (offset 100) and shouldZoomInCamera in e.characterIntent.finalIntent (offset 112)
        // without setting hasCharacterActionState / hasCharacterIntent flags (preserving exact 156B SimEntityData & 4772B data ABI).
        const int32_t ballStateFrame = bState ? bState->stateFrame : 0;
        const ActionState lastBallAct = (ballStateFrame == 0) ? ActionState::None : storage.GetLastBallAction();
        const bool zoomInCam = SystemConfig::ShouldZoomInCameraForBallAction(static_cast<uint8_t>(lastBallAct), ballStateFrame);
        e.actionState.status          = static_cast<int32_t>(lastBallAct);
        e.characterIntent.finalIntent = zoomInCam ? 1 : 0;

        if (traj)
        {
            e.flags.hasVelocityIntent = 1;
            e.velocityIntent.vx = traj->speed;
            float vy = 0.0f;
            if (std::fabs(traj->speed) > 1e-4f && std::fabs(traj->a) > 1e-7f && pos)
            {
                if (traj->type == TrajectoryType::Parabolic)
                {
                    const float instSlope = 2.0f * traj->a * (pos->position.x + traj->b);
                    const float instVy    = instSlope * traj->speed;
                    if (bState && bState->stateFrame == -1 && bState->landingX > 0.0f)
                    {
                        const SimMath::Vec2 contactPos = storage.GetLastBallContactPos();
                        const float x0 = (contactPos.y > 0.0f) ? contactPos.x : pos->position.x;
                        const float y0 = (contactPos.y > 0.0f) ? contactPos.y : pos->position.y;
                        const float dx = std::fabs(bState->landingX - x0);
                        if (dx > 1.0f)
                        {
                            const float xVertex = -traj->b;
                            const float minSegX = std::min(x0, bState->landingX);
                            const float maxSegX = std::max(x0, bState->landingX);
                            float dyTotal = std::max(0.0f, y0 - SystemConfig::MIN_Y);
                            if (xVertex > minSegX + 1.0f && xVertex < maxSegX - 1.0f && traj->c > y0)
                            {
                                dyTotal = std::max(0.0f, traj->c - y0) + std::max(0.0f, traj->c - SystemConfig::MIN_Y);
                            }
                            const float vyMag = std::fabs(traj->speed) * (dyTotal / dx);
                            vy = (instVy >= 0.0f) ? vyMag : -vyMag;
                        }
                        else
                        {
                            vy = instVy;
                        }
                    }
                    else
                    {
                        vy = instVy;
                    }
                }
                else
                {
                    vy = traj->a * traj->speed;
                }
            }
            e.velocityIntent.vy = vy;
        }
    }

    // --- Entity 3: NET (GameConfig::NET = 6) ---
    {
        auto& e = result.entities[3];
        e.entityId = GameConfig::NET;
        std::strncpy(e.roleName, "NET", sizeof(e.roleName) - 1);
        e.team = -1;
        e.isGrounded = 1;
        e.facingDirection = 1;

        // Authoritative geometry from MatchObjectConfig
        e.flags.hasPosition = 1;
        e.position.position.x = MatchObjectConfig::NETPOSITION.x;
        e.position.position.y = MatchObjectConfig::NETPOSITION.y;

        e.flags.hasSize = 1;
        e.size.size.x = MatchObjectConfig::NETSIZE.x;
        e.size.size.y = MatchObjectConfig::NETSIZE.y;
    }

    // --- Authoritative Match & Rally State ---
    auto* mState = storage.GetMatchGamePlayStatePool().get(DEFAULT_MATCH);
    if (mState)
    {
        result.matchState.leftScore = mState->leftScore;
        result.matchState.rightScore = mState->rightScore;
        result.matchState.servingTeam = static_cast<int32_t>(mState->servingTeam);
        result.matchState.leftServingEntity = mState->leftServingEntity;
        result.matchState.rightServingEntity = mState->rightServingEntity;
        result.matchState.finished = mState->finished ? 1 : 0;
        result.matchState.matchWinner = static_cast<int32_t>(mState->matchWinner);
    }

    auto* rState = storage.GetRallyStatePool().get(DEFAULT_MATCH);
    if (rState)
    {
        result.rallyState.lastTouch = rState->lastTouch;
        result.rallyState.touchCount = rState->touchCount;
    }
    const int slowMoFrames = storage.GetSlowMoFramesRemaining();
    result.rallyState.slowMoFramesRemaining = static_cast<uint8_t>(slowMoFrames < 0 ? 0 : (slowMoFrames > 255 ? 255 : slowMoFrames));
    const float contactHeightPx = storage.GetLastSpikeContactHeight();
    const float clampedHeightPx = contactHeightPx < 0.0f ? 0.0f : (contactHeightPx > 65535.0f ? 65535.0f : contactHeightPx);
    result.rallyState.spikeContactHeight = static_cast<uint16_t>(clampedHeightPx + 0.5f);

    // --- Formatted JSON Output ---
    std::snprintf(result.formattedJson, sizeof(result.formattedJson),
        "{\"tick\":%llu,\"entityCount\":4,\"entities\":["
        "{\"id\":%u,\"role\":\"%s\",\"team\":%d,\"pos\":[%.2f,%.2f],\"size\":[%.2f,%.2f],\"action\":%d,\"remainTime\":%.1f,\"jumpFrames\":%d,\"intentMoveX\":%.2f,\"finalIntent\":%d,\"isGrounded\":%d},"
        "{\"id\":%u,\"role\":\"%s\",\"team\":%d,\"pos\":[%.2f,%.2f],\"size\":[%.2f,%.2f],\"action\":%d,\"remainTime\":%.1f,\"jumpFrames\":%d,\"intentMoveX\":%.2f,\"finalIntent\":%d,\"isGrounded\":%d},"
        "{\"id\":%u,\"role\":\"%s\",\"team\":%d,\"pos\":[%.2f,%.2f],\"size\":[%.2f,%.2f],\"traj\":{\"type\":%d,\"speed\":%.2f,\"a\":%.4f,\"b\":%.2f,\"c\":%.2f},\"landingX\":%.2f,\"stateFrame\":%d,\"collisionState\":%d},"
        "{\"id\":%u,\"role\":\"%s\",\"team\":%d,\"pos\":[%.2f,%.2f],\"size\":[%.2f,%.2f]}"
        "],\"match\":{\"score\":[%d,%d],\"servingTeam\":%d,\"finished\":%d,\"winner\":%d,\"lastTouch\":%u,\"touches\":%u}}",
        (unsigned long long)result.tick,
        result.entities[0].entityId, result.entities[0].roleName, result.entities[0].team,
        result.entities[0].position.position.x, result.entities[0].position.position.y,
        result.entities[0].size.size.x, result.entities[0].size.size.y,
        result.entities[0].actionState.status, result.entities[0].actionState.remainTime,
        result.entities[0].jumpFrame.remainingFrames,
        result.entities[0].characterIntent.moveX, result.entities[0].characterIntent.finalIntent,
        result.entities[0].isGrounded,
        result.entities[1].entityId, result.entities[1].roleName, result.entities[1].team,
        result.entities[1].position.position.x, result.entities[1].position.position.y,
        result.entities[1].size.size.x, result.entities[1].size.size.y,
        result.entities[1].actionState.status, result.entities[1].actionState.remainTime,
        result.entities[1].jumpFrame.remainingFrames,
        result.entities[1].characterIntent.moveX, result.entities[1].characterIntent.finalIntent,
        result.entities[1].isGrounded,
        result.entities[2].entityId, result.entities[2].roleName, result.entities[2].team,
        result.entities[2].position.position.x, result.entities[2].position.position.y,
        result.entities[2].size.size.x, result.entities[2].size.size.y,
        result.entities[2].ballTrajectory.type, result.entities[2].ballTrajectory.speed,
        result.entities[2].ballTrajectory.a, result.entities[2].ballTrajectory.b, result.entities[2].ballTrajectory.c,
        result.entities[2].ballGameplayState.landingX, result.entities[2].ballGameplayState.stateFrame, result.entities[2].ballGameplayState.collisionState,
        result.entities[3].entityId, result.entities[3].roleName, result.entities[3].team,
        result.entities[3].position.position.x, result.entities[3].position.position.y,
        result.entities[3].size.size.x, result.entities[3].size.size.y,
        result.matchState.leftScore, result.matchState.rightScore,
        result.matchState.servingTeam, result.matchState.finished, result.matchState.matchWinner,
        result.rallyState.lastTouch, (unsigned int)result.rallyState.touchCount
    );

    return result;
}

SIM_API int32_t Sim_GetCharacterStats(uint32_t entityId, float* outAttack, float* outDefense, float* outSpeed, float* outJump)
{
    if (!RuntimeCharacterStats::GetInstance().GetStat(entityId, outAttack, outDefense, outSpeed, outJump))
        return SIM_ERR_INVALID_ARGUMENT;
    return SIM_OK;
}

SIM_API int32_t Sim_SetCharacterStats(uint32_t entityId, float attack, float defense, float speed, float jump)
{
    if (!RuntimeCharacterStats::GetInstance().SetStat(entityId, attack, defense, speed, jump))
        return SIM_ERR_INVALID_ARGUMENT;
    return SIM_OK;
}

SIM_API int32_t Sim_ResetCharacterStats()
{
    RuntimeCharacterStats::GetInstance().ResetToLoaded();
    return SIM_OK;
}

SIM_API int32_t Sim_GetSlowMoFramesRemaining()
{
    return SimulationRuntime::GetInstance().GetComponentStorage().GetSlowMoFramesRemaining();
}

SIM_API float Sim_GetLastSpikeContactHeight()
{
    return SimulationRuntime::GetInstance().GetComponentStorage().GetLastSpikeContactHeight();
}

SPIKE_API SystemConfig::SimAttackServeConfig* Sim_GetAttackServeConfigPtr()
{
    return &SystemConfig::g_attackServeConfig;
}

} // extern "C"

namespace Simulation
{

bool Init()
{
    return SimulationRuntime::GetInstance().Initialize(nullptr);
}

void Reset()
{
    SimulationRuntime::GetInstance().Reset();
}

void Shutdown()
{
    SimulationRuntime::GetInstance().Shutdown();
}

JsonData Update(const UpdateParameters& parameters)
{
    data d = ::Get_data(parameters.input);
    return JsonData{std::string(d.formattedJson)};
}

JsonData ReceiveData(const Input& input, TypeInput typeInput)
{
    return Update(UpdateParameters{input, typeInput});
}

} // namespace Simulation
