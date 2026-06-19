#pragma once
#include "../ComponentPools/ComponentPool.h"
#include "../Components/PositionComponent.h"
#include "../Components/SizeComponent.h"
#include "../../Config/Match/MatchEntityConfig.h"
class ComponentStorage
{
public:
    ComponentPool<PositionComponent> characterPositionPool;
    ComponentPool<SizeComponent> sizePool;
    ComponentPool<PositionComponent> ballPositionPool;

public:
    ComponentPool<PositionComponent>& GetCharacterPositionPool() { return characterPositionPool; }
    ComponentPool<PositionComponent>& GetBallPositionPool() { return ballPositionPool;
    }
    ComponentPool<SizeComponent>& GetSizePool() { return sizePool; }
    SizeComponent sizeTemp;
    // =========================
    // INIT DEMO DATA (optional)
    // =========================
    void InitDemo();
    
};
