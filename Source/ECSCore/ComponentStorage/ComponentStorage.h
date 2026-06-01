#pragma once
#include "../ComponentPools/ComponentPool.h"
#include "../Components/PositionComponent.h"
#include "../Components/SizeComponent.h"
class ComponentStorage
{
public:
    ComponentPool<PositionComponent> positionPool;
    ComponentPool<SizeComponent> sizePool;

public:
    ComponentPool<PositionComponent>& GetPositionPool() { return positionPool; }

    ComponentPool<SizeComponent>& GetSizePool() { return sizePool; }
    SizeComponent sizeTemp;
    // =========================
    // INIT DEMO DATA (optional)
    // =========================
    void InitDemo();
  
};
