#pragma once
#include "../ComponentPools/ComponentPool.h"
#include "../Components/PositionComponent.h"
#include "../Components/SizeComponent.h"
#include "../Components/JumpUpFrameComponent.h"
#include "../Components/BallTrajectoryComponent.h"
#include "../States/ActionState.h"
#include "../../Config/Match/MatchEntityConfig.h"

#include "../../Config/Match/MatchObjectConfig.h"
#include "../../Config/ECS/ChunkConfig.h"
#include "../../Config/Match/BigRect.h"
#include "../../Config/System/SystemConf.h"
#include "../../Config/Match/MatchObjectConfig.h"

#include "../States/BallGamePlayState.h"
#include "../States/MatchState.h"
#include "../States/RallyState.h"
using namespace ChunkConfig;
using namespace BigRect;
using namespace SystemConfig;
using namespace MatchObjectConfig;
class ComponentStorage
{
public:
    ComponentPool<PositionComponent> characterPositionPool;
    ComponentPool<SizeComponent> characterSizePool;
    ComponentPool<PositionComponent> ballPositionPool;
    ComponentPool<JumpUpFrameComponent> jumpUpFramePool;
    ComponentPool<BallTrajectoryComponent> ballTrajectoryPool;

    ComponentPool<CharacterActionState> characterActionStatePool;
    ComponentPool<BallGamePlayState> ballGameplayStatePool;
    ComponentPool<MatchState> matchStatePool;
    ComponentPool<RallyState> rallyStatePool;

public:
    ComponentPool<PositionComponent>& GetCharacterPositionPool() { return characterPositionPool; }
    ComponentPool<PositionComponent>& GetBallPositionPool() { return ballPositionPool;}
    ComponentPool<SizeComponent>& GetCharacterSizePool() { return characterSizePool; }
    ComponentPool<JumpUpFrameComponent>& GetJumpUpFramePool() { return jumpUpFramePool; }
    ComponentPool<BallTrajectoryComponent>& GetBallTrajectoryPool() { return ballTrajectoryPool; }

    ComponentPool<CharacterActionState>& GetCharacterActionStatePool() { return characterActionStatePool; }
    ComponentPool<BallGamePlayState>& GetBallGameplayStatePool() { return ballGameplayStatePool; }
    ComponentPool<MatchState>& GetMatchGamePlayStatePool() { return matchStatePool; }
    ComponentPool<RallyState>& GetRallyStatePool() { return rallyStatePool; }
    SizeComponent sizeTemp;
    void Reset()
    {
        characterPositionPool.clear();
        characterSizePool.clear();
        ballPositionPool.clear();
        jumpUpFramePool.clear();
        ballTrajectoryPool.clear();

        characterActionStatePool.clear();
        ballGameplayStatePool.clear();
        matchStatePool.clear();
        rallyStatePool.clear();
    }
    // =========================
    // INIT DEMO DATA (optional)
    // =========================
    void InitComponent();
    void InitState();
};
