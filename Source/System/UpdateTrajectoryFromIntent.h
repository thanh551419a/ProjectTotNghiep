#pragma once
#include "../ECSCore/IntentStorage/IntentStorage.h"
#include "../ECSCore/ComponentStorage/ComponentStorage.h"
#include "../Utils/ObjectData.h"
#include "../Config/Match/TrajectoryConfig.h"
#include "../Config/Match/TrajectoryData.h"
#include "TrajectoryTestConfig.h"
#include "axmol.h"
USING_NS_AX;
using namespace TrajectoryConfig;
struct DetectionResult
{
    float distancePercent;  // khoảng cách = ? % chiều cao player
    float angle;            // độ, ngược chiều kim đồng hồ từ trục Ox
};
struct TrajectoryBoost
{
    float v0Multiplier = 1.0f;
    float aFlattenFactor;
};
inline DetectionResult DetectPlayerBall(const ObjectData* data, int characterIndex , int ballIndex, Size PercentChange )
{
    // =====================================================
    // CENTER
    // =====================================================

    Size SizeDelta;
    SizeDelta.x              = (data[characterIndex].size.x * PercentChange.x) / 100;
    SizeDelta.y              = (data[characterIndex].size.y * PercentChange.y) / 100;
    ax::Vec2 CharacterCenter = {data[characterIndex].pos.x + data[characterIndex].size.x * 0.5f + SizeDelta.x / 2,
                                data[characterIndex].pos.y + data[characterIndex].size.y * 0.5f + SizeDelta.y / 2};

    ax::Vec2 ballCenter = {data[ballIndex].pos.x + data[ballIndex].size.x * 0.5f,
                           data[ballIndex].pos.y + data[ballIndex].size.y * 0.5f};

    // =====================================================
    // VECTOR PLAYER -> BALL
    // 
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

   // AXLOG("==================================================");
    return {distancePercent, angle};
}
inline float CalculateC(float v0)
{
    return SystemConfig::MAX_C - (v0 - SystemConfig::MIN_V0) * SystemConfig::RATIO_C;
}
inline float CalculateB(float a, float c,
                        float x0, float y0,
                        int direction,
                        int isSpike)
{
    float delta = sqrt((y0 - c) / a);

    float largerRoot  =  delta - x0;
    float smallerRoot = -delta - x0;

    if (direction == -1)
        return isSpike ? smallerRoot : largerRoot;
    else // direction == -1
        return isSpike ? largerRoot : smallerRoot;
}
inline Vec2 CalculateLandingPoint(const Vec2& startPos, const BallTrajectoryComponent& trajectory, float groundY)
{
    if (trajectory.a == 0.0f)
    {
        return startPos;
    }

    float value = (groundY - trajectory.c) / trajectory.a;

    if (value < 0.0f)
    {
        return startPos;
    }

    float root = sqrtf(value);

    float x1 = -trajectory.b + root;
    float x2 = -trajectory.b - root;

    float landingX = (trajectory.speed > 0.0f) ? std::max(x1, x2) : std::min(x1, x2);

    return {landingX, groundY};
}

inline void UpdateLandingX(ComponentStorage* componentStorage)
{
    //AXLOG("da chay vao landingX");
    auto& trajectoryPool = componentStorage->GetBallTrajectoryPool();
    auto& positionPool   = componentStorage->GetBallPositionPool();
    auto& BallGamelayStatePool   = componentStorage->GetBallGameplayStatePool();

    auto ballTrajectory = trajectoryPool.get(GameConfig::BALL);
    auto ballPosition   = positionPool.get(GameConfig::BALL);
    auto ballGameplayState   = BallGamelayStatePool.get(DEFAULT_MATCH);
    //AXLOG("trajectory = %p", ballTrajectory);
    //AXLOG("position   = %p", ballPosition);
    //AXLOG("gameplay   = %p", ballGameplayState);
    //if (ballTrajectory == nullptr || ballPosition == nullptr || ballGameplayState == nullptr)
    //{
    //    return;
    //}

    Vec2 landingPos = CalculateLandingPoint(ballPosition->position, *ballTrajectory, SystemConfig::MIN_Y);

    ballGameplayState->landingX = landingPos.x;
    //AXLOG("[UpdateLaningX] ballGameplayState %p", ballGameplayState);
    //AXLOG("Landing X : %.2f", landingPos.x);
}
inline void UpdateNewTrajectory(const TrajectoryData& trajectory,
                                ComponentStorage* componentStorage,
                                int reverseDirection,
                                int decreaseC,
                                int powerSpike,
                                TrajectoryBoost trajectoryBoost)
{
    AXLOG("trajectory mới cho bóng ham Update nhan duoc: a=%f, v0=%f", trajectory.a, trajectory.v0);
    auto& ballTrajectoryPool = componentStorage->GetBallTrajectoryPool();  // lấy toàn bộ trajectory của bóng
    auto& posBallPool        = componentStorage->GetBallPositionPool();    // lấy toàn bộ position của bóng

    // Lấy tọa độ bóng ,
    auto ballPos        = posBallPool.get(GameConfig::BALL); // lấy tọa độ của bóng
    auto ballTrajectory = ballTrajectoryPool.get(GameConfig::BALL);  // lấy trajectory của bóng

    //AXLOG("Address pointer ballTrajectory trong UpdateNewTrajectory: %p", ballTrajectory);
    ballTrajectory->type = TrajectoryType::Parabolic;
    ballTrajectory->a    = trajectory.a / trajectoryBoost.aFlattenFactor;

    float c = CalculateC(trajectory.v0);
    if (ballPos->position.y > c)
    {
        c = ballPos->position.y;
    }
    ballTrajectory->c = c - c*decreaseC/100;
    ballTrajectory->b     = CalculateB(ballTrajectory->a, ballTrajectory->c, ballPos->position.x, ballPos->position.y,
                                       reverseDirection, powerSpike);
    ballTrajectory->speed = reverseDirection*(trajectory.v0 / 100.0f ) * 4.0f * trajectoryBoost.v0Multiplier;
    UpdateLandingX(componentStorage);
    /*AXLOG("Ball pos : %f %f ", ballPos->position.x, ballPos->position.y);
    AXLOG("Updated BallTrajectory: a=%f, b=%f, c=%f v0 =%f", ballTrajectory->a, ballTrajectory->b, ballTrajectory->c, ballTrajectory->speed);*/
}
class UpdateTrajectoryFromIntent
{
private:
    IntentStorage* _intentStorage;
    ComponentStorage* _componentStorage;
    TestTrajectory& testTrajectory = TestTrajectory::getInstance();

public:
    UpdateTrajectoryFromIntent(IntentStorage* intentStorage, ComponentStorage* componentStorage)
    {
        _intentStorage = intentStorage;
        _componentStorage = componentStorage;
    }
    
    void update(ObjectData* data, float delta)
    {
        // Lấy toàn bộ IntentStorage và PositionComponent của character
        auto& characterIntentPool = _intentStorage->GetCharacterIntentPool();

        auto intent   = characterIntentPool.components();
        auto entities = characterIntentPool.entities();

        auto rallyState = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);

        auto ball = _componentStorage->GetBallPositionPool().get(GameConfig::BALL);

        auto ballGamePlay     = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
        auto direction        = (data[7].pos.x - data[6].pos.x) > 0
                                    ? -1
                                    : 1;  // tính toán hướng di chuyển của ball , phải sang trái or trái sang phải 
        ActionState lastEvent;
        TrajectoryBoost boost = TrajectoryBoost{1.0, 1.0};
        int powerSpike     = 0;
        //auto& entities = characterIntentPool.entities();
        for (size_t i = 0 ; i < entities.size() ; i++)
        { // duyệt toàn bộ entity có intent của character
            //AXLOG("Intent cua character %d la : %d", i, intent[i].finalIntent);
            lastEvent   = ActionState::None;// reset mỗi vòng
            auto index = entities[i];
            boost      = TrajectoryBoost{1.0, 1.0};
            powerSpike = 0;
            //AXLOG("Intent = %d, direction = %d", intent[i].finalIntent, direction);
            //AXLOG("index của entity này là : %d", index);
            if (index == 7)
                continue;
            if (intent[i].finalIntent == Spike)
            {// có tín hiệu đánh 
                auto detection = DetectPlayerBall(data, index, 7, Size{0,0});  // 7 là index của bóng
                 // tăng 10 lần v0 và giảm a đi 100 lần
                //AXLOG("Player Pos va ball Pos: %f %f %f %f", data[0].pos.x, data[0].pos.y, data[7].pos.x,
                      //data[7].pos.y);
                //AXLOG("Detection distance: %f  angle :%f", detection.distancePercent,detection.angle );
                if (index == 0)
                    testTrajectory.AttackPower = 180;
                else
                    testTrajectory.AttackPower = 150;
                /*DetectionResult detection;
                auto index = testTrajectory.GetXY();
                auto trajectoryData  = testTrajectory.getData(index.first, index.second);
                detection.distancePercent = trajectoryData.distance;
                detection.angle           = trajectoryData.angle;*/
                //AXLOG("Test với cặp index %d %d ", index.first, index.second);
                //AXLOG("Co su kien character %d dap bong ,khoang cach va goc dap la : %d %d", i,detection.distancePercent, detection .angle);

                TrajectoryData newTrajectory =
                    TrajectoryConfig::Get(detection.distancePercent, detection.angle); 

                AXLOG("Trajectory nhan duoc la : a = %f , v0 = %f", newTrajectory.a, newTrajectory.v0);
                //AXLOG("NewTrajectory: a=%f, v0=%f", newTrajectory.a, newTrajectory.v0);
                if (newTrajectory.a != 0.0f || newTrajectory.v0 != 0.0f)
                {
                    
                    lastEvent = ActionState::Spike;
                    if (testTrajectory.AttackPower > 170)
                    {
                        powerSpike = 1;
                        boost         = TrajectoryBoost{6.0f, 5.0f};
                    }
                    else
                        powerSpike = -1;
                    UpdateNewTrajectory(newTrajectory , _componentStorage,direction,DECREASE_C_FOR_SPIKE, powerSpike , boost);  // 7 là index của bóng   
                }
            }
            if (intent[i].finalIntent == Bump)
            {
                //AXLOG("có sự kiện đỡ bóng của character %d", i);
                auto detection = DetectPlayerBall(
                    data, index, 7, Size(SystemConfig::HEIGHT_PERCENT_CHANGE, SystemConfig::WIDTH_PERCENT_CHANGE));
                //AXLOG("khoang cach la : %f ", detection.distancePercent);
                auto ballState = _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH);
                auto characterPos = _componentStorage->GetCharacterPositionPool().get(GameConfig::PLAYER);
                auto dx           = ballState->landingX - characterPos->position.x;
                int direction1     = 0;
                if (characterPos->position.x < ballState->landingX)
                {
                    direction1 = 1;
                }
                else
                {
                    direction1 = -1;
                }
                if (abs(dx) > SPEED)
                {// ngoai vung detect
                    auto& characterIntent = _intentStorage->GetCharacterIntentPool();
                    auto playerIntent     = characterIntent.get(GameConfig::PLAYER);

                    if (playerIntent == nullptr)
                    {
                        CharacterIntent c;
                        c.moveX = SPEED * direction1;
                        characterIntent.add(GameConfig::PLAYER, c);
                    }
                    else
                    {
                        playerIntent->moveX = SPEED * direction1;
                    }
                }
                if (detection.distancePercent <= SystemConfig::DISTANCE_DETECTION_BUMP)
                {
                    lastEvent                    = ActionState::Bump;
                    TrajectoryData newTrajectory = TrajectoryData(-0.006, 120.0f);
                   // AXLOG("Direction la : %s", direction == 1 ? "Trai sang phai " : "Phai sang trai");
                    UpdateNewTrajectory(newTrajectory, _componentStorage, direction, DECREASE_C_FOR_BUMP, powerSpike,
                                        boost);
                }
            }
            if (intent[i].finalIntent == Set)
            {
                auto detection = DetectPlayerBall(
                    data, index, 7, Size(SystemConfig::HEIGHT_PERCENT_CHANGE, SystemConfig::WIDTH_PERCENT_CHANGE));
                // AXLOG("khoang cach la : %f ", detection.distancePercent);
                if (detection.distancePercent <= SystemConfig::DISTANCE_DETECTION_SET)
                {
                    lastEvent                     = ActionState::Set;
                    TrajectoryData newTrajectory = TrajectoryData(-8, 3.0f);
                    // AXLOG("Direction la : %s", direction == 1 ? "Trai sang phai " : "Phai sang trai");
                    UpdateNewTrajectory(newTrajectory, _componentStorage, direction, DECREASE_C_FOR_BUMP, powerSpike,
                                        boost);
                }
            }
            if (intent[i].finalIntent == Serve /*&&
                    _componentStorage->GetBallGameplayStatePool().get(DEFAULT_MATCH)->stateFrame == Reset*/)
            {
                lastEvent                     = ActionState::Serve;
                //AXLOG("powerSpike = %d", powerSpike);
                TrajectoryData newTrajectory = TrajectoryData(-0.0045f, 100.0f);
                if(ballGamePlay->stateFrame == Reset) ballGamePlay->stateFrame     = Alive;
                boost                        = TrajectoryBoost{5.0f, 5.0f};
                UpdateNewTrajectory(newTrajectory, _componentStorage, direction, DECREASE_C_FOR_SPIKE, powerSpike,
                                    boost);
            }
            if (lastEvent != ActionState::None)// có sự kiện xảy ra
            {
                auto rallyState = _componentStorage->GetRallyStatePool().get(DEFAULT_MATCH);
                if (rallyState)
                {
                    AXLOG("TouchCount hiện tại là : %d", rallyState->touchCount);
                }
                else
                {
                    AXLOG("RallyState nullptr");
                }
                auto maxCharacterPerSideCourt = ChunkConfig::CHARACTER_PER_MATCH / 2;
                if (rallyState->lastTouch / maxCharacterPerSideCourt == index / maxCharacterPerSideCourt)
                    rallyState->touchCount++;
                else
                    rallyState->touchCount = 1;
                rallyState->lastTouch = index;  // lưu lại entity vừa đánh bóng

                //set remainTime and status
                auto charState = _componentStorage->GetCharacterActionStatePool().get(index);
                charState->status = lastEvent;
                charState->remainTime = ActionStateInfo::Cooldown[static_cast<size_t>(lastEvent)];
            }
        }
    }
};

