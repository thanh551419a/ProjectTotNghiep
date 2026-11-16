#pragma once
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../ECSCore/IntentStorage/IntentStorage.h"
class FramePreparationSystem
{
private:
    ComponentStorage* _componentStorage;
    IntentStorage* _intentStorage;

    int _prevStateFrame = BallStateFrame::Reset;
    float _walkBackTargetX[ChunkConfig::CHARACTER_PER_MATCH]{};
    int _walkBackTicksRemaining[ChunkConfig::CHARACTER_PER_MATCH]{};

public:
    FramePreparationSystem(ComponentStorage* c, IntentStorage* i);
    void update();
    void Reset()
    {
        _prevStateFrame = BallStateFrame::Reset;
        for (int i = 0; i < ChunkConfig::CHARACTER_PER_MATCH; ++i)
        {
            _walkBackTargetX[i]        = 0.0f;
            _walkBackTicksRemaining[i] = 0;
        }
    }
};
