#include "BallIntentSystem.h"

#pragma once
#include <axmol.h>
#include <cmath>

struct DetectionResult
{
    float distancePercent;  // khoảng cách = ? % chiều cao player
    float angle;            // độ, ngược chiều kim đồng hồ từ trục Ox
};

inline DetectionResult DetectPlayerBall(const ObjectData* data)
{
    // =====================================================
    // CENTER
    // =====================================================

    ax::Vec2 playerCenter = {data[0].pos.x + data[0].size.x * 0.5f, data[0].pos.y + data[0].size.y * 0.5f};

    ax::Vec2 ballCenter = {data[7].pos.x + data[7].size.x * 0.5f, data[7].pos.y + data[7].size.y * 0.5f};

    // =====================================================
    // VECTOR PLAYER -> BALL
    // =====================================================

    ax::Vec2 dir = ballCenter - playerCenter;

    // =====================================================
    // DISTANCE
    // =====================================================

    float distance = dir.length();

    float distancePercent = 0.0f;

    if (data[0].size.y > 0.0f)
    {
        distancePercent = distance / data[0].size.y * 100.0f;
    }

    // =====================================================
    // ANGLE
    // =====================================================

    float angle = std::atan2(dir.y, dir.x) * 180.0f / static_cast<float>(M_PI);

    // atan2 trả [-180,180]
    // đổi sang [0,360)

    if (angle < 0.0f)
    {
        angle += 360.0f;
    }

    // =====================================================
    // RESULT
    // =====================================================

    AXLOG("==================================================");

    AXLOG("PLAYER");
    AXLOG("Pos    : (%.2f, %.2f)", data[0].pos.x, data[0].pos.y);
    AXLOG("Size   : (%.2f, %.2f)", data[0].size.x, data[0].size.y);
    AXLOG("Center : (%.2f, %.2f)", playerCenter.x, playerCenter.y);

    AXLOG("");

    AXLOG("BALL");
    AXLOG("Pos    : (%.2f, %.2f)", data[7].pos.x, data[7].pos.y);
    AXLOG("Size   : (%.2f, %.2f)", data[7].size.x, data[7].size.y);
    AXLOG("Center : (%.2f, %.2f)", ballCenter.x, ballCenter.y);

    AXLOG("");

    AXLOG("RESULT");
    AXLOG("Distance      : %.2f", distance);
    AXLOG("Distance %%H  : %.2f%%", distancePercent);
    AXLOG("Angle         : %.2f degree", angle);

    AXLOG("==================================================");
    return {distancePercent, angle};
}
BallIntentSystem::BallIntentSystem(IntentStorage* intentStorage)
{
    _intentStorage = intentStorage;
    AXLOG("[BallIntentSystem] intentStorage=%p", (void*)_intentStorage);
}

void BallIntentSystem::update(float delta, InputListener::InputFrame input, ObjectData* data)
{
    ComponentPool<BallIntent>& intentPool = _intentStorage->GetBallIntentPool();
    ComponentPool<CharacterIntent>& cIntentPool = _intentStorage->GetCharacterIntentPool(); 

    BallIntent intent;

    CharacterIntent* cIntent = cIntentPool.get(0);
    if (cIntent != nullptr)  // Co intent cua player
        if (cIntent->finalIntent == Spike)
        {
            // có tín hiệu đánh ball
            // lấy data để detection
            auto result = DetectPlayerBall(data);
        }
}
