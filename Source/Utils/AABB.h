#pragma once
#include <axmol.h>

USING_NS_AX;

class AABB
{
public:
    static bool Check(const Vec2& posA, const Vec2& sizeA, const Vec2& posB, const Vec2& sizeB)
    {
        float leftA   = posA.x;
        float rightA  = posA.x + sizeA.x;
        float bottomA = posA.y;
        float topA    = posA.y + sizeA.y;

        float leftB   = posB.x;
        float rightB  = posB.x + sizeB.x;
        float bottomB = posB.y;
        float topB    = posB.y + sizeB.y;

        return rightA > leftB && leftA < rightB && topA > bottomB && bottomA < topB;
    }
};
