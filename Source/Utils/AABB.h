#pragma once
#include "../SimMath/Vec2.h"

using namespace SimMath;
enum class CollisionFace
{
    None,
    Top,
    Side
};
class AABBHelper
{
public:
   
    static bool CheckAABB(const SimMath::Vec2& posA, const SimMath::Size& sizeA, const SimMath::Vec2& posB, const SimMath::Size& sizeB)
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
    static CollisionFace GetCollisionFace(const SimMath::Vec2& ballPos,
                                          const SimMath::Size& ballSize,
                                          const SimMath::Vec2& netPos,
                                          const SimMath::Size& netSize)
    {
        if (!CheckAABB(ballPos, ballSize, netPos, netSize))
        {
            return CollisionFace::None;
        }

        const float netTop = netPos.y + netSize.y;

        const float ballCenterY = ballPos.y + ballSize.y * 0.5f;

        if (ballCenterY >= netTop)
            return CollisionFace::Top;

        return CollisionFace::Side;
    }
};
