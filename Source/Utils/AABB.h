#pragma once
#include <axmol.h>

USING_NS_AX;

class AABBHelper
{
public:
    static bool CheckAABB(const Vec2& posA, const Vec2& sizeA, const Vec2& posB, const Vec2& sizeB)
    {
        const float leftA   = posA.x;
        const float rightA  = leftA + sizeA.x;
        const float bottomA = posA.y;
        const float topA    = bottomA + sizeA.y;

        const float leftB   = posB.x;
        const float rightB  = leftB + sizeB.x;
        const float bottomB = posB.y;
        const float topB    = bottomB + sizeB.y;

        return !(rightA < leftB || leftA > rightB || topA < bottomB || bottomA > topB);
    }
};
