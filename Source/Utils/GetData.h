#pragma once
#include "axmol.h"
#include "ObjectData.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"

inline ObjectData* GetData(ComponentStorage* _storage)
{
    auto& posPool  = _storage->GetPositionPool();
    auto& sizePool = _storage->GetSizePool();
    const auto& entities = posPool.entities();
    static ObjectData cache[8];
    for (int i = 0; i < entities.size(); i++)
    {
        Entity entity = entities[i];
        PositionComponent* pos = posPool.get(entity);
        SizeComponent* size    = sizePool.get(entity);
        if (!pos || !size)
        {
            continue;
        }
        cache[i].pos  = pos->position;
        cache[i].size = size->size;
    }
    return cache;
}
