#pragma once

#include "../ComponentStorage/ComponentStorage.h"
#include "../IntentStorage/IntentStorage.h"
#include "../StateStorage/StateStorage.h"

class Storage
{
public:
    ComponentStorage componentStorage;
    IntentStorage intentStorage;
    StateStorage stateStorage;

    void Init();
    void Reset();
};
