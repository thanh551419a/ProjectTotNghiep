#pragma once
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Utils/ObjectData.h"
#include "../Config/Match/TrajectoryConfig.h"
#include "../Config/Match/TrajectoryData.h"
#include "TrajectoryTestConfig.h"
struct DetectionResult
{
    float distancePercent;  // khoảng cách = ? % chiều cao player
    float angle;            // độ, ngược chiều kim đồng hồ từ trục Ox
};

inline DetectionResult DetectPlayerBall(const ObjectData* data, int characterIndex , int ballIndex)
{
    // =====================================================
    // CENTER
    // =====================================================

    ax::Vec2 CharacterCenter = {data[characterIndex].pos.x + data[characterIndex].size.x * 0.5f,
                                data[characterIndex].pos.y + data[characterIndex].size.y * 0.5f};

    ax::Vec2 ballCenter = {data[ballIndex].pos.x + data[ballIndex].size.x * 0.5f,
                           data[ballIndex].pos.y + data[ballIndex].size.y * 0.5f};

    // =====================================================
    // VECTOR PLAYER -> BALL
    // =====================================================

    ax::Vec2 dir = ballCenter - CharacterCenter;

    // =====================================================
    // DISTANCE
    // =====================================================

    float distance = dir.length();

    float distancePercent = 0.0f;

    if (data[characterIndex].size.y > 0.0f)
    {
        distancePercent = distance / data[characterIndex].size.y * 100.0f;
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

   /* AXLOG("==================================================");

    AXLOG("PLAYER");
    AXLOG("Pos    : (%.2f, %.2f)", data[0].pos.x, data[0].pos.y);
    AXLOG("Size   : (%.2f, %.2f)", data[0].size.x, data[0].size.y);
    AXLOG("Center : (%.2f, %.2f)", CharacterCenter.x, CharacterCenter.y);

    AXLOG("");

    AXLOG("BALL");
    AXLOG("Pos    : (%.2f, %.2f)", data[7].pos.x, data[7].pos.y);
    AXLOG("Size   : (%.2f, %.2f)", data[7].size.x, data[7].size.y);
    AXLOG("Center : (%.2f, %.2f)", ballCenter.x, ballCenter.y);

    AXLOG("");

    AXLOG("RESULT");
    AXLOG("Distance      : %.2f", distance);
    AXLOG("Distance %%H  : %.2f%%", distancePercent);
    AXLOG("Angle         : %.2f degree", angle);*/

    AXLOG("==================================================");
    return {distancePercent, angle};
}
inline float CalculateC(float v0)
{
    return SystemConfig::MAX_C - (v0 - SystemConfig::MIN_V0) * SystemConfig::RATIO_C;
}
inline float CalculateB(float a, float c, float x0, float y0) {
    // Tính toán giá trị b dựa trên a và điểm (x0, y0)
    // Ví dụ: b = y0 - a * x0^2
    return -1*sqrt((y0 - c) / a) - x0;
}
inline void UpdateNewTrajectory(const TrajectoryData& trajectory, ComponentStorage* componentStorage)
{
    AXLOG("trajectory mới cho bóng ham Update nhan duoc: a=%f, v0=%f", trajectory.a, trajectory.v0);
    auto& ballTrajectoryPool = componentStorage->GetBallTrajectoryPool();  // lấy toàn bộ trajectory của bóng
    auto& posBallPool        = componentStorage->GetBallPositionPool();    // lấy toàn bộ position của bóng

    // Lấy tọa độ bóng ,
    auto ballPos        = posBallPool.get(GameConfig::BALL); // lấy tọa độ của bóng
    auto ballTrajectory = ballTrajectoryPool.get(GameConfig::BALL);  // lấy trajectory của bóng

    //AXLOG("Address pointer ballTrajectory trong UpdateNewTrajectory: %p", ballTrajectory);
    ballTrajectory->type = TrajectoryType::Parabolic;
    ballTrajectory->a    = trajectory.a;

    ballTrajectory->c = CalculateC(trajectory.v0);

    ballTrajectory->b = CalculateB(trajectory.a, ballTrajectory->c ,ballPos->position.x , ballPos->position.y);
    ballTrajectory->speed = (trajectory.v0 / 100.0f ) * 4.0f;
    AXLOG("Ball pos : %f %f ", ballPos->position.x, ballPos->position.y);
    AXLOG("Updated BallTrajectory: a=%f, b=%f, c=%f v0 =%f", ballTrajectory->a, ballTrajectory->b, ballTrajectory->c, ballTrajectory->speed);
}
class CheckSpikeEventSystem
{
private:
    IntentStorage* _intentStorage;
    ComponentStorage* _componentStorage;
    TestTrajectory& testTrajectory = TestTrajectory::getInstance();

public:
    CheckSpikeEventSystem(IntentStorage* intentStorage, ComponentStorage* componentStorage)
    {
        _intentStorage = intentStorage;
        _componentStorage = componentStorage;
    }
    
    void update(ObjectData* data, float delta)
    {
        // Lấy toàn bộ IntentStorage và PositionComponent của character
        auto& characterIntentPool = _intentStorage->GetCharacterIntentPool();// lấy Intent của toàn bộ character
        auto& characterPositionPool = _componentStorage->GetCharacterPositionPool();// lấy toàn bộ Postion của character
        auto& ballPool              = _componentStorage->GetBallPositionPool();// lấy tọa độ của bóng 
        auto entities = characterIntentPool.entities();
        auto intent   = characterIntentPool.components();
        for (size_t i = 0 ; i < entities.size() ; i++)
        { // duyết toàn bộ entity có intent của character
            if (intent[i].finalIntent == Spike)
            {// có tín hiệu đánh 
                //auto detection = DetectPlayerBall(data, i, 7);  // 7 là index của bóng
                DetectionResult detection;
                auto index = testTrajectory.GetXY();
                auto data  = testTrajectory.getData(index.first, index.second);
                detection.distancePercent = data.distance;
                detection.angle           = data.angle;
                AXLOG("Test với cặp index %d %d ", index.first, index.second);
                AXLOG("Co su kien character %d dap bong ,khoang cach va goc dap la : %d %d", i,detection.distancePercent, detection .angle);

                TrajectoryData newTrajectory =
                    TrajectoryConfig::Get(detection.distancePercent, detection.angle); 

                AXLOG("NewTrajectory: a=%f, v0=%f", newTrajectory.a, newTrajectory.v0);
                if (newTrajectory.a != 0.0f || newTrajectory.v0 != 0.0f)
                {
                    UpdateNewTrajectory(newTrajectory , _componentStorage);  // 7 là index của bóng   
                }
            }
        }

    }
};

