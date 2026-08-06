#pragma once
#include "UpdateTrajectoryFromIntent.h"
#include "UpdateTrajectoryByCollision.h"
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
    void update(ObjectData* data, float delta, std::ofstream* logFile)
    {
        _updateTrajectoryFromIntent->update(data, delta, logFile);
        _updateTrajectoryByCollision->update(data, delta, logFile);
    }
};
