#pragma once
#include "axmol.h"
#include "ObjectData.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Config/Match/MatchEntityConfig.h"
inline ObjectData* GetPositionData(ComponentStorage* _storage)
{
    auto& posPool  = _storage->GetCharacterPositionPool();
    auto& characterSizePool = _storage->GetCharacterSizePool();
    auto& ballPool       = _storage->GetBallPositionPool();
    const auto& entities = posPool.entities();
    static ObjectData cache[8];
    for (int i = 0; i < entities.size(); i++)
    {
        Entity entity = entities[i];
        PositionComponent* pos = posPool.get(entity);
        SizeComponent* size    = characterSizePool.get(entity);
        if (!pos || !size)
        {
            continue;
        }
        cache[i].pos  = pos->position;
        cache[i].size = size->size;
    }
    cache[6].pos = MatchObjectConfig::NETPOSITION;
    cache[6].size = MatchObjectConfig::NETSIZE;

    PositionComponent* ballPos = ballPool.get(GameConfig::BALL);

    if (ballPos)
    {
        cache[7].pos  = ballPos->position;
        cache[7].size = ax::Vec2(BallSize);
    }
    return cache;// trả về data ,
}
