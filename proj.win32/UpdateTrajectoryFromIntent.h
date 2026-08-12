#pragma once
#include "../../ECSCore/IntentStorage/IntentStorage.h"
#include "../../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../../Utils/ObjectData.h"
#include "../../Config/Match/TrajectoryConfig.h"
#include "../../Config/Match/TrajectoryData.h"
#include "TrajectoryTestConfig.h"
#include "TrajectoryMath.h"
#include "axmol.h"
#include <fstream>

class UpdateTrajectoryFromIntent
{
private:
    IntentStorage* _intentStorage;
    ComponentStorage* _componentStorage;
    TestTrajectory& testTrajectory = TestTrajectory::getInstance();

public:
    UpdateTrajectoryFromIntent(IntentStorage* intentStorage, ComponentStorage* componentStorage);

    void update(ObjectData* data, float delta, std::ofstream* logFile);
};
