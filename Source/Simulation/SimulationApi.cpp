#include "SimulationApi.h"
#include "SimulationRuntime.h"
#include "SpikeNativeApi.h"
#include "../Input/InputMapping.h"
#include "../Config/Match/MatchEntityConfig.h"
#include "../Config/Match/MatchObjectConfig.h"
#include "../Config/System/SystemConf.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"

#include <cstring>
#include <cstdio>
#include <sstream>

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
    {
        auto& e = result.entities[0];
        e.entityId = GameConfig::PLAYER;
        std::strncpy(e.roleName, "PLAYER", sizeof(e.roleName) - 1);
        e.team = 0; // LEFT

        auto* pos = storage.GetCharacterPositionPool().get(GameConfig::PLAYER);
        if (pos)
        {
            e.flags.hasPosition = 1;
            e.position.position.x = pos->position.x;
            e.position.position.y = pos->position.y;
            e.isGrounded = (pos->position.y <= SystemConfig::MIN_Y + 0.1f) ? 1 : 0;
        }

        auto* size = storage.GetCharacterSizePool().get(GameConfig::PLAYER);
        if (size)
        {
            e.flags.hasSize = 1;
            e.size.size.x = size->size.x;
            e.size.size.y = size->size.y;
        }

        auto* jump = storage.GetJumpUpFramePool().get(GameConfig::PLAYER);
        if (jump)
        {
            e.flags.hasJumpUpFrame = 1;
            e.jumpFrame.remainingFrames = jump->remainingFrames;
        }

        auto* act = storage.GetCharacterActionStatePool().get(GameConfig::PLAYER);
        if (act)
        {
            e.flags.hasCharacterActionState = 1;
            e.actionState.status = static_cast<int32_t>(act->status);
            e.actionState.remainTime = act->remainTime;
        }

        auto* intent = intentStorage.GetCharacterIntentPool().get(GameConfig::PLAYER);
        if (intent)
        {
            e.flags.hasCharacterIntent = 1;
            e.characterIntent.moveX = intent->moveX;
            e.characterIntent.finalIntent = static_cast<int32_t>(intent->finalIntent);
            e.facingDirection = (intent->moveX < -0.1f) ? -1 : 1;
        }
        else
        {
            e.facingDirection = 1;
        }

        auto* vel = intentStorage.GetVelocityIntentPool().get(GameConfig::PLAYER);
        if (vel)
        {
            e.flags.hasVelocityIntent = 1;
            e.velocityIntent.vx = vel->vx;
            e.velocityIntent.vy = vel->vy;
        }
    }

    // --- Entity 1: OPPONENT_1 (GameConfig::OPPONENT_1 = 3) ---
    {
        auto& e = result.entities[1];
        e.entityId = GameConfig::OPPONENT_1;
        std::strncpy(e.roleName, "OPPONENT_1", sizeof(e.roleName) - 1);
        e.team = 1; // RIGHT

        auto* pos = storage.GetCharacterPositionPool().get(GameConfig::OPPONENT_1);
        if (pos)
        {
            e.flags.hasPosition = 1;
            e.position.position.x = pos->position.x;
            e.position.position.y = pos->position.y;
            e.isGrounded = (pos->position.y <= SystemConfig::MIN_Y + 0.1f) ? 1 : 0;
        }

        auto* size = storage.GetCharacterSizePool().get(GameConfig::OPPONENT_1);
        if (size)
        {
            e.flags.hasSize = 1;
            e.size.size.x = size->size.x;
            e.size.size.y = size->size.y;
        }

        auto* jump = storage.GetJumpUpFramePool().get(GameConfig::OPPONENT_1);
        if (jump)
        {
            e.flags.hasJumpUpFrame = 1;
            e.jumpFrame.remainingFrames = jump->remainingFrames;
        }

        auto* act = storage.GetCharacterActionStatePool().get(GameConfig::OPPONENT_1);
        if (act)
        {
            e.flags.hasCharacterActionState = 1;
            e.actionState.status = static_cast<int32_t>(act->status);
            e.actionState.remainTime = act->remainTime;
        }

        auto* intent = intentStorage.GetCharacterIntentPool().get(GameConfig::OPPONENT_1);
        if (intent)
        {
            e.flags.hasCharacterIntent = 1;
            e.characterIntent.moveX = intent->moveX;
            e.characterIntent.finalIntent = static_cast<int32_t>(intent->finalIntent);
            e.facingDirection = (intent->moveX > 0.1f) ? 1 : -1;
        }
        else
        {
            e.facingDirection = -1;
        }

        auto* vel = intentStorage.GetVelocityIntentPool().get(GameConfig::OPPONENT_1);
        if (vel)
        {
            e.flags.hasVelocityIntent = 1;
            e.velocityIntent.vx = vel->vx;
            e.velocityIntent.vy = vel->vy;
        }
    }

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
