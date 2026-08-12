#pragma once
#include "../../ECSCore/IntentStorage/IntentStorage.h"
#include "../../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../../Utils/ObjectData.h"
#include "../../Config/Match/TrajectoryConfig.h"
#include "../../Config/Match/TrajectoryData.h"
#include "../TrajectoryTestConfig.h"
#include "TrajectoryUpdate.h"
#include "../../Utils/AABB.h"
#include "axmol.h"+
#include <fstream>
class UpdateTrajectoryByCollision
{
    private:
        ComponentStorage* _componentStorage = nullptr;
        IntentStorage* _intentStorage       = nullptr;

    public:
        UpdateTrajectoryByCollision(IntentStorage* intentStorage, ComponentStorage* componentStorage)
        {
            _componentStorage = componentStorage;
            _intentStorage    = intentStorage;
        }
        void update(ObjectData* data, float delta, std::ofstream* logFile);

};
