#pragma once
#include "../ComponentPools/ComponentPool.h"
#include "../Components/PositionComponent.h"
#include "../Components/SizeComponent.h"
#include "../Components/JumpUpFrameComponent.h"
#include "../Components/BallTrajectoryComponent.h"
#include "../Components/CharacterStatus.h"
#include "../../Config/Match/MatchEntityConfig.h"
#include "../Components/BallGamePlayComponent.h"
class ComponentStorage
{
public:
    ComponentPool<PositionComponent> characterPositionPool;
    ComponentPool<SizeComponent> sizePool;
    ComponentPool<PositionComponent> ballPositionPool;
    ComponentPool<JumpUpFrameComponent> jumpUpFramePool;
    ComponentPool<BallTrajectoryComponent> ballTrajectoryPool;
    ComponentPool<CharacterStatusComponent> characterStatusPool;
    ComponentPool<BallGamePlayComponent> ballGameplayPool;

public:
    ComponentPool<PositionComponent>& GetCharacterPositionPool() { return characterPositionPool; }
    ComponentPool<PositionComponent>& GetBallPositionPool() { return ballPositionPool;}
    ComponentPool<SizeComponent>& GetSizePool() { return sizePool; }
    ComponentPool<JumpUpFrameComponent>& GetJumpUpFramePool() { return jumpUpFramePool; }
    ComponentPool<BallTrajectoryComponent>& GetBallTrajectoryPool() { return ballTrajectoryPool; }
    ComponentPool<CharacterStatusComponent>& GetCharacterStatusPool() { return characterStatusPool; }
    ComponentPool<BallGamePlayComponent>& GetBallGameplayPool() { return ballGameplayPool; }
    SizeComponent sizeTemp;

    // =========================
    // INIT DEMO DATA (optional)
    // =========================
    void InitDemo();
    
};
