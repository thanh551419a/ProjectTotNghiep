#pragma once
#include "Trajectory/UpdateTrajectoryFromIntent.h"
#include "Trajectory/UpdateTrajectoryByCollision.h"
class UpdateTrajectory
{
private:
    UpdateTrajectoryFromIntent* _updateTrajectoryFromIntent = nullptr;
    UpdateTrajectoryByCollision* _updateTrajectoryByCollision = nullptr;
    ComponentStorage* _componentStorage                       = nullptr;
    IntentStorage* _intentStorage                             = nullptr;

public:
    UpdateTrajectory(IntentStorage* intentStorage, ComponentStorage* componentStorage)
    {
        _updateTrajectoryFromIntent = new UpdateTrajectoryFromIntent(intentStorage, componentStorage);
        _updateTrajectoryByCollision = new UpdateTrajectoryByCollision(intentStorage , componentStorage);
    }
    void update(ObjectData* data, float delta, std::ofstream* logFile);
};
