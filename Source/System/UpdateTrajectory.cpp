#include "UpdateTrajectory.h"
void UpdateTrajectory::update(ObjectData* data, float delta, std::ofstream* logFile)
{
    _updateTrajectoryFromIntent->update(data, delta, logFile);
    _updateTrajectoryByCollision->update(data, delta, logFile);
}
